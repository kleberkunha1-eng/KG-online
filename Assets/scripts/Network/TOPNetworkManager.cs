using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TOP.Services;
using TOP.Data;
using TOP.Player;
using kcp2k;

namespace TOP.Network
{
    [DefaultExecutionOrder(-150)]
    public partial class TOPNetworkManager : NetworkManager
    {
        const float ClientPingInterval = 3f;

        public static TOPNetworkManager Instance { get; private set; }
        public static event Action<string> ClientConnectionFailed;

        [Header("Tales of Pirates - Config")]
        [SerializeField] private float autoSaveInterval = 30f;
        private bool savingPlayers;
        [SerializeField] private string serverInstanceId = "server_01";

        [Header("Development Connection")]
        [SerializeField] private string developmentServerAddress = "127.0.0.1";
        [SerializeField] private ushort developmentServerPort = 7777;

        [Header("Scenes")]
        [SerializeField] private string loginScene = "LoginScene";
        [SerializeField] private string characterSelectScene = "CharacterSelectScene";
        [SerializeField] private string gameScene = "GameScene";

        [Header("Spawn")]
        [SerializeField] private Vector3 defaultSpawnPosition = new Vector3(0f, 1.5f, 0f);
        private string authApiVerifyUrl => TOP.Services.ApiConfig.AuthUrl + "/verify";

        private readonly Dictionary<int, PlayerConnection> _connections = new Dictionary<int, PlayerConnection>();
        private readonly Dictionary<long, NetworkConnectionToClient> _accountConnections = new Dictionary<long, NetworkConnectionToClient>();
        private readonly Dictionary<int, RateLimiter> _rateLimiters = new Dictionary<int, RateLimiter>();
        private readonly Dictionary<int, PendingAuth> _pendingAuths = new Dictionary<int, PendingAuth>();
        private float nextClientPingTime;
        private string clientConnectionError;

        private class PendingAuth
        {
            public NetworkConnectionToClient Connection;
            public float ConnectTime;
            public bool IsAuthenticated;
            public long AccountId;
            public string Username;
            public string SessionToken;
        }

        public override void Awake()
        {
            base.Awake();
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (HasCommandLineFlag("--server") && !NetworkServer.active && !NetworkClient.active)
            {
                ConfigureTransportPort(GetServerListenPort());
                StartServer();
            }
        }

        public bool ConnectToGameServer(out string error)
        {
            error = null;
            if (NetworkClient.active)
            {
                error = "A conexao com o servidor ja esta em andamento.";
                return false;
            }

            string host;
            ushort port;
            if (!ApiConfig.TryGetGameServer(out host, out port))
            {
                if (!Application.isEditor)
                {
                    error = "Servidor multiplayer nao configurado. Atualize o launcher ou contate o suporte.";
                    return false;
                }

                host = developmentServerAddress;
                port = developmentServerPort;
            }

            if (string.IsNullOrWhiteSpace(host) || port == 0)
            {
                error = "Endereco do servidor multiplayer invalido.";
                return false;
            }

            networkAddress = host;
            clientConnectionError = null;
            ConfigureTransportPort(port);
            Debug.Log($"[TOPNetworkManager] Conectando ao servidor Mirror em {host}:{port}.");
            StartClient();
            return true;
        }

        private void ConfigureTransportPort(ushort port)
        {
            if (transport is KcpTransport kcpTransport)
                kcpTransport.Port = port;
            else
                Debug.LogWarning($"[TOPNetworkManager] Transporte '{transport?.GetType().Name}' nao aceita configuracao de porta KCP.");
        }

        private static bool HasCommandLineFlag(string flag)
        {
            foreach (string argument in Environment.GetCommandLineArgs())
                if (string.Equals(argument, flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private ushort GetServerListenPort()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                const string portArgument = "--server-port=";
                if (argument.StartsWith(portArgument, StringComparison.OrdinalIgnoreCase)
                    && ushort.TryParse(argument.Substring(portArgument.Length), out ushort port)
                    && port != 0)
                    return port;
            }

            return developmentServerPort;
        }

        private IEnumerator LoadDedicatedWorld()
        {
            Scene worldScene = SceneManager.GetSceneByName(gameScene);
            if (worldScene.isLoaded)
                yield break;

            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(gameScene, LoadSceneMode.Additive);
            if (loadOperation == null)
            {
                Debug.LogError($"[TOPNetworkManager] Nao foi possivel carregar a cena dedicada '{gameScene}'.");
                yield break;
            }

            yield return loadOperation;
            Debug.Log($"[TOPNetworkManager] Mundo dedicado '{gameScene}' carregado.");
        }

        // =================================================================================
        // SERVER LIFECYCLE
        // =================================================================================
        public override void OnStartServer()
        {
            base.OnStartServer();
            Debug.Log("[TOPNetworkManager] Servidor iniciado");
            StartEnvironmentServer();

            NetworkServer.RegisterHandler<AuthRequestMessage>(OnAuthRequest);
            NetworkServer.RegisterHandler<CharacterListRequest>(OnCharacterListRequest);
            NetworkServer.RegisterHandler<CreateCharacterRequest>(OnCreateCharacterRequest);
            NetworkServer.RegisterHandler<SelectCharacterRequest>(OnSelectCharacterRequest);
            NetworkServer.RegisterHandler<DeleteCharacterRequest>(OnDeleteCharacterRequest);
            NetworkServer.RegisterHandler<ClientPing>(OnClientPing);
            NetworkServer.RegisterHandler<MoveItemRequest>(OnMoveItemRequest);
            NetworkServer.RegisterHandler<EquipItemRequest>(OnEquipItemRequest);
            NetworkServer.RegisterHandler<DropItemRequest>(OnDropItemRequest);
            NetworkServer.RegisterHandler<UseItemRequest>(OnUseItemRequest);
            NetworkServer.RegisterHandler<ChatMessage>(OnChatMessage);

            autoCreatePlayer = false;
            InvokeRepeating(nameof(AutoSaveAll), autoSaveInterval, autoSaveInterval);

            if (HasCommandLineFlag("--server"))
                StartCoroutine(LoadDedicatedWorld());
        }

        public override void OnStopServer()
        {
            CancelInvoke();
            environmentRequests.Clear();
            NetworkServer.UnregisterHandler<TOP.World.EnvironmentChange>();

            Debug.Log("[TOPNetworkManager] Servidor fechando — salvando todos os jogadores...");
            foreach (PlayerConnection playerConn in _connections.Values)
            {
                if (playerConn.State == ConnectionState.InGame && playerConn.PlayerController != null)
                {
                    _ = SavePlayerAsync(playerConn);
                }
            }
            base.OnStopServer();
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);

            PlayerConnection playerConn = new PlayerConnection
            {
                ConnectionId = conn.connectionId,
                State = ConnectionState.Login,
                Connection = conn,
                RateLimiter = new RateLimiter(50, 1f),
                ConnectTime = Time.time
            };

            _connections[conn.connectionId] = playerConn;
            _rateLimiters[conn.connectionId] = playerConn.RateLimiter;
            _pendingAuths[conn.connectionId] = new PendingAuth
            {
                Connection = conn,
                ConnectTime = Time.time,
                IsAuthenticated = false
            };

            Debug.Log($"[Server] Cliente conectado: {conn.connectionId} | Aguardando JWT...");
            StartCoroutine(AuthTimeoutCoroutine(conn));
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            Debug.Log($"[Server] OnServerDisconnect conn={conn.connectionId}");

            if (_connections.TryGetValue(conn.connectionId, out PlayerConnection playerConn))
            {
                // ===== SALVA ANTES DE DESCONECTAR =====
                if (playerConn.State == ConnectionState.InGame && playerConn.PlayerController != null)
                {
                    Debug.Log($"[Server] Salvando personagem {playerConn.PlayerController.CharacterName} antes de desconectar...");
                    _ = SaveAndDisconnectAsync(playerConn);
                    if (conn.identity == null)
                        Destroy(playerConn.PlayerController.gameObject);
                }
                else
                {
                    if (playerConn.AccountId > 0)
                        _accountConnections.Remove(playerConn.AccountId);
                }

                _connections.Remove(conn.connectionId);
                _rateLimiters.Remove(conn.connectionId);
            }

            _pendingAuths.Remove(conn.connectionId);
            base.OnServerDisconnect(conn);
        }

        public override void OnClientConnect()
        {
            nextClientPingTime = Time.unscaledTime + ClientPingInterval;
            Debug.Log("[TOPNetworkManager] Cliente conectado ao servidor Mirror");
            if (!Application.isPlaying) return;
            var authHandler = FindAnyObjectByType<ClientAuthHandler>();
            if (authHandler == null)
            {
                Debug.LogError("[TOPNetworkManager] ClientAuthHandler ausente: nao e possivel autenticar.");
                StopClient();
                return;
            }
            authHandler.BeginAuthentication();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            NetworkClient.RegisterHandler<TOP.World.EnvironmentSnapshot>(TOP.World.WorldEnvironment.Receive);
            NetworkClient.RegisterHandler<TOP.World.EnvironmentReply>(TOP.World.WorldEnvironment.ReceiveReply);
            NetworkClient.RegisterHandler<ServerPong>(_ => { });
            NetworkClient.RegisterHandler<ChatMessage>(ChatService.Receive);
            SceneManager.sceneLoaded += OnClientWorldLoaded;
        }

        public override void OnStopClient()
        {
            NetworkClient.UnregisterHandler<TOP.World.EnvironmentSnapshot>();
            NetworkClient.UnregisterHandler<TOP.World.EnvironmentReply>();
            TOP.World.WorldEnvironment.ResetClock();
            NetworkClient.UnregisterHandler<ServerPong>();
            NetworkClient.UnregisterHandler<ChatMessage>();
            SceneManager.sceneLoaded -= OnClientWorldLoaded;
            base.OnStopClient();
        }

        private void OnClientWorldLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != gameScene || !NetworkClient.isConnected) return;
            NetworkClient.PrepareToSpawnSceneObjects();
            if (!NetworkClient.ready) NetworkClient.Ready();
            Debug.Log("[TOPNetworkManager] Cena do mundo carregada. Cliente pronto para receber objetos.");
        }

        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            if (!_connections.TryGetValue(conn.connectionId, out var player)
                || player.State != ConnectionState.InGame || player.PlayerController == null)
            {
                Debug.LogWarning($"[Server] Ready prematuro ignorado para conn={conn.connectionId}.");
                return;
            }
            if (conn.identity == null)
            {
                if (!NetworkServer.AddPlayerForConnection(conn, player.PlayerController.gameObject))
                {
                    Debug.LogError($"[Server] Falha ao associar jogador para conn={conn.connectionId}.");
                    conn.Disconnect();
                }
            }
            else
                base.OnServerReady(conn);
        }

        public override void Update()
        {
            base.Update();

            if (!NetworkClient.isConnected || Time.unscaledTime < nextClientPingTime) return;
            nextClientPingTime = Time.unscaledTime + ClientPingInterval;
            NetworkClient.Send(new ClientPing { ClientTime = Time.unscaledTime });
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            Debug.Log("[TOPNetworkManager] Cliente desconectado do servidor Mirror");
            ClientConnectionFailed?.Invoke(clientConnectionError
                ?? "A conexao com o servidor do jogo foi encerrada. Tente entrar novamente.");
            clientConnectionError = null;
        }

        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            string endpoint = transport is KcpTransport kcp ? networkAddress + ":" + kcp.Port : networkAddress;
            clientConnectionError = error == TransportError.Timeout
                ? "O servidor do jogo nao respondeu em " + endpoint
                    + ". Verifique o servidor e a conexao; o login na API nao garante que o servidor do jogo esteja acessivel."
                : "Falha na conexao com o servidor do jogo em " + endpoint + ": " + reason;
            Debug.LogWarning("[TOPNetworkManager] " + clientConnectionError);
            ClientConnectionFailed?.Invoke(clientConnectionError);
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == loginScene || currentScene == characterSelectScene)
            {
                Debug.Log($"[NetworkManager] Ignorando spawn em cena de menu: {currentScene}");
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("[NetworkManager] ERRO: playerPrefab nao esta atribuido!");
                return;
            }

            Debug.LogWarning("[NetworkManager] OnServerAddPlayer chamado automaticamente — ignorado.");
        }

        // =================================================================================
        // AUTENTICACAO
        // =================================================================================
        private IEnumerator AuthTimeoutCoroutine(NetworkConnectionToClient conn)
        {
            yield return new WaitForSeconds(15f);
            if (conn == null) yield break;

            PendingAuth pending;
            bool hasPending = _pendingAuths.TryGetValue(conn.connectionId, out pending);
            if (hasPending && !pending.IsAuthenticated)
            {
                Debug.LogWarning($"[Auth] Timeout de autenticacao para conn {conn.connectionId}");
                conn.Send(new AuthResponseMessage { accepted = false, errorMessage = "Tempo de autenticacao expirado." });
                conn.Disconnect();
            }
        }

        private void OnAuthRequest(NetworkConnectionToClient conn, AuthRequestMessage msg)
        {
            PendingAuth pending;
            bool hasPending = _pendingAuths.TryGetValue(conn.connectionId, out pending);

            if (!hasPending)
            {
                Debug.LogWarning($"[Auth] Conn {conn.connectionId} nao encontrada.");
                conn.Disconnect();
                return;
            }

            if (string.IsNullOrWhiteSpace(msg.jwtToken))
            {
                conn.Send(new AuthResponseMessage { accepted = false, errorMessage = "Token JWT ausente." });
                conn.Disconnect();
                return;
            }

            StartCoroutine(ValidateJwtRemoteAndContinue(conn, msg.jwtToken));
        }

        private IEnumerator ValidateJwtRemoteAndContinue(NetworkConnectionToClient conn, string token)
        {
            string jsonPayload = $" {{\"token\":\"{token}\"}}";
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);

            using (UnityWebRequest request = new UnityWebRequest(authApiVerifyUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 5;
                double traceStarted = TOP.Diagnostics.GameTrace.Now;
                yield return request.SendWebRequest();
                TOP.Diagnostics.GameTrace.Http(request, traceStarted);

                bool isValid = false;
                long accountId = 0;
                string username = null;
                string error = "Falha na validacao.";

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var response = JsonUtility.FromJson<VerifyApiResponse>(request.downloadHandler.text);
                        if (response != null && response.valid)
                        {
                            isValid = true;
                            accountId = response.accountId;
                            username = response.username;
                        }
                    }
                    catch (Exception ex) { error = ex.Message; }
                }

                FinalizeAuthentication(conn, isValid, accountId, username, error, token);
            }
        }

        [Serializable]
        private class VerifyApiResponse
        {
            public bool valid;
            public long accountId;
            public string username;
        }

        private void FinalizeAuthentication(NetworkConnectionToClient conn, bool isValid, long accountId, string username, string error, string token)
        {
            if (!isValid)
            {
                Debug.LogWarning($"[Auth] Conn {conn.connectionId} falhou: {error}");
                conn.Send(new AuthResponseMessage { accepted = false, errorMessage = error });
                conn.Disconnect();
                return;
            }

            if (_accountConnections.ContainsKey(accountId))
            {
                Debug.LogWarning($"[Auth] Conta {accountId} ja online. Kickando antiga.");
                if (_accountConnections.TryGetValue(accountId, out var oldConn) && oldConn != null)
                    oldConn.Disconnect();
            }

            PendingAuth pending;
            if (_pendingAuths.TryGetValue(conn.connectionId, out pending))
            {
                pending.IsAuthenticated = true;
                pending.AccountId = accountId;
                pending.Username = username;
                pending.SessionToken = token;
            }

            PlayerConnection playerConn;
            if (_connections.TryGetValue(conn.connectionId, out playerConn))
            {
                playerConn.State = ConnectionState.CharacterSelect;
                playerConn.AccountId = accountId;
                playerConn.Username = username;
                playerConn.SessionToken = token;
            }

            _accountConnections[accountId] = conn;
            conn.Send(new AuthResponseMessage { accepted = true, errorMessage = null });
            Debug.Log($"[Auth] Conn {conn.connectionId} autenticada como '{username}' (AccountID: {accountId})");
        }

        bool CheckRateLimit(int connectionId)
        {
            if (_rateLimiters.TryGetValue(connectionId, out RateLimiter limiter))
                return limiter.CanProcess();
            return true;
        }

        bool IsAuthenticated(int connectionId, out PlayerConnection playerConn, out string error)
        {
            error = null;
            playerConn = null;

            if (!_connections.TryGetValue(connectionId, out playerConn))
            {
                error = "CONNECTION_NOT_FOUND";
                return false;
            }

            PendingAuth pending;
            bool hasPending = _pendingAuths.TryGetValue(connectionId, out pending);
            if (!hasPending || !pending.IsAuthenticated)
            {
                error = "NOT_AUTHENTICATED";
                return false;
            }

            if (playerConn.AccountId == 0 && pending.AccountId > 0)
            {
                playerConn.AccountId = pending.AccountId;
                playerConn.Username = pending.Username;
            }
            if (string.IsNullOrEmpty(playerConn.SessionToken) && !string.IsNullOrEmpty(pending.SessionToken))
                playerConn.SessionToken = pending.SessionToken;

            return true;
        }

        /// <summary>
        /// Retorna o JWT da conta conectada nesta conexao. Usado por Commands (ex.: PlayerInventory)
        /// que precisam chamar o DatabaseService com o token do jogador especifico, nunca um estatico global.
        /// </summary>
        public string GetSessionToken(int connectionId)
        {
            return _connections.TryGetValue(connectionId, out PlayerConnection playerConn) ? playerConn.SessionToken : null;
        }

        // =================================================================================
        // CHARACTER - LIST
        // =================================================================================
        async void OnCharacterListRequest(NetworkConnectionToClient conn, CharacterListRequest msg)
        {
            Debug.Log($"[Server] OnCharacterListRequest conn={conn.connectionId}");

            if (!CheckRateLimit(conn.connectionId)) return;
            if (!IsAuthenticated(conn.connectionId, out PlayerConnection playerConn, out string error))
            {
                conn.Send(new CharacterListResponse { Success = false, Error = error });
                if (error == "NOT_AUTHENTICATED") conn.Disconnect();
                return;
            }

            if (playerConn.State != ConnectionState.CharacterSelect)
            {
                conn.Send(new CharacterListResponse { Success = false, Error = "INVALID_STATE" });
                return;
            }

            List<CharacterPreviewData> dbChars = await DatabaseService.Instance.GetCharacterListAsync(playerConn.AccountId, playerConn.SessionToken);
            Debug.Log($"[Server] Carregou {dbChars.Count} personagens do banco para account {playerConn.AccountId}");

            var netChars = new NetworkCharacterPreview[dbChars.Count];
            for (int i = 0; i < dbChars.Count; i++)
            {
                var c = dbChars[i];
                netChars[i] = new NetworkCharacterPreview
                {
                    Id = c.Id,
                    SlotIndex = c.SlotIndex,
                    Name = c.Name ?? "",
                    Gender = c.Gender,
                    Job = c.Job,
                    Level = c.Level,
                    MapName = c.MapName ?? "garner",
                    PosX = c.PosX,
                    PosY = c.PosY,
                    PosZ = c.PosZ,
                    RotationY = c.RotationY,
                    HairStyle = c.HairStyle,
                    HairColor = c.HairColor,
                    FaceStyle = c.FaceStyle,
                    EquippedItems = c.Equipped ?? new int[0],
                    LastOnlineTicks = c.LastOnline.HasValue ? c.LastOnline.Value.Ticks : 0
                };
            }

            conn.Send(new CharacterListResponse { Success = true, Characters = netChars });
            Debug.Log($"[Server] CharacterListResponse enviado com {netChars.Length} personagens.");
        }

        // =================================================================================
        // CHARACTER - CREATE
        // =================================================================================
        async void OnCreateCharacterRequest(NetworkConnectionToClient conn, CreateCharacterRequest msg)
        {
            Debug.Log($"[Server] OnCreateCharacterRequest conn={conn.connectionId} | Slot={msg.SlotIndex} | Name={msg.Name} | Job={msg.Job}");

            if (!CheckRateLimit(conn.connectionId)) return;
            if (!IsAuthenticated(conn.connectionId, out PlayerConnection playerConn, out string error))
            {
                conn.Send(new CreateCharacterResponse { Success = false, Error = error });
                if (error == "NOT_AUTHENTICATED") conn.Disconnect();
                return;
            }

            if (playerConn.State != ConnectionState.CharacterSelect)
            {
                conn.Send(new CreateCharacterResponse { Success = false, Error = "INVALID_STATE" });
                return;
            }

            if (string.IsNullOrWhiteSpace(msg.Name) || msg.Name.Length < 3)
            {
                conn.Send(new CreateCharacterResponse { Success = false, Error = "NAME_TOO_SHORT" });
                return;
            }

            if (msg.SlotIndex > 2)
            {
                conn.Send(new CreateCharacterResponse { Success = false, Error = "INVALID_SLOT" });
                return;
            }

            var city = StartCities.Get(msg.StartCity);
            if (city == null || !city.enabled)
            {
                conn.Send(new CreateCharacterResponse { Success = false, Error = "CITY_UNAVAILABLE" });
                return;
            }

            var result = await DatabaseService.Instance.CreateCharacterAsync(
                playerConn.AccountId, msg.SlotIndex, msg.Name, msg.Gender, msg.Job,
                msg.HairStyle, msg.HairColor, playerConn.SessionToken, city, msg.FaceStyle);

            conn.Send(new CreateCharacterResponse
            {
                Success = result.success,
                CharacterId = result.charId,
                Error = result.success ? null : result.error
            });

            if (result.success)
                Debug.Log($"[CharCreate] ✅ {playerConn.Username} criou '{msg.Name}' (ID={result.charId})");
            else
                Debug.LogWarning($"[CharCreate] ❌ Falha: {result.error}");
        }

        // =================================================================================
        // CHARACTER - DELETE
        // =================================================================================
        async void OnDeleteCharacterRequest(NetworkConnectionToClient conn, DeleteCharacterRequest msg)
        {
            if (!CheckRateLimit(conn.connectionId)) return;
            if (!IsAuthenticated(conn.connectionId, out PlayerConnection playerConn, out string error))
            {
                conn.Send(new DeleteCharacterResponse { Success = false, Error = error });
                if (error == "NOT_AUTHENTICATED") conn.Disconnect();
                return;
            }

            var result = await DatabaseService.Instance.DeleteCharacterAsync(
                msg.CharacterId, playerConn.AccountId, msg.Password, playerConn.SessionToken);

            conn.Send(new DeleteCharacterResponse
            {
                Success = result.success,
                Error = result.success ? null : result.error
            });
        }

        // =================================================================================
        // CHARACTER - SELECT (ENTER WORLD) — CORRIGIDO: spawn único + fallback posição
        // =================================================================================
        async void OnSelectCharacterRequest(NetworkConnectionToClient conn, SelectCharacterRequest msg)
        {
            Debug.Log($"[Server] OnSelectCharacterRequest conn={conn.connectionId} | CharId={msg.CharacterId}");

            if (!CheckRateLimit(conn.connectionId)) return;
            if (!IsAuthenticated(conn.connectionId, out PlayerConnection playerConn, out string error))
            {
                conn.Send(new SelectCharacterResponse { Success = false, Error = error });
                if (error == "NOT_AUTHENTICATED") conn.Disconnect();
                return;
            }

            if (playerConn.State != ConnectionState.CharacterSelect)
            {
                conn.Send(new SelectCharacterResponse { Success = false, Error = "INVALID_STATE" });
                return;
            }

            CharacterData charData = null;
            try
            {
                charData = await DatabaseService.Instance.LoadCharacterAsync(msg.CharacterId, playerConn.AccountId, playerConn.SessionToken);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EnterWorld] Erro ao carregar personagem do banco: {ex}");
                conn.Send(new SelectCharacterResponse { Success = false, Error = "DB_ERROR" });
                return;
            }

            if (charData == null)
            {
                Debug.LogWarning($"[Server] Personagem {msg.CharacterId} não encontrado.");
                conn.Send(new SelectCharacterResponse { Success = false, Error = "CHARACTER_NOT_FOUND" });
                return;
            }

            if (!_connections.TryGetValue(conn.connectionId, out var currentConnection)
                || currentConnection != playerConn)
            {
                Debug.LogWarning($"[EnterWorld] Conn={conn.connectionId} desconectou durante o carregamento.");
                return;
            }

            // ===== SPAWN DO PLAYER (ÚNICO) =====
            GameObject playerObj;

            if (conn.identity != null)
            {
                // Já existe um player object para esta conexão (raro com autoCreatePlayer=false)
                playerObj = conn.identity.gameObject;
                Debug.Log($"[EnterWorld] Usando player existente: {playerObj.name}");
            }
            else
            {
                if (playerPrefab == null)
                {
                    Debug.LogError("[EnterWorld] ERRO: playerPrefab nao atribuido!");
                    conn.Send(new SelectCharacterResponse { Success = false, Error = "SERVER_ERROR" });
                    return;
                }

                playerObj = Instantiate(playerPrefab);
                playerObj.name = $"Player_{conn.connectionId}_{charData.Name}";
                playerObj.SetActive(false); // Desativa para evitar Start()/Awake() prematuros


                Debug.Log($"[EnterWorld] Novo player instanciado: {playerObj.name}");
            }

            // ===== POSICIONAMENTO ÚNICO (com fallback para spawn padrão) =====
            Vector3 spawnPos = new Vector3(charData.PosX, charData.PosY, charData.PosZ);
            if (spawnPos == Vector3.zero)
            {
                spawnPos = defaultSpawnPosition;
                Debug.Log($"[EnterWorld] Posição zero detectada no banco, usando spawn padrão: {spawnPos}");
            }

            playerObj.transform.position = spawnPos;
            playerObj.transform.rotation = Quaternion.Euler(0, charData.RotationY, 0);

            // Ativa APENAS se estiver desativado (evita re-trigger de componentes)
            if (!playerObj.activeSelf)
                playerObj.SetActive(true);

            // ===== INICIALIZAÇÃO DO CONTROLLER =====
            PlayerController controller = playerObj.GetComponent<PlayerController>();
            if (controller == null)
            {
                Debug.LogError("[EnterWorld] ERRO: PlayerPrefab sem PlayerController!");
                if (conn.identity == null) Destroy(playerObj);
                conn.Send(new SelectCharacterResponse { Success = false, Error = "SERVER_ERROR" });
                return;
            }

            controller.InitializeFromCharacterData(charData);
            playerConn.PlayerController = controller;
            playerConn.CharacterId = msg.CharacterId;
            playerConn.State = ConnectionState.InGame;

            // ===== RESPOSTA AO CLIENTE =====
            conn.Send(new SelectCharacterResponse
            {
                Success = true,
                CharacterId = msg.CharacterId,
                MapName = charData.MapName,
                Position = spawnPos,
                RotationY = charData.RotationY
            });

            // ===== AUDIT LOG (não-crítico) =====
            try
            {
                await DatabaseService.Instance.LogAuditAsync(
                    playerConn.AccountId, msg.CharacterId, "ENTER_WORLD",
                    new { map = charData.MapName, pos = spawnPos }, conn.address, playerConn.SessionToken);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EnterWorld] Falha ao registrar audit log: {ex.Message}");
            }

            Debug.Log($"[EnterWorld] ✅ {charData.Name} entrou em {charData.MapName} em {spawnPos}");
        }

        // =================================================================================
        // INVENTORY / EQUIPMENT / ITEMS
        // =================================================================================
        void OnMoveItemRequest(NetworkConnectionToClient conn, MoveItemRequest msg)
        {
            if (!CheckRateLimit(conn.connectionId)) return;
            if (conn.identity == null) return;
            PlayerInventory inv = conn.identity.GetComponent<PlayerInventory>();
            if (inv != null) inv.MoveItemOnServer(msg.FromSlot, msg.ToSlot);
        }

        void OnEquipItemRequest(NetworkConnectionToClient conn, EquipItemRequest msg)
        {
            if (!CheckRateLimit(conn.connectionId)) return;
            if (conn.identity == null) return;
            PlayerEquipment equip = conn.identity.GetComponent<PlayerEquipment>();
            if (equip != null) equip.EquipFromInventory(msg.InventorySlot, msg.TargetSlot);
        }

        void OnDropItemRequest(NetworkConnectionToClient conn, DropItemRequest msg)
        {
            if (!CheckRateLimit(conn.connectionId)) return;
            if (conn.identity == null) return;
            PlayerInventory inv = conn.identity.GetComponent<PlayerInventory>();
            if (inv != null) inv.DropItemOnServer(msg.SlotIndex, msg.Quantity, msg.DropPosition);
        }

        void OnUseItemRequest(NetworkConnectionToClient conn, UseItemRequest msg)
        {
            if (!CheckRateLimit(conn.connectionId)) return;
            if (conn.identity == null) return;
            PlayerConsumables consumables = conn.identity.GetComponent<PlayerConsumables>();
            if (consumables != null) consumables.UseItemOnServer(msg.SlotIndex);
        }

        // =================================================================================
        // CHAT
        // =================================================================================
        void OnChatMessage(NetworkConnectionToClient conn, ChatMessage msg)
        {
            if (!CheckRateLimit(conn.connectionId)) return;
            if (string.IsNullOrWhiteSpace(msg.Text) || msg.Text.Length > ChatService.MaxLength)
            {
                conn.Send(new ChatMessage { Channel = ChatChannel.System, Text = "Mensagem de chat invalida." });
                return;
            }
            if (conn.identity == null) return;
            if (!Enum.IsDefined(typeof(ChatChannel), msg.Channel) || msg.Channel == ChatChannel.System)
            {
                conn.Send(new ChatMessage { Channel = ChatChannel.System, Text = "Canal de chat invalido." });
                return;
            }

            var senderPc = conn.identity.GetComponent<PlayerController>();
            string senderName = senderPc != null ? senderPc.CharacterName : "???";

            switch (msg.Channel)
            {
                case ChatChannel.Local:
                    var local = new ChatMessage
                    {
                        Channel = ChatChannel.Local, Text = msg.Text.Trim(),
                        SenderName = senderName, SenderNetId = conn.identity.netId
                    };
                    foreach (var recipient in NetworkServer.connections.Values)
                    {
                        if (recipient?.identity == null) continue;
                        var player = recipient.identity.GetComponent<PlayerController>();
                        if (player == null || senderPc == null) continue;
                        if (ChatService.IsLocalRecipient(senderPc.MapName, conn.identity.transform.position,
                            player.MapName, recipient.identity.transform.position))
                            recipient.Send(local);
                    }
                    break;
                case ChatChannel.Whisper:
                    SendWhisper(conn, senderName, msg);
                    break;

                case ChatChannel.Party:
                    SendToParty(conn, senderName, msg);
                    break;

                case ChatChannel.Guild:
                    // Ainda nao ha sistema de guilda; avisa apenas quem enviou.
                    conn.Send(new ChatMessage { Channel = ChatChannel.System, Text = "Sistema de guilda ainda nao disponivel." });
                    break;

                default:
                    // World, Shout, Trade, System: todos tratados como broadcast global por enquanto.
                    NetworkServer.SendToAll(new ChatMessage
                    {
                        Channel = msg.Channel,
                        Text = $"{senderName}: {msg.Text}"
                    });
                    break;
            }
        }

        void SendWhisper(NetworkConnectionToClient sender, string senderName, ChatMessage msg)
        {
            if (string.IsNullOrWhiteSpace(msg.TargetName)) return;
            foreach (var kv in NetworkServer.connections)
            {
                var pc = kv.Value?.identity != null ? kv.Value.identity.GetComponent<PlayerController>() : null;
                if (pc == null || pc.CharacterName != msg.TargetName) continue;

                kv.Value.Send(new ChatMessage { Channel = ChatChannel.Whisper, Text = $"{senderName} sussurra: {msg.Text}", TargetName = senderName });
                sender.Send(new ChatMessage { Channel = ChatChannel.Whisper, Text = $"Para {msg.TargetName}: {msg.Text}", TargetName = msg.TargetName });
                return;
            }
            sender.Send(new ChatMessage { Channel = ChatChannel.System, Text = $"Jogador '{msg.TargetName}' nao encontrado." });
        }

        void SendToParty(NetworkConnectionToClient sender, string senderName, ChatMessage msg)
        {
            var party = sender.identity.GetComponent<PlayerParty>();
            if (party == null || party.PartyId == 0)
            {
                sender.Send(new ChatMessage { Channel = ChatChannel.System, Text = "Voce nao esta em um grupo." });
                return;
            }
            foreach (var targetConn in PlayerParty.ConnectionsInPartyOf(party))
                targetConn.Send(new ChatMessage { Channel = ChatChannel.Party, Text = $"{senderName}: {msg.Text}" });
        }

        // =================================================================================
        // PING
        // =================================================================================
        void OnClientPing(NetworkConnectionToClient conn, ClientPing msg)
        {
            if (_connections.TryGetValue(conn.connectionId, out PlayerConnection playerConn))
                playerConn.LastPingTime = Time.time;

            conn.Send(new ServerPong
            {
                ClientTime = msg.ClientTime,
                ServerTime = Time.time
            });
        }

        // =================================================================================
        // AUTO SAVE + SAVE ON DISCONNECT
        // =================================================================================
        async void AutoSaveAll()
        {
            if (savingPlayers) return;
            savingPlayers = true;
            try
            {
                foreach (PlayerConnection conn in _connections.Values.ToArray())
                {
                    if (conn.State == ConnectionState.InGame && conn.PlayerController != null)
                    {
                        try { await SavePlayerAsync(conn); }
                        catch (Exception ex) { Debug.LogError($"[AutoSave] Erro: {ex}"); }
                    }
                }
            }
            finally { savingPlayers = false; }
        }

        async Task SavePlayerAsync(PlayerConnection playerConn)
        {
            if (playerConn?.PlayerController == null) return;
            if (playerConn.PlayerController.GetComponent<PlayerQuests>()?.HasPendingCompletion ?? false)
            {
                Debug.Log("[SavePlayer] Entrega atomica em andamento; nao salvar um snapshot anterior.");
                return;
            }

            CharacterData data = playerConn.PlayerController.GetCharacterData();
            if (data != null)
            {
                data.PosX = playerConn.PlayerController.transform.position.x;
                data.PosY = playerConn.PlayerController.transform.position.y;
                data.PosZ = playerConn.PlayerController.transform.position.z;
                data.RotationY = playerConn.PlayerController.transform.rotation.eulerAngles.y;

                bool saved = await DatabaseService.Instance.SaveCharacterAsync(data, playerConn.SessionToken);
                if (saved)
                    Debug.Log($"[SavePlayer] ✅ {data.Name} salvo em {data.MapName} ({data.PosX:F1}, {data.PosY:F1}, {data.PosZ:F1})");
                else
                {
                    Debug.LogError($"[SavePlayer] Falha ao salvar {data.Name}; verifique a API de persistencia.");
                    if (DatabaseService.Instance.RequiresCharacterReload(data.Id) && playerConn.PlayerController != null)
                    {
                        playerConn.PlayerController.RpcShowMessage("Reconecte para recuperar seu personagem com seguranca.", PlayerMessageType.Warning);
                        playerConn.Connection?.Disconnect();
                    }
                }
            }
        }

        async Task SaveAndDisconnectAsync(PlayerConnection playerConn)
        {
            if (playerConn?.PlayerController != null
                && !(playerConn.PlayerController.GetComponent<PlayerQuests>()?.HasPendingCompletion ?? false))
            {
                CharacterData data = playerConn.PlayerController.GetCharacterData();
                if (data != null)
                {
                    data.PosX = playerConn.PlayerController.transform.position.x;
                    data.PosY = playerConn.PlayerController.transform.position.y;
                    data.PosZ = playerConn.PlayerController.transform.position.z;
                    data.RotationY = playerConn.PlayerController.transform.rotation.eulerAngles.y;

                    bool saved = await DatabaseService.Instance.SaveCharacterAsync(data, playerConn.SessionToken);
                    Debug.Log($"[SaveAndDisconnect] {(saved ? "✅" : "❌")} {data.Name} salvo antes de desconectar.");
                }
            }

            _accountConnections.Remove(playerConn.AccountId);
        }

        public bool IsAccountOnline(long accountId) => _accountConnections.ContainsKey(accountId);
    }
}