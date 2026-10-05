using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Pipeline de build Windows com dois canais (Development / Release) e integracao com o
/// pipeline externo de assinatura/verificacao/hash (Tools/Signing, Tools/Release). Reaproveita
/// a logica de build existente em <see cref="GameBuild"/> (usada pelo publicador de updates)
/// para nao duplicar a configuracao de cenas/saida.
///
/// Menu:
///   Tools/Build/Windows Development  - build rapida, sem assinatura obrigatoria.
///   Tools/Build/Windows Release      - build de producao: versiona, assina (se configurado),
///                                      verifica assinatura, gera hashes + manifest.
///   Tools/Build/Verify Release       - so roda a verificacao de assinatura na build existente.
/// </summary>
public static class WindowsBuildPipeline
{
    [MenuItem("Tools/Build/Windows Development")]
    public static void BuildDevelopment()
    {
        Run(BuildReleaseConfig.Channel.Development);
    }

    [MenuItem("Tools/Build/Windows Release")]
    public static void BuildRelease()
    {
        Run(BuildReleaseConfig.Channel.Release);
    }

    [MenuItem("Tools/Build/Verify Release")]
    public static void VerifyRelease()
    {
        var exe = Path.GetFullPath(Path.Combine(BuildReleaseConfig.OutputDirectory, BuildReleaseConfig.ProductName + ".exe"));
        if (!File.Exists(exe))
        {
            Debug.LogError($"[WindowsBuildPipeline] Nao encontrei {exe}. Rode um build primeiro.");
            return;
        }
        var (code, output) = RunPowerShell("Tools/Signing/Verify-Signature.ps1", $"-Files \"{exe}\"");
        Debug.Log(output);
        Debug.Log(code == 0 ? "[WindowsBuildPipeline] Assinatura OK." : "[WindowsBuildPipeline] Assinatura invalida ou ausente.");
    }

    static void Run(BuildReleaseConfig.Channel channel)
    {
        var log = new StringBuilder();
        void Line(string s) { log.AppendLine(s); Debug.Log(s); }

        Line($"[BUILD] Iniciando build Windows ({channel})");

        var version = PlayerSettings.bundleVersion;
        var buildId = GitShortSha();
        Line($"[BUILD] Versao: {version}  Build: {(string.IsNullOrEmpty(buildId) ? "(sem git)" : buildId)}");

        PlayerSettings.companyName = "KG";
        PlayerSettings.productName = BuildReleaseConfig.ProductName;

        var outDir = BuildReleaseConfig.OutputDirectory;
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);

        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var options = channel == BuildReleaseConfig.Channel.Development
            ? BuildOptions.Development | BuildOptions.AllowDebugging
            : BuildOptions.None;

        var buildOpts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outDir + "/" + BuildReleaseConfig.ProductName + ".exe",
            target = BuildTarget.StandaloneWindows64,
            options = options,
        };

        var report = BuildPipeline.BuildPlayer(buildOpts);
        Line($"[BUILD] Resultado: {report.summary.result} | {report.summary.totalSize / 1048576} MB | {report.summary.totalErrors} erros | {report.summary.totalTime}");

        if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors > 0)
        {
            Debug.LogError("[BUILD] Build falhou - release abortado.");
            return;
        }

        var exe = Path.GetFullPath(Path.Combine(outDir, BuildReleaseConfig.ProductName + ".exe"));

        if (channel == BuildReleaseConfig.Channel.Development)
        {
            Line("[SIGN] Canal Development - assinatura opcional (nao obrigatoria).");
            Line("[RELEASE] Build de desenvolvimento pronta em " + Path.GetFullPath(outDir));
            return;
        }

        // Canal Release: assina (se SIGNING_MODE estiver configurado), verifica, gera hashes/manifest.
        var signingMode = Environment.GetEnvironmentVariable("SIGNING_MODE");
        if (string.IsNullOrEmpty(signingMode) || signingMode == "None")
        {
            Debug.LogWarning("[SIGN] CODE SIGNING NOT CONFIGURED. Defina SIGNING_MODE e as variaveis necessarias " +
                              "(ver Docs/CodeSigning.md) antes de distribuir publicamente. A build nao assinada " +
                              "continua disponivel em " + exe + ", mas nao deve ser publicada como release oficial.");
        }
        else
        {
            Line($"[SIGN] Assinando {exe} (modo {signingMode}) ...");
            var (signCode, signOut) = RunPowerShell("Tools/Signing/Sign-WindowsBuild.ps1", $"-Files \"{exe}\"");
            Line(signOut);
            if (signCode != 0)
            {
                Debug.LogError("[SIGN] Falha ao assinar - release abortado.");
                return;
            }

            Line("[VERIFY] Verificando assinatura ...");
            var (verifyCode, verifyOut) = RunPowerShell("Tools/Signing/Verify-Signature.ps1", $"-Files \"{exe}\"");
            Line(verifyOut);
            if (verifyCode != 0)
            {
                Debug.LogError("[VERIFY] Assinatura invalida - ABORT RELEASE.");
                return;
            }
        }

        Line("[HASH] Gerando checksums e manifest ...");
        var (hashCode, hashOut) = RunPowerShell(
            "Tools/Release/Generate-Manifest.ps1",
            $"-Path \"{Path.GetFullPath(outDir)}\" -Version \"{version}\" -BuildId \"{buildId}\"");
        Line(hashOut);
        if (hashCode != 0)
        {
            Debug.LogError("[HASH] Falha ao gerar checksums/manifest - release abortado.");
            return;
        }

        Line("[RELEASE] Release pronto: " + Path.GetFullPath(outDir));
        Line("[RELEASE] Use Tools/Release/Build-Release.ps1 para empacotar, gerar instalador (opcional) e publicar.");
    }

    static string GitShortSha()
    {
        try
        {
            var (code, output) = RunProcess("git", "rev-parse --short HEAD");
            return code == 0 ? output.Trim() : "";
        }
        catch { return ""; }
    }

    static (int code, string output) RunPowerShell(string relativeScriptPath, string args)
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var scriptPath = Path.Combine(projectRoot, relativeScriptPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(scriptPath))
        {
            return (1, $"[ERROR] Script nao encontrado: {scriptPath}");
        }
        return RunProcess("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {args}");
    }

    static (int code, string output) RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
        };
        using var p = Process.Start(psi);
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        var combined = stdout + (string.IsNullOrEmpty(stderr) ? "" : "\n" + stderr);
        return (p.ExitCode, combined);
    }
}
