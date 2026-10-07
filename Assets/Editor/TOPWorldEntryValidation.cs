using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

[InitializeOnLoad]
public static class TOPWorldEntryValidation
{
    const string Request = "Tools/prepare-world-entry.request";

    static TOPWorldEntryValidation() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try
        {
            int checkedCount = 0, changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset.GetComponentInChildren<EnemyAI>(true) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool dirty = false;
                    foreach (var ai in root.GetComponentsInChildren<EnemyAI>(true))
                    {
                        checkedCount++;
                        var agent = ai.GetComponent<NavMeshAgent>();
                        if (agent == null) throw new InvalidDataException($"Enemy without NavMeshAgent: {path}");
                        if (agent.enabled) { agent.enabled = false; dirty = true; }
                    }
                    if (dirty) { PrefabUtility.SaveAsPrefabAsset(root, path); changed++; }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Tools/world-entry-validation-results.txt",
                $"PASS Enemy agents default disabled: checked={checkedCount}, changed prefabs={changed}\n");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            File.WriteAllText("Tools/world-entry-validation-results.txt", "FAILED " + e);
        }
    }
}
