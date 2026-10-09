using Mirror;
using TOP.Player;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TOP.UI
{
    public sealed class PlayerInteractionMenu : MonoBehaviour
    {
        static PlayerInteractionMenu instance;
        PlayerController target;
        Rect window = new Rect(20, 20, 245, 190), inviteWindow = new Rect(30, 90, 300, 150);
        Vector2 scroll, pressedAt;
        uint inviter;
        string inviterName;
        float inviteUntil;

        public static bool BlocksMouse => instance != null && ((instance.target != null && !RightClickPending) || instance.inviter != 0);
        public static bool RightClickPending { get; private set; }
        PlayerCombat Local => NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.GetComponent<PlayerCombat>() : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            instance = new GameObject("PlayerInteractionMenu").AddComponent<PlayerInteractionMenu>();
            DontDestroyOnLoad(instance.gameObject);
        }

        public static void ShowDuelInvite(uint challenger, string name)
        {
            if (instance == null) return;
            instance.inviter = challenger;
            instance.inviterName = name;
            instance.inviteUntil = Time.unscaledTime + 30;
        }

        void Update()
        {
            if (Local == null)
            {
                target = null;
                inviter = 0;
                RightClickPending = false;
                return;
            }
            if (inviter != 0 && Time.unscaledTime > inviteUntil) inviter = 0;
            if (Input.GetKeyDown(KeyCode.Escape)) { target = null; Respond(false); }
            if (Input.GetMouseButtonDown(0) && target != null
                && !window.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                target = null;
            if (Input.GetMouseButtonDown(1))
            {
                RightClickPending = false;
                if (Camera.main == null || TOP.Admin.AdminPanel.BlocksMouse
                    || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    || Pko.PkoUi.PointerOverWindow()) return;
                pressedAt = Input.mousePosition;
                if (Physics.Raycast(Camera.main.ScreenPointToRay(pressedAt), out var hit, 100,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var player = hit.collider.GetComponentInParent<PlayerController>();
                    if (player != null && !player.isLocalPlayer)
                    {
                        target = player;
                        RightClickPending = true;
                    }
                }
            }
            if (RightClickPending && Input.GetMouseButton(1)
                && Vector2.Distance(pressedAt, Input.mousePosition) > 6)
            { RightClickPending = false; target = null; }
            if (Input.GetMouseButtonUp(1) && RightClickPending)
            {
                RightClickPending = false;
                window.position = new Vector2(pressedAt.x, Screen.height - pressedAt.y);
            }
        }

        void OnGUI()
        {
            if (Local == null) return;
            if (target != null && !RightClickPending)
                window = GameWindowControls.Window(7960, window, Draw, target.CharacterName, () => target = null);
            if (inviter != 0)
                inviteWindow = GameWindowControls.Window(7961, inviteWindow, id =>
                {
                    GUILayout.Label(inviterName + " desafia voce para um duelo.");
                    GUILayout.Label("Sem morte: termina com 1 HP. Limite de distancia: 20m.");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Aceitar")) Respond(true);
                    if (GUILayout.Button("Recusar")) Respond(false);
                    GUILayout.EndHorizontal();
                }, "Desafio de duelo", () => Respond(false));
        }

        void Respond(bool accept)
        {
            if (inviter == 0) return;
            Local?.CmdRespondDuel(inviter, accept);
            inviter = 0;
        }

        void Draw(int id)
        {
            var combat = Local;
            if (target == null || combat == null) return;
            scroll = GUILayout.BeginScrollView(scroll);
            var local = NetworkClient.localPlayer;
            var party = local.GetComponent<PlayerParty>();
            var trade = local.GetComponent<PlayerTrade>();
            GUI.enabled = trade != null;
            if (GUILayout.Button("Negociar / Trade")) { trade.CmdRequestTrade(target.CharacterName); target = null; }
            GUI.enabled = party != null && target != null;
            if (GUILayout.Button("Convidar para Party")) { party.CmdPartyInvite(target.CharacterName); target = null; }
            GUI.enabled = target != null && (combat.DuelOpponentNetId == 0 || combat.DuelOpponentNetId == target.netId);
            if (GUILayout.Button(target != null && combat.DuelOpponentNetId == target.netId ? "Atacar no duelo" : "Desafiar para duelo"))
            {
                if (combat.DuelOpponentNetId == target.netId) local.GetComponent<PlayerController>().CmdAttackTarget(target.netIdentity, 0);
                else combat.CmdRequestDuel(target.netIdentity);
                target = null;
            }
            GUI.enabled = combat.DuelOpponentNetId != 0;
            if (GUILayout.Button("Encerrar duelo")) { combat.CmdCancelDuel(); target = null; }
            GUI.enabled = true;
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 210, 22));
        }

        void OnDestroy() { if (instance == this) { instance = null; RightClickPending = false; } }
    }
}
