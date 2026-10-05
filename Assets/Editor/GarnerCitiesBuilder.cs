using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace TOP.EditorTools
{
    // Builds the other Garner hubs (Thundoria, Icicle, havens...) as terrain tiles from garner.map and fills them with the
    // scene models of the matching theme. The original object placement file is not shipped, so the layout is a packed arrangement.
    public static class GarnerCitiesBuilder
    {
        const int TileSize = 256;
        const string SceneDir = "Assets/ImportedClient/Models/model/scene/";

        class City { public string Name, Region; string[] prefixes; public Func<string, bool> Filter; public string[] Prefixes => prefixes; public City(string n, string r, string[] p, Func<string, bool> f = null) { Name = n; Region = r; prefixes = p; Filter = f; } }

        static readonly City[] Cities =
        {
            new City("Thundoria_Castle", "Thundoria Castle", new[] { "lt-bd" }, n => !IsHarbor(n)),
            new City("Thundoria_Harbor", "Thundoria Harbor", new[] { "lt-bd" }, IsHarbor),
            new City("Glacier_Isle", "Glacier Isle", new[] { "bl-bd", "dd_bd" }),
            new City("Chaldea_Haven", "Chaldea Haven", new[] { "cc-bd" }),
            new City("Solace_Haven", "Solace Haven", new[] { "sl-bd" }),
            new City("Abandon_Mine_Haven", "Abandon Mine Haven", new[] { "mz-bd" }),
            new City("Valhalla_Haven", "Valhalla Haven", new[] { "kyjj-bd" }),
            new City("Andes_Forest_Haven", "Andes Forest Haven", new string[0]),
            new City("Rockery_Haven", "Rockery Haven", new string[0]),
            new City("Zephyr_Isle", "Zephyr Isle", new string[0]),
            new City("Outlaw_Isle", "Outlaw Isle", new string[0]),
        };

        static Dictionary<string, string> englishNames;
        static bool IsHarbor(string model)
        {
            if (englishNames == null) LoadNames();
            return englishNames.TryGetValue(model, out var n) && (n.Contains("Harbo") || n.Contains("Navy") || n.Contains("Dock") || n.Contains("Ship") || n.Contains("Pier"));
        }

        static void LoadNames()
        {
            englishNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = @"E:\NEW SV\Client\scripts\table\sceneobjinfo.txt";
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path, Encoding.GetEncoding(28591)))
            {
                var c = line.Split('\t');
                if (c.Length > 2 && !line.StartsWith("//")) englishNames[Path.GetFileNameWithoutExtension(c[1])] = c[2];
            }
        }

        public static List<Terrain> Build(out int placed)
        {
            placed = 0;
            var terrains = new List<Terrain>();
            var root = GameObject.Find("GarnerCities") ?? new GameObject("GarnerCities");
            foreach (var child in root.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var npcByRegion = ReadNpcCenters();
            var matCache = new HashSet<Material>();
            foreach (var city in Cities)
            {
                if (!npcByRegion.TryGetValue(city.Region, out var pts) || pts.Count == 0) continue;
                var min = new Vector2(pts.Min(p => p.x), pts.Min(p => p.y)); var max = new Vector2(pts.Max(p => p.x), pts.Max(p => p.y));
                var center = (min + max) / 2f;
                int sx = Mathf.Clamp(Mathf.RoundToInt(center.x) - TileSize / 2, 0, 4096 - TileSize), sz = Mathf.Clamp(Mathf.RoundToInt(center.y) - TileSize / 2, 0, 4096 - TileSize);
                var tgo = GarnerTerrainImporter.BuildTile("Garner_" + city.Name, $"Assets/ImportedClient/Garner_{city.Name}.asset", sx, sz, TileSize);
                tgo.transform.SetParent(root.transform, true);
                var terrain = tgo.GetComponent<Terrain>(); terrains.Add(terrain);
                var surface = tgo.GetComponent<NavMeshSurface>() ?? tgo.AddComponent<NavMeshSurface>();
                surface.layerMask = LayerMask.GetMask("Ground", "Terrain"); surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

                var models = ListModels(city);
                if (models.Count > 0)
                {
                    var holder = new GameObject("Scenery_" + city.Name); holder.transform.SetParent(root.transform);
                    var plaza = pts.Select(p => new Vector3(p.x - GarnerTerrainImporter.OriginX, 0, GarnerTerrainImporter.OriginZ - p.y)).ToArray();
                    placed += Pack(models, terrain, holder.transform, plaza, matCache);
                }
                surface.BuildNavMesh();
            }
            return terrains;
        }

        // Nature/landmark models (rocks, volcano, whirlpool, dragon lair...) packed around the Argent city area.
        public static int PlaceOutskirts(Terrain argent)
        {
            var root = GameObject.Find("GarnerCities") ?? new GameObject("GarnerCities");
            var old = root.transform.Find("Scenery_ArgentOutskirts"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var holder = new GameObject("Scenery_ArgentOutskirts"); holder.transform.SetParent(root.transform);
            var models = new List<GameObject>();
            foreach (string prefix in new[] { "nml-bd", "hlcx" })
                foreach (string path in Directory.GetFiles(SceneDir, prefix + "*.obj").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                    if (model != null) models.Add(model);
                }
            var avoid = new List<Vector3>();
            for (int gx = -140; gx <= 140; gx += 20) for (int gz = -140; gz <= 140; gz += 20) avoid.Add(new Vector3(gx, 0, gz));
            int n = Pack(models, argent, holder.transform, avoid.ToArray(), new HashSet<Material>(), 12f, 20f, 20f, 60f);
            Debug.Log($"[Outskirts] models={models.Count} placed={n} low={LowSkips} avoid={AvoidSkips} size={SizeSkips}");
            return n;
        }

        static Dictionary<string, List<Vector2>> ReadNpcCenters()
        {
            var result = new Dictionary<string, List<Vector2>>();
            string file = "Assets/ImportedClient/ServerData/garner/garnernpc.txt";
            if (!File.Exists(file)) return result;
            foreach (var line in File.ReadAllLines(file, Encoding.GetEncoding(28591)))
            {
                if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line)) continue;
                var c = line.Split('\t');
                if (c.Length < 9) continue;
                var xy = c[5].Split(',');
                if (xy.Length < 2 || !float.TryParse(xy[0], out float x) || !float.TryParse(xy[1], out float z)) continue;
                if (!result.TryGetValue(c[8].Trim(), out var list)) result[c[8].Trim()] = list = new List<Vector2>();
                list.Add(new Vector2(x / 100f, z / 100f));
            }
            return result;
        }

        static List<GameObject> ListModels(City city)
        {
            var list = new List<GameObject>();
            foreach (string prefix in city.Prefixes)
                foreach (string path in Directory.GetFiles(SceneDir, prefix + "*.obj").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (city.Filter != null && !city.Filter(Path.GetFileNameWithoutExtension(path))) continue;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path.Replace('\\', '/'));
                    if (model != null) list.Add(model);
                }
            return list;
        }

        // Greedy packing: each model takes the free land slot nearest the focus; slots near NPCs, on water or overlapping are skipped.
        public static int Pack(List<GameObject> models, Terrain terrain, Transform parent, Vector3[] avoid, HashSet<Material> materials, float margin = 4f, float minX = 8f, float minZ = 8f, float maxSize = 120f)
        {
            Vector3 origin = terrain.transform.position;
            float width = terrain.terrainData.size.x, depth = terrain.terrainData.size.z;
            var focus = avoid.Length > 0 ? new Vector3(avoid.Average(a => a.x), 0, avoid.Average(a => a.z)) : origin + new Vector3(width / 2f, 0, depth / 2f);
            float step = Mathf.Max(6f, margin + 2f);
            var candidates = new List<Vector3>();
            for (float cx = minX; cx < width - minX; cx += step) for (float cz = minZ; cz < depth - minZ; cz += step) candidates.Add(origin + new Vector3(cx, 0, cz));
            candidates.Sort((a, b) => (a - focus).sqrMagnitude.CompareTo((b - focus).sqrMagnitude));
            var placedRects = new List<Rect>();
            bool Land(Vector3 p) { float y = terrain.SampleHeight(p) + origin.y; return y >= -1.2f; }
            int count = 0;
            foreach (var model in models)
            {
                var size = ModelSize(model);
                if (size.x > maxSize || size.z > maxSize) { SizeSkips++; continue; }
                float hx = size.x / 2f, hz = size.z / 2f;
                Vector3 pos = default; bool found = false;
                foreach (var cand in candidates)
                {
                    if (cand.x - hx < origin.x + minX || cand.x + hx > origin.x + width - minX || cand.z - hz < origin.z + minZ || cand.z + hz > origin.z + depth - minZ) continue;
                    if (avoid.Any(a => Mathf.Abs(a.x - cand.x) < hx + 5f && Mathf.Abs(a.z - cand.z) < hz + 5f)) { AvoidSkips++; continue; }
                    var rect = new Rect(cand.x - hx - margin / 2f, cand.z - hz - margin / 2f, size.x + margin, size.z + margin);
                    if (placedRects.Any(r => r.Overlaps(rect))) continue;
                    if (!Land(cand) || !Land(cand + new Vector3(hx, 0, hz)) || !Land(cand + new Vector3(-hx, 0, hz)) || !Land(cand + new Vector3(hx, 0, -hz)) || !Land(cand + new Vector3(-hx, 0, -hz))) { LowSkips++; continue; }
                    pos = cand; placedRects.Add(rect); found = true; break;
                }
                if (!found) continue;
                pos.y = terrain.SampleHeight(pos) + origin.y;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
                go.name = model.name; go.transform.position = pos;
                var renderers = go.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                    go.transform.position += new Vector3(pos.x - b.center.x, 0, pos.z - b.center.z);
                    b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                    var col = go.AddComponent<BoxCollider>(); col.center = go.transform.InverseTransformPoint(b.center); col.size = b.size;
                    var obstacle = go.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box; obstacle.size = col.size; obstacle.center = col.center; obstacle.carving = true;
                }
                foreach (var r in renderers) foreach (var m in r.sharedMaterials)
                    if (m != null && materials.Add(m)) { var t = m.mainTexture; m.shader = Shader.Find("Universal Render Pipeline/Lit"); m.mainTexture = t; EditorUtility.SetDirty(m); }
                count++;
            }
            return count;
        }

        public static int LowSkips, AvoidSkips, SizeSkips;
        static readonly Dictionary<GameObject, Vector3> sizeCache = new Dictionary<GameObject, Vector3>();
        static Vector3 ModelSize(GameObject model)
        {
            if (sizeCache.TryGetValue(model, out var cached)) return cached;
            var renderers = model.GetComponentsInChildren<Renderer>();
            Vector3 size = new Vector3(6, 4, 6);
            if (renderers.Length > 0) { var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds); size = b.size; }
            size = new Vector3(Mathf.Max(size.x, 2f), size.y, Mathf.Max(size.z, 2f));
            return sizeCache[model] = size;
        }
    }
}
