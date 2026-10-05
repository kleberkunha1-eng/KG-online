using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TOP.EditorTools
{
    // Places every scene object of the original garner.obj (position, height offset and yaw) on the imported terrain tiles.
    public static class GarnerObjectPlacer
    {
        const string Table = "Assets/ImportedClient/Map/garner.objects.txt";
        const string SceneDir = "Assets/ImportedClient/Models/model/scene/";

        const float YawSign = -1f; // validated against the original minimap: the original yaw turns opposite to Unity

        public static string Place(IEnumerable<Terrain> terrains)
        {
            var tiles = terrains.Where(t => t != null).ToList();
            var root = GameObject.Find("GarnerObjects") ?? new GameObject("GarnerObjects");
            foreach (var c in root.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(c.gameObject);
            var holders = new Dictionary<Terrain, Transform>();
            foreach (var t in tiles) { var h = new GameObject("Objects_" + t.name); h.transform.SetParent(root.transform); holders[t] = h.transform; }

            var prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            var fixedMats = new HashSet<Material>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int placed = 0, outside = 0, missing = 0, effects = 0;
            var missingNames = new HashSet<string>();

            foreach (var line in File.ReadLines(Table))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var c = line.Split('\t');
                if (c[0] != "0") { effects++; continue; }
                float wx = int.Parse(c[2]) / 100f - GarnerTerrainImporter.OriginX, wz = GarnerTerrainImporter.OriginZ - int.Parse(c[3]) / 100f;
                Terrain tile = null;
                foreach (var t in tiles)
                {
                    var p = t.transform.position; var s = t.terrainData.size;
                    if (wx >= p.x && wx < p.x + s.x && wz >= p.z && wz < p.z + s.z) { tile = t; break; }
                }
                if (tile == null) { outside++; continue; }
                if (!prefabs.TryGetValue(c[1], out var model))
                {
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(SceneDir + c[1] + ".obj");
                    prefabs[c[1]] = model;
                }
                if (model == null) { missing++; missingNames.Add(c[1]); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
                go.name = c[1];
                go.transform.SetParent(holders[tile], false);
                var pos = new Vector3(wx, 0, wz);
                pos.y = tile.SampleHeight(pos) + tile.transform.position.y + int.Parse(c[4]) / 100f;
                // Unity's OBJ import maps (x,y,z) to (-x,z,y); the original Y axis points south, so the mesh needs a 180 degree turn and yaw maps directly.
                
                go.transform.SetPositionAndRotation(pos, Quaternion.identity);
                go.isStatic = true;

                var renderers = go.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers)
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || !fixedMats.Add(m)) continue;
                        var tex = m.mainTexture; m.shader = lit; m.mainTexture = tex; m.SetFloat("_Smoothness", 0); EditorUtility.SetDirty(m);
                    }
                if (renderers.Length > 0)
                {
                    var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                    // Colisão vem da grade de bloqueio original (WorldBlockGrid), não de caixas nos modelos.
                }
                go.transform.rotation = Quaternion.Euler(0, YawSign * float.Parse(c[5]) + 180f, 0);
                placed++;
            }
            string report = $"placed={placed} outsideTiles={outside} missingModels={missing} ({string.Join(",", missingNames)}) effectsPending={effects}";
            Debug.Log("[GarnerObjectPlacer] " + report);
            return report;
        }
    }
}
