using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TOP.Player;
using TOP.Network;
using Mirror;
using TOP.Services;

namespace TOP.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Panels")]
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private GameObject characterSelectPanel;
        [SerializeField] private GameObject hudPanel;
        [SerializeField] private GameObject chatPanel;
        [SerializeField] private GameObject inventoryPanel;

        [Header("HUD")]
        [SerializeField] private Slider hpBar;
        [SerializeField] private Slider mpBar;
        [SerializeField] private Slider spBar;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI goldText;
        [SerializeField] private TextMeshProUGUI messageText;

        [Header("Chat")]
        [SerializeField] private TMP_InputField chatInput;
        [SerializeField] private TextMeshProUGUI chatHistory;

        [SerializeField] private Slider expBar;
        [SerializeField] private TextMeshProUGUI hpText, mpText, spText, expText;
        private PlayerController localPlayer;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnEnable() { ChatService.Received += OnNetworkChat; }
        void OnDisable() { ChatService.Received -= OnNetworkChat; }
        void OnNetworkChat(ChatMessage message)
        {
            AddChatMessage(string.IsNullOrEmpty(message.SenderName) ? message.Text : message.SenderName + ": " + message.Text, message.Channel);
        }

// UIManager.cs - Método Start()
void Start()
{
    if (chatInput != null) chatInput.onSubmit.AddListener(_ => OnChatSubmit());
    // Só mostra loginPanel se ele existir (cena de login)
    if (loginPanel != null)
    {
        ShowPanel(loginPanel);
    }
    // Se estiver na GameScene, mostra HUD
    else if (hudPanel != null)
    {
        ShowPanel(hudPanel);
    }
}

        void Update()
        {
            if (localPlayer == null)
            {
                FindLocalPlayer();
                return;
            }

            UpdateHUD();
        }

        // =================================================================================
        // LOCAL PLAYER
        // =================================================================================
        void FindLocalPlayer()
        {
            var players = FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
            foreach (var player in players)
            {
                if (player.isLocalPlayer)
                {
                    localPlayer = player;
                    OnLocalPlayerFound();
                    break;
                }
            }
        }

        void OnLocalPlayerFound()
        {
            ShowPanel(hudPanel);
            if (nameText != null)
                nameText.text = localPlayer.CharacterName;
        }

        // =================================================================================
        // HUD UPDATE
        // =================================================================================
        void UpdateHUD()
        {
            if (nameText != null) nameText.text = localPlayer.CharacterName;
            if (expBar != null) { expBar.maxValue = localPlayer.ExperienceToNextLevel; expBar.value = localPlayer.Exp; }
            if (hpText != null) hpText.text = $"{localPlayer.CurrentHp}/{localPlayer.MaxHp}";
            if (mpText != null) mpText.text = $"{localPlayer.CurrentMp}/{localPlayer.MaxMp}";
            if (spText != null) spText.text = $"{localPlayer.CurrentSp}/{localPlayer.MaxSp}";
            if (expText != null) expText.text = $"XP {localPlayer.Exp}/{localPlayer.ExperienceToNextLevel}";
            if (hpBar != null)
            {
                hpBar.maxValue = localPlayer.MaxHp;
                hpBar.value = localPlayer.CurrentHp;
            }

            if (mpBar != null)
            {
                mpBar.maxValue = localPlayer.MaxMp;
                mpBar.value = localPlayer.CurrentMp;
            }

            if (spBar != null)
            {
                spBar.maxValue = localPlayer.MaxSp;
                spBar.value = localPlayer.CurrentSp;
            }

            if (levelText != null)
                levelText.text = $"Lv. {localPlayer.Level}";

            if (goldText != null)
                goldText.text = $"{localPlayer.Gold} G";
        }

        // =================================================================================
        // PANELS
        // =================================================================================
// UIManager.cs - Método ShowPanel()
public void ShowPanel(GameObject panel)
{
    if (panel == null) return; // <-- proteção essencial
    
    // Esconde todos os painéis conhecidos
    if (loginPanel != null) loginPanel.SetActive(false);
    if (characterSelectPanel != null) characterSelectPanel.SetActive(false);
    if (hudPanel != null) hudPanel.SetActive(false);
    
    panel.SetActive(true);
}

        public void ShowLogin() => ShowPanel(loginPanel);
        public void ShowCharacterSelect() => ShowPanel(characterSelectPanel);
        public void ShowHUD() => ShowPanel(hudPanel);

        // =================================================================================
        // MESSAGES
        // =================================================================================
        public void ShowMessage(string text, PlayerMessageType type = PlayerMessageType.Info)
        {
            if (messageText == null) return;

            messageText.text = text;
            messageText.color = type switch
            {
                PlayerMessageType.Error => Color.red,
                PlayerMessageType.Warning => Color.yellow,
                PlayerMessageType.LevelUp => Color.cyan,
                PlayerMessageType.ItemAcquired => Color.green,
                _ => Color.white
            };

            CancelInvoke(nameof(ClearMessage));
            Invoke(nameof(ClearMessage), 3f);
        }

        void ClearMessage()
        {
            if (messageText != null)
                messageText.text = "";
        }

        // =================================================================================
        // CHAT
        // =================================================================================
        public void AddChatMessage(string text, ChatChannel channel = ChatChannel.World)
        {
            if (chatHistory == null) return;

            string prefix = channel switch
            {
                ChatChannel.World => "[Mundo]",
                ChatChannel.Local => "[Local]",
                ChatChannel.Party => "[Grupo]",
                ChatChannel.Guild => "[Guilda]",
                ChatChannel.Whisper => "[Sussurro]",
                ChatChannel.System => "[Sistema]",
                ChatChannel.Shout => "[Grito]",
                ChatChannel.Trade => "[Comercio]",
                _ => ""
            };

            chatHistory.richText = false;
            chatHistory.text += $"\n{prefix} {text}";
            if (chatHistory.text.Length > 8000) chatHistory.text = chatHistory.text.Substring(chatHistory.text.Length - 6000);
        }

        public void OnChatSubmit()
        {
            if (chatInput == null || string.IsNullOrWhiteSpace(chatInput.text)) return;
            if (ChatService.TrySend(chatInput.text, ChatChannel.Local, out string error)) chatInput.text = "";
            else AddChatMessage(error, ChatChannel.System);
        }

        // =================================================================================
        // LOGIN
        // =================================================================================
        public void OnLoginSubmit(string username, string password)
        {
            if (NetworkClient.connection != null)
            {
                NetworkClient.connection.Send(new LoginRequest
                {
                    Username = username,
                    Password = password
                });
            }
        }

        public void OnLoginResponse(LoginResponse response)
        {
            if (response.Success)
            {
                ShowCharacterSelect();
            }
            else
            {
                ShowMessage($"Login falhou: {response.ErrorCode}", PlayerMessageType.Error);
            }
        }

        // =================================================================================
        // INVENTORY
        // =================================================================================
        public void ToggleInventory()
        {
            inventoryPanel?.SetActive(!inventoryPanel.activeSelf);
        }
    }
}
