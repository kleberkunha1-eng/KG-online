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
            if (existingRoot != null) UnityEngine.Object.DestroyImmediate(existingRoot);

            var root = new GameObject("AscaronPopulation");
            var npcRoot = new GameObject("NPCs"); npcRoot.transform.SetParent(root.transform, false);
            var monsterRoot = new GameObject("Monsters"); monsterRoot.transform.SetParent(root.transform, false);

            int npcLayer = LayerMask.NameToLayer("Enemy") >= 0 ? LayerMask.NameToLayer("Enemy") : 0;

            int npcCount = 0;
            foreach (var npc in PkoTables.Npcs.Where(n => n.Location == "Argent City" && InsideTile(n.X, n.Y)))
            {
                var go = BuildNpc(npc, ToWorld(npc.X, npc.Y, terrain));
                go.transform.SetParent(npcRoot.transform, true);
                npcCount++;
            }

            int monsterCount = 0;
            foreach (var mon in PkoTables.WorldMonsters.Where(m => m.Continent == "Ascaron" && InsideTile(m.X, m.Y)))
            {
                Vector3 pos = ToWorld(mon.X, mon.Y, terrain);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, monsterRoot.transform);
                go.name = "Monster_" + mon.Id + "_" + Sanitize(mon.Name);
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
                "NPC visuals are placeholder capsules (no unique 3D model resolver yet for characterinfo IDs). Monsters reuse the Enemy_Slime visual, scaled/tinted per level, with real name/level/stats.\n");
            Debug.Log($"[AscaronPopulationSetup] {npcCount} NPCs e {monsterCount} monstros adicionados em Argent City.");
        }

        static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "NPC" : s.Replace(" ", "_").Replace("-", "_").Replace("'", "");

        static GameObject BuildNpc(PkoNpc npc, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "NPC_" + npc.Id + "_" + Sanitize(npc.Name);
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);

            var npcType = InferType(npc.Name);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = ColorForType(npcType) };

            var identity = go.AddComponent<NetworkIdentity>();
            identity.serverOnly = false;

            var interactable = go.AddComponent<NPCInteractable>();
            var so = new SerializedObject(interactable);
            so.FindProperty("npcId").stringValue = npc.Id.ToString();
            so.FindProperty("npcName").stringValue = npc.Name;
            so.FindProperty("npcType").enumValueIndex = (int)npcType;
            so.FindProperty("interactionRange").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var label = new GameObject("Nameplate", typeof(TextMeshPro));
            label.transform.SetParent(go.transform, false);
            label.transform.localPosition = new Vector3(0, 1.4f, 0);
            var tmp = label.GetComponent<TextMeshPro>();
            tmp.text = npc.Name;
            tmp.fontSize = 3; tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.yellow;

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
