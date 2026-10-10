using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Linq;

namespace TOP.Player
{
    // Sistema de grupo (Party) server-autoritativo. Os grupos existem so em memoria no servidor
    // (lista estatica por PartyId); cada jogador carrega este componente para expor seu PartyId/
    // lideranca e a lista de membros (sincronizados por SyncVar, no mesmo padrao de string
    // serializada ja usado por PlayerInventory/PlayerEquipment).
    public class PlayerParty : NetworkBehaviour
    {
        public const int MaxPartySize = 6;
        const float InviteTimeout = 30f;

        [SyncVar(hook = nameof(OnPartyIdChanged))] public int PartyId;
        [SyncVar] public bool IsLeader;
        [SyncVar(hook = nameof(OnMembersChanged))] string _membersData = "";

        public event System.Action OnPartyChanged;
        public List<string> Members { get; private set; } = new List<string>();

        static readonly Dictionary<int, List<PlayerParty>> _parties = new Dictionary<int, List<PlayerParty>>();
        static int _nextPartyId = 1;

        class PendingInvite { public PlayerParty Inviter; public float Time; }
        static readonly Dictionary<string, PendingInvite> _pendingInvites = new Dictionary<string, PendingInvite>();

        PlayerController _pc;

        void Awake() { _pc = GetComponent<PlayerController>(); }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            TOP.UI.PartyUI.Bind(this);
        }

        [Server]
        void ShowMsg(string text, PlayerMessageType type) => _pc?.RpcShowMessage(text, type);

        [Command]
        public void CmdPartyInvite(string targetName)
        {
            if (_pc == null || _pc.ArenaInstanceId > 0 || string.IsNullOrWhiteSpace(targetName) || targetName == _pc.CharacterName) return;
            if (PartyId != 0 && !IsLeader) { ShowMsg("Apenas o lider do grupo pode convidar.", PlayerMessageType.Warning); return; }
            if (PartyId != 0 && _parties.TryGetValue(PartyId, out var current) && current.Count >= MaxPartySize) { ShowMsg("Grupo cheio.", PlayerMessageType.Warning); return; }

            var target = FindByName(targetName);
            if (target == null) { ShowMsg("Jogador nao encontrado.", PlayerMessageType.Warning); return; }
            if (target == this || target._pc.ArenaInstanceId > 0) return;
            if (target.PartyId != 0) { ShowMsg(targetName + " ja esta em um grupo.", PlayerMessageType.Warning); return; }

            _pendingInvites[targetName] = new PendingInvite { Inviter = this, Time = Time.time };
            target.TargetPartyInvite(target.connectionToClient, _pc.CharacterName);
            ShowMsg("Convite enviado para " + targetName + ".", PlayerMessageType.Info);
        }

        [TargetRpc]
        void TargetPartyInvite(NetworkConnectionToClient conn, string inviterName)
        {
            TOP.UI.PartyUI.ShowInvite(this, inviterName);
        }

        [Command]
        public void CmdPartyAccept(string inviterName)
        {
            if (_pc != null && _pc.ArenaInstanceId > 0) return;
            if (_pc == null) return;
            if (!_pendingInvites.TryGetValue(_pc.CharacterName, out var invite) || invite.Inviter == null)
            { ShowMsg("Convite expirado.", PlayerMessageType.Warning); return; }
            _pendingInvites.Remove(_pc.CharacterName);
            var inviter = invite.Inviter;
            if (inviter._pc == null || inviter._pc.ArenaInstanceId > 0 || inviter._pc.CharacterName != inviterName || Time.time - invite.Time > InviteTimeout)
            { ShowMsg("Convite expirado.", PlayerMessageType.Warning); return; }
            if (PartyId != 0) { ShowMsg("Voce ja esta em um grupo.", PlayerMessageType.Warning); return; }

            if (inviter.PartyId == 0)
            {
                inviter.PartyId = _nextPartyId++;
                inviter.IsLeader = true;
                _parties[inviter.PartyId] = new List<PlayerParty> { inviter };
            }
            if (!_parties.TryGetValue(inviter.PartyId, out var list) || list.Count >= MaxPartySize)
            { ShowMsg("Grupo cheio.", PlayerMessageType.Warning); return; }

            PartyId = inviter.PartyId;
            IsLeader = false;
            list.Add(this);
            RebuildMembersFor(list);
            ShowMsg("Voce entrou no grupo de " + inviterName + ".", PlayerMessageType.Success);
        }

        [Command]
        public void CmdPartyDecline(string inviterName)
        {
            if (_pc != null) _pendingInvites.Remove(_pc.CharacterName);
        }

        [Command]
        public void CmdPartyLeave() { if (_pc != null && _pc.ArenaInstanceId == 0) LeaveParty(true); }

        [Server]
        void LeaveParty(bool notifySelf)
        {
            if (PartyId == 0 || !_parties.TryGetValue(PartyId, out var list)) { PartyId = 0; IsLeader = false; _membersData = ""; return; }
            list.Remove(this);
            int oldPartyId = PartyId;
            bool wasLeader = IsLeader;
            PartyId = 0; IsLeader = false; _membersData = "";
            if (notifySelf) ShowMsg("Voce saiu do grupo.", PlayerMessageType.Info);

            if (list.Count <= 1)
            {
                foreach (var m in list) { m.PartyId = 0; m.IsLeader = false; m._membersData = ""; m.ShowMsg("O grupo foi desfeito.", PlayerMessageType.Info); }
                _parties.Remove(oldPartyId);
            }
            else
            {
                if (wasLeader) list[0].IsLeader = true;
                RebuildMembersFor(list);
            }
        }

        [Command]
        public void CmdPartyKick(string targetName)
        {
            if (_pc == null || _pc.ArenaInstanceId > 0 || !IsLeader || PartyId == 0 || !_parties.TryGetValue(PartyId, out var list)) return;
            var target = list.FirstOrDefault(m => m._pc != null && m._pc.CharacterName == targetName);
            if (target == null || target == this) return;
            target.LeaveParty(true);
            ShowMsg(targetName + " foi removido do grupo.", PlayerMessageType.Info);
        }

        [Server]
        static void RebuildMembersFor(List<PlayerParty> list)
        {
            string data = string.Join(";", list.Where(m => m._pc != null).Select(m => (m.IsLeader ? "*" : "") + m._pc.CharacterName));
            foreach (var m in list) m._membersData = data;
        }

        void OnPartyIdChanged(int oldValue, int newValue) => OnPartyChanged?.Invoke();

        void OnMembersChanged(string oldValue, string newValue)
        {
            Members = string.IsNullOrEmpty(newValue) ? new List<string>() : newValue.Split(';').ToList();
            OnPartyChanged?.Invoke();
        }

        [Server]
        static PlayerParty FindByName(string name)
        {
            foreach (var kv in NetworkServer.spawned)
            {
                var pp = kv.Value.GetComponent<PlayerParty>();
                var pc = kv.Value.GetComponent<PlayerController>();
                if (pp != null && pc != null && pc.CharacterName == name) return pp;
            }
            return null;
        }

        // Usado pelo roteamento de chat de grupo em TOPNetworkManager.
        public static IEnumerable<NetworkConnectionToClient> ConnectionsInPartyOf(PlayerParty p)
        {
            if (p == null || p.PartyId == 0 || !_parties.TryGetValue(p.PartyId, out var list)) yield break;
            foreach (var m in list)
                if (m.connectionToClient != null) yield return m.connectionToClient;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (NetworkServer.active) LeaveParty(false);
        }
    }
}
