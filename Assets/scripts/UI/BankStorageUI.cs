using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TOP.Data;
using TOP.Inventory;
using TOP.NPC;
using TOP.Player;
using UnityEngine;

namespace TOP.UI
{
    public sealed class BankStorageUI : MonoBehaviour
    {
        [Serializable]
        sealed class BankSnapshot
        {
            public List<BankItemView> Items = new List<BankItemView>();
        }

        [Serializable]
        sealed class BankItemView
        {
            public ushort SlotIndex;
            public int ItemId;
            public int Quantity;
        }

        static BankStorageUI instance;
        NPCInteractable banker;
        List<BankItemView> items = new List<BankItemView>();
        Rect window = new Rect(340, 70, 760, 560);
        Vector2 inventoryScroll, bankScroll;
        string quantity = "1";

        public static bool BlocksMouse => instance != null && instance.banker != null;

        public static void Open(NPCInteractable npc, string json)
        {
            if (instance == null)
            {
                instance = new GameObject("BankStorageUI").AddComponent<BankStorageUI>();
                DontDestroyOnLoad(instance.gameObject);
            }
            instance.banker = npc;
            var snapshot = JsonUtility.FromJson<BankSnapshot>(json);
            instance.items = snapshot?.Items ?? new List<BankItemView>();
            instance.quantity = "1";
            instance.inventoryScroll = Vector2.zero;
            instance.bankScroll = Vector2.zero;
        }

        void Update()
        {
            var player = NetworkClient.localPlayer;
            if (banker == null) return;
            if (player == null || Input.GetKeyDown(KeyCode.Escape)
                || Vector3.Distance(player.transform.position, banker.transform.position) > banker.InteractionRange
                || player.GetComponent<PlayerStats>() == null || player.GetComponent<PlayerStats>().IsDead)
                banker = null;
        }

        void OnGUI()
        {
            if (banker == null || NetworkClient.localPlayer == null) return;
            window = GameWindowControls.Window(8642, window, Draw, "Banco pessoal - 32 espacos", () => banker = null);
        }

        void Draw(int id)
        {
            var player = NetworkClient.localPlayer;
            var inventory = player != null ? player.GetComponent<PlayerInventory>() : null;
            if (banker == null || inventory == null) return;
            GUILayout.Label("Armazenamento pessoal do personagem. Sem taxa ou transferencia de ouro.");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Quantidade por operacao", GUILayout.Width(180));
            quantity = GUILayout.TextField(quantity, GUILayout.Width(90));
            GUILayout.Label("Depositos preservam os atributos e as restricoes originais.");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("Inventario (40 espacos)");
            inventoryScroll = GUILayout.BeginScrollView(inventoryScroll, GUILayout.Height(430));
            for (int slotIndex = 0; slotIndex < inventory.totalSlots; slotIndex++)
            {
                var item = inventory.GetSlot(slotIndex);
                if (item == null || item.IsEmpty) continue;
                string name = PkoTables.Items.TryGetValue(item.ItemId, out var definition) ? definition.Name : "Item " + item.ItemId;
                GUI.enabled = banker != null && !item.IsEquipped && !item.IsLocked
                    && (!item.OwnerCharacterId.HasValue || item.OwnerCharacterId.Value == player.GetComponent<PlayerController>().CharacterId);
                if (GUILayout.Button((slotIndex + 1) + ". " + name + " x" + item.Quantity + (item.IsEquipped ? " (equipado)" : "")
                    + "  -> Depositar", GUILayout.Height(30)) && ReadQuantity(out int amount))
                    banker.CmdDepositBankItem(slotIndex, amount);
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("Banco (8 linhas x 4 colunas, 1 pagina)");
            bankScroll = GUILayout.BeginScrollView(bankScroll, GUILayout.Height(430));
            for (int slotIndex = 0; slotIndex < 32; slotIndex++)
            {
                var item = items.FirstOrDefault(candidate => candidate != null && candidate.SlotIndex == slotIndex);
                if (item == null)
                {
                    GUILayout.Label((slotIndex + 1) + ". [vazio]", GUILayout.Height(30));
                    continue;
                }
                string name = PkoTables.Items.TryGetValue(item.ItemId, out var definition) ? definition.Name : "Item " + item.ItemId;
                if (GUILayout.Button((slotIndex + 1) + ". " + name + " x" + item.Quantity + "  <- Retirar", GUILayout.Height(30))
                    && ReadQuantity(out int amount))
                    banker.CmdWithdrawBankItem(slotIndex, amount);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Label("Clicar transfere a quantidade informada. Itens proibidos pela regra original nao podem ser depositados.");
        }

        bool ReadQuantity(out int amount)
        {
            if (int.TryParse(quantity, out amount) && amount > 0) return true;
            var ui = UIManager.Instance;
            if (ui != null) ui.ShowMessage("Informe uma quantidade inteira maior que zero.", PlayerMessageType.Warning);
            return false;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
