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
        EditorSceneManager.RestoreSceneManagerSetup(scenes);
        SessionState.EraseString(Restore);
        string startScene = SessionState.GetString(StartScene, "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(startScene)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
        SessionState.EraseString(StartScene);
    }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode) return;
        const string request = "Tools/validate-world-entry.request";
        const string adminRequest = "Tools/validate-admin-generation.request";
        bool admin = File.Exists(adminRequest);
        if (!File.Exists(request) && !admin) return;
        if (File.Exists("Tools/build.request") || File.Exists("Tools/prepare-world-entry.request")
            || File.Exists("Tools/validate-visuals.request")) return;
        if (admin) File.Delete(adminRequest);
        if (File.Exists(request)) File.Delete(request);
        SessionState.SetBool("TOP.AdminGenerationSmoke", admin);
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
