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
        string quantityInput = "1";
        int selectedSlot = -1;
        Vector2 inventoryScroll, myOfferScroll, partnerOfferScroll;

        Rect win = new Rect(400, 100, 460, 480);
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
            instance.quantityInput = "1";
            instance.selectedSlot = -1;
            instance.inventoryScroll = instance.myOfferScroll = instance.partnerOfferScroll = Vector2.zero;
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
            instance.selectedSlot = -1;
            instance.quantityInput = "1";
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

            DrawOfferList(myOfferRaw, new Rect(10, 46, half, 110), ref myOfferScroll);
            DrawOfferList(partnerOfferRaw, new Rect(20 + half, 46, half, 110), ref partnerOfferScroll);

            GUI.Label(new Rect(10, 160, 60, 20), "Ouro:");
            if (!myLocked) myGoldInput = GUI.TextField(new Rect(70, 160, half - 60, 20), myGoldInput);
            else GUI.Label(new Rect(70, 160, half - 60, 20), myGoldInput);
            if (GUI.Button(new Rect(10, 184, half, 22), "Definir ouro") && !myLocked)
            {
                if (ulong.TryParse(myGoldInput, out var g)) local.CmdSetOfferGold(g);
                else UIManager.Instance?.ShowMessage("Informe uma quantidade de ouro valida.", PlayerMessageType.Warning);
            }
            GUI.Label(new Rect(20 + half, 160, half, 20), "Ouro: " + partnerGold);
            GUI.Label(new Rect(10, 212, win.width - 20, 20), "Itens do inventario (equipados nao podem ser trocados)");
            var inventory = local.GetComponent<PlayerInventory>();
            var inventoryArea = new Rect(10, 236, win.width - 20, 140);
            int availableSlots = 0;
            if (inventory != null)
                for (int i = 0; i < inventory.totalSlots; i++)
                    if (inventory.GetSlot(i) != null && !inventory.GetSlot(i).IsEquipped) availableSlots++;
            GUI.enabled = !myLocked;
            inventoryScroll = GUI.BeginScrollView(inventoryArea, inventoryScroll,
                new Rect(0, 0, inventoryArea.width - 22, availableSlots * 26));
            int row = 0;
            if (inventory != null)
                for (int i = 0; i < inventory.totalSlots; i++)
                {
                    var item = inventory.GetSlot(i);
                    if (item == null || item.IsEquipped) continue;
                    var data = ItemDatabase.Instance?.GetItem(item.ItemId);
                    string itemName = data != null ? data.itemName : "Item#" + item.ItemId;
                    if (GUI.Button(new Rect(0, row * 26, inventoryArea.width - 24, 24),
                        (selectedSlot == i ? "> " : "") + (i + 1) + ": " + itemName + " x" + item.Quantity))
                    {
                        selectedSlot = i;
                        quantityInput = "1";
                    }
                    row++;
                }
            GUI.EndScrollView();
            GUI.Label(new Rect(10, 382, 85, 22), "Quantidade:");
            quantityInput = GUI.TextField(new Rect(95, 382, 60, 22), quantityInput);
            GUI.enabled = !myLocked && selectedSlot >= 0;
            if (GUI.Button(new Rect(165, 382, 125, 24), "Ofertar item"))
            {
                var item = inventory != null ? inventory.GetSlot(selectedSlot) : null;
                if (item != null && int.TryParse(quantityInput, out int quantity) && quantity > 0 && quantity <= item.Quantity)
                    local.CmdSetOfferItem(selectedSlot, quantity);
                else UIManager.Instance?.ShowMessage("Selecione um item e uma quantidade disponivel.", PlayerMessageType.Warning);
            }
            if (GUI.Button(new Rect(300, 382, win.width - 310, 24), "Retirar"))
                local.CmdSetOfferItem(selectedSlot, 0);
            GUI.enabled = true;

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

        void DrawOfferList(string raw, Rect area, ref Vector2 scroll)
        {
            GUI.Box(area, "");
            if (string.IsNullOrEmpty(raw)) return;
            var entries = raw.Split(';');
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0, 0, area.width - 20, entries.Length * 22 + 8));
            float y = 4;
            foreach (var entry in entries)
            {
                var p = entry.Split(':');
                if (p.Length < 2 || !int.TryParse(p[0], out int itemId) || !int.TryParse(p[1], out int qty)) continue;
                var data = ItemDatabase.Instance?.GetItem(itemId);
                string name = data != null ? data.itemName : ("Item#" + itemId);
                GUI.Label(new Rect(4, y, area.width - 24, 22), $"{name} x{qty}");
                y += 22;
            }
            GUI.EndScrollView();
        }
    }
}
