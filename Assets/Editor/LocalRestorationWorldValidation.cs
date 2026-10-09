using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LocalRestorationWorldValidation
{
    const string Active = "TOP.LocalRestorationWorldValidation";
    const string Started = "TOP.LocalRestorationWorldValidationStarted";
    const string Result = "Tools/world-entry-smoke-results.txt";

    static LocalRestorationWorldValidation()
    {
        EditorApplication.update += CheckCompletion;
    }

    public static void Run()
    {
        if (!Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Execute apenas em uma instancia batch isolada do Editor.");

        foreach (var entry in EditorBuildSettings.scenes.Where(scene => scene.enabled))
        {
            var scene = EditorSceneManager.OpenScene(entry.path);
            int missing = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            if (missing != 0)
                throw new InvalidDataException(entry.path + ": scripts ausentes=" + missing);
            Debug.Log("[LocalRestoration] PASS scene " + entry.path + ": zero scripts ausentes.");
        }

        SessionState.SetString(Started, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        SessionState.SetBool(Active, true);
        SessionState.SetBool("TOP.WorldEntrySmokeActive", true);
        SessionState.SetBool("TOP.AdminGenerationSmoke", false);
        SessionState.SetBool("TOP.NewCharacterSmoke", false);
        EditorSceneManager.OpenScene("Assets/Scenes/GameScene.unity");
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/GameScene.unity");
        EditorApplication.EnterPlaymode();
    }

    static void CheckCompletion()
    {
        if (!SessionState.GetBool(Active, false)) return;
        long ticks = long.Parse(SessionState.GetString(Started, ""), CultureInfo.InvariantCulture);
        var started = new DateTime(ticks, DateTimeKind.Utc);
        if (DateTime.UtcNow - started > TimeSpan.FromMinutes(3))
        {
            SessionState.SetBool(Active, false);
            Debug.LogError("[LocalRestoration] Timeout no teste de entrada no mundo.");
            EditorApplication.Exit(1);
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !File.Exists(Result) || File.GetLastWriteTimeUtc(Result) < started) return;

        string[] lines = File.ReadAllLines(Result);
        bool failed = !lines.Any(line => line.StartsWith("PASS ", StringComparison.Ordinal))
            || lines.Any(line => line.StartsWith("FAIL ", StringComparison.Ordinal));
        SessionState.SetBool(Active, false);
        if (failed) Debug.LogError(string.Join("\n", lines));
        else Debug.Log(string.Join("\n", lines));
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
