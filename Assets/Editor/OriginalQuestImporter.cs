using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class OriginalQuestImporter
{
    [MenuItem("Tools/PKO/Import Original Lua Quests")]
    public static void Import()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        string node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe");
        if (!File.Exists(node)) throw new FileNotFoundException("Install Node.js before importing original quests.", node);
        var start = new ProcessStartInfo(node, "\"" + Path.Combine(root, "Tools", "Import-OriginalQuests.cjs") + "\"")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using (var process = Process.Start(start))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            string message = output.GetAwaiter().GetResult();
            string failure = error.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException("Original quest import failed: " + failure);
            UnityEngine.Debug.Log("[OriginalQuests] " + message.Trim());
            if (!string.IsNullOrWhiteSpace(failure)) UnityEngine.Debug.LogWarning(failure.Trim());
        }
        AssetDatabase.ImportAsset("Assets/Resources/OriginalQuests.json", ImportAssetOptions.ForceUpdate);
    }

    public static void RunBatch()
    {
        try { Import(); EditorApplication.Exit(0); }
        catch (Exception error) { UnityEngine.Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
