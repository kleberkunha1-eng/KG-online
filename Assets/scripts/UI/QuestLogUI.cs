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
            var inventory = local.GetComponent<PlayerInventory>();
            float contentHeight = 0;
            foreach (var (questId, _) in local.ActiveQuests)
                if (QuestTable.All.TryGetValue(questId, out var definition)) contentHeight += RowHeight(definition);
            scroll = GUI.BeginScrollView(new Rect(10, 24, win.width - 20, win.height - 34), scroll, new Rect(0, 0, win.width - 40, Mathf.Max(200, contentHeight)));
            float y = 0;
            foreach (var (questId, progress) in local.ActiveQuests)
            {
                if (!QuestTable.All.TryGetValue(questId, out var def)) continue;
                GUI.Label(new Rect(0, y, win.width - 50, 18), def.Name + " (#" + def.Id + ")");
                string progressText = def.Objective.Type switch
                {
                    QuestObjectiveType.TalkTo => progress >= 1 ? "Conversa realizada" : "Fale com o alvo indicado",
                    QuestObjectiveType.Kill => $"Derrotar: {progress}/{def.Objective.Required} ({def.Objective.Target})",
                    QuestObjectiveType.Collect => CollectionLabel(inventory, def.Objective.ItemId, def.Objective.Target, def.Objective.Required),
                    _ => "",
                };
                float extra = RowHeight(def) - 96;
                if (def.Objective.Type == QuestObjectiveType.Collect && def.Objective.CollectionItems != null
                    && def.Objective.CollectionItems.Length > 0)
                {
                    for (int i = 0; i < def.Objective.CollectionItems.Length; i++)
                    {
                        var required = def.Objective.CollectionItems[i];
                        GUI.Label(new Rect(0, y + 18 + i * 18, win.width - 50, 18),
                            required == null ? "Material de missao invalido"
                                : CollectionLabel(inventory, required.ItemId, "", required.Quantity));
                    }
                }
                else GUI.Label(new Rect(0, y + 18, win.width - 50, 18), progressText);
                GUI.Label(new Rect(0, y + 36 + extra, win.width - 50, 18), $"Recompensa: {ExperienceLabel(def)} exp, {def.RewardGold} ouro");
                if (def.RewardItemQty > 0)
                    GUI.Label(new Rect(0, y + 54 + extra, win.width - 50, 18),
                        $"Item: {ItemLabel(def.RewardItemId, def.RewardItemName)} x{def.RewardItemQty}");

                if (GUI.Button(new Rect(0, y + 74 + extra, 90, 20), "Abandonar"))
                    local.CmdAbandonQuest(questId);
                if (GUI.Button(new Rect(100, y + 74 + extra, 90, 20), "Entregar"))
                    local.CmdTurnInQuest(questId);

                y += RowHeight(def);
            }

            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, win.width, 20));
        }

        internal static float RowHeight(QuestDef definition) => 96 + (definition.Objective.Type == QuestObjectiveType.Collect
            ? 18 * Mathf.Max(0, (definition.Objective.CollectionItems?.Length ?? 1) - 1) : 0);

        internal static string ExperienceLabel(QuestDef definition) => definition.RewardExpMaximumExclusive > definition.RewardExp
            ? $"{definition.RewardExp}-{definition.MaximumExperienceReward}" : definition.RewardExp.ToString();

        static string ItemLabel(int itemId, string legacyName) => itemId > 0
            ? (PkoTables.Items.TryGetValue(itemId, out var item) ? item.Name : "#" + itemId) : legacyName;

        internal static string CollectionLabel(PlayerInventory inventory, int itemId, string legacyName, int required)
        {
            int resolvedId = itemId > 0 ? itemId : PlayerQuests.ResolveItemIdByName(legacyName);
            long available = inventory != null ? inventory.GetQuestMaterialCount(resolvedId) : 0;
            return $"Coletar: {ItemLabel(resolvedId, legacyName)} {available}/{required}";
        }

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
