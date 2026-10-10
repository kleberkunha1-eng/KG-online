using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TOPWorldEntrySmokeRunner
{
    const string Active = "TOP.WorldEntrySmokeActive";
    const string Restore = "TOP.WorldEntrySmokeRestore";
    const string StartScene = "TOP.WorldEntrySmokeStartScene";

    [Serializable]
    class SavedScenes { public SavedScene[] scenes; }

    [Serializable]
    class SavedScene { public string path; public bool loaded; public bool active; }

    static TOPWorldEntrySmokeRunner()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        string restore = SessionState.GetString(Restore, "");
        if (SessionState.GetBool(Active, false) || restore.Length == 0) return;
        var scenes = JsonUtility.FromJson<SavedScenes>(restore).scenes.Select(scene => new SceneSetup
        {
            path = scene.path, isLoaded = scene.loaded, isActive = scene.active
        }).ToArray();
        if (scenes.Any(scene => scene.isLoaded) && scenes.Any(scene => scene.isActive))
            EditorSceneManager.RestoreSceneManagerSetup(scenes);
        SessionState.EraseString(Restore);
        string startScene = SessionState.GetString(StartScene, "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(startScene)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
        SessionState.EraseString(StartScene);
        if (SessionState.GetBool("TOP.SocialSmokeBatch", false))
        {
            SessionState.SetBool("TOP.SocialSmokeBatch", false);
            bool passed = File.Exists("Tools/social-gameplay-results.txt")
                && !File.ReadLines("Tools/social-gameplay-results.txt").Any(line => line.StartsWith("FAIL "));
            EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
        }
    }

    public static void RunSocialBatch()
    {
        SocialComponentInstaller.Install();
        SessionState.SetBool("TOP.SocialSmokeBatch", true);
        File.WriteAllText("Tools/validate-social-gameplay.request", "validate");
        Poll();
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string request = "Tools/validate-world-entry.request";
        const string adminRequest = "Tools/validate-admin-generation.request";
        const string characterRequest = "Tools/validate-new-character-gameplay.request";
        const string environmentRequest = "Tools/validate-environment-network.request";
        const string socialRequest = "Tools/validate-social-gameplay.request";
        bool admin = File.Exists(adminRequest);
        bool newCharacter = File.Exists(characterRequest);
        bool environment = File.Exists(environmentRequest);
        bool social = File.Exists(socialRequest);
        if (!File.Exists(request) && !admin && !newCharacter && !environment && !social) return;
        if (File.Exists("Tools/build.request") || File.Exists("Tools/prepare-world-entry.request")
            || File.Exists("Tools/validate-visuals.request")) return;
        if (admin) File.Delete(adminRequest);
        if (newCharacter) File.Delete(characterRequest);
        if (File.Exists(request)) File.Delete(request);
        if (environment) File.Delete(environmentRequest);
        if (social) File.Delete(socialRequest);
        SessionState.SetBool("TOP.AdminGenerationSmoke", admin);
        SessionState.SetBool("TOP.NewCharacterSmoke", newCharacter);
        SessionState.SetBool("TOP.EnvironmentSmoke", environment);
        SessionState.SetBool("TOP.SocialSmoke", social);
        TOPAutoSave.SaveNow();
        SessionState.SetString(Restore, JsonUtility.ToJson(new SavedScenes
        {
            scenes = EditorSceneManager.GetSceneManagerSetup().Select(scene => new SavedScene
            {
                path = scene.path, loaded = scene.isLoaded, active = scene.isActive
            }).ToArray()
        }));
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        SessionState.SetString(StartScene, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Active, true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/GameScene.unity");
        EditorApplication.isPlaying = true;
    }
}
