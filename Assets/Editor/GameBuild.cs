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
        if (!File.Exists(Request)) return;
        // Se o Editor estiver em Play Mode, o request ficaria parado para sempre (nunca builda
        // enquanto isPlaying == true). Sai do Play Mode automaticamente e aguarda o proximo tick.
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
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
        // Garante que toda build gerada ja aponte para a API publica (Cloudflare), mesmo quando
        // o jogo e aberto fora do launcher proprio (ex.: build publicada direto no itch.io, sem o
        // argumento --api= que o launcher injeta). Sem isso o cliente cai no fallback
        // http://127.0.0.1:3000, que so existe na maquina do desenvolvedor.
        File.WriteAllText(Out + "/api.json", "{\"apiUrl\":\"https://gamekg.pages.dev\"}");
        return r.summary.result + " | " + (r.summary.totalSize / 1048576) + " MB | " + r.summary.totalErrors + " errors | " + r.summary.totalTime;
    }
}