using System;
using System.Threading.Tasks;
using TOP.Data;
using TOP.Network;
using TOP.Services;
namespace TOP.Player
{
    public static class PlayerDurableAction
    {
        public static async Task<bool> Run(PlayerController pc, Action<CharacterData> prepare, Action<CharacterData> apply = null, bool allowStall = false, bool synchronizeBefore = false)
        {
            if (pc == null || !pc.IsInitialized || pc.connectionToClient == null || TOPNetworkManager.Instance == null) return false;
            var inventory = pc.GetComponent<PlayerInventory>();
            if (inventory == null || pc.ArenaInstanceId > 0 || inventory.HasQuestTransaction || (!allowStall && inventory.RejectQuestMutation())) return false;
            if (!await DatabaseService.Instance.SupportsGameplayAsync(TOPNetworkManager.Instance.GetSessionToken(pc.connectionToClient.connectionId)))
            {
                if (pc != null && pc.connectionToClient != null) pc.RpcShowMessage("API/migracao 0011 incompativel: operacao bloqueada antes de consumir itens/ouro.", PlayerMessageType.Warning);
                return false;
            }
            if (pc == null || pc.connectionToClient == null) return false;
            string error;
            var reservation = inventory.PrepareQuestTransaction(0, 0, 0, 0, out error);
            if (reservation == null) return false;
            CharacterData live = null, snapshot = null;
            try
            {
                if (synchronizeBefore)
                {
                    var prior = await DatabaseService.Instance.PersistCharacterAsync(pc.CharacterId, () => pc.GetCharacterData(), TOPNetworkManager.Instance.GetSessionToken(pc.connectionToClient.connectionId), 0);
                    if (!prior.success) return false;
                }
                var saved = await DatabaseService.Instance.PersistCharacterAsync(pc.CharacterId, () =>
                {
                    if (pc == null || pc.connectionToClient == null) throw new InvalidOperationException("Player disconnected.");
                    live = pc.GetCharacterData();
                    snapshot = live.CopySnapshot();
                    snapshot.Inventory = reservation.PreparedItems;
                    prepare(snapshot);
                    snapshot.GameplayStateVersion = 1;
                    return snapshot;
                }, TOPNetworkManager.Instance.GetSessionToken(pc.connectionToClient.connectionId), 0);
                if (!saved.success)
                {
                    if (pc != null && pc.connectionToClient != null)
                    {
                        pc.RpcShowMessage("Operacao nao confirmada: " + saved.error, PlayerMessageType.Warning);
                        if (saved.error == "RELOAD_REQUIRED" || saved.error == "SAVE_CONFLICT" || saved.error == "OPERATION_MISMATCH") pc.connectionToClient.Disconnect();
                    }
                    return false;
                }
                live.SaveRevision = snapshot.SaveRevision;
                live.Gameplay = snapshot.Gameplay;
                live.GameplayStateVersion = 1;
                live.Inventory = snapshot.Inventory;
                live.Skills = snapshot.Skills;
                live.Gold = snapshot.Gold;
                live.SkillPoints = snapshot.SkillPoints;
                reservation.Dispose();
                reservation = null;
                if (pc == null) return true;
                pc.AdminSetGold(snapshot.Gold);
                pc.SkillPoints = snapshot.SkillPoints;
                inventory.InitializeFromData(snapshot.Inventory);
                var equipment = pc.GetComponent<PlayerEquipment>();
                if (equipment != null) equipment.LoadEquippedFromInventory(snapshot.Inventory);
                apply?.Invoke(snapshot);
                return true;
            }
            catch (Exception ex)
            {
                if (pc != null && pc.connectionToClient != null) pc.RpcShowMessage(ex.Message, PlayerMessageType.Warning);
                return false;
            }
            finally { if (reservation != null) reservation.Dispose(); }
        }
    }
}
