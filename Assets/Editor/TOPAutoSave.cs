using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TOPAutoSave
{
    const double Interval = 60d;
    static readonly string Key = "TOP.AutoSave." + Path.GetFullPath(".").ToLowerInvariant();
    static double next;
    static bool saving;
    public static bool Enabled => EditorPrefs.GetBool(Key, true);

    static TOPAutoSave()
    {
        next = EditorApplication.timeSinceStartup + Interval;
        EditorApplication.update += Tick;
        EditorApplication.quitting += SaveIfEnabled;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) SaveIfEnabled();
        };
    }

    static void Tick()
    {
        if (!Enabled || EditorApplication.timeSinceStartup < next || EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling || EditorApplication.isUpdating || BuildPipeline.isBuildingPlayer) return;
        next = EditorApplication.timeSinceStartup + Interval;
        SaveNow();
    }

    static void SaveIfEnabled() { if (Enabled) SaveNow(); }

    [MenuItem("Tools/Autosave/Enabled")]
    static void Toggle() { EditorPrefs.SetBool(Key, !Enabled); }

    [MenuItem("Tools/Autosave/Enabled", true)]
    static bool ValidateToggle() { Menu.SetChecked("Tools/Autosave/Enabled", Enabled); return true; }

    [MenuItem("Tools/Autosave/Save Now")]
    public static void SaveNow()
    {
        if (saving || EditorApplication.isPlaying || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) return;
        saving = true;
        try
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.isDirty || EditorSceneManager.IsPreviewScene(scene)) continue;
                string name = string.IsNullOrEmpty(scene.path) ? "Untitled_" + scene.handle : Path.GetFileNameWithoutExtension(scene.path);
                string directory = Path.Combine("Library", "TOPAutosave", name);
                Directory.CreateDirectory(directory);
                string recovery = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
                if (!EditorSceneManager.SaveScene(scene, recovery, true))
                    throw new IOException("Could not create scene recovery copy: " + recovery);
                if (!string.IsNullOrEmpty(scene.path) && !EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not autosave scene: " + scene.path);
                foreach (string old in Directory.GetFiles(directory, "*.unity").OrderByDescending(File.GetLastWriteTimeUtc).Skip(10))
                    File.Delete(old);
                Debug.Log("[TOPAutoSave] Saved scene/recovery copy: " + name);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Library/TOP-autosave-status.txt", "Enabled=" + Enabled + "\nLastSavedUtc=" + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception e) { Debug.LogError("[TOPAutoSave] Save failed: " + e); }
        finally { saving = false; }
    }

    [MenuItem("Tools/Autosave/Open Recovery Folder")]
    static void OpenRecovery()
    {
        string path = Path.GetFullPath(Path.Combine("Library", "TOPAutosave"));
        Directory.CreateDirectory(path);
        EditorUtility.RevealInFinder(path);
    }
}
