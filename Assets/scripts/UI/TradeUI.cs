using UnityEngine;
using TOP.Player;
using TOP.Inventory;

namespace TOP.UI
{
    // Painel de comercio direto (negociacao em tempo real entre 2 jogadores). Abre automaticamente
    // ao receber convite ou ao iniciar uma troca; fecha ao concluir/cancelar. Segue o mesmo padrao
    // IMGUI de PartyUI/GuildUI (GUI.Window, escala por Screen.height/900f).
    public class TradeUI : MonoBehaviour
    {
        static TradeUI instance;
        PlayerTrade local;

        bool open, hasInvite;
        string inviterName, partnerName, reqTargetBuffer = "";
        string myOfferRaw = "", partnerOfferRaw = "";
        ulong myGold, partnerGold;
        bool myLocked, partnerLocked;
        string myGoldInput = "0";

        Rect win = new Rect(400, 150, 420, 300);
        Rect inviteWin = new Rect(500, 300, 260, 110);

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void Bind(PlayerTrade pt)
        {
            if (instance == null)
            {
                var go = new GameObject("TradeUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<TradeUI>();
            }
            if (instance.local == pt) return;
            instance.open = instance.hasInvite = false;
            instance.partnerName = instance.inviterName = null;
            instance.myOfferRaw = instance.partnerOfferRaw = instance.reqTargetBuffer = "";
            instance.myGold = instance.partnerGold = 0;
            instance.myLocked = instance.partnerLocked = false;
            instance.myGoldInput = "0";
            instance.local = pt;
        }

        public static void ShowInvite(PlayerTrade pt, string inviterName)
        {
            if (instance == null) return;
            instance.local = pt;
            instance.hasInvite = true;
            instance.inviterName = inviterName;
        }

        public static void OnTradeStarted(PlayerTrade pt, string partner)
        {
            if (instance == null) return;
            instance.local = pt;
            instance.open = true;
            instance.hasInvite = false;
            instance.partnerName = partner;
            instance.myOfferRaw = instance.partnerOfferRaw = "";
            instance.myGold = instance.partnerGold = 0;
            instance.myLocked = instance.partnerLocked = false;
            instance.myGoldInput = "0";
        }

        public static void OnOfferUpdated(PlayerTrade pt, string myOffer, ulong myGold, bool myLocked, string partnerOffer, ulong partnerGold, bool partnerLocked)
        {
            if (instance == null) return;
            instance.myOfferRaw = myOffer; instance.myGold = myGold; instance.myLocked = myLocked;
            instance.partnerOfferRaw = partnerOffer; instance.partnerGold = partnerGold; instance.partnerLocked = partnerLocked;
        }

        public static void OnTradeClosed(PlayerTrade pt, string reason)
        {
            if (instance == null) return;
            instance.open = false;
            if (!string.IsNullOrEmpty(reason)) TOP.UI.UIManager.Instance?.ShowMessage(reason, TOP.Player.PlayerMessageType.Info);
        }

        void Update()
        {
            if (local == null) open = hasInvite = false;
        }

        public static bool ToggleLocal()
        {
            if (instance == null || instance.local == null) return false;
            if (instance.hasInvite)
            {
                instance.local.CmdRespondTrade(instance.inviterName, false);
                instance.hasInvite = false;
            }
            else if (instance.open)
            {
                if (instance.partnerName != null) instance.local.CmdCancelTrade();
                instance.open = false;
            }
            else
            {
                instance.open = true;
                instance.partnerName = null;
            }
            return true;
        }

        void OnGUI()
        {
            if (local == null) return;
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));

            if (hasInvite) inviteWin = GameWindowControls.Window(7745, inviteWin, DrawInvite, "Pedido de comercio", () =>
            {
                local.CmdRespondTrade(inviterName, false);
                hasInvite = false;
            });
            else if (open) win = GameWindowControls.Window(7744, win, Draw,
                partnerName != null ? ("Comercio com " + partnerName) : "Comercio (Alt+Y)", () =>
                {
                    if (partnerName != null) local.CmdCancelTrade();
                    open = false;
                });

            GUI.matrix = old;
        }

        void DrawInvite(int id)
        {
            GUI.Label(new Rect(10, 25, 240, 30), inviterName + " quer negociar com voce.");
            if (GUI.Button(new Rect(10, 70, 110, 26), "Aceitar"))
            {
                local.CmdRespondTrade(inviterName, true);
                hasInvite = false;
            }
            if (GUI.Button(new Rect(140, 70, 110, 26), "Recusar"))
            {
                local.CmdRespondTrade(inviterName, false);
                hasInvite = false;
            }
        }

        void Draw(int id)
        {
            if (partnerName == null)
            {
                GUI.Label(new Rect(10, 25, 100, 20), "Jogador:");
                reqTargetBuffer = GUI.TextField(new Rect(100, 25, win.width - 180, 22), reqTargetBuffer);
                if (GUI.Button(new Rect(win.width - 75, 25, 60, 22), "Pedir") && !string.IsNullOrWhiteSpace(reqTargetBuffer))
                {
                    local.CmdRequestTrade(reqTargetBuffer.Trim());
                    reqTargetBuffer = "";
                }
                if (GUI.Button(new Rect(10, win.height - 36, win.width - 20, 26), "Fechar")) open = false;
                GUI.DragWindow(new Rect(0, 0, win.width, 20));
                return;
            }

            float half = (win.width - 30) / 2f;
            GUI.Label(new Rect(10, 24, half, 20), "Sua oferta" + (myLocked ? " [TRAVADA]" : ""));
            GUI.Label(new Rect(20 + half, 24, half, 20), partnerName + (partnerLocked ? " [TRAVADO]" : ""));

            DrawOfferList(myOfferRaw, new Rect(10, 46, half, 150));
            DrawOfferList(partnerOfferRaw, new Rect(20 + half, 46, half, 150));

            GUI.Label(new Rect(10, 200, 60, 20), "Ouro:");
            if (!myLocked) myGoldInput = GUI.TextField(new Rect(70, 200, half - 60, 20), myGoldInput);
            else GUI.Label(new Rect(70, 200, half - 60, 20), myGoldInput);
            if (GUI.Button(new Rect(10, 224, half, 20), "Definir ouro") && !myLocked)
            {
                if (ulong.TryParse(myGoldInput, out var g)) local.CmdSetOfferGold(g);
            }
            GUI.Label(new Rect(20 + half, 200, half, 20), "Ouro: " + partnerGold);

            string lockLabel = myLocked ? "Destravar" : "Travar oferta";
            if (GUI.Button(new Rect(10, win.height - 60, win.width - 20, 24), lockLabel))
                local.CmdToggleLock();

            if (GUI.Button(new Rect(10, win.height - 32, win.width - 20, 24), "Cancelar comercio"))
            {
                local.CmdCancelTrade();
                open = false;
            }

            GUI.DragWindow(new Rect(0, 0, win.width, 20));
        }

        void DrawOfferList(string raw, Rect area)
        {
            GUI.Box(area, "");
            if (string.IsNullOrEmpty(raw)) return;
            float y = area.y + 4;
            foreach (var entry in raw.Split(';'))
            {
                var p = entry.Split(':');
                if (p.Length < 2 || !int.TryParse(p[0], out int itemId) || !int.TryParse(p[1], out int qty)) continue;
                var data = ItemDatabase.Instance?.GetItem(itemId);
                string name = data != null ? data.itemName : ("Item#" + itemId);
                GUI.Label(new Rect(area.x + 4, y, area.width - 8, 18), $"{name} x{qty}");
                y += 18;
            }
        }
    }
}
