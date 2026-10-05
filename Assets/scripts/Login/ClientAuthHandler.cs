using UnityEngine;
using Mirror;
using TOP.Core;

namespace TOP.Network
{
    /// <summary>
    /// No CLIENTE: envia o JWT para o servidor Mirror.
    /// Funciona tanto em Host quanto em Client puro.
    /// </summary>
    public class ClientAuthHandler : MonoBehaviour
    {
        [Header("Configuracao")]
        [Tooltip("Se true, desconecta do Mirror caso o login REST ainda nao tenha sido feito.")]
        [SerializeField] private bool disconnectIfNotLoggedIn = true;

        [Tooltip("Delay em segundos antes de enviar o JWT (Host mode precisa de delay maior)")]
        [SerializeField] private float sendDelay = 0.5f;

        private bool _authSent = false;
        private float _connectTime = 0f;

        void OnEnable()
        {
            NetworkClient.OnConnectedEvent += OnConnected;
            NetworkClient.RegisterHandler<AuthResponseMessage>(OnAuthResponse);
        }

        void OnDisable()
        {
            NetworkClient.OnConnectedEvent -= OnConnected;
            NetworkClient.UnregisterHandler<AuthResponseMessage>();
        }

        void Update()
        {
            // Fallback: se conectou mas ainda nao enviou auth, tenta enviar
            if (!_authSent && NetworkClient.isConnected && Time.time > _connectTime + sendDelay)
            {
                Debug.Log("[ClientAuth] Fallback: enviando JWT apos delay...");
                SendAuthToken();
            }
        }

        private void OnConnected()
        {
            _connectTime = Time.time;
            _authSent = false;

            Debug.Log("[ClientAuth] Conectado ao Mirror. Aguardando delay para enviar JWT...");

            // Em Host mode, precisamos esperar um frame para tudo inicializar
            Invoke(nameof(SendAuthToken), sendDelay);
        }

        private void SendAuthToken()
        {
            if (_authSent) return;
            _authSent = true;

            if (!LoginNetworkClient.IsLoggedIn)
            {
                Debug.LogError("[ClientAuth] Conectado ao Mirror sem token JWT!");

                if (disconnectIfNotLoggedIn)
                {
                    NetworkClient.Disconnect();
                }
                return;
            }

            if (!NetworkClient.isConnected)
            {
                Debug.LogWarning("[ClientAuth] NetworkClient nao esta mais conectado. Cancelando envio.");
                return;
            }

            // Envia o token para o servidor validar
            NetworkClient.Send(new AuthRequestMessage
            {
                jwtToken = LoginNetworkClient.AuthToken,
                accountId = LoginNetworkClient.AccountId
            });

            Debug.Log("[ClientAuth] Token JWT enviado ao servidor Mirror.");
        }

        private void OnAuthResponse(AuthResponseMessage msg)
        {
            if (msg.accepted)
            {
                Debug.Log("[ClientAuth] Autenticacao aceita pelo servidor! Pronto para selecionar personagem.");
                GameFlowManager.Instance?.OnMirrorAuthenticated();
            }
            else
            {
                Debug.LogError($"[ClientAuth] Autenticacao rejeitada: {msg.errorMessage}");
                NetworkClient.Disconnect();
            }
        }
    }
}