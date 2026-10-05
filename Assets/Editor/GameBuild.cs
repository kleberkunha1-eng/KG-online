using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Gera a build Windows 64 em Build/GameProjectKG. Menu: Tools > Build Game. Tambem dispara ao criar o arquivo Tools/build.request.</summary>
[InitializeOnLoad]
public static class GameBuild
{
    const string Out = "Build/GameProjectKG";
    const string Request = "Tools/build.request";
    const string Result = "Tools/build-result.txt";

    static GameBuild() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        File.Delete(Request);
        File.WriteAllText(Result, Build());
    }

    [MenuItem("Tools/Build Game (Windows)")]
    public static void BuildMenu() { Debug.Log(Build()); }

    public static string Build()
    {
        PlayerSettings.companyName = "KG";
        PlayerSettings.productName = "GameProjectKG";
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (Directory.Exists(Out)) Directory.Delete(Out, true);
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Out + "/GameProjectKG.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var r = BuildPipeline.BuildPlayer(opts);
        return r.summary.result + " | " + (r.summary.totalSize / 1048576) + " MB | " + r.summary.totalErrors + " errors | " + r.summary.totalTime;
    }
}