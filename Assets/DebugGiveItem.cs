using UnityEngine;
using TOP.Core; 
using TOP.Inventory;    
using TOP.Player;

public class DebugGiveItem : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private int itemId = 1001;
    [SerializeField] private int quantity = 1;
    [SerializeField] private KeyCode giveKey = KeyCode.G;

    void Start()
    {
        Debug.Log("[DebugGiveItem] ====== INICIANDO TESTE DO ITEMDATABASE ======");

        var db = ItemDatabase.Instance;
        if (db == null)
        {
            Debug.LogError("[DebugGiveItem] ❌ ItemDatabase.Instance é NULL! Crie um ItemDatabase.asset!");
            return;
        }

        Debug.Log("[DebugGiveItem] ✅ ItemDatabase.Instance encontrado!");

        var item = db.GetItem(itemId);
        if (item == null)
            Debug.LogError("[DebugGiveItem] ❌ GetItem(" + itemId + ") retornou NULL!");
        else
            Debug.Log("[DebugGiveItem] ✅ GetItem(" + itemId + "): " + item.itemName);

        var equip = db.GetEquipment(itemId);
        if (equip == null)
            Debug.LogError("[DebugGiveItem] ❌ GetEquipment(" + itemId + ") retornou NULL!");
        else
            Debug.Log("[DebugGiveItem] ✅ GetEquipment(" + itemId + "): " + equip.itemName + " | Slot: " + equip.slot);

        Debug.Log("[DebugGiveItem] 📦 Itens no banco: " + db.GetAllItems().Count);
        Debug.Log("[DebugGiveItem] ⚔️ Equipamentos no banco: " + db.GetAllEquipment().Count);
        Debug.Log("[DebugGiveItem] ====== FIM DO TESTE ======");
    }

    void Update()
    {
        if (Input.GetKeyDown(giveKey))
        {
            GiveItem();
        }
    }

    private void GiveItem()
    {
        // TROCADO: FindAnyObjectByType para FindObjectOfType para maior compatibilidade
        PlayerInventory playerInventory = Object.FindAnyObjectByType<PlayerInventory>(); 

        if (playerInventory == null)
        {
            Debug.LogError("[DebugGiveItem] ❌ PlayerInventory não encontrado na cena!");
            return;
        }

        Debug.Log("[DebugGiveItem] Tentando adicionar item " + itemId + "...");

        // Se estivermos no Servidor (ou Host)
        if (playerInventory.isServer)
        {
            int emptySlot = playerInventory.FindEmptySlot();    
            if (emptySlot != -1)
            {
                playerInventory.AddItem(itemId, quantity, (ushort)emptySlot);
                Debug.Log("[DebugGiveItem] ✅ Servidor: Item " + itemId + " adicionado no slot " + emptySlot + "!");
            }
            else
            {
                Debug.LogWarning("[DebugGiveItem] ❌ Inventário cheio!");
            }
        }
        else
        {
            // Se for um Cliente, envia o comando para o servidor
            Debug.Log("[DebugGiveItem] Enviando comando para servidor...");
            playerInventory.CmdAddItemDebug(itemId, quantity);
        }
    }
}