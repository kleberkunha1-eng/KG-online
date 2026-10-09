using UnityEngine;
using TOP.Player;
using TOP.Data;

namespace TOP.UI
{
    // Diario de missoes (tecla J). Mostra missoes ativas com progresso, permite abandonar e
    // entregar quando concluidas. Quando um NPC oferece uma missao (PlayerQuests.OnQuestOffered),
    // exibe um popup de aceitar/recusar.
    public class QuestLogUI : MonoBehaviour
    {
        static QuestLogUI instance;
        PlayerQuests local;

        bool open;
        Vector2 scroll;
        Rect win = new Rect(520, 120, 340, 320);

        int? offerQuestId;
        Rect offerWin = new Rect(620, 300, 300, 120);

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void Bind(PlayerQuests pq)
        {
            if (instance == null)
            {
                var go = new GameObject("QuestLogUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<QuestLogUI>();
            }
            if (instance.local == pq) return;
            if (instance.local != null) instance.local.OnQuestOffered -= instance.HandleOffer;
            instance.open = false;
            instance.offerQuestId = null;
            instance.local = pq;
            pq.OnQuestOffered += instance.HandleOffer;
        }

        void HandleOffer(int questId) => offerQuestId = questId;

        void Update()
        {
            if (local == null) { open = false; offerQuestId = null; }
        }

        public static bool ToggleLocal()
        {
            if (instance == null || instance.local == null) return false;
            instance.open = !instance.open;
            return true;
        }

        void OnGUI()
        {
            if (local == null) return;
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));

            if (open)
                win = GameWindowControls.Window(7751, win, Draw, "Missoes (Alt+J / Alt+Q)", () => open = false);

            if (offerQuestId.HasValue)
                offerWin = GameWindowControls.Window(7752, offerWin, DrawOffer, "Nova Missao", () => offerQuestId = null);

            GUI.matrix = old;
        }

        void Draw(int id)
        {
            scroll = GUI.BeginScrollView(new Rect(10, 24, win.width - 20, win.height - 34), scroll, new Rect(0, 0, win.width - 40, Mathf.Max(200, local.ActiveQuests.Count * 96)));
            float y = 0;
            foreach (var (questId, progress) in local.ActiveQuests)
            {
                if (!QuestTable.All.TryGetValue(questId, out var def)) continue;
                GUI.Label(new Rect(0, y, win.width - 50, 18), def.Name + " (#" + def.Id + ")");
                string progressText = def.Objective.Type switch
                {
                    QuestObjectiveType.TalkTo => progress >= 1 ? "Conversa realizada" : "Fale com o alvo indicado",
                    QuestObjectiveType.Kill => $"Derrotar: {progress}/{def.Objective.Required} ({def.Objective.Target})",
                    QuestObjectiveType.Collect => $"Coletar: {ItemLabel(def.Objective.ItemId, def.Objective.Target)} x{def.Objective.Required}",
                    _ => "",
                };
                GUI.Label(new Rect(0, y + 18, win.width - 50, 18), progressText);
                GUI.Label(new Rect(0, y + 36, win.width - 50, 18), $"Recompensa: {def.RewardExp} exp, {def.RewardGold} ouro");
                if (def.RewardItemQty > 0)
                    GUI.Label(new Rect(0, y + 54, win.width - 50, 18),
                        $"Item: {ItemLabel(def.RewardItemId, def.RewardItemName)} x{def.RewardItemQty}");

                if (GUI.Button(new Rect(0, y + 74, 90, 20), "Abandonar"))
                    local.CmdAbandonQuest(questId);
                if (GUI.Button(new Rect(100, y + 74, 90, 20), "Entregar"))
                    local.CmdTurnInQuest(questId);

                y += 96;
            }
            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, win.width, 20));
        }

        static string ItemLabel(int itemId, string legacyName) => itemId > 0
            ? (PkoTables.Items.TryGetValue(itemId, out var item) ? item.Name : "#" + itemId) : legacyName;

        void DrawOffer(int id)
        {
            int questId = offerQuestId.Value;
            string name = QuestTable.All.TryGetValue(questId, out var def) ? def.Name : ("#" + questId);
            string desc = def != null ? def.Description : "";
            GUI.Label(new Rect(10, 24, offerWin.width - 20, 18), name);
            GUI.Label(new Rect(10, 44, offerWin.width - 20, 40), desc);
            if (GUI.Button(new Rect(10, offerWin.height - 30, (offerWin.width - 30) / 2, 24), "Aceitar"))
            {
                local.CmdAcceptQuest(questId);
                offerQuestId = null;
            }
            if (GUI.Button(new Rect(offerWin.width / 2 + 5, offerWin.height - 30, (offerWin.width - 30) / 2, 24), "Recusar"))
                offerQuestId = null;
            GUI.DragWindow(new Rect(0, 0, offerWin.width, 20));
        }
    }
}
