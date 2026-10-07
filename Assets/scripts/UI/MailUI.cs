using UnityEngine;
using TOP.Player;
using TOP.Inventory;

namespace TOP.UI
{
    // Painel de correio (tecla M). Lista cartas recebidas, permite ler/resgatar anexo/excluir,
    // e enviar uma nova carta (com ouro e opcionalmente 1 item do inventario, pelo id do item).
    public class MailUI : MonoBehaviour
    {
        static MailUI instance;
        PlayerMail local;

        bool open;
        Vector2 scroll;
        Rect win = new Rect(520, 120, 320, 340);

        string sendTarget = "", sendSubject = "", sendBody = "", sendGold = "0", sendItemId = "", sendItemQty = "1";
        bool showSendForm;

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void Bind(PlayerMail pm)
        {
            if (instance == null)
            {
                var go = new GameObject("MailUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<MailUI>();
            }
            if (instance.local == pm) return;
            instance.open = instance.showSendForm = false;
            instance.sendTarget = instance.sendSubject = instance.sendBody = instance.sendItemId = "";
            instance.sendGold = "0";
            instance.sendItemQty = "1";
            instance.local = pm;
        }

        void Update()
        {
            if (local == null) { open = false; return; }
        }

        public static bool ToggleLocal()
        {
            if (instance == null || instance.local == null) return false;
            instance.open = !instance.open;
            if (instance.open) instance.local.CmdRefreshMail();
            return true;
        }

        void OnGUI()
        {
            if (!open || local == null) return;
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            win = GameWindowControls.Window(7741, win, Draw, "Correio (Alt+M)", () => open = false);
            GUI.matrix = old;
        }

        void Draw(int id)
        {
            if (GUI.Button(new Rect(10, 20, 100, 22), showSendForm ? "Ver Caixa" : "Nova carta"))
                showSendForm = !showSendForm;
            if (GUI.Button(new Rect(win.width - 70, 20, 60, 22), "Atualizar"))
                local.CmdRefreshMail();

            if (showSendForm) DrawSendForm();
            else DrawInbox();

            GUI.DragWindow(new Rect(0, 0, win.width, 20));
        }

        void DrawSendForm()
        {
            float y = 50;
            GUI.Label(new Rect(10, y, 80, 20), "Destinatario:"); sendTarget = GUI.TextField(new Rect(100, y, win.width - 110, 20), sendTarget); y += 26;
            GUI.Label(new Rect(10, y, 80, 20), "Assunto:"); sendSubject = GUI.TextField(new Rect(100, y, win.width - 110, 20), sendSubject); y += 26;
            GUI.Label(new Rect(10, y, 80, 20), "Mensagem:"); y += 20;
            sendBody = GUI.TextArea(new Rect(10, y, win.width - 20, 60), sendBody); y += 66;
            GUI.Label(new Rect(10, y, 50, 20), "Ouro:"); sendGold = GUI.TextField(new Rect(60, y, 80, 20), sendGold);
            GUI.Label(new Rect(150, y, 60, 20), "ItemId:"); sendItemId = GUI.TextField(new Rect(210, y, 50, 20), sendItemId);
            GUI.Label(new Rect(265, y, 25, 20), "Qtd:"); sendItemQty = GUI.TextField(new Rect(win.width - 40, y, 30, 20), sendItemQty);
            y += 28;
            if (GUI.Button(new Rect(10, y, win.width - 20, 26), "Enviar"))
            {
                long.TryParse(sendGold, out long gold);
                int.TryParse(sendItemId, out int itemId);
                int.TryParse(sendItemQty, out int qty);
                if (itemId <= 0) itemId = -1;
                local.CmdSendMail(sendTarget.Trim(), sendSubject, sendBody, gold, itemId, itemId > 0 ? Mathf.Max(1, qty) : 0, 0);
                sendTarget = sendSubject = sendBody = ""; sendGold = "0"; sendItemId = ""; sendItemQty = "1";
                showSendForm = false;
            }
        }

        void DrawInbox()
        {
            scroll = GUI.BeginScrollView(new Rect(10, 50, win.width - 20, win.height - 65), scroll, new Rect(0, 0, win.width - 40, Mathf.Max(200, local.Mails.Count * 70)));
            float y = 0;
            foreach (var m in local.Mails)
            {
                string title = (m.IsRead ? "" : "[NOVA] ") + m.SenderName + " - " + m.Subject;
                GUI.Label(new Rect(0, y, win.width - 50, 18), title);
                GUI.Label(new Rect(0, y + 18, win.width - 50, 18), m.Body);
                string attach = m.HasAttachment ? $"Ouro: {m.Gold}" + (m.ItemId >= 0 ? $" | {PkoItemName(m.ItemId)} x{m.ItemQuantity}" : "") : "(sem anexo)";
                GUI.Label(new Rect(0, y + 36, win.width - 50, 18), attach);

                if (m.HasAttachment && !m.IsClaimed)
                {
                    if (GUI.Button(new Rect(win.width - 110, y + 2, 90, 20), "Resgatar"))
                        local.CmdClaimMail(m.Id);
                }
                else if (GUI.Button(new Rect(win.width - 110, y + 2, 90, 20), "Excluir"))
                {
                    local.CmdDeleteMail(m.Id);
                }
                y += 58;
            }
            GUI.EndScrollView();
        }

        static string PkoItemName(int itemId)
        {
            var data = ItemDatabase.Instance?.GetItem(itemId);
            return data != null ? data.itemName : ("Item#" + itemId);
        }
    }
}
