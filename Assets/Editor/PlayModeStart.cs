using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using UnityEditor;
using UnityEditor.SceneManagement;
using TOP.Services;

namespace TOP.EditorTools
{
    // Escolhe a cena inicial do botao Play e oferece "Iniciar jogo" pelo fluxo completo:
    // LoginScene -> CharacterSelectScene -> GameScene (a API de login e iniciada se necessario).
    [InitializeOnLoad]
    public static class PlayModeStart
    {
        const string ModeKey = "TOP.PlayStartMode"; // 0 = fluxo completo (padrao), 1 = direto na GameScene
        const string FullMenu = "TOP/Play Mode/Fluxo completo (Login)";
        const string DevMenu = "TOP/Play Mode/Direto na GameScene (dev offline)";
        const string Scenes = "Assets/Scenes/";
        const int ApiPort = 3000;

        static PlayModeStart() { Apply(); EditorApplication.playModeStateChanged += OnState; }

        static int Mode { get => EditorPrefs.GetInt(ModeKey, 0); set { EditorPrefs.SetInt(ModeKey, value); Apply(); } }

        static void Apply()
        {
            string scene = Mode == 0 && !SessionState.GetBool("TOP.WorldEntrySmokeActive", false)
                ? "LoginScene" : "GameScene";
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Scenes + scene + ".unity");
        }

        static void OnState(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode && Mode == 0
                && !SessionState.GetBool("TOP.WorldEntrySmokeActive", false)) EnsureApi();
        }

        [MenuItem("TOP/Iniciar jogo (fluxo completo) %#p", false, 0)]
        static void StartFullFlow()
        {
            Mode = 0;
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }

        [MenuItem(FullMenu)] static void SetFull() { Mode = 0; }
        [MenuItem(FullMenu, true)] static bool ValidateFull() { Menu.SetChecked(FullMenu, Mode == 0); return true; }
        [MenuItem(DevMenu)] static void SetDev() { Mode = 1; }
        [MenuItem(DevMenu, true)] static bool ValidateDev() { Menu.SetChecked(DevMenu, Mode == 1); return true; }

        static bool ApiUp()
        {
            try { using (var c = new TcpClient()) { var r = c.BeginConnect("127.0.0.1", ApiPort, null, null); return r.AsyncWaitHandle.WaitOne(300) && c.Connected; } }
            catch { return false; }
        }

        static void EnsureApi()
        {
            if (!string.Equals(ApiConfig.Root, "http://127.0.0.1:3000", System.StringComparison.OrdinalIgnoreCase)) return;
            if (ApiUp()) return;
            string dir = Path.GetFullPath("API");
            if (!File.Exists(Path.Combine(dir, "server.js"))) { UnityEngine.Debug.LogWarning("[PlayMode] API/server.js nao encontrado."); return; }
            try
            {
                var p = Process.Start(new ProcessStartInfo("node", "server.js") { WorkingDirectory = dir, CreateNoWindow = true, UseShellExecute = false });
                SessionState.SetInt("TOP.ApiPid", p.Id);
                for (int i = 0; i < 20 && !ApiUp(); i++) System.Threading.Thread.Sleep(250);
                UnityEngine.Debug.Log("[PlayMode] API de login iniciada na porta " + ApiPort + ".");
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("[PlayMode] Nao foi possivel iniciar a API (node instalado?): " + e.Message); }
        }
    }
}
