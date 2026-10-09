using Mirror;
using UnityEngine;

namespace TOP.Player
{
    public partial class PlayerCombat
    {
        [SyncVar] public uint DuelOpponentNetId;
        PlayerCombat duelOpponent, pendingChallenger, outgoingChallenge;
        float challengeExpires, nextChallenge;

        [Command]
        public void CmdRequestDuel(NetworkIdentity identity)
        {
            var opponent = identity != null ? identity.GetComponent<PlayerCombat>() : null;
            if (Time.time < nextChallenge)
            {
                _controller?.RpcShowMessage("Aguarde antes de enviar outro desafio.", PlayerMessageType.Warning);
                return;
            }
            nextChallenge = Time.time + 3;
            if (!AvailableForDuel(opponent) || duelOpponent != null || opponent.duelOpponent != null
                || pendingChallenger != null || outgoingChallenge != null
                || opponent.pendingChallenger != null || opponent.outgoingChallenge != null)
            {
                _controller?.RpcShowMessage("Duelo indisponivel: aproximem-se, fiquem vivos e terminem negociacoes/desafios.", PlayerMessageType.Warning);
                return;
            }
            outgoingChallenge = opponent;
            opponent.pendingChallenger = this;
            opponent.challengeExpires = Time.time + 30;
            opponent.TargetDuelInvite(opponent.connectionToClient, netId, _controller.CharacterName);
            _controller.RpcShowMessage("Desafio de duelo enviado.", PlayerMessageType.Info);
        }

        bool AvailableForDuel(PlayerCombat other) => other != null && other != this
            && connectionToClient != null && other.connectionToClient != null
            && _controller != null && other._controller != null
            && _controller.IsInitialized && other._controller.IsInitialized
            && _stats != null && other._stats != null && !_stats.IsDead && !other._stats.IsDead
            && _controller.MapName == other._controller.MapName
            && Vector3.Distance(transform.position, other.transform.position) <= 20
            && !(GetComponent<PlayerTrade>()?.InTrade ?? false)
            && !(other.GetComponent<PlayerTrade>()?.InTrade ?? false);

        [TargetRpc]
        void TargetDuelInvite(NetworkConnectionToClient connection, uint challenger, string name) =>
            TOP.UI.PlayerInteractionMenu.ShowDuelInvite(challenger, name);

        [Command]
        public void CmdRespondDuel(uint challenger, bool accept)
        {
            var opponent = pendingChallenger;
            bool valid = opponent != null && opponent.netId == challenger && opponent.outgoingChallenge == this
                && Time.time <= challengeExpires;
            ClearChallenge();
            if (!valid || !AvailableForDuel(opponent) || duelOpponent != null || opponent.duelOpponent != null)
            {
                _controller?.RpcShowMessage("Desafio expirado ou indisponivel.", PlayerMessageType.Warning);
                return;
            }
            if (!accept)
            {
                opponent._controller.RpcShowMessage("Desafio recusado.", PlayerMessageType.Info);
                return;
            }
            duelOpponent = opponent;
            opponent.duelOpponent = this;
            DuelOpponentNetId = opponent.netId;
            opponent.DuelOpponentNetId = netId;
            StopAttack();
            opponent.StopAttack();
            _controller.RpcShowMessage("Duelo iniciado. Clique no oponente para atacar. Fim com 1 HP.", PlayerMessageType.Info);
            opponent._controller.RpcShowMessage("Duelo iniciado. Clique no oponente para atacar. Fim com 1 HP.", PlayerMessageType.Info);
        }

        [Command]
        public void CmdCancelDuel()
        {
            ClearChallenge();
            if (duelOpponent != null) EndDuel("Duelo cancelado.");
        }

        [Server]
        public bool CanDuelAttack(PlayerCombat opponent) => opponent != null && duelOpponent == opponent
            && opponent.duelOpponent == this && AvailableForDuel(opponent);

        [Server]
        public void ApplyDuelDamage(PlayerCombat opponent, int damage)
        {
            if (!CanDuelAttack(opponent) || damage <= 0) return;
            int finalDamage = Mathf.Max(1, damage - opponent._stats.PhysicalDefense);
            opponent._stats.SetCurrentHpMpSp(Mathf.Max(1, opponent._stats.CurrentHp - finalDamage),
                opponent._stats.CurrentMp, opponent._stats.CurrentSp);
            if (opponent._stats.CurrentHp <= 1) EndDuel("Duelo encerrado. Vencedor: " + _controller.CharacterName);
        }

        [Server]
        void UpdateDuel()
        {
            if (pendingChallenger != null && Time.time > challengeExpires) ClearChallenge();
            if (duelOpponent != null && !AvailableForDuel(duelOpponent)) EndDuel("Duelo encerrado: distancia, mapa ou estado invalido.");
        }

        void ClearChallenge()
        {
            if (pendingChallenger != null) pendingChallenger.outgoingChallenge = null;
            if (outgoingChallenge != null) outgoingChallenge.pendingChallenger = null;
            pendingChallenger = outgoingChallenge = null;
        }

        [Server]
        void EndDuel(string message)
        {
            var opponent = duelOpponent;
            duelOpponent = null;
            DuelOpponentNetId = 0;
            StopAttack();
            _movement?.Stop();
            _controller?.RpcShowMessage(message, PlayerMessageType.Info);
            if (opponent == null) return;
            opponent.duelOpponent = null;
            opponent.DuelOpponentNetId = 0;
            opponent.StopAttack();
            opponent._movement?.Stop();
            opponent._controller?.RpcShowMessage(message, PlayerMessageType.Info);
        }

        public override void OnStopServer()
        {
            ClearChallenge();
            if (duelOpponent != null) EndDuel("Duelo encerrado: jogador desconectou.");
            base.OnStopServer();
        }
    }
}
