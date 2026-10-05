using System;
using System.IO;
using UnityEngine;

namespace TOP.Services
{
    [Serializable] class ApiConfigFile
    {
        public string apiUrl;
        public string gameServerHost;
        public ushort gameServerPort;
    }

    /// <summary>
    /// Endereco da API do jogo. Ordem: argumento --api=URL (passado pelo launcher), arquivo api.json ao lado do jogo, padrao local.
    /// </summary>
    public static class ApiConfig
    {
        const string Fallback = "http://127.0.0.1:3000";
        static string root;
        static ApiConfigFile config;

        public static string Root
        {
            get
            {
                if (root != null) return root;
                string found = null;
                foreach (var a in Environment.GetCommandLineArgs())
                    if (a.StartsWith("--api=", StringComparison.OrdinalIgnoreCase)) found = a.Substring(6);
                if (string.IsNullOrEmpty(found))
                {
                    try
                    {
                        string file = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "api.json");
                        if (File.Exists(file))
                        {
                            config = JsonUtility.FromJson<ApiConfigFile>(File.ReadAllText(file));
                            found = config?.apiUrl;
                        }
                    }
                    catch (Exception e) { Debug.LogWarning("[ApiConfig] api.json invalido: " + e.Message); }
                }
                root = string.IsNullOrWhiteSpace(found) ? Fallback : found.Trim().TrimEnd('/');
                return root;
            }
        }

        public static string AuthUrl => Root + "/api/auth";
        public static string AdminUrl => Root + "/api";
        public static string GameUrl => Root + "/api/game";

        public static bool TryGetGameServer(out string host, out ushort port)
        {
            host = null;
            port = 0;

            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument.StartsWith("--game-server=", StringComparison.OrdinalIgnoreCase))
                    host = argument.Substring("--game-server=".Length).Trim();
                else if (argument.StartsWith("--game-server-port=", StringComparison.OrdinalIgnoreCase)
                    && ushort.TryParse(argument.Substring("--game-server-port=".Length), out ushort parsedPort))
                    port = parsedPort;
            }

            // Root initializes the configuration file if it has not been loaded yet.
            _ = Root;
            if (string.IsNullOrWhiteSpace(host))
                host = config?.gameServerHost?.Trim();
            if (port == 0)
                port = config?.gameServerPort ?? 0;

            return !string.IsNullOrWhiteSpace(host) && port != 0;
        }
    }
}