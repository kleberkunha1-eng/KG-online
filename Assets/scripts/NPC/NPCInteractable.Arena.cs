using Mirror;
using TOP.Data;
using TOP.Systems;
using TOP.Player;

namespace TOP.NPC
{
    public partial class NPCInteractable
    {
        public bool IsArenaAdministrator => NpcId == "53" && NpcName == "Arena Administrator";
        [Command(requiresAuthority = false)]
        public void CmdObtainArenaMedal(NetworkConnectionToClient sender = null)
        {
            if (!IsArenaAdministrator || !TryCustomer(sender, out var pc, out var bag)) return;
            int count = bag.GetInventoryData().FindAll(i => i.ItemId == 3849).Count;
            int slot = bag.FindEmptySlot();
            if (!OriginalArenaRules.CanObtain(pc.Level, pc.Gold, count, slot))
            { pc.RpcShowMessage("Medalha exige nivel >25, 50000 ouro, nenhum exemplar e um slot livre.", PlayerMessageType.Warning); return; }
            if (!bag.AddItem(3849, 1, (ushort)slot)) return;
            var medal = bag.GetSlot(slot);
            medal.MedalHonor = medal.MedalWins = medal.MedalEntries = medal.MedalKills = medal.MedalDeaths = 10;
            medal.IsLocked = true; medal.OwnerCharacterId = pc.CharacterId;
            pc.Gold -= 50000; bag.SerializeInventory();
            if (TOP.Network.TOPNetworkManager.Instance != null) TOP.Network.TOPNetworkManager.Instance.SavePlayerAfterDeath(pc);
            pc.RpcShowMessage("Medal of Valor recebido (atributos originais grade 97).", PlayerMessageType.Success);
        }
        [Command(requiresAuthority = false)]
        public void CmdArenaRegister(bool party, bool cancel, NetworkConnectionToClient sender = null)
        {
            if (!IsArenaAdministrator || !TryCustomer(sender, out var pc, out _) || ArenaCoordinator.Instance == null) return;
            if (cancel) { ArenaCoordinator.Instance.Cancel(pc); pc.RpcShowMessage("Inscricao cancelada.", PlayerMessageType.Info); return; }
            if (!ArenaCoordinator.Instance.Register(pc, party, out var error)) pc.RpcShowMessage(error, PlayerMessageType.Warning);
            else pc.RpcShowMessage("Inscrito por ate 120s; aguarde adversario no Argent Bar.", PlayerMessageType.Info);
        }
    }
}
