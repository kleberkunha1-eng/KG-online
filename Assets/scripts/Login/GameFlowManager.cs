using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using TOP.Network;

namespace TOP.Core
{
    /// <summary>
    /// Orquestra o fluxo de cenas do jogo: Login → CharSelect → Game.
    /// Singleton persistente (DDOL).
    /// </summary>
    public class GameFlowManager : MonoBehaviour
    {
        public static GameFlowManager Instance { get; private set; }

        [Header("Cenas")]
        [SerializeField] private string loginSceneName = "LoginScene";
        [SerializeField] private string charSelectSceneName = "CharacterSelectScene";
        [SerializeField] private string gameSceneName = "GameScene";
        AsyncOperation worldLoad;
        ThreadPriority previousLoadingPriority;
        bool loadingPriorityChanged;
        float worldLoadStarted;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// Chamado pelo ClientAuthHandler quando o servidor Mirror aceita o JWT.
        /// </summary>
        public void OnMirrorAuthenticated()
        {
            Debug.Log("[GameFlow] JWT aceito pelo Mirror. Indo para selecao de personagem...");
            CharacterListClient.Request();
            SceneManager.LoadSceneAsync(charSelectSceneName);
        }

        /// <summary>
        /// Chamado quando o jogador seleciona um personagem e entra no mundo.
        /// </summary>
        public void EnterGameWorld(string mapName)
        {
            if (worldLoad != null) return;
            Debug.Log($"[GameFlow] Entrando no mundo: {mapName}");
            previousLoadingPriority = Application.backgroundLoadingPriority;
            loadingPriorityChanged = true;
            Application.backgroundLoadingPriority = ThreadPriority.High;
            worldLoadStarted = Time.realtimeSinceStartup;
            worldLoad = SceneManager.LoadSceneAsync(gameSceneName);
            worldLoad.completed += OnWorldLoaded;
        }

        void OnWorldLoaded(AsyncOperation operation)
        {
            RestoreLoadingPriority();
            Debug.Log($"[Loading] GameScene ready in {Time.realtimeSinceStartup - worldLoadStarted:F2}s.");
            worldLoad = null;
        }

        void RestoreLoadingPriority()
        {
            if (!loadingPriorityChanged) return;
            Application.backgroundLoadingPriority = previousLoadingPriority;
            loadingPriorityChanged = false;
        }

        void OnDestroy()
        {
            if (worldLoad != null) worldLoad.completed -= OnWorldLoaded;
            RestoreLoadingPriority();
            if (Instance == this) Instance = null;
        }

        void OnGUI()
        {
            if (worldLoad == null) return;
            float progress = Mathf.Clamp01(worldLoad.progress / .9f);
            GUI.Box(new Rect(Screen.width * .5f - 170, Screen.height * .5f - 35, 340, 70),
                $"Carregando mundo: {progress:P0}\n{Time.realtimeSinceStartup - worldLoadStarted:F1}s");
        }

        /// <summary>
        /// Volta para login (desconecta de tudo).
        /// </summary>
        public void Logout()
        {
            if (NetworkClient.isConnected)
                NetworkClient.Disconnect();

            LoginNetworkClient.Instance?.Logout();
            SceneManager.LoadScene(loginSceneName);
        }
    }
}