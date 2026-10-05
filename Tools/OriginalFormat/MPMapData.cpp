#include "Stdafx.h"
#include "MPTile.h"
#include "MPMap.h"
#include "AssetLoaders.h"  // Corsairs::Engine::Render::MapLoader
#include "lwgraphicsutil.h"
#include "assert.h"
#include <sys/types.h>
#include <sys/stat.h>
#include <io.h>  // _chmod — ранее тянулся через umbrella util.h
#include "MPMapDef.h"

using namespace std;

#define MAX_DEL_SECTION				200
#define MAX_SECTION_BUFFER          16


BOOL MPMap::Load(const char* pszMapName, BOOL bEdit) {
	using MapLoader = Corsairs::Engine::Render::MapLoader;

	if (bEdit) {
		_chmod(pszMapName, _S_IWRITE);
	}

	Corsairs::Engine::Render::MapLoadDiagnostics diag;
	if (LW_FAILED(MapLoader::OpenStream(_stream, pszMapName, bEdit != FALSE, diag))) {
		ToLogService("map", LogLevel::Error,
					 "Load Map [{}] Error! status={} ({})",
					 pszMapName,
					 Corsairs::Engine::Render::ToString(diag.status),
					 diag.detail);
		return FALSE;
	}

	const auto& header = _stream.Header();
	_nWidth = header.Width;
	_nHeight = header.Height;
	_nSectionWidth = header.SectionWidth;
	_nSectionHeight = header.SectionHeight;
	_nSectionCntX = _stream.SectionCountX();
	_nSectionCntY = _stream.SectionCountY();
	_nSectionCnt = _nSectionCntX * _nSectionCntY;

	_bEdit = bEdit;

	ClearSectionArray();

	// ZRBlock не открывает .map повторно — он работает поверх уже открытого
	// MPMap'ом MapStream'а (header/offsets/bulk-кеш). Attach сбрасывает
	// массив активных секций и кеширует геометрию.
	_block->Attach(_stream);

	return TRUE;
}

int MPMap::GetValidSectionCnt() {
	int nValidCnt = 0;
	for (int sy = 0; sy < _nSectionCntY; ++sy) {
		for (int sx = 0; sx < _nSectionCntX; ++sx) {
			if (_stream.SectionOffset(sx, sy) != 0) {
				++nValidCnt;
			}
		}
	}
	return nValidCnt;
}

void MPMap::SetSectionTileData(int nX, int nY, BYTE btTexNo) {
	if (btTexNo == 0 || !_bEdit) {
		return;
	}

	MPActiveMapSection* pSection = GetActiveSection(nX / _nSectionWidth, nY / _nSectionHeight);
	if (!pSection) {
		return;
	}

	SAFE_DELETE(pSection->pTileData);
	pSection->pTileData = new MPTile[_nSectionWidth * _nSectionHeight];
	for (int y = 0; y < _nSectionHeight; y++) {
		for (int x = 0; x < _nSectionWidth; x++) {
			MPTile* pTile = pSection->pTileData + y * _nSectionWidth + x;
			pTile->Init();
			pTile->TexLayer[0].btTexNo = btTexNo;
			pTile->TexLayer[0].btAlphaNo = 15;
			pTile->fHeight = 0.6f + (float)(rand() % 30) / 100.0f;
			pTile->dwColor = 0xffffffff;
			pTile->sRegion = 1; //
		}
	}

	_SaveSection(pSection);
}

//-----------------
//-----------------
void MPMap::_SaveSection(MPActiveMapSection* pSection) {
	if (!_bEdit || !_stream.IsOpen()) {
		return;
	}

	if (LW_FAILED(Corsairs::Engine::Render::MapLoader::WriteSection(
			_stream, pSection->nX, pSection->nY, pSection->pTileData))) {
		ToLogService("errors", LogLevel::Error,
					 "[MPMap::_SaveSection] WriteSection failed for section [{},{}]",
					 pSection->nX, pSection->nY);
		return;
	}
	pSection->dwDataOffset = _stream.SectionOffset(pSection->nX, pSection->nY);
}

//-----------------
//-----------------
void MPMap::_LoadSectionData(MPActiveMapSection* pSection) {
	pSection->dwDataOffset = _stream.SectionOffset(pSection->nX, pSection->nY);
	if (pSection->dwDataOffset == 0) {
		return;
	}

	pSection->pTileData = new MPTile[_nSectionWidth * _nSectionHeight];
	if (LW_FAILED(Corsairs::Engine::Render::MapLoader::ReadSection(
			_stream, pSection->nX, pSection->nY, pSection->pTileData))) {
		ToLogService("errors", LogLevel::Error,
					 "[MPMap::_LoadSectionData] ReadSection failed for section [{},{}]",
					 pSection->nX, pSection->nY);
	}
}


MPActiveMapSection* MPMap::LoadSectionData(int nSectionX, int nSectionY) {
	MPActiveMapSection* pSection = new MPActiveMapSection;
	pSection->Init();
	pSection->nX = nSectionX;
	pSection->nY = nSectionY;

	_LoadSectionData(pSection);
	_ActiveSectionArray[nSectionY][nSectionX] = pSection;
	_ActiveSectionList.push_back(pSection);
	return pSection;
}

void MPMap::ClearSectionData(int nSectionX, int nSectionY) {
	if (LW_FAILED(Corsairs::Engine::Render::MapLoader::ClearSection(_stream, nSectionX, nSectionY))) {
		ToLogService("errors", LogLevel::Error,
					 "[MPMap::ClearSectionData] ClearSection failed for [{},{}]",
					 nSectionX, nSectionY);
	}

	MPActiveMapSection* pSection = GetActiveSection(nSectionX, nSectionY);
	if (pSection) // Section
	{
		_ActiveSectionList.remove(pSection);
		_ActiveSectionArray[nSectionY][nSectionX] = NULL;
		SAFE_DELETE(pSection);
	}
}

void MPMap::FullLoading() {
	if (!_stream.IsOpen()) {
		return;
	}

	// Section
	for (int i = 0; i < _nSectionCnt; i++) {
		int nSectionX = i % _nSectionCntX;
		int nSectionY = i / _nSectionCntX;
		if (_stream.SectionOffset(nSectionX, nSectionY) != 0) {
			MPActiveMapSection* pSection = LoadSectionData(nSectionX, nSectionY);
			_pfnProc(0, pSection->nX, pSection->nY, (DWORD)(pSection), this);
		}
	}
}

void MPMap::DynamicLoading(DWORD dwTimeParam) {
	if (!_stream.IsOpen()) {
		return;
	}

	int nCenterSectionX = _fShowCenterX / _nSectionWidth;
	int nCenterSectionY = _fShowCenterY / _nSectionHeight;

	if (nCenterSectionX == _nLastSectionX && nCenterSectionY == _nLastSectionY) {
		return;
	}
	else {
		_nLastSectionX = nCenterSectionX;
		_nLastSectionY = nCenterSectionY;
	}

	Corsairs::Util::MPTimer t;
	t.Begin();
	int nCurSectionX = (int)(_fShowCenterX - (float)_nShowWidth / 2.0f) / _nSectionWidth;
	int nCurSectionY = (int)(_fShowCenterY - (float)_nShowHeight / 2.0f) / _nSectionHeight;

	int nEndSectionX = (int)(_fShowCenterX + (float)_nShowWidth / 2.0f) / _nSectionWidth;
	int nEndSectionY = (int)(_fShowCenterY + (float)_nShowHeight / 2.0f) / _nSectionHeight;

	int nShowSectionCntX = nEndSectionX - nCurSectionX;
	int nShowSectionCntY = nEndSectionY - nCurSectionY;

	if (_nShowWidth % _nSectionWidth != 0) {
		nShowSectionCntX++;
	}
	if (_nShowHeight % _nSectionHeight != 0) nShowSectionCntY++;

	list<MPActiveMapSection*> _NewList;

	for (int y = 0; y < nShowSectionCntY; y++) {
		int nSectionY = nCurSectionY + y;

		if (nSectionY < 0 || nSectionY >= _nSectionCntY) {
			continue;
		}
		for (int x = 0; x < nShowSectionCntX; x++) {
			int nSectionX = nCurSectionX + x;

			if (nSectionX < 0 || nSectionX >= _nSectionCntX) {
				continue;
			}

			MPActiveMapSection* pSection = GetActiveSection(nSectionX, nSectionY);
			if (!pSection) {
				pSection = LoadSectionData(nSectionX, nSectionY);
				_NewList.push_back(pSection);
			}
			pSection->dwActiveTime = dwTimeParam;
		}
	}

	if ((int)(_ActiveSectionList.size()) >= _nSectionBufferSize) {
		static MPActiveMapSection* DelSectionList[MAX_DEL_SECTION];
		int n = 0;
		BOOL bDelFail = FALSE;
		for (list<MPActiveMapSection*>::iterator it = _ActiveSectionList.begin(); it != _ActiveSectionList.end(); it
			 ++) {
			MPActiveMapSection* pCur = (*it);
			if (pCur->dwActiveTime != dwTimeParam) {
				DelSectionList[n] = pCur;
				n++;
				if (n >= MAX_DEL_SECTION) {
					bDelFail = TRUE;
					break;
				}
			}
		}
		if (bDelFail) {
			ToLogService("map", "Section, Buffer, n = {}", n);
		}

		for (int i = 0; i < n; i++) // Section Tile Data
		{
			UpdateRender(TRUE);
			_ActiveSectionList.remove(DelSectionList[i]);
			if (DelSectionList[i]->dwDataOffset != 0) {
				_SaveSection(DelSectionList[i]);
			}
			if (_pfnProc) {
				_pfnProc(1, DelSectionList[i]->nX, DelSectionList[i]->nY, (DWORD)(DelSectionList[i]), this);
			}
			_ActiveSectionArray[DelSectionList[i]->nY][DelSectionList[i]->nX] = NULL;
			SAFE_DELETE(DelSectionList[i]->pTileData);
			SAFE_DELETE(DelSectionList[i]);
		}
	}


	// MapNotice
	for (list<MPActiveMapSection*>::iterator it = _NewList.begin(); it != _NewList.end(); it++) {
		MPActiveMapSection* pNewSection = (*it);
		if (_pfnProc) {
			_pfnProc(0, pNewSection->nX, pNewSection->nY, (DWORD)(pNewSection), this);
		}
		UpdateRender(TRUE);
	}

	_NewList.clear();


	m_dwActiveSectionCnt = (DWORD)(_ActiveSectionList.size());


	DWORD dwLoadingTime = t.End();
	if (dwLoadingTime > m_dwMaxLoadingTime) {
		m_dwMaxLoadingTime = dwLoadingTime;
	}

	if (dwLoadingTime) {
		m_dwLoadingTime[_dwLoadingCnt] = dwLoadingTime;
		_dwLoadingCnt++;
		if (_dwLoadingCnt >= 3) {
			_dwLoadingCnt = 0;
		}
	}

	_block->GetBlockByRange(_fShowCenterX, _fShowCenterY, _pathFindRange);
}

void MPMap::ClearAllSection(BOOL bSaveFlag) {
	for (list<MPActiveMapSection*>::iterator it = _ActiveSectionList.begin(); it != _ActiveSectionList.end(); it++) {
		MPActiveMapSection* pSection = (*it);
		if (pSection->dwDataOffset) {
			if (bSaveFlag) {
				_SaveSection((*it));
			}
		}
		if (bSaveFlag) {
			if (_pfnProc) {
				_pfnProc(1, pSection->nX, pSection->nY, (DWORD)(pSection), this);
			}
		}
		_ActiveSectionArray[pSection->nY][pSection->nX] = NULL;
		SAFE_DELETE(pSection->pTileData);
		SAFE_DELETE(pSection);
	}

	ClearSectionArray();
	_ActiveSectionList.clear();
}

void MPMap::ClearSectionArray() {
	memset(&_ActiveSectionArray, 0, 512 * 512 * 4);
}
