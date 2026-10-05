using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AI;
using Unity.AI.Navigation;
using TMPro;
using Mirror;
using TOP.Player;
using TOP.Core;
using TOP.UI;
using TOP.Bootstrap;

namespace TOP.EditorTools
{
    // An explicit request file lets command-line maintenance use the already open editor.
    [InitializeOnLoad]
    public static class PlayableWorldSetup
    {
        const string Request = "Tools/configure-world.request";
        static PlayableWorldSetup() { EditorApplication.update += Poll; }
        static void Poll()
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                return;
            }
            File.Delete(Request);
            try { Configure(); }
            catch (Exception e) { File.WriteAllText("Tools/configure-world.error.txt", e.ToString()); Debug.LogException(e); }
        }

        static T Ensure<T>(GameObject go) where T : Component { var c=go.GetComponent<T>(); return c != null ? c : go.AddComponent<T>(); }
        static T Find<T>(string name) where T : Component => UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include).FirstOrDefault(x => x.name == name);
        static void Set(UnityEngine.Object owner, string field, UnityEngine.Object value)
        {
            var so = new SerializedObject(owner);
            var p = so.FindProperty(field);
            if (p == null) throw new Exception(owner.GetType().Name + "." + field + " not found");
            p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Number(UnityEngine.Object owner, string field, float value)
        {
            var so = new SerializedObject(owner); var p = so.FindProperty(field);
            if (p.propertyType == SerializedPropertyType.Integer) p.intValue = (int)value; else p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        static Material Material(string name, string texture)
        {
            string path = "Assets/ImportedClient/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texture);
            m.SetFloat("_Smoothness", 0); EditorUtility.SetDirty(m); return m;
        }
        static void Layer(Transform root, int layer) { foreach(var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer; }
        static bool HasSceneModel(string name) => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ImportedClient/Models/model/scene/"+name+".obj") != null;
        static Vector3 OnTerrain(GameObject terrainObject, Vector3 position)
        {
            var terrain = terrainObject.GetComponent<Terrain>();
            if(terrain != null) position.y = terrain.SampleHeight(position)+terrain.transform.position.y;
            return position;
        }

        static void Place(string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect=Find<RectTransform>(name); if(rect==null) return;
            rect.anchorMin=rect.anchorMax=rect.pivot=anchor; rect.anchoredPosition=position; rect.sizeDelta=size;
        }
        static void LayoutHUD()
        {
            var canvas=Find<Canvas>("Canvas");
            if(canvas!=null) { var scaler=canvas.GetComponent<CanvasScaler>(); if(scaler!=null) { scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution=new Vector2(1280,720); scaler.matchWidthOrHeight=.5f; } }
            var top=new Vector2(0,1); var hud=Find<RectTransform>("HUDPanel");
            if(hud!=null)
            {
                var background=hud.Find("StatsBackground");
                if(background==null) { background=new GameObject("StatsBackground",typeof(RectTransform),typeof(Image)).transform; background.SetParent(hud,false); }
                background.SetAsFirstSibling(); background.GetComponent<Image>().color=new Color(.035f,.07f,.11f,.88f); background.GetComponent<Image>().raycastTarget=false;
                Place("StatsBackground",top,new Vector2(12,-12),new Vector2(290,150));
            }
            Place("Txt_Name",top,new Vector2(24,-20),new Vector2(200,24));
            Place("Txt_Level",top,new Vector2(238,-20),new Vector2(60,24));
            int row=0;
            foreach(string prefix in new[]{"HP","MP","SP"})
            {
                Place(prefix+"_Bar",top,new Vector2(24,-52-row*26),new Vector2(264,20));
                Place("Txt_"+prefix,top,new Vector2(28,-52-row*26),new Vector2(254,20));
                var text=Find<TextMeshProUGUI>("Txt_"+prefix); if(text!=null) { text.fontSize=13; text.color=Color.white; text.alignment=TextAlignmentOptions.Center; text.raycastTarget=false; }
                row++;
            }
            Place("Txt_Gold",top,new Vector2(24,-132),new Vector2(250,22));
            foreach(string name in new[]{"Txt_Name","Txt_Level","Txt_Gold"}) { var text=Find<TextMeshProUGUI>(name); if(text!=null) {text.fontSize=16; text.color=new Color(1,.9f,.62f); text.raycastTarget=false;} }
            Place("MenuPanel",top,new Vector2(14,-174),new Vector2(286,30));
            Place("MiniMap_BG",Vector2.one,new Vector2(-16,-16),new Vector2(168,168));
            Place("ChatPanel",Vector2.zero,new Vector2(16,76),new Vector2(330,130));
            var chat=Find<Image>("ChatPanel"); if(chat!=null) chat.color=new Color(.025f,.05f,.08f,.7f);
            var history=Find<TextMeshProUGUI>("Txt_ChatHistory"); if(history!=null) { history.fontSize=13; history.color=Color.white; history.raycastTarget=false; }
            Place("Exp_Bar",new Vector2(.5f,0),new Vector2(0,12),new Vector2(520,14));
            Place("Txt_Exp",new Vector2(.5f,0),new Vector2(0,12),new Vector2(520,16));
            var xp=Find<TextMeshProUGUI>("Txt_Exp"); if(xp!=null) {xp.fontSize=12; xp.color=Color.white; xp.alignment=TextAlignmentOptions.Center; xp.raycastTarget=false;}
            var panel=Find<Image>("HUDPanel"); if(panel!=null) panel.raycastTarget=false;
        }

        [MenuItem("TOP/Configure playable world")]
        public static void Configure()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            Directory.CreateDirectory("Assets/ImportedClient");
            // Preserve all existing open scenes, including unsaved edits.
            var previous = EditorSceneManager.GetSceneManagerSetup();
            foreach (var dirtyScene in Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                .Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).Where(scene => scene.isDirty))
            {
                if (string.IsNullOrEmpty(dirtyScene.path))
                    throw new InvalidOperationException("An open scene has unsaved changes and no file path. Save it in Unity before configuring the world.");
                if (!EditorSceneManager.SaveScene(dirtyScene))
                    throw new InvalidOperationException("Could not save the open scene before configuring the world: " + dirtyScene.path);
            }
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            foreach (var pair in new[] { (8,"Ground"), (9,"Player"), (10,"Enemy"), (11,"Terrain") })
            {
                if (LayerMask.NameToLayer(pair.Item2) >= 0) continue;
                int index = pair.Item1; while (index < 32 && !string.IsNullOrEmpty(layers.GetArrayElementAtIndex(index).stringValue)) index++;
                if (index == 32) throw new Exception("No free layers");
                layers.GetArrayElementAtIndex(index).stringValue = pair.Item2;
            }
            tags.ApplyModifiedPropertiesWithoutUndo();

            string playerPath = "Assets/Prefabs/Player.prefab";
            var player = PrefabUtility.LoadPrefabContents(playerPath);
            var controllerAsset=player.GetComponent<Animator>().runtimeAnimatorController;
            foreach(var clip in controllerAsset.animationClips.Distinct())
            {
                var settings=AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopBlendPositionY=true; settings.keepOriginalPositionY=false; settings.heightFromFeet=true;
                AnimationUtility.SetAnimationClipSettings(clip,settings); EditorUtility.SetDirty(clip);
            }
            var oldVisualRoots=new System.Collections.Generic.HashSet<GameObject>();
            foreach(var renderer in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var root=renderer.transform; while(root.parent!=player.transform && root.parent!=null) root=root.parent;
                if(root!=player.transform) oldVisualRoots.Add(root.gameObject);
            }
            foreach(var oldVisual in oldVisualRoots) UnityEngine.Object.DestroyImmediate(oldVisual);
            var sourceModel=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/1LancePKO/1LancePKO.fbx");
            var visual=UnityEngine.Object.Instantiate(sourceModel,player.transform,false); visual.name="CharacterVisual";
            visual.transform.localPosition=Vector3.zero; visual.transform.localRotation=Quaternion.identity;
            var visualAnimator=visual.GetComponentInChildren<Animator>(); visualAnimator.runtimeAnimatorController=controllerAsset; visualAnimator.applyRootMotion=false;
            Ensure<PlayerVisualAnimationEvents>(visualAnimator.gameObject);
            Ensure<PlayerVisualGrounding>(visualAnimator.gameObject);
            player.GetComponent<Animator>().enabled=false;
            player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Layer(player.transform, LayerMask.NameToLayer("Player"));
            if(player.GetComponent<PlayerRespawn>() == null) player.AddComponent<PlayerRespawn>();
            if(player.GetComponent<PlayerAnimation>() != null) player.GetComponent<Animator>().applyRootMotion = false;
            if(player.GetComponent<Rigidbody>() != null) { player.GetComponent<Rigidbody>().isKinematic = true; player.GetComponent<Rigidbody>().useGravity = false; }
            var follow = player.GetComponent<CameraFollow>();
            if(follow != null) { follow.groundLayers = LayerMask.GetMask("Ground","Terrain"); follow.minVerticalAngle = 15; follow.maxVerticalAngle = 75; }
            File.WriteAllLines("Tools/player-prefab-audit.txt", player.GetComponentsInChildren<Renderer>().Select(r=>r.name+" bounds="+r.bounds+" scale="+r.transform.lossyScale).Concat(player.GetComponentsInChildren<Animator>().Select(a=>"Animator="+a.name+" Avatar="+a.avatar)));
            PrefabUtility.SaveAsPrefabAsset(player, playerPath); PrefabUtility.UnloadPrefabContents(player);

            string enemyPath = "Assets/Prefabs/Enemy/Mob_Slime.prefab";
            var enemy = PrefabUtility.LoadPrefabContents(enemyPath);
            Layer(enemy.transform, LayerMask.NameToLayer("Enemy"));
            if (enemy.GetComponent<NetworkIdentity>() == null) enemy.AddComponent<NetworkIdentity>();
            if (enemy.GetComponent<EnemyStats>() == null) enemy.AddComponent<EnemyStats>();
            if (enemy.GetComponent<EnemyAI>() == null) enemy.AddComponent<EnemyAI>();
            if (enemy.GetComponent<NavMeshAgent>() == null) enemy.AddComponent<NavMeshAgent>();
            if (enemy.GetComponent<Collider>() == null) { var c = enemy.AddComponent<CapsuleCollider>(); c.center = Vector3.up*.6f; c.height=1.2f; c.radius=.5f; }
            if (enemy.GetComponent<NetworkTransformBase>() == null) enemy.AddComponent<NetworkTransformReliable>();
            Number(enemy.GetComponent<EnemyStats>(), "_attack", 35);
            foreach(var animator in enemy.GetComponentsInChildren<Animator>(true)) Ensure<EnemyAnimationEvents>(animator.gameObject);
            foreach(var canvas in enemy.GetComponentsInChildren<Canvas>(true)) UnityEngine.Object.DestroyImmediate(canvas.gameObject);
            Ensure<TOP.UI.EnemyWorldBar>(enemy);
            PrefabUtility.SaveAsPrefabAsset(enemy, enemyPath); PrefabUtility.UnloadPrefabContents(enemy);
            var enemyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(enemyPath);

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
            foreach(var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if(behaviour != null && behaviour.GetType().Name == "KillPlaneTrigger") UnityEngine.Object.DestroyImmediate(behaviour);
            if(UnityEngine.Object.FindAnyObjectByType<TOP.Systems.EffectManager>() == null) new GameObject("EffectManager").AddComponent<TOP.Systems.EffectManager>();
            Directory.CreateDirectory("Assets/Resources/Skills");
            foreach(int id in new[]{9001,9002})
            {
                string path="Assets/Resources/Skills/"+id+".asset";
                var skill=AssetDatabase.LoadAssetAtPath<SkillData>(path);
                if(skill==null) { skill=ScriptableObject.CreateInstance<SkillData>(); AssetDatabase.CreateAsset(skill,path); }
                skill.skillId=id; skill.skillName=id==9001?"Golpe poderoso":"Recuperar";
                skill.targetType=id==9001?SkillTargetType.SingleEnemy:SkillTargetType.Self;
                skill.baseDamage=25; skill.healAmount=id==9002?60:0; skill.mpCost=id==9001?8:15; skill.cooldown=id==9001?3:8; skill.range=3;
                EditorUtility.SetDirty(skill);
            }
            var ground = GameObject.Find("Ground");
            if (ground == null) { ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name="Ground"; }
            ground.layer = LayerMask.NameToLayer("Ground");
            ground.transform.position=Vector3.zero; ground.transform.localScale = new Vector3(16,1,16);
            var terrainMaterial = Material("Ground", "Assets/ImportedClient/Terrain/grass05.png");
            terrainMaterial.mainTextureScale = new Vector2(40,40);
            ground.GetComponent<Renderer>().sharedMaterial = terrainMaterial;
            var surface = Ensure<NavMeshSurface>(ground);
            surface.layerMask = LayerMask.GetMask("Ground","Terrain");
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            ground.SetActive(false);
            var importedTerrain = GarnerTerrainImporter.Build();
            surface = Ensure<NavMeshSurface>(importedTerrain);
            surface.layerMask = LayerMask.GetMask("Ground","Terrain");
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            var spawn = UnityEngine.Object.FindAnyObjectByType<SpawnManager>() ?? new GameObject("SpawnManager").AddComponent<SpawnManager>();
            var point = GameObject.Find("Spawn_Garner") ?? new GameObject("Spawn_Garner"); point.transform.position = new Vector3(0,.65f,0);
            var spawnSO = new SerializedObject(spawn); var points=spawnSO.FindProperty("spawnPoints"); points.arraySize=1; points.GetArrayElementAtIndex(0).objectReferenceValue=point.transform; spawnSO.ApplyModifiedPropertiesWithoutUndo();
            var quickPlay = GameObject.Find("GameSceneDevBootstrap") ?? new GameObject("GameSceneDevBootstrap");
            var quickPlayBootstrap = Ensure<GameSceneDevBootstrap>(quickPlay);
            Set(quickPlayBootstrap, "playerPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"));
            var quickPlaySO = new SerializedObject(quickPlayBootstrap);
            var quickPlayPrefabs = quickPlaySO.FindProperty("networkSpawnPrefabs");
            quickPlayPrefabs.arraySize = 2;
            quickPlayPrefabs.GetArrayElementAtIndex(0).objectReferenceValue = enemyAsset;
            quickPlayPrefabs.GetArrayElementAtIndex(1).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemy/Prefabs/Enemy_Slime.prefab");
            quickPlaySO.ApplyModifiedPropertiesWithoutUndo();
            for(int i=0;i<3;i++)
            {
                var go = GameObject.Find("ConfiguredSpawner_"+i) ?? new GameObject("ConfiguredSpawner_"+i);
                go.transform.position = new Vector3(12+i*14,0,12+i*8);
                var spawner = Ensure<EnemySpawner>(go);
                Set(spawner,"enemyPrefab",enemyAsset); Set(spawner,"spawnCenter",go.transform);
                Number(spawner,"maxEnemiesPerSpawner",3); Number(spawner,"spawnInterval",2); Number(spawner,"spawnRadius",7);
            }
            RenderSettings.skybox=null; RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight=new Color(.72f,.76f,.8f); RenderSettings.fog=false;
            var main = Camera.main;
            if(main == null) { main = new GameObject("MainCamera").AddComponent<Camera>(); main.tag="MainCamera"; main.gameObject.AddComponent<AudioListener>(); }
            main.clearFlags=CameraClearFlags.SolidColor; main.backgroundColor=new Color(.38f,.61f,.76f); main.fieldOfView=45;
            main.cullingMask=~0; main.nearClipPlane=.1f; main.farClipPlane=400;
            main.transform.SetPositionAndRotation(new Vector3(0,15,-12), Quaternion.Euler(50,0,0));
            var duplicateFollow = main.GetComponent<TOP.Systems.CameraController>(); if(duplicateFollow != null) UnityEngine.Object.DestroyImmediate(duplicateFollow);
            var ui = UnityEngine.Object.FindAnyObjectByType<UIManager>();
            if(ui == null) throw new Exception("GameScene UIManager missing");
            foreach(var pair in new[]{("hpBar","HP_Bar"),("mpBar","MP_Bar"),("spBar","SP_Bar"),("expBar","Exp_Bar")}) { var slider = Find<Slider>(pair.Item2); Set(ui,pair.Item1,slider); if(slider != null) slider.interactable=false; }
            foreach(var pair in new[]{("hpText","Txt_HP"),("mpText","Txt_MP"),("spText","Txt_SP"),("expText","Txt_Exp"),("levelText","Txt_Level"),("nameText","Txt_Name"),("goldText","Txt_Gold"),("messageText","Txt_Message"),("chatHistory","Txt_ChatHistory")}) Set(ui,pair.Item1,Find<TextMeshProUGUI>(pair.Item2));
            Set(ui,"chatInput",Find<TMP_InputField>("Input_Chat"));
            LayoutHUD();
            foreach(var text in UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
            {
                if(text.fontSharedMaterial!=null)
                {
                    string path="Assets/ImportedClient/HUD_"+text.font.name+".mat";
                    var fontMaterial=AssetDatabase.LoadAssetAtPath<Material>(path);
                    if(fontMaterial==null) { fontMaterial=new Material(text.fontSharedMaterial); AssetDatabase.CreateAsset(fontMaterial,path); }
                    fontMaterial.SetColor("_FaceColor",Color.white); EditorUtility.SetDirty(fontMaterial); text.fontSharedMaterial=fontMaterial;
                }
                text.color=Color.white; text.enableVertexGradient=false;
            }
            var key1=Find<TextMeshProUGUI>("Txt_Key_0"); if(key1!=null) key1.text="1\nGolpe";
            var key2=Find<TextMeshProUGUI>("Txt_Key_1"); if(key2!=null) key2.text="2\nCura";
            foreach(var key in new[]{key1,key2}) if(key!=null) { key.rectTransform.sizeDelta=new Vector2(46,46); key.fontSize=11; key.alignment=TextAlignmentOptions.Center; }
            var mapBG = GameObject.Find("MiniMap_BG");
            if(mapBG != null)
            {
                var display = mapBG.GetComponent<RawImage>();
                if(display == null) { var old=mapBG.GetComponent<Image>(); if(old != null) UnityEngine.Object.DestroyImmediate(old); display=mapBG.AddComponent<RawImage>(); }
                var minimap = Ensure<MinimapRenderer>(mapBG); Set(minimap,"minimapDisplay",display);
                var label=Find<TextMeshProUGUI>("Txt_MiniMap"); if(label != null) label.gameObject.SetActive(false);
                var indicator = mapBG.transform.Find("PlayerIndicator");
                if(indicator == null) { indicator=new GameObject("PlayerIndicator",typeof(RectTransform),typeof(Image)).transform; indicator.SetParent(mapBG.transform,false); }
                var image=indicator.GetComponent<Image>(); image.color=Color.yellow; image.raycastTarget=false;
                image.rectTransform.anchorMin=image.rectTransform.anchorMax=image.rectTransform.pivot=new Vector2(.5f,.5f); image.rectTransform.sizeDelta=new Vector2(7,7); image.rectTransform.anchoredPosition=Vector2.zero;
                Set(minimap,"playerIndicator",image);
            }
            var music=GameObject.Find("WorldMusic") ?? new GameObject("WorldMusic");
            var audio=Ensure<AudioSource>(music); audio.clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ImportedClient/Audio/Argent.ogg"); audio.loop=true; audio.playOnAwake=true; audio.volume=.18f;
            var scenery = GameObject.Find("ArgentScenery"); if(scenery == null) scenery = new GameObject("ArgentScenery");
            var argentBuildings = new[]
            {
                ("by-bd001", new Vector3(-54, 0, -56)), ("by-bd002", new Vector3(-27, 0, -56)),
                ("by-bd003", new Vector3(0, 0, -56)), ("by-bd004", new Vector3(27, 0, -56)),
                ("by-bd005", new Vector3(54, 0, -56)), ("by-bd007", new Vector3(-54, 0, -28)),
                ("by-bd008", new Vector3(-27, 0, -28)), ("by-bd009", new Vector3(27, 0, -28)),
                ("by-bd010", new Vector3(54, 0, -28)), ("by-bd011", new Vector3(-54, 0, 0)),
                ("by-bd013", new Vector3(54, 0, 0)), ("by-bd015", new Vector3(-54, 0, 28)),
                ("by-bd016", new Vector3(-27, 0, 28)), ("by-bd017", new Vector3(27, 0, 28)),
                ("by-bd018", new Vector3(54, 0, 28)), ("by-bd020", new Vector3(0, 0, 56))
            };
            var extraArgentBuildings = new System.Collections.Generic.List<(string, Vector3)>();
            string[] extraModelNames = Enumerable.Range(21, 29)
                .Select(i => "by-bd" + i.ToString("000"))
                .Where(HasSceneModel)
                .Concat(new[]{"by-bd014","by-bd014-1","by-bd014-2","by-bd014-3","by-bd014-4","by-bd014-5","by-bd014-6","by-bd014-7","by-bd014-8"}.Where(HasSceneModel))
                .Distinct()
                .ToArray();
            var extraPositions = new[]
            {
                new Vector3(-84,0,-70), new Vector3(-56,0,-84), new Vector3(-28,0,-84), new Vector3(0,0,-84), new Vector3(28,0,-84), new Vector3(56,0,-84), new Vector3(84,0,-70),
                new Vector3(-84,0,-42), new Vector3(84,0,-42), new Vector3(-84,0,-14), new Vector3(84,0,-14), new Vector3(-84,0,14), new Vector3(84,0,14), new Vector3(-84,0,42), new Vector3(84,0,42),
                new Vector3(-70,0,70), new Vector3(-42,0,84), new Vector3(-14,0,84), new Vector3(14,0,84), new Vector3(42,0,84), new Vector3(70,0,70),
                new Vector3(-68,0,-6), new Vector3(68,0,-6), new Vector3(-68,0,20), new Vector3(68,0,20), new Vector3(-42,0,58), new Vector3(42,0,58),
                new Vector3(-18,0,-70), new Vector3(18,0,-70), new Vector3(0,0,70), new Vector3(-96,0,0), new Vector3(96,0,0), new Vector3(0,0,-96), new Vector3(0,0,96)
            };
            for(int i=0; i<extraModelNames.Length && i<extraPositions.Length; i++)
                extraArgentBuildings.Add((extraModelNames[i], extraPositions[i]));
            foreach(var oldExtra in scenery.transform.Cast<Transform>().Where(t => t.name.StartsWith("ArgentExtra_", StringComparison.Ordinal)).ToArray())
                UnityEngine.Object.DestroyImmediate(oldExtra.gameObject);
            foreach(var entry in argentBuildings)
            {
                string modelName=entry.Item1;
                var existing=scenery.transform.Find(modelName); if(existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ImportedClient/Models/model/scene/"+modelName+".obj");
                if(model==null) { Debug.LogWarning("Missing imported Argent structure: "+modelName); continue; }
                var building=(GameObject)PrefabUtility.InstantiatePrefab(model); building.name=modelName; building.transform.SetParent(scenery.transform);
                var groundTerrain=importedTerrain.GetComponent<Terrain>();
                Vector3 position=entry.Item2;
                position.y=groundTerrain.SampleHeight(position)+groundTerrain.transform.position.y;
                building.transform.position=position;
                var renderers=building.GetComponentsInChildren<Renderer>();
                if(renderers.Length>0)
                {
                    var bounds=renderers[0].bounds; foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    var collider=building.AddComponent<BoxCollider>();
                    collider.center=building.transform.InverseTransformPoint(bounds.center);
                    collider.size=bounds.size;
                    var obstacle=building.AddComponent<NavMeshObstacle>();
                    obstacle.shape=NavMeshObstacleShape.Box; obstacle.size=collider.size; obstacle.carving=true;
                }
                foreach(var renderer in renderers) foreach(var mat in renderer.sharedMaterials)
                {
                    if(mat==null) continue; var texture=mat.mainTexture; mat.shader=Shader.Find("Universal Render Pipeline/Lit"); mat.mainTexture=texture; mat.SetFloat("_Smoothness",0); EditorUtility.SetDirty(mat);
                }
            }
            foreach(var entry in extraArgentBuildings)
            {
                string modelName=entry.Item1;
                string instanceName="ArgentExtra_"+modelName;
                var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ImportedClient/Models/model/scene/"+modelName+".obj");
                if(model==null) continue;
                var building=(GameObject)PrefabUtility.InstantiatePrefab(model); building.name=instanceName; building.transform.SetParent(scenery.transform);
                building.transform.position=OnTerrain(importedTerrain, entry.Item2);
                building.transform.rotation=Quaternion.Euler(0f, (extraArgentBuildings.IndexOf(entry)%4)*90f, 0f);
                var renderers=building.GetComponentsInChildren<Renderer>();
                if(renderers.Length>0)
                {
                    var bounds=renderers[0].bounds; foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    var collider=building.AddComponent<BoxCollider>();
                    collider.center=building.transform.InverseTransformPoint(bounds.center);
                    collider.size=bounds.size;
                    var obstacle=building.AddComponent<NavMeshObstacle>();
                    obstacle.shape=NavMeshObstacleShape.Box; obstacle.size=collider.size; obstacle.carving=true;
                }
                foreach(var renderer in renderers) foreach(var mat in renderer.sharedMaterials)
                {
                    if(mat==null) continue; var texture=mat.mainTexture; mat.shader=Shader.Find("Universal Render Pipeline/Lit"); mat.mainTexture=texture; mat.SetFloat("_Smoothness",0); EditorUtility.SetDirty(mat);
                }
            }
            var cityTerrains = GarnerCitiesBuilder.Build(out int cityModels);
            // Authored layouts are replaced by the original positions from garner.obj.
            foreach(var child in scenery.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var citiesRoot = GameObject.Find("GarnerCities");
            if(citiesRoot != null) foreach(var child in citiesRoot.transform.Cast<Transform>().Where(t => t.name.StartsWith("Scenery_", StringComparison.Ordinal)).ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
            var allTiles = new System.Collections.Generic.List<Terrain>{ importedTerrain.GetComponent<Terrain>() }; allTiles.AddRange(cityTerrains);
            string placement = GarnerObjectPlacer.Place(allTiles);
            File.WriteAllText("Tools/garner-cities-report.txt", $"City tiles: {cityTerrains.Count}; {placement}\n");
            GarnerWorldPopulator.Populate(importedTerrain, enemyAsset, cityTerrains);
            foreach(var manager in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include))
            {
                foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Enemy/Variants"})){ var v=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if(v!=null && !manager.spawnPrefabs.Contains(v)) manager.spawnPrefabs.Add(v); }
                EditorUtility.SetDirty(manager);
            }
            EditorSceneManager.SaveScene(scene);

            foreach(var path in new[]{"Assets/Scenes/LoginScene.unity","Assets/Scenes/CharacterSelectScene.unity"})
            {
                var menuScene=EditorSceneManager.OpenScene(path);
                foreach(var manager in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include))
                {
                    if(!manager.spawnPrefabs.Contains(enemyAsset)) manager.spawnPrefabs.Add(enemyAsset);
                    foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Enemy/Variants"})){ var v=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)); if(v!=null && !manager.spawnPrefabs.Contains(v)) manager.spawnPrefabs.Add(v); }
                    EditorUtility.SetDirty(manager);
                }
                EditorSceneManager.SaveScene(menuScene);
            }
            AssetDatabase.SaveAssets();
            var dependencies=AssetDatabase.GetDependencies(new[]{"Assets/Scenes/LoginScene.unity","Assets/Scenes/CharacterSelectScene.unity","Assets/Scenes/GameScene.unity"},true);
            File.WriteAllLines("Tools/scene-dependencies.txt",dependencies.OrderBy(x=>x));
            File.WriteAllText("Tools/configure-world.done.txt",DateTime.UtcNow.ToString("O")+"\nScene, prefabs, navigation, HUD, minimap and network spawn registration saved.\n");
            if(previous.Length>0 && previous.Any(x=>x.isLoaded && x.isActive && !string.IsNullOrEmpty(x.path))) EditorSceneManager.RestoreSceneManagerSetup(previous);
            AssetDatabase.ForceReserializeAssets(new[]{"Assets/Scenes/LoginScene.unity","Assets/Scenes/CharacterSelectScene.unity","Assets/Scenes/GameScene.unity","Assets/Prefabs/Player.prefab","Assets/Enemy/Prefabs/Enemy_Slime.prefab"});
        }
    }
}

