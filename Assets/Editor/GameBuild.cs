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
        bool stagingOnly = File.ReadAllText(Request).Trim().Equals("stage", System.StringComparison.OrdinalIgnoreCase);
        File.Delete(Request);
        File.WriteAllText(Result, Build(!stagingOnly));
    }

    [MenuItem("Tools/Build Game (Windows)")]
    public static void BuildMenu() { Debug.Log(Build()); }

    [MenuItem("Tools/Build Game (Windows, staging only)")]
    public static void BuildStagingMenu() { Debug.Log(Build(false)); }

    public static string Build() => Build(true);

    static string Build(bool promote)
    {
        if (EditorUtility.scriptCompilationFailed)
        {
            Debug.LogError("[GameBuild] Corrija os erros de compilacao antes de gerar a build. A build existente foi preservada.");
            return "Failed | script compilation errors";
        }
        PlayerSettings.companyName = "KG";
        PlayerSettings.productName = "GameProjectKG";
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        string staging = Out + ".building";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = staging + "/GameProjectKG.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        var r = BuildPipeline.BuildPlayer(opts);
        string summary = r.summary.result + " | " + (r.summary.totalSize / 1048576) + " MB | "
            + r.summary.totalErrors + " errors | " + r.summary.totalTime;
        // Garante que toda build gerada ja aponte para a API publica (Cloudflare), mesmo quando
        // o jogo e aberto fora do launcher proprio (ex.: build publicada direto no itch.io, sem o
        // argumento --api= que o launcher injeta). Sem isso o cliente cai no fallback
        // http://127.0.0.1:3000, que so existe na maquina do desenvolvedor.
        if (r.summary.result == BuildResult.Succeeded)
        {
            var (host, port) = ReadServerAddress();
            File.WriteAllText(staging + "/api.json",
                "{\"apiUrl\":\"" + TOP.Services.ApiConfig.PublishedApiUrl + "\",\"gameServerHost\":\"" + host + "\",\"gameServerPort\":" + port + "}");
            File.WriteAllText(Path.Combine(staging, ".itch.toml"),
                "[[actions]]\nname = \"Jogar KG Online\"\npath = \"GameProjectKG.exe\"\nplatform = \"windows\"\n");
            if (!promote) return summary + " | Staging ready: " + staging + " | Active build unchanged";
            string previous = Out + ".previous." + System.DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            if (Directory.Exists(Out)) Directory.Move(Out, previous);
            try { Directory.Move(staging, Out); }
            catch
            {
                if (Directory.Exists(previous) && !Directory.Exists(Out)) Directory.Move(previous, Out);
                throw;
            }
        }
        return summary;
    }

    // Le o endereco publico do servidor dedicado (host e porta) de Tools/server-address.txt,
    // arquivo local e NAO versionado (contem o endereco do tunel playit.gg, que pode mudar).
    // Formato esperado na 1a linha: host:porta (ex.: abc123.joinmc.link:25565).
    // Se o arquivo nao existir, gera build com gameServerHost vazio (cliente mostra erro claro
    // em vez de tentar conectar a um servidor inexistente).
    static (string host, int port) ReadServerAddress()
    {
        const string path = "Tools/server-address.txt";
        if (!File.Exists(path)) return ("", 7777);
        var line = File.ReadAllLines(path).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"));
        if (string.IsNullOrWhiteSpace(line)) return ("", 7777);
        var parts = line.Trim().Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var port)) return ("", 7777);
        return (parts[0], port);
    }
}