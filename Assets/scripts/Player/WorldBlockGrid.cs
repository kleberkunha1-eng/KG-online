using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace TOP.Player
{
    // Grade de bloqueio original (50 cm por célula) extraída do garner.map: bit 0x80 de btBlock.
    public static class WorldBlockGrid
    {
        public const float OriginX = 2218f;
        public const float OriginZ = 2782f;

        static byte[] _bits;
        static int _width;
        static bool _loaded;

        static void Load()
        {
            _loaded = true;
            var asset = Resources.Load<TextAsset>("garner.block");
            if (asset == null) return;
            using (var gz = new GZipStream(new MemoryStream(asset.bytes), CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                var all = ms.ToArray();
                _width = System.BitConverter.ToInt32(all, 0);
                _bits = new byte[all.Length - 4];
                System.Buffer.BlockCopy(all, 4, _bits, 0, _bits.Length);
            }
        }

        public static bool IsBlocked(Vector3 world)
        {
            if (!_loaded) Load();
            if (_bits == null) return false;
            int cx = Mathf.FloorToInt((world.x + OriginX) * 2f);
            int cy = Mathf.FloorToInt((OriginZ - world.z) * 2f);
            if (cx < 0 || cy < 0 || cx >= _width || cy >= _width) return true;
            int i = cy * _width + cx;
            return (_bits[i >> 3] & (1 << (i & 7))) != 0;
        }
    }
}
