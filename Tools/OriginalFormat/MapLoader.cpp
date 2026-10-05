// Реализация MapLoader (.map). Два API:
//
//   • Снапшот (Load/LoadEx/Save) — читает заголовок, таблицу оффсетов и body
//     «как есть» в MapInfo и пишет обратно побайтно. Используется
//     round-trip-тестом (AssetLoaderTests) и тулзами; тяжёлые зависимости
//     (MPTile) не нужны.
//
//   • Stream (OpenStream/ReadSection/WriteSection/ClearSection) — для MPMap.
//     Open читает header+offsets, в non-edit копирует body в std::vector<byte>;
//     Read/Write декодируют по одной секции (SNewFileTile ↔ MPTile) и в
//     edit-режиме держат FILE* для in-place правок offset-таблицы.
//
// Round-trip Load→Save детерминирован: тело файла копируется без переинтерпретации
// SNewFileTile-структур. Что сломано — ловит ParseHeader/ValidateOffsets, а
// не «зашьётся» в копии.

#include "AssetLoaders.h"

#include "logutil.h"
#include "lwgraphicsutil.h"  // LW_RGB565TODWORD / LW_RGBDWORDTO565
#include "MPTile.h"
#include "ZRBlock.h"         // ::ZRBlockData — для ReadSectionBlockData

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <format>
#include <memory>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace Corsairs::Engine::Render {

namespace {

struct FileCloser {
    void operator()(std::FILE* fp) const noexcept {
        if (fp != nullptr) {
            std::fclose(fp);
        }
    }
};
using UniqueFile = std::unique_ptr<std::FILE, FileCloser>;

// Поддерживаемые значения header.MapFlag. Файл с kMapFlagBase+1 — это незавершённый
// MapTool-файл (см. MPMap::Load в MPMapData.cpp), он явно отвергается.
[[nodiscard]] constexpr bool IsKnownMapFlag(std::int32_t flag) noexcept {
    return flag == MapLoader::kCurrentMapFlag || flag == MapLoader::kLegacyMapFlag;
}

// Проверка геометрии: размеры должны быть положительные, секции —
// строго больше нуля. Делимости nWidth/nSectionWidth НЕ требуем: рантайм
// (`MPMap::Load`) считает count через целочисленное деление и игнорирует
// «хвост». В датасете есть валидный пример room.map (52×52 при секции 8×8 →
// 6×6=36 секций, 4 столбца/строки тайлов на правом и нижнем краях не
// загружаются). Если бы мы reject'или такие файлы, тест бы их пометил
// невалидными — но MPMap их грузит.
[[nodiscard]] bool DimensionsValid(const MPMapFileHeader& h) noexcept {
    if (h.Width <= 0 || h.Height <= 0) {
        return false;
    }
    if (h.SectionWidth <= 0 || h.SectionHeight <= 0) {
        return false;
    }
    if (h.SectionWidth > h.Width || h.SectionHeight > h.Height) {
        return false;
    }
    return true;
}

[[nodiscard]] std::size_t SectionCount(const MPMapFileHeader& h) noexcept {
    const std::size_t cntX = static_cast<std::size_t>(h.Width / h.SectionWidth);
    const std::size_t cntY = static_cast<std::size_t>(h.Height / h.SectionHeight);
    return cntX * cntY;
}

[[nodiscard]] std::size_t TileBytesPerSection(const MPMapFileHeader& h) noexcept {
    return static_cast<std::size_t>(h.SectionWidth) *
           static_cast<std::size_t>(h.SectionHeight) *
           sizeof(SNewFileTile);
}

[[nodiscard]] std::int64_t FileSize(std::FILE* fp) noexcept {
    if (std::fseek(fp, 0, SEEK_END) != 0) {
        return -1;
    }
    const long sz = std::ftell(fp);
    if (std::fseek(fp, 0, SEEK_SET) != 0) {
        return -1;
    }
    return static_cast<std::int64_t>(sz);
}

void LogLoadFailure(std::string_view file, const MapLoadDiagnostics& diag) {
    ToLogService("errors", LogLevel::Error,
                 "[MapLoader] {} ({}): file={}, mapFlag={}",
                 ToString(diag.status), diag.detail,
                 (file.empty() ? std::string_view{"(null)"} : file),
                 diag.mapFlag);
}

} // namespace

// =============================================================================
// MapLoader (.map)
// =============================================================================

LW_RESULT MapLoader::Load(MapInfo& info, std::string_view file) {
    MapLoadDiagnostics diag;
    return LoadEx(info, file, diag);
}

LW_RESULT MapLoader::LoadEx(MapInfo& info, std::string_view file,
                            MapLoadDiagnostics& diag) {
    diag = {};
    info = MapInfo{};

    UniqueFile fp{std::fopen(std::string{file}.c_str(), "rb")};
    if (!fp) {
        diag.status = MapLoadStatus::FileOpenFailed;
        diag.detail = "fopen failed";
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    const std::int64_t totalSize = FileSize(fp.get());
    if (totalSize < 0) {
        diag.status = MapLoadStatus::FileOpenFailed;
        diag.detail = "fseek/ftell failed";
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    if (totalSize < static_cast<std::int64_t>(sizeof(MPMapFileHeader))) {
        diag.status = MapLoadStatus::HeaderTruncated;
        diag.detail = std::format("file size {} < sizeof(MPMapFileHeader)={}",
                                   totalSize, sizeof(MPMapFileHeader));
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    if (std::fread(&info.header, sizeof(MPMapFileHeader), 1, fp.get()) != 1) {
        diag.status = MapLoadStatus::HeaderTruncated;
        diag.detail = "short fread of MPMapFileHeader";
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }
    diag.mapFlag = info.header.MapFlag;

    if (!IsKnownMapFlag(info.header.MapFlag)) {
        diag.status = MapLoadStatus::BadMagic;
        diag.detail = std::format("MapFlag={} (expected {} или {})",
                                   info.header.MapFlag,
                                   kCurrentMapFlag, kLegacyMapFlag);
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    if (!DimensionsValid(info.header)) {
        diag.status = MapLoadStatus::InconsistentDimensions;
        diag.detail = std::format(
            "width={}, height={}, sectionWidth={}, sectionHeight={}",
            info.header.Width, info.header.Height,
            info.header.SectionWidth, info.header.SectionHeight);
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    const std::size_t sectionCount = SectionCount(info.header);
    const std::size_t offsetTableBytes = sectionCount * sizeof(std::uint32_t);
    const std::size_t prefixBytes = sizeof(MPMapFileHeader) + offsetTableBytes;
    if (static_cast<std::int64_t>(prefixBytes) > totalSize) {
        diag.status = MapLoadStatus::OffsetTableTruncated;
        diag.detail = std::format(
            "header+offsets={} > file_size={}", prefixBytes, totalSize);
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    info.offsets.resize(sectionCount);
    if (sectionCount > 0) {
        if (std::fread(info.offsets.data(), sizeof(std::uint32_t),
                       sectionCount, fp.get()) != sectionCount) {
            diag.status = MapLoadStatus::OffsetTableTruncated;
            diag.detail = std::format("short fread of offsets[{}]", sectionCount);
            LogLoadFailure(file, diag);
            return LW_RET_FAILED;
        }
    }

    const std::size_t bodyBytes = static_cast<std::size_t>(totalSize) - prefixBytes;
    info.body.resize(bodyBytes);
    if (bodyBytes > 0) {
        if (std::fread(info.body.data(), 1, bodyBytes, fp.get()) != bodyBytes) {
            diag.status = MapLoadStatus::BodyTruncated;
            diag.detail = std::format("short fread of body, expected {} bytes",
                                       bodyBytes);
            LogLoadFailure(file, diag);
            return LW_RET_FAILED;
        }
    }

    // Sanity: каждый ненулевой offsets[i] должен попадать в [prefix, file_size − tileBytes].
    // Битый offset — признак повреждения файла; runtime будет читать мусор.
    const std::size_t tileBytes = TileBytesPerSection(info.header);
    for (std::size_t i = 0; i < sectionCount; ++i) {
        const std::uint32_t off = info.offsets[i];
        if (off == 0) {
            continue;
        }
        if (off < prefixBytes || off + tileBytes > static_cast<std::uint64_t>(totalSize)) {
            diag.status = MapLoadStatus::BodyTruncated;
            diag.detail = std::format(
                "offsets[{}]={} вне допустимого диапазона [{}, {}]",
                i, off, prefixBytes,
                static_cast<std::uint64_t>(totalSize) - tileBytes);
            LogLoadFailure(file, diag);
            return LW_RET_FAILED;
        }
    }

    diag.status = MapLoadStatus::Ok;
    return LW_RET_OK;
}

LW_RESULT MapLoader::Save(const MapInfo& info, std::string_view file) {
    UniqueFile fp{std::fopen(std::string{file}.c_str(), "wb")};
    if (!fp) {
        ToLogService("errors", LogLevel::Error,
                     "[MapLoader::Save] fopen failed: {}", file);
        return LW_RET_FAILED;
    }

    if (std::fwrite(&info.header, sizeof(MPMapFileHeader), 1, fp.get()) != 1) {
        ToLogService("errors", LogLevel::Error,
                     "[MapLoader::Save] fwrite header failed: {}", file);
        return LW_RET_FAILED;
    }

    if (!info.offsets.empty()) {
        if (std::fwrite(info.offsets.data(), sizeof(std::uint32_t),
                        info.offsets.size(), fp.get()) != info.offsets.size()) {
            ToLogService("errors", LogLevel::Error,
                         "[MapLoader::Save] fwrite offsets failed: {}", file);
            return LW_RET_FAILED;
        }
    }

    if (!info.body.empty()) {
        if (std::fwrite(info.body.data(), 1, info.body.size(), fp.get())
            != info.body.size()) {
            ToLogService("errors", LogLevel::Error,
                         "[MapLoader::Save] fwrite body failed: {}", file);
            return LW_RET_FAILED;
        }
    }

    return LW_RET_OK;
}

// =============================================================================
// MapStream
// =============================================================================

MapStream::~MapStream() {
    Close();
}

MapStream::MapStream(MapStream&& other) noexcept {
    *this = std::move(other);
}

MapStream& MapStream::operator=(MapStream&& other) noexcept {
    if (this == &other) {
        return *this;
    }
    Close();
    _fp = std::exchange(other._fp, nullptr);
    _edit = std::exchange(other._edit, false);
    _header = other._header;
    other._header = MPMapFileHeader{};
    _sectionCntX = std::exchange(other._sectionCntX, 0);
    _sectionCntY = std::exchange(other._sectionCntY, 0);
    _offsets = std::move(other._offsets);
    other._offsets.clear();
    _bulkData = std::move(other._bulkData);
    other._bulkData.clear();
    _bulkBaseOffset = std::exchange(other._bulkBaseOffset, 0);
    return *this;
}

bool MapStream::IsOpen() const noexcept {
    return _fp != nullptr || !_bulkData.empty() || !_offsets.empty();
}

std::uint32_t MapStream::SectionOffset(int sectionX, int sectionY) const noexcept {
    if (sectionX < 0 || sectionY < 0
        || sectionX >= _sectionCntX || sectionY >= _sectionCntY) {
        return 0;
    }
    const std::size_t idx = static_cast<std::size_t>(sectionY) * _sectionCntX + sectionX;
    if (idx >= _offsets.size()) {
        return 0;
    }
    return _offsets[idx];
}

void MapStream::Close() noexcept {
    if (_fp != nullptr) {
        std::fclose(_fp);
        _fp = nullptr;
    }
    _edit = false;
    _header = MPMapFileHeader{};
    _sectionCntX = 0;
    _sectionCntY = 0;
    _offsets.clear();
    _bulkData.clear();
    _bulkBaseOffset = 0;
}

// =============================================================================
// MapLoader stream-API
// =============================================================================

namespace {

using ::Corsairs::Util::TileInfo_Pack;
using ::Corsairs::Util::TileInfo_Unpack;

void DecodeTile(const SNewFileTile& src, ::MPTile& dst) {
    dst.Init();
    TileInfo_Unpack(src.TileInfo, src.BaseTex,
                    reinterpret_cast<std::uint8_t*>(&dst.TexLayer[0]));
    dst.dwColor = LW_RGB565TODWORD(src.Color);
    dst.dwColor |= 0xff000000;
    dst.fHeight = static_cast<float>(src.Height * 10) / 100.0f;
    dst.btIsland = src.Island;
    dst.sRegion = src.Region;
    std::memcpy(&dst.btBlock[0], &src.Block[0], 4);
}

void EncodeTile(const ::MPTile& src, SNewFileTile& dst) {
    dst = SNewFileTile{};  // занулить хвостовые поля (Region/Island/Block — через default-инициализаторы)
    TileInfo_Pack(reinterpret_cast<const std::uint8_t*>(&src.TexLayer[0]),
                  dst.TileInfo, dst.BaseTex);
    dst.Height = static_cast<std::int8_t>(src.fHeight * 100 / 10);
    dst.Color  = static_cast<std::int16_t>(LW_RGBDWORDTO565(src.dwColor));
    dst.Region = src.sRegion;
    dst.Island = src.btIsland;
    std::memcpy(&dst.Block[0], &src.btBlock[0], 4);
}

} // namespace

LW_RESULT MapLoader::OpenStream(MapStream& stream, std::string_view file,
                                bool edit, MapLoadDiagnostics& diag) {
    diag = {};
    stream.Close();

    UniqueFile fp{std::fopen(std::string{file}.c_str(), edit ? "r+b" : "rb")};
    if (!fp) {
        diag.status = MapLoadStatus::FileOpenFailed;
        diag.detail = edit ? "fopen r+b failed" : "fopen rb failed";
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    const std::int64_t totalSize = FileSize(fp.get());
    if (totalSize < static_cast<std::int64_t>(sizeof(MPMapFileHeader))) {
        diag.status = MapLoadStatus::HeaderTruncated;
        diag.detail = std::format("file size {} < sizeof(MPMapFileHeader)={}",
                                   totalSize, sizeof(MPMapFileHeader));
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    MPMapFileHeader header{};
    if (std::fread(&header, sizeof(MPMapFileHeader), 1, fp.get()) != 1) {
        diag.status = MapLoadStatus::HeaderTruncated;
        diag.detail = "short fread of MPMapFileHeader";
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }
    diag.mapFlag = header.MapFlag;

    if (!IsKnownMapFlag(header.MapFlag)) {
        diag.status = MapLoadStatus::BadMagic;
        diag.detail = std::format("MapFlag={} (expected {} или {})",
                                   header.MapFlag,
                                   MapLoader::kCurrentMapFlag,
                                   MapLoader::kLegacyMapFlag);
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }
    if (!DimensionsValid(header)) {
        diag.status = MapLoadStatus::InconsistentDimensions;
        diag.detail = std::format(
            "width={}, height={}, sectionWidth={}, sectionHeight={}",
            header.Width, header.Height,
            header.SectionWidth, header.SectionHeight);
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    const std::int32_t cntX = header.Width / header.SectionWidth;
    const std::int32_t cntY = header.Height / header.SectionHeight;
    const std::size_t sectionCount = static_cast<std::size_t>(cntX) * cntY;
    const std::size_t offsetTableBytes = sectionCount * sizeof(std::uint32_t);
    const std::size_t prefixBytes = sizeof(MPMapFileHeader) + offsetTableBytes;
    if (static_cast<std::int64_t>(prefixBytes) > totalSize) {
        diag.status = MapLoadStatus::OffsetTableTruncated;
        diag.detail = std::format("header+offsets={} > file_size={}",
                                   prefixBytes, totalSize);
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    std::vector<std::uint32_t> offsets(sectionCount);
    if (sectionCount > 0
        && std::fread(offsets.data(), sizeof(std::uint32_t),
                       sectionCount, fp.get()) != sectionCount) {
        diag.status = MapLoadStatus::OffsetTableTruncated;
        diag.detail = std::format("short fread of offsets[{}]", sectionCount);
        LogLoadFailure(file, diag);
        return LW_RET_FAILED;
    }

    std::vector<std::byte> bulk;
    if (!edit) {
        const std::size_t bodyBytes = static_cast<std::size_t>(totalSize) - prefixBytes;
        bulk.resize(bodyBytes);
        if (bodyBytes > 0
            && std::fread(bulk.data(), 1, bodyBytes, fp.get()) != bodyBytes) {
            diag.status = MapLoadStatus::BodyTruncated;
            diag.detail = std::format("short fread of body, expected {} bytes",
                                       bodyBytes);
            LogLoadFailure(file, diag);
            return LW_RET_FAILED;
        }
    }

    stream._fp = fp.release();
    stream._edit = edit;
    stream._header = header;
    stream._sectionCntX = cntX;
    stream._sectionCntY = cntY;
    stream._offsets = std::move(offsets);
    stream._bulkData = std::move(bulk);
    stream._bulkBaseOffset = static_cast<std::uint32_t>(prefixBytes);

    diag.status = MapLoadStatus::Ok;
    return LW_RET_OK;
}

LW_RESULT MapLoader::ReadSection(const MapStream& stream,
                                  int sectionX, int sectionY,
                                  ::MPTile* outTiles) {
    if (outTiles == nullptr) {
        return LW_RET_FAILED;
    }
    const std::uint32_t off = stream.SectionOffset(sectionX, sectionY);
    if (off == 0) {
        return LW_RET_FAILED;
    }

    constexpr std::size_t tileSize = sizeof(SNewFileTile);
    const std::int32_t sw = stream.Header().SectionWidth;
    const std::int32_t sh = stream.Header().SectionHeight;

    SNewFileTile fileTile{};

    if (stream._edit) {
        if (stream._fp == nullptr) {
            return LW_RET_FAILED;
        }
        if (std::fseek(stream._fp, static_cast<long>(off), SEEK_SET) != 0) {
            ToLogService("errors", LogLevel::Error,
                         "[MapLoader::ReadSection] fseek to offset {} failed", off);
            return LW_RET_FAILED;
        }
        for (std::int32_t y = 0; y < sh; ++y) {
            for (std::int32_t x = 0; x < sw; ++x) {
                if (std::fread(&fileTile, tileSize, 1, stream._fp) != 1) {
                    ToLogService("errors", LogLevel::Error,
                                 "[MapLoader::ReadSection] short fread tile [{},{}] in section [{},{}]",
                                 x, y, sectionX, sectionY);
                    return LW_RET_FAILED;
                }
                DecodeTile(fileTile, outTiles[y * sw + x]);
            }
        }
        return LW_RET_OK;
    }

    if (off < stream._bulkBaseOffset) {
        return LW_RET_FAILED;
    }
    std::size_t pos = off - stream._bulkBaseOffset;
    const std::size_t needed = static_cast<std::size_t>(sw) * sh * tileSize;
    if (pos + needed > stream._bulkData.size()) {
        ToLogService("errors", LogLevel::Error,
                     "[MapLoader::ReadSection] bulk overflow at section [{},{}]: "
                     "need {} from offset {}, have {}",
                     sectionX, sectionY, needed, pos, stream._bulkData.size());
        return LW_RET_FAILED;
    }
    for (std::int32_t y = 0; y < sh; ++y) {
        for (std::int32_t x = 0; x < sw; ++x) {
            std::memcpy(&fileTile, stream._bulkData.data() + pos, tileSize);
            pos += tileSize;
            DecodeTile(fileTile, outTiles[y * sw + x]);
        }
    }
    return LW_RET_OK;
}

LW_RESULT MapLoader::WriteSection(MapStream& stream,
                                   int sectionX, int sectionY,
                                   const ::MPTile* tiles) {
    if (!stream._edit || stream._fp == nullptr || tiles == nullptr) {
        return LW_RET_FAILED;
    }
    if (sectionX < 0 || sectionY < 0
        || sectionX >= stream._sectionCntX || sectionY >= stream._sectionCntY) {
        return LW_RET_FAILED;
    }

    constexpr std::size_t tileSize = sizeof(SNewFileTile);
    const std::int32_t sw = stream._header.SectionWidth;
    const std::int32_t sh = stream._header.SectionHeight;
    const std::size_t idx = static_cast<std::size_t>(sectionY) * stream._sectionCntX + sectionX;

    std::uint32_t off = stream._offsets[idx];
    if (off != 0) {
        if (std::fseek(stream._fp, static_cast<long>(off), SEEK_SET) != 0) {
            return LW_RET_FAILED;
        }
    }
    else {
        if (std::fseek(stream._fp, 0, SEEK_END) != 0) {
            return LW_RET_FAILED;
        }
        const long pos = std::ftell(stream._fp);
        if (pos < 0) {
            return LW_RET_FAILED;
        }
        off = static_cast<std::uint32_t>(pos);
    }

    SNewFileTile fileTile{};
    for (std::int32_t y = 0; y < sh; ++y) {
        for (std::int32_t x = 0; x < sw; ++x) {
            EncodeTile(tiles[y * sw + x], fileTile);
            if (std::fwrite(&fileTile, tileSize, 1, stream._fp) != 1) {
                ToLogService("errors", LogLevel::Error,
                             "[MapLoader::WriteSection] short fwrite tile [{},{}] in section [{},{}]",
                             x, y, sectionX, sectionY);
                return LW_RET_FAILED;
            }
        }
    }

    // Обновить on-disk offset entry и кеш.
    stream._offsets[idx] = off;
    const std::size_t entryFileOffset = sizeof(MPMapFileHeader)
                                        + idx * sizeof(std::uint32_t);
    if (std::fseek(stream._fp, static_cast<long>(entryFileOffset), SEEK_SET) != 0) {
        return LW_RET_FAILED;
    }
    if (std::fwrite(&off, sizeof(off), 1, stream._fp) != 1) {
        return LW_RET_FAILED;
    }
    return LW_RET_OK;
}

LW_RESULT MapLoader::ReadSectionBlockData(const MapStream& stream,
                                           int sectionX, int sectionY,
                                           ::ZRBlockData* outBlocks) {
    if (outBlocks == nullptr) {
        return LW_RET_FAILED;
    }
    const std::uint32_t off = stream.SectionOffset(sectionX, sectionY);
    if (off == 0) {
        return LW_RET_FAILED;
    }

    constexpr std::size_t tileSize = sizeof(SNewFileTile);
    const std::int32_t sw = stream.Header().SectionWidth;
    const std::int32_t sh = stream.Header().SectionHeight;

    SNewFileTile fileTile{};

    auto Apply = [&](::ZRBlockData* dst, const SNewFileTile& src) {
        dst->sRegion = src.Region;
        std::memcpy(&dst->btBlock[0], &src.Block[0], 4);
    };

    if (stream._edit) {
        if (stream._fp == nullptr) {
            return LW_RET_FAILED;
        }
        if (std::fseek(stream._fp, static_cast<long>(off), SEEK_SET) != 0) {
            ToLogService("errors", LogLevel::Error,
                         "[MapLoader::ReadSectionBlockData] fseek to offset {} failed", off);
            return LW_RET_FAILED;
        }
        for (std::int32_t y = 0; y < sh; ++y) {
            for (std::int32_t x = 0; x < sw; ++x) {
                if (std::fread(&fileTile, tileSize, 1, stream._fp) != 1) {
                    ToLogService("errors", LogLevel::Error,
                                 "[MapLoader::ReadSectionBlockData] short fread tile [{},{}] in section [{},{}]",
                                 x, y, sectionX, sectionY);
                    return LW_RET_FAILED;
                }
                Apply(&outBlocks[y * sw + x], fileTile);
            }
        }
        return LW_RET_OK;
    }

    if (off < stream._bulkBaseOffset) {
        return LW_RET_FAILED;
    }
    std::size_t pos = off - stream._bulkBaseOffset;
    const std::size_t needed = static_cast<std::size_t>(sw) * sh * tileSize;
    if (pos + needed > stream._bulkData.size()) {
        ToLogService("errors", LogLevel::Error,
                     "[MapLoader::ReadSectionBlockData] bulk overflow at section [{},{}]: "
                     "need {} from offset {}, have {}",
                     sectionX, sectionY, needed, pos, stream._bulkData.size());
        return LW_RET_FAILED;
    }
    for (std::int32_t y = 0; y < sh; ++y) {
        for (std::int32_t x = 0; x < sw; ++x) {
            std::memcpy(&fileTile, stream._bulkData.data() + pos, tileSize);
            pos += tileSize;
            Apply(&outBlocks[y * sw + x], fileTile);
        }
    }
    return LW_RET_OK;
}

LW_RESULT MapLoader::ClearSection(MapStream& stream,
                                   int sectionX, int sectionY) {
    if (!stream._edit || stream._fp == nullptr) {
        return LW_RET_FAILED;
    }
    if (sectionX < 0 || sectionY < 0
        || sectionX >= stream._sectionCntX || sectionY >= stream._sectionCntY) {
        return LW_RET_FAILED;
    }
    const std::size_t idx = static_cast<std::size_t>(sectionY) * stream._sectionCntX + sectionX;
    stream._offsets[idx] = 0;

    const std::size_t entryFileOffset = sizeof(MPMapFileHeader)
                                        + idx * sizeof(std::uint32_t);
    if (std::fseek(stream._fp, static_cast<long>(entryFileOffset), SEEK_SET) != 0) {
        return LW_RET_FAILED;
    }
    const std::uint32_t zero = 0;
    if (std::fwrite(&zero, sizeof(zero), 1, stream._fp) != 1) {
        return LW_RET_FAILED;
    }
    return LW_RET_OK;
}

} // namespace Corsairs::Engine::Render
