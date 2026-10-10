using System;
using System.Collections.Generic;
using TOP.Data;
using TOP.Player;
using UnityEngine;

namespace TOP.Systems
{
    public static class ArenaWorld
    {
        static byte[] map;
        static readonly Dictionary<int, GameObject> built = new Dictionary<int, GameObject>();
        public static Vector3 Origin(int room) => new Vector3(10000 + room * 128, 0, 0);
        public static Vector3 Spawn(int room, int side) => Origin(room) + new Vector3(44, Height(44, side == 1 ? 21 : 66), side == 1 ? -21 : -66);
        static void Load() { if (map == null) map = Resources.Load<TextAsset>("PKOArena/teampk.map").bytes; }
        static int At(int x, int y)
        {
            Load();
            if (x < 0 || y < 0 || x >= 96 || y >= 96) return -1;
            int offset = BitConverter.ToInt32(map, 20 + (y / 8 * 12 + x / 8) * 4);
            return offset == 0 ? -1 : offset + (y % 8 * 8 + x % 8) * 15;
        }
        static float Height(int x, int y) { int p = At(x, y); return p < 0 ? -2 : (sbyte)map[p + 7] * .1f; }
        public static bool IsBlocked(int room, Vector3 position)
        {
            var local = position - Origin(room);
            int x = Mathf.FloorToInt(local.x), y = Mathf.FloorToInt(-local.z), p = At(x, y);
            if (p < 0) return true;
            int sub = (Mathf.FloorToInt(-local.z * 2) & 1) * 2 + (Mathf.FloorToInt(local.x * 2) & 1);
            return (map[p + 11 + sub] & 128) != 0;
        }
        public static Vector3 AttributePosition(int room, Vector3 position)
        {
            Vector3 local = position - Origin(room);
            return new Vector3(local.x - WorldBlockGrid.OriginX, local.y, WorldBlockGrid.OriginZ + local.z);
        }
        public static void Ensure(int room)
        {
            if (room < 1 || room > OriginalArenaRules.Copies) return;
            if (built.TryGetValue(room, out var existing) && existing != null) return;
            Load();
            var root = new GameObject("Original_teampk_copy_" + room);
            root.transform.position = Origin(room);
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>();
            int[] ids = { 8, 9, 21, 43 }; string[] textures = { "grass02", "grass01", "brick03", "dirt01" };
            var triangles = new List<int>[4]; for (int i = 0; i < 4; i++) triangles[i] = new List<int>();
            for (int y = 0; y < 95; y++) for (int x = 0; x < 95; x++)
            {
                int p = At(x, y); if (p < 0) continue;
                int start = vertices.Count;
                vertices.Add(new Vector3(x, Height(x, y), -y)); vertices.Add(new Vector3(x + 1, Height(x + 1, y), -y));
                vertices.Add(new Vector3(x, Height(x, y + 1), -y - 1)); vertices.Add(new Vector3(x + 1, Height(x + 1, y + 1), -y - 1));
                uvs.Add(new Vector2(x / 4f, y / 4f)); uvs.Add(new Vector2((x + 1) / 4f, y / 4f));
                uvs.Add(new Vector2(x / 4f, (y + 1) / 4f)); uvs.Add(new Vector2((x + 1) / 4f, (y + 1) / 4f));
                int index = Array.IndexOf(ids, map[p + 4]); if (index < 0) index = 1;
                triangles[index].AddRange(new[] { start, start + 1, start + 2, start + 1, start + 3, start + 2 });
            }
            var mesh = new Mesh { name = "Original_teampk", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.subMeshCount = 4;
            for (int i = 0; i < 4; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                var renderer = root.AddComponent<MeshRenderer>(); var materials = new Material[4];
                for (int i = 0; i < 4; i++) { materials[i] = new Material(Shader.Find("Universal Render Pipeline/Lit")); materials[i].mainTexture = Resources.Load<Texture2D>("PKOArena/" + textures[i]); }
                renderer.sharedMaterials = materials;
            }
            root.AddComponent<MeshCollider>().sharedMesh = mesh; root.layer = LayerMask.NameToLayer("Terrain");
            built[room] = root;
        }
        public static void Reset() { built.Clear(); }
    }
}
