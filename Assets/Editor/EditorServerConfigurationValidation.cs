using System;
using System.IO;
using System.Reflection;
using System.Text;
using TOP.Services;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class EditorServerConfigurationValidation
{
    [Serializable]
    class ExpectedApiConfig
    {
        public string apiUrl;
    }

    static readonly string Request = Path.Combine(Application.dataPath, "../Tools/validate-editor-server.request");

    static EditorServerConfigurationValidation() { EditorApplication.update += Poll; }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        Run();
    }

    [MenuItem("Tools/PKO/Validate Editor Server Configuration")]
    public static void Run()
    {
        var report = new StringBuilder();
        int passed = 0, failed = 0;
        Action<bool, string> check = (ok, message) =>
        {
            report.AppendLine((ok ? "PASS " : "FAIL ") + message);
            if (ok) passed++; else failed++;
        };
        var type = typeof(ApiConfig);
        var rootField = type.GetField("root", BindingFlags.Static | BindingFlags.NonPublic);
        var configField = type.GetField("config", BindingFlags.Static | BindingFlags.NonPublic);
        object previousRoot = rootField.GetValue(null), previousConfig = configField.GetValue(null);
        string fixture = Path.Combine(Application.dataPath, "../Library/editor-server-validation-fixture.txt");
        try
        {
            rootField.SetValue(null, null);
            configField.SetValue(null, null);
            string expectedApi = ApiConfig.PublishedApiUrl;
            string apiFile = Path.Combine(Application.dataPath, "../api.json");
            if (File.Exists(apiFile))
            {
                var local = JsonUtility.FromJson<ExpectedApiConfig>(File.ReadAllText(apiFile));
                if (!string.IsNullOrWhiteSpace(local?.apiUrl))
                    expectedApi = local.apiUrl.Trim().TrimEnd('/');
            }
            foreach (var argument in Environment.GetCommandLineArgs())
                if (argument.StartsWith("--api=", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(argument.Substring(6)))
                    expectedApi = argument.Substring(6).Trim().TrimEnd('/');
            check(ApiConfig.Root == expectedApi, "Editor keeps the explicitly configured API/JWT identity.");
            check(ApiConfig.TryGetGameServer(out string localHost, out ushort localPort)
                && localHost == "127.0.0.1" && localPort != 0, "Editor connects directly to a local dedicated server.");
            check(ApiConfig.TryGetPublishedGameServer(out string publicHost, out ushort publicPort)
                && publicHost != localHost && publicPort != 0, "Published endpoint is separate from Editor override.");

            var parser = type.GetMethod("TryReadGameServerAddress", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var test in new[]
            {
                ("# comment\r\n\r\n 127.0.0.1:7777 \r\n", true, "127.0.0.1", (ushort)7777),
                ("game.example:22538", true, "game.example", (ushort)22538),
                ("127.0.0.1:0", false, (string)null, (ushort)0),
                ("127.0.0.1:65536", false, (string)null, (ushort)0),
                ("127.0.0.1:not-a-port", false, (string)null, (ushort)0),
                (":7777", false, (string)null, (ushort)0),
                ("# comment only", false, (string)null, (ushort)0)
            })
            {
                File.WriteAllText(fixture, test.Item1);
                object[] args = { fixture, null, (ushort)0 };
                bool accepted = (bool)parser.Invoke(null, args);
                check(accepted == test.Item2 && Equals(args[1], test.Item3) && Equals(args[2], test.Item4),
                    "Endpoint parsing: " + test.Item1.Replace("\r", "").Replace("\n", " / "));
            }
            File.Delete(fixture);
            object[] missingArgs = { fixture, null, (ushort)0 };
            check(!(bool)parser.Invoke(null, missingArgs), "Missing address file is not accepted.");

            object explicitConfig = Activator.CreateInstance(configField.FieldType, true);
            configField.FieldType.GetField("gameServerHost").SetValue(explicitConfig, "explicit.example");
            configField.FieldType.GetField("gameServerPort").SetValue(explicitConfig, (ushort)30001);
            configField.SetValue(null, explicitConfig);
            check(ApiConfig.TryGetGameServer(out string explicitHost, out ushort explicitPort)
                && explicitHost == "explicit.example" && explicitPort == 30001,
                "Explicit api.json destination keeps priority over Editor override.");
        }
        catch (Exception e) { Debug.LogException(e); check(false, e.ToString()); }
        finally
        {
            rootField.SetValue(null, previousRoot);
            configField.SetValue(null, previousConfig);
            if (File.Exists(fixture)) File.Delete(fixture);
        }
        report.Insert(0, $"Passed={passed} Failed={failed}\n");
        File.WriteAllText(Path.Combine(Application.dataPath, "../Tools/editor-server-validation-results.txt"), report.ToString());
        if (failed > 0) Debug.LogError(report.ToString()); else Debug.Log(report.ToString());
    }
}
