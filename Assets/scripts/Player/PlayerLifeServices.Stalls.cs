using System;
using System.Linq;
using Mirror;
using TOP.Data;
using TOP.Network;
using TOP.Services;
using UnityEngine;
namespace TOP.Player
{
    public partial class PlayerLifeServices
    {
        [Command]
        public async void CmdSetStall(string name, int slot, int quantity, long price)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 32 || name.Any(char.IsControl) || quantity <= 0 || price <= 0 || price > 1000000000L / quantity) return;
            await PlayerDurableAction.Run(pc, data =>
            {
                var stats = GetComponent<PlayerStats>(); var combat = GetComponent<PlayerCombat>(); var skills = GetComponent<PlayerSkills>();
                if (stats == null || stats.IsDead || pc.IsAboardBoat || combat == null || combat.DuelOpponentNetId != 0 || skills == null || skills.GetSkillLevel(241) <= 0) throw new InvalidOperationException("Aprenda Set Stall (241), em terra, vivo e fora de duelo.");
                var item = PlayerForge.Available(data, slot);
                if (item.Quantity < quantity || item.OwnerCharacterId.HasValue || !PkoTables.Items.TryGetValue(item.ItemId, out var definition) || !definition.Tradeable || definition.Type == 59 || string.IsNullOrEmpty(item.UniqueItemId)) throw new InvalidOperationException("Item nao vendavel (fairies exigem transferencia de atributos ainda nao suportada).");
                if (data.Gameplay.Offers.Count >= 24 || data.Gameplay.Offers.Any(o => o.ItemKey == item.UniqueItemId)) throw new InvalidOperationException("Oferta repetida ou limite de 24 itens.");
                data.Gameplay.StallName = name.Trim();
                data.Gameplay.Offers.Add(new StallOffer { ItemKey = item.UniqueItemId, ItemId = item.ItemId, Quantity = quantity, Price = price });
            }, data => { GetComponent<PlayerMovement>().Stop(); Synchronize(); }, true);
        }
        [Command]
        public async void CmdCloseStall()
        {
            await PlayerDurableAction.Run(pc, data => { data.Gameplay.Offers.Clear(); data.Gameplay.StallName = ""; }, data => Synchronize(), true);
        }
        [Command]
        public async void CmdBuyStall(uint sellerNetId, string itemKey, int quantity)
        {
            if (!NetworkServer.spawned.TryGetValue(sellerNetId, out var identity)) return;
            var seller = identity.GetComponent<PlayerLifeServices>();
            if (seller == null || seller == this || !CanBuy(seller) || quantity <= 0 || string.IsNullOrEmpty(itemKey)) return;
            string error;
            var buyerReservation = inventory.PrepareQuestTransaction(0, 0, 0, 0, out error);
            if (buyerReservation == null) return;
            var sellerReservation = seller.inventory.PrepareQuestTransaction(0, 0, 0, 0, out error);
            if (sellerReservation == null) { buyerReservation.Dispose(); return; }
            try
            {
                var manager = TOPNetworkManager.Instance; var database = DatabaseService.Instance;
                string buyerToken = manager.GetSessionToken(pc.connectionToClient.connectionId), sellerToken = manager.GetSessionToken(seller.pc.connectionToClient.connectionId);
                var buyerSaved = await database.PersistCharacterAsync(pc.CharacterId, () => pc.GetCharacterData(), buyerToken, 0);
                var sellerSaved = await database.PersistCharacterAsync(seller.pc.CharacterId, () => seller.pc.GetCharacterData(), sellerToken, 0);
                if (!buyerSaved.success || !sellerSaved.success || !CanBuy(seller)) return;
                int slot = 0;
                var occupied = inventory.GetInventoryData();
                while (slot < inventory.totalSlots && occupied.Any(i => i.SlotIndex == slot)) slot++;
                if (slot >= inventory.totalSlots) return;
                var purchased = await database.PurchaseStallAsync(pc.CharacterId, seller.pc.CharacterId,
                    () => pc.GetCharacterData(), () => seller.pc.GetCharacterData(), buyerToken, itemKey, quantity, slot,
                    (buyerData, sellerData) =>
                    {
                        buyerReservation.Dispose(); sellerReservation.Dispose();
                        ApplyPurchase(pc, inventory, buyerData); ApplyPurchase(seller.pc, seller.inventory, sellerData);
                        Synchronize(); seller.Synchronize();
                    });
                if (!purchased.success && (purchased.error == "RELOAD_REQUIRED" || purchased.error == "SAVE_CONFLICT" || purchased.error == "OPERATION_MISMATCH"))
                { pc.connectionToClient.Disconnect(); if (seller.pc.connectionToClient != null) seller.pc.connectionToClient.Disconnect(); }
                else pc.RpcShowMessage(purchased.success ? "Compra concluida." : purchased.error, purchased.success ? PlayerMessageType.Success : PlayerMessageType.Warning);
            }
            catch (Exception ex) { if (pc != null) pc.RpcShowMessage(ex.Message, PlayerMessageType.Warning); }
            finally { buyerReservation.Dispose(); sellerReservation.Dispose(); }
        }
        bool CanBuy(PlayerLifeServices seller)
        {
            var a = GetComponent<PlayerStats>(); var b = seller.GetComponent<PlayerStats>();
            return pc != null && seller.pc != null && pc.connectionToClient != null && seller.pc.connectionToClient != null && seller.StallActive && !StallActive && pc.ArenaInstanceId == 0 && seller.pc.ArenaInstanceId == 0 && !pc.IsAboardBoat && !seller.pc.IsAboardBoat && pc.MapName == seller.pc.MapName && a != null && b != null && !a.IsDead && !b.IsDead && Vector3.Distance(transform.position, seller.transform.position) <= 5;
        }
        static void ApplyPurchase(PlayerController controller, PlayerInventory inv, CharacterData saved)
        {
            var live = controller.GetCharacterData();
            live.SaveRevision = saved.SaveRevision; live.Inventory = saved.Inventory; live.Gameplay = saved.Gameplay; live.GameplayStateVersion = 1;
            controller.AdminSetGold(saved.Gold); inv.InitializeFromData(saved.Inventory);
        }
        public override void OnStartLocalPlayer() { base.OnStartLocalPlayer(); TOP.UI.LifeServicesUI.Bind(this); }
    }
}
