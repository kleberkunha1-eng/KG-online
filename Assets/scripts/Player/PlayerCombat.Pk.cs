using Mirror;
using TOP.Data;
using System;
using System.Linq;

namespace TOP.Player
{
    public partial class PlayerCombat
    {
        [Server]
        public bool CanPlayerAttack(PlayerCombat opponent) => CanDuelAttack(opponent) || CanPkAttack(opponent);

        [Server]
        public bool CanPkAttack(PlayerCombat opponent)
        {
            if (opponent == null || opponent == this || _controller == null || opponent._controller == null
                || !_controller.IsInitialized || !opponent._controller.IsInitialized || _stats == null || opponent._stats == null
                || _stats.IsDead || opponent._stats.IsDead || connectionToClient == null || opponent.connectionToClient == null
                || _controller.MapName != "teampk" || opponent._controller.MapName != _controller.MapName
                || _controller.BoatOperationPending || opponent._controller.BoatOperationPending
                || (GetComponent<PlayerInventory>()?.HasQuestTransaction ?? false)
                || (opponent.GetComponent<PlayerInventory>()?.HasQuestTransaction ?? false)
                || (GetComponent<PlayerTrade>()?.InTrade ?? false) || (opponent.GetComponent<PlayerTrade>()?.InTrade ?? false)
                || (GetComponent<PlayerQuests>()?.HasPendingCompletion ?? false)
                || (opponent.GetComponent<PlayerQuests>()?.HasPendingCompletion ?? false)) return false;
            return OriginalPvpRules.CanFightInArena(_controller.MapName, transform.position, GetComponent<PlayerParty>()?.PartyId ?? 0,
                opponent._controller.MapName, opponent.transform.position, opponent.GetComponent<PlayerParty>()?.PartyId ?? 0);
        }

        [Server]
        public void ApplyPlayerDamage(PlayerCombat opponent, int damage)
        {
            if (CanDuelAttack(opponent)) { ApplyDuelDamage(opponent, damage); return; }
            if (!CanPkAttack(opponent) || damage <= 0) return;
            opponent._stats.TakeDamage(damage);
            if (opponent._stats.IsDead)
            {
                var equipment = opponent.GetComponent<PlayerInventory>()?.GetInventoryData()
                    .Where(item => item.IsEquipped).Select(item => item.ItemId).ToArray();
                if (OriginalPvpRules.ArenaLosesStamina(opponent._controller.Level, DateTime.Now.Hour, equipment))
                    opponent._stats.SetCurrentHpMpSp(0, opponent._stats.CurrentMp, 0);
                StopAttack();
                opponent.StopAttack();
                _controller.RpcShowMessage("Vitoria na arena sobre " + opponent._controller.CharacterName + ".", PlayerMessageType.Info);
                opponent._controller.RpcShowMessage("Derrota na arena. Sem perda de EXP ou itens no teampk original.", PlayerMessageType.Death);
            }
        }
    }
}
