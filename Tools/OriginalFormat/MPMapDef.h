#pragma once

#include <cstdint>
#include <type_traits>


// ===========================================================================
//  MPMapDef — бинарный контракт формата файла .map (MindPower3D terrain).
//
//  Файл .map состоит из:
//      [MPMapFileHeader, 20 байт]
//      [std::uint32_t offsets[sectionCount]]
//      [body: конкатенация секций; каждая секция — массив SNewFileTile
//             размером SectionWidth × SectionHeight, 15 байт на тайл]
//
//  Структуры читаются/пишутся напрямую через fread/fwrite, поэтому их
//  раскладка зафиксирована: размер и порядок полей менять нельзя. Имена
//  полей и underlying-типы (при сохранении ширины) — свободны для правок,
//  они на байты в файле не влияют. Страховку даёт static_assert ниже.
// ===========================================================================


namespace Corsairs::Util {

// ---------------------------------------------------------------------------
// Версии формата.
//   kMapFlagBase    — базовое magic-значение (history: «780624»).
//   kMapFlagLegacy  — предыдущая версия, читается на вход, обратно не пишется.
//   kMapFlagCurrent — текущая версия; именно её записывает MapLoader::Save.
// ---------------------------------------------------------------------------

inline constexpr std::int32_t kMapFlagBase    = 780624;
inline constexpr std::int32_t kMapFlagLegacy  = 780626; // base + 2
inline constexpr std::int32_t kMapFlagCurrent = 780627; // base + 3


// ---------------------------------------------------------------------------
// MPMapFileHeader — 20-байтный заголовок (5 × int32 без паддинга).
// Sentinel ширины через static_assert ниже.
// ---------------------------------------------------------------------------

struct MPMapFileHeader {
    std::int32_t MapFlag;
    std::int32_t Width;
    std::int32_t Height;
    std::int32_t SectionWidth;
    std::int32_t SectionHeight;
};


#pragma pack(push, 1)

// ---------------------------------------------------------------------------
// SNewFileTile — сериализация одного тайла, ровно 15 байт.
//
//   TileInfo (4 байта) — три верхних слоя текстур (текстура+альфа),
//                        упакованы по 10 бит на слой = 30 бит. См. TileInfo_*.
//   BaseTex  (1 байт)  — базовый слой (всегда полностью непрозрачен).
//   Color    (2 байта) — RGB565 для тонировки тайла.
//   Height   (1 байт)  — высота тайла в единицах по 10 см.
//   Region   (2 байта) — идентификатор региона (используется ZRBlock).
//   Island   (1 байт)  — индекс «острова» (плавающая платформа).
//   Block[4] (4 байта) — per-quadrant флаги проходимости.
// ---------------------------------------------------------------------------

struct SNewFileTile {
    std::uint32_t TileInfo{0};
    std::uint8_t  BaseTex{0};
    std::int16_t  Color{0};
    std::int8_t   Height{0};
    std::int16_t  Region{0};
    std::uint8_t  Island{0};
    std::uint8_t  Block[4]{0, 0, 0, 0};
};

#pragma pack(pop)


// Страховка на случай случайной правки полей или режима паддинга.
static_assert(sizeof(MPMapFileHeader) == 20,
              "MPMapFileHeader должен быть ровно 20 байт (5 x int32 без паддинга)");
static_assert(sizeof(SNewFileTile) == 15,
              "SNewFileTile должен быть ровно 15 байт (бинарный формат .map)");
static_assert(std::is_trivially_copyable_v<MPMapFileHeader>);
static_assert(std::is_trivially_copyable_v<SNewFileTile>);


// ---------------------------------------------------------------------------
// Упаковка/распаковка трёх верхних слоёв тайла в одно 32-битное слово.
//
// Раскладка TileInfo (бит 31 — старший):
//   [31:26] Tex1   (6 бит, диапазон 0..63)
//   [25:22] Alpha1 (4 бит, диапазон 0..15)
//   [21:16] Tex2   (6 бит)
//   [15:12] Alpha2 (4 бит)
//   [11:6 ] Tex3   (6 бит)
//   [ 5:2 ] Alpha3 (4 бит)
//   [ 1:0 ] не используются
//
// Источник/приёмник pbtTile — 8-байтный массив MPTile::TexLayer (4 слоя
// по 2 байта: tex+alpha). Layer[0] = (BaseTex, kBaseAlphaOpaque) — базовый
// слой, у него альфа фиксированно максимальна, поэтому в файле хранится
// отдельным байтом BaseTex. Layer[1..3] — кодируются в TileInfo.
// ---------------------------------------------------------------------------

inline constexpr int           kTileTex1Shift     = 26;
inline constexpr int           kTileAlpha1Shift   = 22;
inline constexpr int           kTileTex2Shift     = 16;
inline constexpr int           kTileAlpha2Shift   = 12;
inline constexpr int           kTileTex3Shift     = 6;
inline constexpr int           kTileAlpha3Shift   = 2;
inline constexpr std::uint32_t kTileTexMask       = 0x3Fu; // 6 бит
inline constexpr std::uint32_t kTileAlphaMask     = 0x0Fu; // 4 бит
inline constexpr std::uint8_t  kBaseAlphaOpaque   = 15;

void TileInfo_Pack(const std::uint8_t* pbtTile,
                   std::uint32_t& tileInfo,
                   std::uint8_t& baseTex) noexcept;

void TileInfo_Unpack(std::uint32_t tileInfo,
                     std::uint8_t baseTex,
                     std::uint8_t* pbtTile) noexcept;

} // namespace Corsairs::Util
