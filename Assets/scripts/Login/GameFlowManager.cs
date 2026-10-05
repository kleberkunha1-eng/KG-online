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
            SceneManager.LoadScene(charSelectSceneName);
        }

        /// <summary>
        /// Chamado quando o jogador seleciona um personagem e entra no mundo.
        /// </summary>
        public void EnterGameWorld(string mapName)
        {
            Debug.Log($"[GameFlow] Entrando no mundo: {mapName}");
            SceneManager.LoadScene(gameSceneName);
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