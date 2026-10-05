using System;
using System.IO;
using UnityEngine;

namespace TOP.Services
{
    [Serializable] class ApiConfigFile { public string apiUrl; }

    /// <summary>
    /// Endereco da API do jogo. Ordem: argumento --api=URL (passado pelo launcher), arquivo api.json ao lado do jogo, padrao local.
    /// </summary>
    public static class ApiConfig
    {
        const string Fallback = "http://127.0.0.1:3000";
        static string root;

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
                        if (File.Exists(file)) found = JsonUtility.FromJson<ApiConfigFile>(File.ReadAllText(file))?.apiUrl;
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
    }
}