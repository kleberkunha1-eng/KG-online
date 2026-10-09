using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TOP.Core;
using TOP.Systems;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace TOP.EditorTools
{
    // Places NPCs and monster zones at the real coordinates from the original Garner server tables.
    public static class GarnerWorldPopulator
    {
        const string DataDir = "Assets/ImportedClient/ServerData/garner/";
        const string ModelDir = "Assets/ImportedClient/Models/model/character/";

        static Vector3 ToWorld(float cmX, float cmZ) => new Vector3(cmX / 100f - GarnerTerrainImporter.OriginX, 0, GarnerTerrainImporter.OriginZ - cmZ / 100f);

        static bool TryPoint(string s, out float x, out float z)
        {
            x = z = 0; var p = s.Split(',');
            return p.Length == 2 && float.TryParse(p[0], out x) && float.TryParse(p[1], out z);
        }

        // One enemy prefab per original monster model: Mob_Slime logic with the converted skinned visual.
        static GameObject MobVariant(GameObject baseEnemy, string monsterId, Dictionary<int, int> frameworks)
        {
            foreach (var part in new[] { monsterId })
            {
                if (!int.TryParse(part, out int id) || !frameworks.TryGetValue(id, out int fw)) continue;
                string visualPath = "Assets/Prefabs/Skinned/" + fw.ToString("0000") + ".prefab";
                var visual = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
                if (visual == null) continue;
                string variantPath = "Assets/Prefabs/Enemy/Variants/Mob_" + fw.ToString("0000") + ".prefab";
                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
                if (existing != null) return existing;
                Directory.CreateDirectory("Assets/Prefabs/Enemy/Variants");
                var contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(baseEnemy));
                foreach (var child in contents.transform.Cast<Transform>().ToArray())
                    if (child.name != "SelectionIndicator" && child.GetComponentInChildren<Renderer>(true) != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
                foreach (var a in contents.GetComponentsInChildren<Animator>(true))
                {
                    foreach (var na in a.GetComponents<Mirror.NetworkAnimator>()) UnityEngine.Object.DestroyImmediate(na);
                    foreach (var ev in a.GetComponents<EnemyAnimationEvents>()) UnityEngine.Object.DestroyImmediate(ev);
                    UnityEngine.Object.DestroyImmediate(a);
                }
                var model = (GameObject)PrefabUtility.InstantiatePrefab(visual, contents.transform);
                model.name = "Visual";
                var saved = PrefabUtility.SaveAsPrefabAsset(contents, variantPath);
                PrefabUtility.UnloadPrefabContents(contents);
                return saved;
            }
            return null;
        }

        public static void Populate(GameObject terrainObject, GameObject enemyPrefab, IList<Terrain> extraTerrains = null)
        {
            var allTerrains = new List<Terrain> { terrainObject.GetComponent<Terrain>() };
            if (extraTerrains != null) allTerrains.AddRange(extraTerrains);
            Terrain FindTerrain(Vector3 p)
            {
                foreach (var t in allTerrains)
                {
                    var tp0 = t.transform.position; var sz = t.terrainData.size;
                    if (p.x >= tp0.x && p.z >= tp0.z && p.x <= tp0.x + sz.x && p.z <= tp0.z + sz.z) return t;
                }
                return null;
            }
            var root = GameObject.Find("GarnerNPCs") ?? new GameObject("GarnerNPCs");
            var configuredNpcs = terrainObject.scene.GetRootGameObjects()
                .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<TOP.NPC.NPCInteractable>(true)).ToArray();
            foreach (var child in root.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var latin = Encoding.GetEncoding(28591);
            var frameworks = new Dictionary<int, int>();
            foreach (var row in File.ReadAllLines("Assets/ImportedClient/ServerData/characterinfo.txt", latin))
            {
                var cols = row.Split('\t');
                if (cols.Length > 5 && int.TryParse(cols[0], out int rid) && int.TryParse(cols[5], out int fw)) frameworks[rid] = fw;
            }
            int placedNpc = 0, missing = 0;
            string npcFile = DataDir + "garnernpc.txt";
            if (File.Exists(npcFile))
            {
                foreach (var line in File.ReadAllLines(npcFile, latin))
                {
                    if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line)) continue;
                    var c = line.Split('\t');
                    if (c.Length < 8 || !int.TryParse(c[3], out int chrId) || !TryPoint(c[5], out float x, out float z)) continue;
                    string originalName = "NPC_" + c[0] + "_" + c[1];
                    if (configuredNpcs.Any(npc => npc.NpcName == c[1] && npc.transform.Find(originalName) != null))
                    { placedNpc++; continue; }
                    var pos = ToWorld(x, z);
                    var terrain = FindTerrain(pos);
                    if (terrain == null) continue;
                    pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y;
                    int modelId = frameworks.TryGetValue(chrId, out int fwId) ? fwId : chrId;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + modelId.ToString("0000") + "000000.obj");
                    if (model == null) { missing++; continue; }
                    var npc = (GameObject)PrefabUtility.InstantiatePrefab(model);
                    npc.name = "NPC_" + c[0] + "_" + c[1];
                    npc.transform.SetParent(root.transform);
                    float.TryParse(c[7], out float angle);
                    npc.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, angle, 0));
                    var renderers = npc.GetComponentsInChildren<Renderer>();
                    foreach (var r in renderers) foreach (var m in r.sharedMaterials) if (m != null) { var t = m.mainTexture; m.shader = Shader.Find("Universal Render Pipeline/Lit"); m.mainTexture = t; EditorUtility.SetDirty(m); }
                    if (renderers.Length > 0)
                    {
                        var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                        var col = npc.AddComponent<CapsuleCollider>();
                        col.center = npc.transform.InverseTransformPoint(bounds.center);
                        col.height = Mathf.Max(bounds.size.y, 1f); col.radius = Mathf.Max(Mathf.Min(bounds.size.x, bounds.size.z) * .4f, .3f);
                        var obstacle = npc.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Capsule; obstacle.carving = true;
                        obstacle.center = col.center; obstacle.height = col.height; obstacle.radius = col.radius;
                        var label = new GameObject("Label"); label.transform.SetParent(npc.transform, false);
                        label.transform.position = new Vector3(bounds.center.x, bounds.max.y + .6f, bounds.center.z);
                        var tm = label.AddComponent<TextMesh>(); tm.text = c[1]; tm.anchor = TextAnchor.LowerCenter; tm.characterSize = .045f; tm.fontSize = 48; tm.color = Color.yellow;
                        label.AddComponent<WorldLabelBillboard>();
                    }
                    placedNpc++;
                }
            }

            var zonesRoot = GameObject.Find("GarnerSpawnZones") ?? new GameObject("GarnerSpawnZones");
            foreach (var child in zonesRoot.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            int zones = 0;
            string spawnFile = DataDir + "garnerChaSpn.txt";
            if (File.Exists(spawnFile) && enemyPrefab != null)
            {
                foreach (var line in File.ReadAllLines(spawnFile, latin))
                {
                    if (line.StartsWith("//") || string.IsNullOrWhiteSpace(line)) continue;
                    var c = line.Split('\t');
                    if (c.Length < 6 || !TryPoint(c[1], out float x1, out float z1) || !TryPoint(c[2], out float x2, out float z2)) continue;
                    var center = ToWorld((x1 + x2) / 2f, (z1 + z2) / 2f);
                    var terrain = FindTerrain(center);
                    if (terrain == null) continue;
                    var tp = terrain.transform.position;
                    center.y = terrain.SampleHeight(center) + tp.y;
                    var go = new GameObject("Zone_" + c[0] + (c.Length > 10 ? "_" + c[10].Trim() : ""));
                    go.transform.SetParent(zonesRoot.transform); go.transform.position = center;
                    var ids = c[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    var counts = c.Length > 5 ? c[5].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                    for (int k = 0; k < ids.Length; k++)
                    {
                        var holder = new GameObject("Spawner_" + ids[k].Trim());
                        holder.transform.SetParent(go.transform, false);
                        var spawner = holder.AddComponent<EnemySpawner>();
                        var so = new SerializedObject(spawner);
                        var zonePrefab = MobVariant(enemyPrefab, ids[k].Trim(), frameworks) ?? enemyPrefab;
                        int cnt = k < counts.Length && int.TryParse(counts[k].Trim(), out int cv) ? cv : 4;
                        so.FindProperty("enemyPrefab").objectReferenceValue = zonePrefab;
                        so.FindProperty("spawnCenter").objectReferenceValue = go.transform;
                        so.FindProperty("maxEnemiesPerSpawner").intValue = Mathf.Clamp(cnt / 3, 2, 4);
                        so.FindProperty("spawnInterval").floatValue = 6f;
                        so.FindProperty("spawnRadius").floatValue = Mathf.Clamp(Mathf.Min(Mathf.Abs(x2 - x1), Mathf.Abs(z2 - z1)) / 200f, 5f, 25f);
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                    zones++;
                }
            }
            File.WriteAllText("Tools/garner-population-report.txt", $"NPCs placed at original server coordinates: {placedNpc} (model missing: {missing})\nMonster zones inside imported terrain: {zones}\n");
        }
    }
}


