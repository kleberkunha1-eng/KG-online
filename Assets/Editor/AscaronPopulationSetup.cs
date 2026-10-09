using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Mirror;
using TMPro;
using TOP.Core;
using TOP.Data;
using TOP.NPC;

namespace TOP.EditorTools
{
    // Popula o mapa Garner/Argent City com os NPCs (npclist.txt) e monstros (monsterlist.txt)
    // do cliente original, usando as coordenadas exatas convertidas pelo mesmo sistema de
    // origem usado pelo GarnerTerrainImporter (OriginX/OriginZ).
    // Um arquivo de requisicao permite disparar isso sem interacao manual no Editor (mesmo padrao do PlayableWorldSetup).
    [InitializeOnLoad]
    public static class AscaronPopulationSetup
    {
        const string Request = "Tools/populate-ascaron.request";

        static AscaronPopulationSetup() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                return;
            }
            File.Delete(Request);
            try { Populate(); }
            catch (Exception e) { File.WriteAllText("Tools/populate-ascaron.error.txt", e.ToString()); Debug.LogException(e); }
        }

        // Mesma origem usada pelo GarnerTerrainImporter: original (OriginX,OriginZ) = Unity (0,0).
        // O eixo Y original cresce para o sul, por isso Unity Z = OriginZ - y.
        const int OriginX = 2218, OriginZ = 2782;
        // Regiao da tile de terreno importada (StartX..StartX+Size, StartZ..StartZ+Size).
        const int TileMinX = 1706, TileMaxX = 1706 + 1024, TileMinZ = 2270, TileMaxZ = 2270 + 1024;

        static Vector3 ToWorld(double x, double y, Terrain terrain)
        {
            float wx = (float)(x - OriginX);
            float wz = (float)(OriginZ - y);
            float wy = terrain != null ? terrain.SampleHeight(new Vector3(wx, 0, wz)) + terrain.transform.position.y : 0f;
            return new Vector3(wx, wy, wz);
        }

        static bool InsideTile(double x, double y) => x >= TileMinX && x <= TileMaxX && y >= TileMinZ && y <= TileMaxZ;

        static Mesh VisualMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        public static void AuditExistingNpcVisualsBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("NPC audit requires edit mode.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            var roots = scene.GetRootGameObjects();
            var transforms = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var renderers = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => VisualMesh(renderer) != null).ToArray();
            var lines = roots.Select(root => "ROOT " + root.name).ToList();
            foreach (var npc in roots.SelectMany(root => root.GetComponentsInChildren<NPCInteractable>(true)))
            {
                lines.Add("NPC " + npc.NpcId + " | " + npc.NpcName + " | " + npc.transform.position);
                string expected = "NPC_" + (int.Parse(npc.NpcId) - 1) + "_" + npc.NpcName;
                var original = transforms.Where(transform => transform.name == expected).ToArray();
                lines.Add("  EXACT " + expected + " | objects=" + original.Length + " | renderers="
                    + (original.Length == 1 ? original[0].GetComponentsInChildren<Renderer>(true).Count(renderer => VisualMesh(renderer) != null) : 0));
                var nearest = renderers.Where(renderer => !renderer.transform.IsChildOf(npc.transform))
                    .Select(renderer => (renderer, distance: Vector2.Distance(new Vector2(renderer.bounds.center.x, renderer.bounds.center.z),
                        new Vector2(npc.transform.position.x, npc.transform.position.z))))
                    .OrderBy(entry => entry.distance).Take(3);
                foreach (var entry in nearest)
                {
                    var mesh = VisualMesh(entry.renderer);
                    lines.Add("  NEAR " + entry.distance.ToString("F3") + " | " + entry.renderer.name
                        + " | parent " + entry.renderer.transform.parent?.name + " | mesh " + AssetDatabase.GetAssetPath(mesh));
                }
            }
            File.WriteAllLines("Tools/npc-existing-visuals-audit.txt", lines);
            Debug.Log("[NPC Audit] " + lines.Count + " records; scene not modified.");
        }

        [MenuItem("TOP/World/Bind Services to Existing Original NPCs")]
        public static void BindExistingNpcVisualsBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before binding NPC visuals.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            var transforms = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            var npcs = transforms.Select(transform => transform.GetComponent<NPCInteractable>()).Where(npc => npc != null).ToArray();
            var records = File.ReadAllLines("Assets/ImportedClient/ServerData/garner/garnernpc.txt", System.Text.Encoding.GetEncoding(28591));
            var active = records.Where(line => !line.StartsWith("//") && !string.IsNullOrWhiteSpace(line)).Select(line => line.Split('\t')).ToArray();
            var bindings = new System.Collections.Generic.List<(NPCInteractable npc, Transform visual, string configuration, ulong sceneId)>();
            var disabled = new System.Collections.Generic.List<NPCInteractable>();
            foreach (var npc in npcs)
            {
                if (!int.TryParse(npc.NpcId, out int id)) throw new InvalidDataException("NPC ID is not an original catalog ID: " + npc.NpcId);
                var rows = active.Where(row => row[1] == npc.NpcName && int.Parse(row[0]) + 1 == id).ToArray();
                if (rows.Length == 0 && records.Any(line => line.StartsWith("//") && line.Split('\t').Length > 1
                    && line.Split('\t')[1] == npc.NpcName))
                { disabled.Add(npc); continue; }
                if (rows.Length != 1) throw new InvalidDataException("Original placement missing or ambiguous: " + npc.NpcName);
                string name = "NPC_" + rows[0][0] + "_" + rows[0][1];
                var originals = transforms.Where(transform => transform.name == name).ToArray();
                if (originals.Length != 1 || !originals[0].GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => VisualMesh(renderer) != null && AssetDatabase.GetAssetPath(VisualMesh(renderer))
                        .StartsWith("Assets/ImportedClient/Models/model/character/", StringComparison.Ordinal)))
                    throw new InvalidDataException("Existing original map model missing or ambiguous: " + name);
                if (originals[0].GetComponent<NetworkIdentity>() != null)
                    throw new InvalidDataException("Original model already has a separate network identity: " + name);
                var filter = npc.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name != "Capsule" && filter.sharedMesh.name != "Sphere")
                    throw new InvalidDataException("Custom NPC mesh would be overwritten: " + npc.NpcName);
                bindings.Add((npc, originals[0], EditorJsonUtility.ToJson(npc), npc.GetComponent<NetworkIdentity>().sceneId));
            }
            string backup = Path.Combine("Library", "TOPAutosave", "NpcBindings", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            File.Copy(scene.path, backup, false);
            var report = new System.Collections.Generic.List<string> { "Scene backup: " + backup };
            foreach (var binding in bindings)
            {
                var npc = binding.npc;
                if (!binding.visual.IsChildOf(npc.transform))
                {
                    npc.transform.localScale = Vector3.one;
                    npc.transform.SetPositionAndRotation(binding.visual.position, binding.visual.rotation);
                    binding.visual.SetParent(npc.transform, true);
                }
                var renderer = npc.GetComponent<MeshRenderer>();
                var filter = npc.GetComponent<MeshFilter>();
                if (renderer != null) UnityEngine.Object.DestroyImmediate(renderer);
                if (filter != null) UnityEngine.Object.DestroyImmediate(filter);
                var placeholderCollider = npc.GetComponent<CapsuleCollider>();
                if (placeholderCollider != null) UnityEngine.Object.DestroyImmediate(placeholderCollider);
                var nameplate = npc.transform.Find("Nameplate");
                if (nameplate != null && nameplate.GetComponent<TextMeshPro>() != null) nameplate.gameObject.SetActive(false);
                if (EditorJsonUtility.ToJson(npc) != binding.configuration || npc.GetComponent<NetworkIdentity>().sceneId != binding.sceneId)
                    throw new InvalidDataException("NPC service configuration/network identity changed during binding: " + npc.NpcName);
                report.Add("BOUND " + npc.NpcId + " | " + npc.NpcName + " | " + binding.visual.name
                    + " | existing model/collider/materials retained; service and sceneId unchanged");
                EditorUtility.SetDirty(npc.gameObject);
            }
            foreach (var npc in disabled)
            {
                var disabledRoot = npc.transform.parent.name == "OriginalDisabledNPCs"
                    ? npc.transform.parent : npc.transform.parent.Find("OriginalDisabledNPCs");
                if (disabledRoot == null)
                {
                    disabledRoot = new GameObject("OriginalDisabledNPCs").transform;
                    disabledRoot.SetParent(npc.transform.parent, false);
                }
                // Mirror activates scene identities, but deliberately skips identities under inactive parents.
                disabledRoot.gameObject.SetActive(false);
                npc.transform.SetParent(disabledRoot, true);
                npc.gameObject.SetActive(false);
                report.Add("INACTIVE " + npc.NpcId + " | " + npc.NpcName + " | placement commented out in original Garner source; placeholder retained inactive, not deleted");
            }
            foreach (string cursor in new[] { "mouseon", "chat", "drag", "attack" })
            {
                var importer = AssetImporter.GetAtPath("Assets/Resources/PKOCursors/" + cursor + ".png") as TextureImporter;
                if (importer == null) throw new InvalidDataException("Original cursor image missing: " + cursor);
                importer.textureType = TextureImporterType.Cursor;
                importer.isReadable = true;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.Add("Active original bindings: " + bindings.Count + "; inactive original-disabled NPCs: " + disabled.Count);
            File.WriteAllLines("Tools/npc-visual-bindings-results.txt", report);
            Debug.Log("[NPC Bindings] " + bindings.Count + " original map models bound; no replacement characters created.");
        }

        [MenuItem("TOP/World/Populate Ascaron NPCs and Monsters")]
        public static void Populate()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");

            var terrainGo = GameObject.Find("Garner_Argent");
            Terrain terrain = terrainGo != null ? terrainGo.GetComponent<Terrain>() : Terrain.activeTerrain;
            if (terrain == null) throw new Exception("Garner terrain not found. Run TOP/Configure playable world first.");

            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy/Prefabs/Enemy_Slime.prefab");
            if (enemyPrefab == null) throw new Exception("Assets/Enemy/Prefabs/Enemy_Slime.prefab not found.");

            var existingRoot = GameObject.Find("AscaronPopulation");
            var root = existingRoot != null ? existingRoot : new GameObject("AscaronPopulation");
            var npcBranch = root.transform.Find("NPCs");
            if (npcBranch == null) { npcBranch = new GameObject("NPCs").transform; npcBranch.SetParent(root.transform, false); }
            var monsterBranch = root.transform.Find("Monsters");
            if (monsterBranch == null) { monsterBranch = new GameObject("Monsters").transform; monsterBranch.SetParent(root.transform, false); }
            var existingNpcs = npcBranch.GetComponentsInChildren<NPCInteractable>(true).Select(npc => npc.NpcId).ToHashSet();

            int npcCount = 0;
            foreach (var npc in PkoTables.Npcs.Where(n => n.Location == "Argent City" && InsideTile(n.X, n.Y)))
            {
                if (existingNpcs.Contains(npc.Id.ToString())) { npcCount++; continue; }
                var go = BuildNpc(npc, ToWorld(npc.X, npc.Y, terrain));
                go.transform.SetParent(npcBranch, true);
                npcCount++;
            }

            int monsterCount = 0;
            foreach (var mon in PkoTables.WorldMonsters.Where(m => m.Continent == "Ascaron" && InsideTile(m.X, m.Y)))
            {
                string name = "Monster_" + mon.Id + "_" + Sanitize(mon.Name);
                if (monsterBranch.Find(name) != null) { monsterCount++; continue; }
                Vector3 pos = ToWorld(mon.X, mon.Y, terrain);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, monsterBranch);
                go.name = name;
                go.transform.position = pos;
                ConfigureMonster(go, mon);
                monsterCount++;
            }

            EditorUtility.SetDirty(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            File.WriteAllText("Tools/populate-ascaron-report.txt",
                $"NPCs placed (Argent City): {npcCount}\nMonsters placed (Ascaron, inside imported tile): {monsterCount}\n" +
                "Coordinates converted 1:1 from the original client tables (npclist.txt / monsterlist.txt) using the same origin as GarnerTerrainImporter.\n" +
                "NPC services reuse original models already placed by GarnerWorldPopulator. Existing NPC configuration and monster instances are retained; no placeholder NPC geometry is generated.\n");
            Debug.Log($"[AscaronPopulationSetup] {npcCount} NPCs e {monsterCount} monstros adicionados em Argent City.");
        }

        static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "NPC" : s.Replace(" ", "_").Replace("-", "_").Replace("'", "");

        static GameObject BuildNpc(PkoNpc npc, Vector3 position)
        {
            string originalName = "NPC_" + (npc.Id - 1) + "_" + npc.Name;
            var original = GameObject.Find(originalName);
            if (original == null || original.GetComponentsInChildren<Renderer>(true).All(renderer => VisualMesh(renderer) == null))
                throw new InvalidDataException("Place the verified original map NPC before creating its services: " + originalName);
            var go = new GameObject("NPC_" + npc.Id + "_" + Sanitize(npc.Name));
            go.name = "NPC_" + npc.Id + "_" + Sanitize(npc.Name);
            go.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            original.transform.SetParent(go.transform, true);

            var npcType = InferType(npc.Name);
            var identity = go.AddComponent<NetworkIdentity>();
            identity.serverOnly = false;

            var interactable = go.AddComponent<NPCInteractable>();
            var so = new SerializedObject(interactable);
            so.FindProperty("npcId").stringValue = npc.Id.ToString();
            so.FindProperty("npcName").stringValue = npc.Name;
            so.FindProperty("npcType").enumValueIndex = (int)npcType;
            so.FindProperty("interactionRange").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            return go;
        }

        static NPCType InferType(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("hairstylist") || n.Contains("hair")) return NPCType.Hairdresser;
            if (n.Contains("blacksmith") || n.Contains("forge")) return NPCType.Blacksmith;
            if (n.Contains("banker")) return NPCType.Banker;
            if (n.Contains("teleport")) return NPCType.Teleporter;
            if (n.Contains("physican") || n.Contains("physician") || n.Contains("healer") || n.Contains("priest")) return NPCType.Healer;
            if (n.Contains("guild")) return NPCType.GuildMaster;
            if (n.Contains("skill") || n.Contains("trainer")) return NPCType.SkillMaster;
            if (n.Contains("stable") || n.Contains("mount")) return NPCType.StableMaster;
            if (n.Contains("trader") || n.Contains("merchant") || n.Contains("secretary")) return NPCType.Merchant;
            if (n.Contains("guard") || n.Contains("citizen") || n.Contains("granny") || n.Contains("daniel")) return NPCType.Other;
            return NPCType.QuestGiver;
        }

        static Color ColorForType(NPCType type) => type switch
        {
            NPCType.Merchant => new Color(1f, .82f, .2f),
            NPCType.Blacksmith => new Color(.55f, .35f, .2f),
            NPCType.Banker => new Color(.2f, .7f, .3f),
            NPCType.Teleporter => new Color(.3f, .6f, 1f),
            NPCType.Healer => new Color(1f, .4f, .6f),
            NPCType.GuildMaster => new Color(.6f, .2f, .8f),
            NPCType.SkillMaster => new Color(.8f, .3f, .1f),
            NPCType.StableMaster => new Color(.5f, .4f, .3f),
            NPCType.Hairdresser => new Color(1f, .6f, .85f),
            _ => new Color(.7f, .7f, .75f)
        };

        static void ConfigureMonster(GameObject go, PkoWorldMonster mon)
        {
            var statsComponent = go.GetComponent("EnemyStats");
            if (statsComponent == null) return;

            int level = Mathf.Max(1, mon.Level);
            int maxHealth = 40 + level * 18;
            int attack = 6 + level * 3;
            int defense = 2 + level * 2;
            int expReward = 4 + level * 3;
            int goldReward = 2 + level * 2;

            var so = new SerializedObject(statsComponent);
            SetIfPresent(so, "_enemyName", mon.Name);
            SetIfPresent(so, "_level", level);
            SetIfPresent(so, "_health", maxHealth);
            SetIfPresent(so, "_maxHealth", maxHealth);
            SetIfPresent(so, "_attack", attack);
            SetIfPresent(so, "_defense", defense);
            SetIfPresent(so, "_experienceReward", expReward);
            SetIfPresent(so, "_goldReward", goldReward);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Variação visual simples por nível ate existir um resolvedor de modelo por characterinfo ID.
            float scale = Mathf.Clamp(0.8f + level * 0.02f, 0.8f, 1.6f);
            go.transform.localScale = Vector3.one * scale;
        }

        static void SetIfPresent(SerializedObject so, string field, int value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.intValue = value;
        }

        static void SetIfPresent(SerializedObject so, string field, string value)
        {
            var p = so.FindProperty(field);
            if (p != null) p.stringValue = value;
        }
    }
}
