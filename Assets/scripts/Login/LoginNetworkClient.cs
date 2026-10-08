using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace TOP.Network
{
    [DefaultExecutionOrder(-100)]
    // ============================================================
    // ESTRUTURAS DE DADOS PARA JSON (API Node.js)
    // ============================================================
    [Serializable]
    public class LoginRequestData
    {
        public string username;
        public string password;
    }

    [Serializable]
    public class RegisterRequestData
    {
        public string username;
        public string password;
        public string email;
    }

    [Serializable]
    public class AuthResponse
    {
        public bool success;
        public string token;
        public long accountId;
        public string username;
        public string error;
        public bool isAdmin;
    }

    /// <summary>
    /// Cliente HTTP para comunicacao com a API Node.js de Autenticacao.
    /// Singleton persistente entre cenas.
    /// </summary>
    public class LoginNetworkClient : MonoBehaviour
    {
        public static LoginNetworkClient Instance { get; private set; }

        [Header("Configuracao da API")]
        private string apiBaseUrl => TOP.Services.ApiConfig.AuthUrl;

        [Header("Timeouts")]
        [SerializeField] private int requestTimeout = 10;

        // Token JWT armazenado em memoria volatil (mais seguro que PlayerPrefs)
        public static string AuthToken { get; private set; }
        public static long AccountId { get; private set; }
        public static bool IsAdmin { get; private set; }
        public static string AccountUsername { get; private set; }
        public static bool IsLoggedIn => !string.IsNullOrEmpty(AuthToken);

        // Eventos para UI
        public event Action<string> OnError;
        public event Action<string, long> OnLoginSuccess;
        public event Action OnRegisterSuccess;

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ============================================================
        // REGISTRO
        // ============================================================
        public void Register(string username, string password, string email)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                OnError?.Invoke("Preencha todos os campos.");
                return;
            }
            StartCoroutine(RegisterCoroutine(username, password, email));
        }

        private IEnumerator RegisterCoroutine(string username, string password, string email)
        {
            var payload = new RegisterRequestData
            {
                username = username,
                password = password,
                email = email
            };

            string json = JsonUtility.ToJson(payload);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest request = new UnityWebRequest(apiBaseUrl + "/register", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = requestTimeout;

                double traceStarted = TOP.Diagnostics.GameTrace.Now;
                yield return request.SendWebRequest();
                TOP.Diagnostics.GameTrace.Http(request, traceStarted);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    OnError?.Invoke(GetRequestError(request));
                    yield break;
                }

                // CORRECAO: sintaxe correta do FromJson (um unico < )
                AuthResponse response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text);

                if (response == null || !response.success)
                {
                    OnError?.Invoke(response != null ? response.error : "Erro desconhecido do servidor.");
                    yield break;
                }

                Debug.Log("[LoginNetworkClient] Conta criada: " + username);
                OnRegisterSuccess?.Invoke();
            }
        }

        // ============================================================
        // LOGIN
        // ============================================================
        public void Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                OnError?.Invoke("Preencha usuario e senha.");
                return;
            }
            StartCoroutine(LoginCoroutine(username, password));
        }

        private IEnumerator LoginCoroutine(string username, string password)
        {
            var payload = new LoginRequestData
            {
                username = username,
                password = password
            };

            string json = JsonUtility.ToJson(payload);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (UnityWebRequest request = new UnityWebRequest(apiBaseUrl + "/login", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = requestTimeout;

                double traceStarted = TOP.Diagnostics.GameTrace.Now;
                yield return request.SendWebRequest();
                TOP.Diagnostics.GameTrace.Http(request, traceStarted);

                if (request.result != UnityWebRequest.Result.Success)
                {
                    OnError?.Invoke(GetRequestError(request));
                    yield break;
                }

                // CORRECAO: sintaxe correta do FromJson (um unico < )
                AuthResponse response = JsonUtility.FromJson<AuthResponse>(request.downloadHandler.text);

                if (response == null || !response.success)
                {
                    OnError?.Invoke(response != null ? response.error : "Falha na autenticacao.");
                    yield break;
                }

                // Armazena credenciais em memoria
                AuthToken = response.token;
                AccountId = response.accountId;
                AccountUsername = response.username;
                IsAdmin = response.isAdmin;

                Debug.Log("[LoginNetworkClient] Login OK: " + AccountUsername + " (ID: " + AccountId + ")");
                OnLoginSuccess?.Invoke(AuthToken, AccountId);
            }
        }

        // ============================================================
        // LOGOUT
        // ============================================================
        public void Logout()
        {
            AuthToken = null;
            AccountId = 0;
            IsAdmin = false;
            AccountUsername = null;
            Debug.Log("[LoginNetworkClient] Logout realizado. Token limpo.");
        }

        private string GetRequestError(UnityWebRequest request)
        {
            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                AuthResponse response = TryParseAuthResponse(request.downloadHandler?.text);
                if (response != null && !string.IsNullOrWhiteSpace(response.error))
                    return response.error;

                return $"Erro do servidor ({request.responseCode}): {request.error}";
            }

            return $"Erro de conexao com a API ({apiBaseUrl}). Verifique se o servidor Node esta rodando na porta 3000. Detalhe: {request.error}";
        }

        private AuthResponse TryParseAuthResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<AuthResponse>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
