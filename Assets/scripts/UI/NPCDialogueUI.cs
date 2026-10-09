using System;
using Mirror;
using TOP.Core;
using TOP.Data;
using TOP.NPC;
using TOP.Player;
using UnityEngine;

namespace TOP.UI
{
    public sealed class NPCDialogueUI : MonoBehaviour
    {
        static NPCDialogueUI instance;
        NPCInteractable npc;
        string[] lines = Array.Empty<string>();
        int[] items = Array.Empty<int>(), offers = Array.Empty<int>(), turnIns = Array.Empty<int>();
        Rect window = new Rect(60, 100, 430, 430);
        Vector2 scroll;
        string quantity = "1";
        bool selling;
        public static bool BlocksMouse => instance != null && instance.npc != null;

        public static void Open(NPCInteractable npc, string[] lines, int[] items, int[] offers, int[] turnIns)
        {
            if (instance == null)
            {
                instance = new GameObject("NPCDialogueUI").AddComponent<NPCDialogueUI>();
                DontDestroyOnLoad(instance.gameObject);
            }
            instance.npc = npc;
            instance.lines = lines;
            instance.items = items;
            instance.offers = offers;
            instance.turnIns = turnIns;
            instance.scroll = Vector2.zero;
            instance.quantity = "1";
            instance.selling = false;
        }

        void Update()
        {
            var player = NetworkClient.localPlayer;
            if (npc == null) return;
            if (player == null || Input.GetKeyDown(KeyCode.Escape)
                || Vector3.Distance(player.transform.position, npc.transform.position) > npc.InteractionRange
                || player.GetComponent<PlayerStats>().IsDead) npc = null;
        }

        void OnGUI()
        {
            if (npc == null || NetworkClient.localPlayer == null) return;
            window = GameWindowControls.Window(7962, window, Draw, npc.NpcName, () => npc = null);
        }

        bool ReadQuantity(out int amount)
        {
            if (int.TryParse(quantity, out amount) && amount > 0) return true;
            UIManager.Instance?.ShowMessage("Informe uma quantidade inteira maior que zero.", PlayerMessageType.Warning);
            return false;
        }

        void Draw(int id)
        {
            if (npc == null) return;
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var line in lines) GUILayout.Label(line);
            if (lines.Length == 0) GUILayout.Label(npc.GetInteractionName());
            if (offers.Length > 0 || turnIns.Length > 0)
            {
                GUILayout.Label("Missoes");
                foreach (int quest in offers)
                    if (QuestTable.All.TryGetValue(quest, out var def) && GUILayout.Button("Consultar: " + def.Name))
                        npc.CmdQuestAction(quest, false);
                foreach (int quest in turnIns)
                    if (QuestTable.All.TryGetValue(quest, out var def) && GUILayout.Button("Entregar: " + def.Name))
                        npc.CmdQuestAction(quest, true);
            }
            if (GUILayout.Button("Diario de missoes")) QuestLogUI.ToggleLocal();
            if (npc.NpcType == NPCType.Blacksmith && GUILayout.Button("Abrir forja")) npc.CmdOpenForge();
            if (npc.NpcType == NPCType.GuildMaster && GUILayout.Button("Abrir guilda")) GuildUI.ToggleLocal();
            if (npc.FullHealCost >= 0)
            {
                GUILayout.Label("Cura completa: " + npc.FullHealCost + " ouro. Gratuita abaixo do nivel 6 com missao 500 concluida.");
                if (GUILayout.Button("Restaurar vida, mana e stamina")) npc.CmdFullHeal();
            }
            foreach (var recipe in npc.Recipes)
            {
                if (recipe == null || !PkoTables.Items.TryGetValue(recipe.ResultItemId, out var result)
                    || !PkoTables.Items.TryGetValue(recipe.MaterialItemId, out var material)
                    || !PkoTables.Items.TryGetValue(recipe.BottleItemId, out var bottle)) continue;
                GUILayout.Label(material.Name + " x" + recipe.MaterialQuantity + " + " + bottle.Name
                    + " x1 + " + recipe.GoldCost + " ouro");
                if (GUILayout.Button("Preparar: " + result.Name)) npc.CmdCraftRecipe(recipe.ResultItemId);
            }
            if (items.Length > 0)
            {
                GUILayout.Label("Ouro: " + NetworkClient.localPlayer.GetComponent<PlayerController>().Gold);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Comprar")) { selling = false; scroll = Vector2.zero; }
                if (GUILayout.Button("Vender")) { selling = true; scroll = Vector2.zero; }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Quantidade", GUILayout.Width(100));
                quantity = GUILayout.TextField(quantity, GUILayout.Width(80));
                GUILayout.EndHorizontal();
                if (!selling)
                    foreach (int itemId in items)
                    {
                        if (!PkoTables.Items.TryGetValue(itemId, out var item)) continue;
                        if (GUILayout.Button(item.Name + " - " + item.Price + " ouro/un. (max " + item.Stack + ")")
                            && ReadQuantity(out int amount)) npc.CmdBuyItem(itemId, amount);
                    }
                else
                {
                    GUILayout.Label("Venda por metade do preco base. Refino/gemas e itens equipados sao protegidos.");
                    var inventory = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
                    for (int i = 0; i < inventory.totalSlots; i++)
                    {
                        var slot = inventory.GetSlot(i);
                        if (slot == null || slot.IsEquipped || !PkoTables.Items.TryGetValue(slot.ItemId, out var item)) continue;
                        GUI.enabled = item.Tradeable && item.Price >= 2 && slot.RefineLevel == 0
                            && Array.TrueForAll(slot.Gems, gem => gem < 0);
                        if (GUILayout.Button((i + 1) + ": " + item.Name + " x" + slot.Quantity + " - " + item.Price / 2 + " ouro/un.")
                            && ReadQuantity(out int amount)) npc.CmdSellItem(i, amount);
                        GUI.enabled = true;
                    }
                }
            }
            else if (npc.NpcType == NPCType.Merchant) GUILayout.Label("Este NPC ainda nao tem catalogo de loja importado.");
            else if (npc.NpcType == NPCType.Banker || (npc.NpcType == NPCType.Healer && npc.FullHealCost < 0 && npc.Recipes.Count == 0)
                || npc.NpcType == NPCType.StableMaster
                || npc.NpcType == NPCType.SkillMaster)
                GUILayout.Label("Servico original ainda nao importado; nenhuma cobranca sera realizada.");
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, window.width - 30, 22));
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}
