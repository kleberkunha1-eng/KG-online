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
        public const string PublishedApiUrl = "https://gamekg.pages.dev";
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
                if (string.IsNullOrWhiteSpace(found) && HasEditorGameServerAddressFile())
                {
                    found = PublishedApiUrl;
                    Debug.Log("[ApiConfig] Usando a API publicada para autenticar no servidor de teste do Editor.");
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

            if (Application.isEditor && (string.IsNullOrWhiteSpace(host) || port == 0)
                && TryReadEditorGameServerAddress(out string editorHost, out ushort editorPort))
            {
                if (string.IsNullOrWhiteSpace(host)) host = editorHost;
                if (port == 0) port = editorPort;
            }

            return !string.IsNullOrWhiteSpace(host) && port != 0;
        }

        static bool TryReadEditorGameServerAddress(out string host, out ushort port)
        {
            host = null;
            port = 0;

            string path = EditorGameServerAddressPath();
            if (!File.Exists(path)) return false;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException e)
            {
                Debug.LogWarning("[ApiConfig] Nao foi possivel ler Tools/server-address.txt: " + e.Message);
                return false;
            }
            catch (UnauthorizedAccessException e)
            {
                Debug.LogWarning("[ApiConfig] Sem permissao para ler Tools/server-address.txt: " + e.Message);
                return false;
            }

            foreach (string entry in lines)
            {
                string line = entry.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                int separator = line.LastIndexOf(':');
                if (separator <= 0 || separator == line.Length - 1
                    || !ushort.TryParse(line.Substring(separator + 1).Trim(), out ushort parsedPort)
                    || parsedPort == 0)
                {
                    Debug.LogWarning("[ApiConfig] Tools/server-address.txt invalido; esperado host:porta.");
                    return false;
                }

                host = line.Substring(0, separator).Trim();
                if (host.Length == 0)
                {
                    Debug.LogWarning("[ApiConfig] Tools/server-address.txt nao contem um host.");
                    return false;
                }

                port = parsedPort;
                Debug.Log("[ApiConfig] Servidor de teste do Editor: " + host + ":" + port);
                return true;
            }

            Debug.LogWarning("[ApiConfig] Tools/server-address.txt nao contem um endereco host:porta.");
            return false;
        }

        static bool HasEditorGameServerAddressFile()
        {
            return Application.isEditor && File.Exists(EditorGameServerAddressPath());
        }

        static string EditorGameServerAddressPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
            return Path.Combine(projectRoot, "Tools", "server-address.txt");
        }
    }
}