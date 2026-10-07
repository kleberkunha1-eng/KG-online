using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TOP.Core;
using TOP.Data;
using TOP.UI.Pko;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TOP.Services;
using TOP.Network;
using Mirror;

[InitializeOnLoad]
public static class PkoVisualValidation
{
    const string Request = "Tools/validate-visuals.request";
    const string CharacterListRequest = "Tools/validate-character-list.request";
    const string MageRequest = "Tools/validate-mage-wings.request";
    static PkoVisualValidation() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (File.Exists(MageRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(MageRequest);
            RunMage();
            return;
        }
        if (File.Exists(CharacterListRequest) && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(CharacterListRequest);
            RunCharacterList();
            return;
        }
        if (!File.Exists(Request) || File.Exists("Tools/build-wings.request") || File.Exists("Tools/build.request")
            || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/PKO/Validate Mage Wings")]
    public static void RunMage()
    {
        var report = new StringBuilder();
        int failed = 0;
        Action<bool, string> check = (ok, message) =>
        {
            report.AppendLine((ok ? "PASS " : "FAIL ") + message);
            if (!ok) failed++;
        };
        try
        {
            var mage = Resources.Load<GameObject>("Wings/Animated/MageWings");
            if (mage == null) throw new FileNotFoundException("Mage Wings prefab is missing.");
            var animation = mage.GetComponent<Animation>();
            check(animation != null && animation.playAutomatically && animation.wrapMode == WrapMode.Loop,
                "Saved Mage prefab plays the Emperor animation in a loop");
            check(mage.GetComponentsInChildren<Renderer>().Length == 23,
                "Saved Mage prefab contains the wing, ten feathers and twelve wind trails");
            check(mage.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m =>
                m != null && m.shader != null && m.shader.isSupported)),
                "All Emperor materials are present and supported");
            check(mage.GetComponentsInChildren<SkinnedMeshRenderer>().All(r =>
                r.sharedMesh != null && r.sharedMesh.boneWeights.Length == r.sharedMesh.vertexCount
                && r.sharedMaterials.All(m => m.mainTexture != null)),
                "Emperor skinning and wing texture survive prefab saving");
            CheckMotion(mage, check);
            Capture(mage, "Tools/mage-wings-preview.png");
        }
        catch (Exception e) { Debug.LogException(e); check(false, e.ToString()); }
        report.Insert(0, $"Failed={failed}\n");
        File.WriteAllText("Tools/mage-wings-validation-results.txt", report.ToString());
        if (failed > 0) Debug.LogError(report.ToString()); else Debug.Log(report.ToString());
    }

    [MenuItem("Tools/PKO/Validate Character List Readiness")]
    public static void RunCharacterList()
    {
        if (NetworkClient.active || NetworkServer.active)
            throw new InvalidOperationException("Character-list validation requires disconnected edit mode.");
        var state = typeof(NetworkClient).GetField("connectState", BindingFlags.Static | BindingFlags.NonPublic);
        var connection = typeof(NetworkClient).GetProperty("connection", BindingFlags.Static | BindingFlags.Public);
        var gate = typeof(PkoFlow).GetMethod("CanRequestCharacterList", BindingFlags.Static | BindingFlags.NonPublic);
        var previousState = state.GetValue(null);
        var previousConnection = NetworkClient.connection;
        var report = new StringBuilder();
        int failed = 0;
        void Check(bool expected, string message)
        {
            bool ok = (bool)gate.Invoke(null, null) == expected;
            report.AppendLine((ok ? "PASS " : "FAIL ") + message);
            if (!ok) failed++;
        }
        try
        {
            connection.SetValue(null, new NetworkConnectionToServer());
            state.SetValue(null, Enum.Parse(state.FieldType, "Connected"));
            NetworkClient.connection.isReady = false;
            Check(true, "Connected selection requests characters without world Ready");
            NetworkClient.connection.isReady = true;
            Check(true, "Connected ready session can request characters");
            state.SetValue(null, Enum.Parse(state.FieldType, "Disconnected"));
            Check(false, "Disconnected session cannot request characters");
            state.SetValue(null, Enum.Parse(state.FieldType, "Connected"));
            connection.SetValue(null, null);
            Check(false, "Missing connection cannot request characters");
        }
        catch (Exception e)
        {
            failed++;
            report.AppendLine("FAIL " + e);
            Debug.LogException(e);
        }
        finally
        {
            connection.SetValue(null, previousConnection);
            state.SetValue(null, previousState);
        }
        report.Insert(0, $"Failed={failed}\n");
        File.WriteAllText("Tools/character-list-validation-results.txt", report.ToString());
        if (failed > 0) Debug.LogError(report.ToString()); else Debug.Log(report.ToString());
    }

    [MenuItem("Tools/PKO/Validate Windows and Wings")]
    public static void Run()
    {
        var report = new StringBuilder();
        int passed = 0, failed = 0;
        Action<bool, string> check = (ok, message) =>
        {
            report.AppendLine((ok ? "PASS " : "FAIL ") + message);
            if (ok) passed++; else failed++;
        };
        try
        {
            foreach (var item in PkoTables.Items.Values)
            {
                if (item.EquipSlots.Length == 0 || PkoTables.SlotOf(item) != EquipmentSlot.Wing) continue;
                string path = item.Id == PkoTables.MeshyMageWingsItemId
                    ? "Wings/Animated/MageWings" : "Wings/Items/" + item.Id;
                var prefab = Resources.Load<GameObject>(path);
                check(prefab != null, $"Wing {item.Id} ({item.Name}) has its own visual");
                if (prefab == null) continue;
                foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    check(renderer.sharedMesh != null && renderer.sharedMesh.boneWeights.Length == renderer.sharedMesh.vertexCount,
                        $"Wing {item.Id} has a skinned mesh");
                    check(renderer.sharedMaterials.All(m => m != null && m.shader != null && m.shader.isSupported && m.mainTexture != null),
                        $"Wing {item.Id} has supported textured materials");
                }
            }
            var mage = Resources.Load<GameObject>("Wings/Animated/MageWings");
            var rebirth = Resources.Load<GameObject>("Wings/Animated/1chim1");
            check(mage != null && rebirth != null && mage.GetComponent<Animation>().clip != rebirth.GetComponent<Animation>().clip,
                "Mage Wings animation is independent of Rebirth");
            check(mage != null && mage.GetComponent<Animation>().clip.name == "Asas_Penas_Vento_Loop",
                "Mage Wings uses the Emperor wind-loop animation");
            if (mage != null) { CheckMotion(mage, check); Capture(mage, "Tools/mage-wings-preview.png"); }
            if (rebirth != null) { CheckMotion(rebirth, check); Capture(rebirth, "Tools/rebirth-wings-preview.png"); }
            CheckWindows(check);
            check(ChatService.IsLocalRecipient("garner", Vector3.zero, "garner", Vector3.right * ChatService.LocalRange),
                "Local chat includes boundary distance");
            check(!ChatService.IsLocalRecipient("garner", Vector3.zero, "garner", Vector3.right * (ChatService.LocalRange + .01f)),
                "Local chat excludes distant players");
            check(!ChatService.IsLocalRecipient("garner", Vector3.zero, "ascaron", Vector3.zero),
                "Local chat excludes other maps");
            check(ChatService.Format(new ChatMessage { Channel = ChatChannel.Local, SenderName = "Tester", Text = "Hello" }) == "[Local] Tester: Hello",
                "Local chat shows authoritative sender name once");
            CheckBubble(check);
            CheckWorldEntry(check);
            check(TOPAutoSave.Enabled, "Unity Editor autosave is enabled");
            TOPAutoSave.SaveNow();
            check(File.Exists("Library/TOP-autosave-status.txt"), "Unity Editor autosave persisted status");
            AuditMaterials(report);
        }
        catch (Exception e) { Debug.LogException(e); check(false, e.ToString()); }
        report.Insert(0, $"Passed={passed} Failed={failed}\n");
        File.WriteAllText("Tools/visual-validation-results.txt", report.ToString());
        if (failed > 0) Debug.LogError(report.ToString()); else Debug.Log(report.ToString());
    }

    static void CheckMotion(GameObject prefab, Action<bool, string> check)
    {
        var root = UnityEngine.Object.Instantiate(prefab);
        var first = new Mesh();
        var second = new Mesh();
        try
        {
            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            var clip = root.GetComponent<Animation>().clip;
            clip.SampleAnimation(root, 0f);
            renderer.BakeMesh(first);
            first.RecalculateBounds();
            if (prefab.name == "MageWings")
            {
                check(renderer.bones.Length >= 6 && clip.name == "Asas_Penas_Vento_Loop"
                    && clip.length > 7.8f && clip.length < 8.1f,
                    "Mage Wings retains the Emperor rig and eight-second animation");
            }
            clip.SampleAnimation(root, clip.length * 0.4f);
            renderer.BakeMesh(second);
            second.RecalculateBounds();
            var a = first.vertices; var b = second.vertices;
            float motion = 0f;
            for (int i = 0; i < a.Length; i++) motion = Mathf.Max(motion, (a[i] - b[i]).magnitude);
            check(motion > 0.001f, $"{prefab.name} animated vertex displacement={motion:F5}");
            check(float.IsFinite(motion) && second.bounds.size.x > 0f, $"{prefab.name} animation has valid mesh bounds");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }
    }

    static void CheckWorldEntry(Action<bool, string> check)
    {
        if (NetworkClient.active || NetworkServer.active)
            throw new InvalidOperationException("World-entry validation requires disconnected edit mode.");
        var root = new GameObject("WorldEntryValidation");
        try
        {
            var manager = root.AddComponent<TOPNetworkManager>();
            manager.OnClientConnect();
            check(!NetworkClient.ready, "Initial client connection does not request world readiness");
            var connection = new NetworkConnectionToClient(int.MaxValue);
            manager.OnServerReady(connection);
            check(!connection.isReady && connection.identity == null,
                "Server rejects premature Ready without creating world observers");
            var auth = root.AddComponent<ClientAuthHandler>();
            var sent = typeof(ClientAuthHandler).GetField("_authSent", BindingFlags.Instance | BindingFlags.NonPublic);
            sent.SetValue(auth, true);
            auth.BeginAuthentication();
            check(!(bool)sent.GetValue(auth), "Authentication is reset for each new connection");
            int agents = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (var ai in prefab.GetComponentsInChildren<EnemyAI>(true))
                {
                    agents++;
                    check(!ai.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled,
                        $"Enemy {prefab.name} agent is disabled before client instantiation");
                }
            }
            check(agents > 0, "World-entry validation inspected monster prefabs");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void CheckWindows(Action<bool, string> check)
    {
        foreach (string texture in new[] { "help2", "help4" })
            AssetDatabase.ImportAsset($"Assets/Resources/PKOUI/tex/texture/ui/{texture}.tga", ImportAssetOptions.ForceUpdate);
        var go = new GameObject("WindowValidation");
        var ui = go.AddComponent<PkoUi>();
        try
        {
            typeof(PkoUi).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui, null);
            foreach (string name in new[] { "frmInv", "frmState", "frmSkill", "frmChat", "frmMinimap" })
            {
                var window = ui.Get(name);
                check(window != null && window.Draggable && window.GetComponent<Image>().raycastTarget, name + " draggable background receives mouse events");
                if (window == null) continue;
                window.Open();
                Vector2 before = window.Rect.anchoredPosition;
                var e = new PointerEventData(EventSystem.current) { delta = new Vector2(40f, 20f) };
                ExecuteEvents.Execute(window.gameObject, e, ExecuteEvents.beginDragHandler);
                ExecuteEvents.Execute(window.gameObject, e, ExecuteEvents.dragHandler);
                check(Vector2.Distance(window.Rect.anchoredPosition - before, e.delta / ui.Canvas.scaleFactor) < 0.01f,
                    name + " drag moves by canvas-scaled delta");
                window.Close();
            }
            var shortcuts = new Dictionary<KeyCode, string>
            {
                { KeyCode.E, "frmInv" }, { KeyCode.S, "frmSkill" }, { KeyCode.A, "frmState" },
                { KeyCode.Q, "frmMission" }, { KeyCode.W, "frmBigmap" }, { KeyCode.R, "frmSearch" },
                { KeyCode.P, "frmTeamMenber1" }, { KeyCode.C, "frmManage" }, { KeyCode.F, "frmQQ" },
                { KeyCode.H, "frmStartHelp" }, { KeyCode.O, "frmSystem" }, { KeyCode.Z, "frmNpcShow" },
                { KeyCode.X, "frmRoleAllInfo" }
            };
            foreach (var shortcut in shortcuts)
            {
                var window = ui.Get(shortcut.Value);
                window.Close();
                bool opened = ui.ActivateShortcut(shortcut.Key, true, false, false) && window.IsOpen;
                bool closed = ui.ActivateShortcut(shortcut.Key, true, false, false) && !window.IsOpen;
                check(opened && closed, $"Alt+{shortcut.Key}: {shortcut.Value} toggles once (no duplicate aliases)");
            }
            var paths = new HashSet<string>();
            foreach (var form in ui.Defs.Values)
                foreach (var component in form.comps)
                    foreach (var image in component.imgs)
                        if (!string.IsNullOrEmpty(image.t)) paths.Add(image.t);
            var missing = paths.Where(path => ui.Tex(path) == null).ToArray();
            check(missing.Length == 0, $"UI textures: {paths.Count} checked, missing={missing.Length} {string.Join(", ", missing)}");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    static void CheckBubble(Action<bool, string> check)
    {
        var player = new GameObject("SpeechValidation");
        try
        {
            TOP.Player.PlayerSpeechBubble.Show(player, "<b>Mensagem local</b>");
            var label = player.GetComponentInChildren<Text>();
            check(label != null && label.text == "<b>Mensagem local</b>" && !label.supportRichText,
                "Speech bubble renders text literally without rich text injection");
            check(label != null && label.transform.parent.position.y > player.transform.position.y,
                "Speech bubble is placed above the player");
        }
        finally { UnityEngine.Object.DestroyImmediate(player); }
    }

    static void Capture(GameObject prefab, string path)
    {
        var preview = new PreviewRenderUtility();
        var root = UnityEngine.Object.Instantiate(prefab);
        Texture2D image = null;
        try
        {
            preview.AddSingleGO(root);
            var animation = root.GetComponent<Animation>();
            animation.clip.SampleAnimation(root, animation.clip.length * .4f);
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh);
                    mesh.RecalculateBounds();
                    skin.localBounds = mesh.bounds;
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
            var renderers = root.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            Vector3 center = bounds.center;
            float radius = Mathf.Max(bounds.size.x, bounds.size.y) * 0.7f;
            preview.camera.orthographic = true;
            preview.camera.orthographicSize = radius;
            preview.camera.transform.position = center + Vector3.back * (radius * 3f + 1f);
            preview.camera.transform.LookAt(center);
            preview.camera.nearClipPlane = .01f;
            preview.camera.farClipPlane = radius * 10f + 10f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.1f, .12f, .17f);
            preview.lights[0].intensity = 1.3f;
            preview.lights[0].transform.rotation = Quaternion.Euler(35f, 30f, 0f);
            preview.BeginStaticPreview(new Rect(0f, 0f, 640f, 640f));
            preview.Render(true);
            image = preview.EndStaticPreview();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            preview.Cleanup();
        }
    }

    static void AuditMaterials(StringBuilder report)
    {
        var assets = AssetDatabase.GetDependencies(new[]
        {
            "Assets/Scenes/GameScene.unity", "Assets/Prefabs/Player.prefab"
        }, true);
        int inspected = 0, missing = 0;
        foreach (string path in assets.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)
            || p.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)))
            foreach (var material in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>())
            {
                inspected++;
                if (material.shader == null || !material.shader.isSupported)
                {
                    report.AppendLine($"MATERIAL_UNSUPPORTED {path} | {material.name}");
                    missing++;
                }
                else if ((material.HasProperty("_MainTex") || material.HasProperty("_BaseMap")) && material.mainTexture == null)
                    report.AppendLine($"MATERIAL_NO_ALBEDO {path} | {material.name}");
            }
        report.AppendLine($"Scene material audit: inspected={inspected}, unsupported={missing}; untextured procedural materials may be intentional.");
    }
}
