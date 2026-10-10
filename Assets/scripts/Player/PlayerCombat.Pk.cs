using Mirror;
using UnityEngine;
using TOP.Data;
using TOP.Core;

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
                || _controller.BoatOperationPending || opponent._controller.BoatOperationPending
                || (GetComponent<PlayerInventory>() != null && GetComponent<PlayerInventory>().HasQuestTransaction)
                || (opponent.GetComponent<PlayerInventory>() != null && opponent.GetComponent<PlayerInventory>().HasQuestTransaction)
                || (GetComponent<PlayerTrade>() != null && GetComponent<PlayerTrade>().InTrade)
                || (opponent.GetComponent<PlayerTrade>() != null && opponent.GetComponent<PlayerTrade>().InTrade)
                || (GetComponent<PlayerQuests>() != null && GetComponent<PlayerQuests>().HasPendingCompletion)
                || (opponent.GetComponent<PlayerQuests>() != null && opponent.GetComponent<PlayerQuests>().HasPendingCompletion)) return false;

            if (_controller.ArenaInstanceId != opponent._controller.ArenaInstanceId) return false;
            if (_controller.ArenaInstanceId > 0 && !TOP.Systems.ArenaCoordinator.Opponents(_controller, opponent._controller)) return false;
            Vector3 sourcePosition = _controller.ArenaInstanceId > 0 ? TOP.Systems.ArenaWorld.AttributePosition(_controller.ArenaInstanceId, transform.position) : transform.position;
            Vector3 targetPosition = opponent._controller.ArenaInstanceId > 0 ? TOP.Systems.ArenaWorld.AttributePosition(opponent._controller.ArenaInstanceId, opponent.transform.position) : opponent.transform.position;
            PlayerParty sourceParty = GetComponent<PlayerParty>();
            PlayerParty targetParty = opponent.GetComponent<PlayerParty>();
            PlayerGuild sourceGuild = GetComponent<PlayerGuild>();
            PlayerGuild targetGuild = opponent.GetComponent<PlayerGuild>();
            bool sourceGuildKnown = !OriginalPvpRules.RequiresGuildData(_controller.MapName) || (sourceGuild != null && sourceGuild.GuildDataReady);
            bool targetGuildKnown = !OriginalPvpRules.RequiresGuildData(opponent._controller.MapName) || (targetGuild != null && targetGuild.GuildDataReady);
            return OriginalPvpRules.CanFightInMap(_controller.MapName, sourcePosition,
                sourceParty != null ? sourceParty.PartyId : 0, sourceGuild != null ? sourceGuild.GuildId : 0,
                sourceGuildKnown, 0, opponent._controller.MapName, targetPosition,
                targetParty != null ? targetParty.PartyId : 0, targetGuild != null ? targetGuild.GuildId : 0,
                targetGuildKnown, 0);
        }

        [Server]
        public void ApplyPlayerDamage(PlayerCombat opponent, int damage)
        {
            if (CanDuelAttack(opponent)) { ApplyDuelDamage(opponent, damage); return; }
            if (!CanPkAttack(opponent) || damage <= 0) return;
            opponent._stats.TakeDamage(damage, netId, DamageType.Physical);
            if (!opponent._stats.IsDead) return;
            if (TOP.Systems.ArenaCoordinator.Instance != null) TOP.Systems.ArenaCoordinator.Instance.PlayerKilled(_controller, opponent._controller);
            StopAttack();
            opponent.StopAttack();
            _controller.RpcShowMessage("Vitoria sobre " + opponent._controller.CharacterName + ".", PlayerMessageType.Info);
            opponent._controller.RpcShowMessage("Derrota. O PK original nao concede pontos de crime nem recompensa de EXP.", PlayerMessageType.Death);
        }
    }
}
