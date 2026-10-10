using System;
using UnityEngine;
using Mirror;
using System.Collections.Generic;
using TOP.Data;
using TOP.Services;
using TOP.Network;

namespace TOP.Player
{
    // Guilda persistida via API (tabelas guilds/guild_members). GuildName e sincronizado como
    // SyncVar (para exibir acima da cabeca do personagem, como no cliente original); o restante
    // do roster (membros, patente, aviso) e consultado sob demanda e empurrado so para o dono.
    public class PlayerGuild : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnGuildNameChanged))] public string GuildName = "";
        [SyncVar] public int GuildId;
        [SyncVar] public bool GuildDataReady;

        PlayerController _pc;
        public GuildData Guild { get; private set; }
        public event System.Action OnGuildChanged;

        void Awake() => _pc = GetComponent<PlayerController>();

        string Token => TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);

        [Server]
        public async void RefreshGuildInternal()
        {
            if (_pc == null || connectionToClient == null || TOPNetworkManager.Instance == null) return;
            GuildDataReady = false;
            try
            {
                GuildData data = await DatabaseService.Instance.GetGuildAsync(_pc.CharacterId, Token);
                if (data == null) return;
                Guild = data;
                GuildId = data.InGuild ? data.GuildId : 0;
                GuildName = data.InGuild ? data.Name : "";
                GuildDataReady = true;
                TargetGuildUpdated(connectionToClient, Serialize(data));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Guild] Membership refresh failed; guild-protected PK stays blocked: " + ex.Message);
            }
        }

        [Command]
        public void CmdRefreshGuild() => RefreshGuildInternal();

        [Command]
        public async void CmdCreateGuild(string name)
        {
            if (_pc == null || !GuildDataReady || GuildId != 0 || string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.Length < 3 || name.Length > 32 || name.IndexOfAny(new[] { '|', ';', ':', '\n', '\r' }) >= 0) return;
            bool created = await PlayerDurableAction.Run(_pc, data =>
            {
                var movement = GetComponent<PlayerMovement>();
                var npc = movement != null ? movement.ActiveNpc : null;
                if (npc == null || npc.NpcName != "Icicle Royal - Mas" || npc.NpcId != "254" || !npc.CanInteract(movement)) throw new InvalidOperationException("Fale com Mas em Icicle para criar uma guilda.");
                if (data.Gold < 100000) throw new InvalidOperationException("A guilda custa 100.000 ouro e uma Stone of Oath.");
                var stone = data.Inventory.Find(i => i.ItemId == 1780 && !i.IsEquipped && !i.IsLocked && i.Quantity > 0);
                if (stone == null) throw new InvalidOperationException("Falta Stone of Oath (1780).");
                if (--stone.Quantity == 0) data.Inventory.Remove(stone);
                data.Gold -= 100000;
                data.GuildCreateName = name;
            }, synchronizeBefore: true);
            if (created) { _pc.RpcShowMessage("Guilda criada.", PlayerMessageType.Success); RefreshGuildInternal(); }
        }

        PlayerGuild pendingInviter;
        double invitationExpires;
        [SyncVar] public string InvitationFrom = "";
        [Command]
        public void CmdGuildInvite(string targetName)
        {
            if (_pc == null || Guild == null || !Guild.InGuild || (Guild.MyRank != "Lider" && Guild.MyRank != "Oficial")) return;
            foreach (var identity in NetworkServer.spawned.Values)
            {
                var target = identity.GetComponent<PlayerGuild>();
                var pc = identity.GetComponent<PlayerController>();
                if (target == null || pc == null || pc.CharacterName != targetName || target == this || !target.GuildDataReady || target.GuildId != 0) continue;
                target.pendingInviter = this;
                target.invitationExpires = NetworkTime.time + 30;
                target.InvitationFrom = GuildName;
                pc.RpcShowMessage("Convite de guilda: " + GuildName + ". Abra Guilda para aceitar.", PlayerMessageType.Info);
                return;
            }
            _pc.RpcShowMessage("Convites exigem jogador online e sem guilda.", PlayerMessageType.Warning);
        }
        [Command]
        public async void CmdGuildAnswerInvite(bool accept)
        {
            var inviter = pendingInviter;
            bool valid = invitationExpires >= NetworkTime.time;
            pendingInviter = null;
            InvitationFrom = "";
            if (!accept || !valid || inviter == null || inviter._pc == null || _pc == null || inviter.connectionToClient == null || GuildId != 0) return;
            var result = await DatabaseService.Instance.GuildInviteAsync(inviter._pc.CharacterId, _pc.CharacterName, inviter.Token);
            if (result.success) { RefreshGuildInternal(); inviter.RefreshGuildInternal(); }
        }
        [Command]
        public async void CmdGuildSetRank(string targetName, bool officer)
        {
            if (_pc == null || Guild == null || Guild.MyRank != "Lider") return;
            if (await DatabaseService.Instance.GuildSetRankAsync(_pc.CharacterId, targetName, officer, Token)) RefreshGuildMembers();
        }

        [Command]
        public async void CmdGuildLeave()
        {
            if (_pc == null) return;
            if (await DatabaseService.Instance.GuildLeaveAsync(_pc.CharacterId, Token))
            {
                RefreshGuildMembers();
                GuildName = "";
                GuildId = 0;
                GuildDataReady = true;
                _pc.RpcShowMessage("Voce saiu da guilda.", PlayerMessageType.Info);
                RefreshGuildInternal();
            }
        }

        [Command]
        public async void CmdGuildKick(string targetName)
        {
            if (_pc == null) return;
            var (success, error) = await DatabaseService.Instance.GuildKickAsync(_pc.CharacterId, targetName, Token);
            _pc.RpcShowMessage(success ? targetName + " foi expulso da guilda." : FriendlyError(error), success ? PlayerMessageType.Info : PlayerMessageType.Warning);
            if (success) RefreshGuildMembers();
        }

        [Command]
        public async void CmdGuildSetNotice(string notice)
        {
            if (await DatabaseService.Instance.GuildSetNoticeAsync(_pc.CharacterId, notice ?? "", Token)) RefreshGuildInternal();
        }

        [Server]
        void RefreshGuildMembers()
        {
            foreach (var identity in NetworkServer.spawned.Values)
            {
                var member = identity.GetComponent<PlayerGuild>();
                if (member != null && member.GuildId == GuildId) member.RefreshGuildInternal();
            }
        }
        static string FriendlyError(string error) => error switch
        {
            "INVALID_NAME" => "Nome de guilda invalido (3-32 caracteres).",
            "ALREADY_IN_GUILD" => "Voce ja esta em uma guilda.",
            "INSUFFICIENT_GOLD" => "Ouro insuficiente (custo: 100.000).",
            "NAME_TAKEN" => "Ja existe uma guilda com esse nome.",
            "NOT_AUTHORIZED" => "Apenas o lider ou oficiais podem fazer isso.",
            "PLAYER_NOT_FOUND" => "Jogador nao encontrado.",
            "TARGET_IN_GUILD" => "Esse jogador ja esta em uma guilda.",
            "NOT_IN_GUILD" => "Voce nao esta em uma guilda.",
            _ => "Operacao de guilda falhou.",
        };

        static string Serialize(GuildData d)
        {
            if (d == null || !d.InGuild) return "";
            var members = new List<string>();
            foreach (var m in d.Members) members.Add(m.CharacterId + ":" + m.Name + ":" + m.Rank);
            return string.Join("|", d.GuildId, Escape(d.Name), Escape(d.Notice), d.Level, d.LeaderCharacterId, d.MyRank, string.Join(";", members));
        }

        static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "/").Replace(";", ",");

        [TargetRpc]
        void TargetGuildUpdated(NetworkConnectionToClient conn, string data)
        {
            if (string.IsNullOrEmpty(data)) { Guild = null; OnGuildChanged?.Invoke(); return; }
            var p = data.Split('|');
            var g = new GuildData
            {
                InGuild = true, GuildId = int.Parse(p[0]), Name = p[1], Notice = p[2], Level = int.Parse(p[3]),
                LeaderCharacterId = long.Parse(p[4]), MyRank = p[5],
            };
            if (p.Length > 6 && !string.IsNullOrEmpty(p[6]))
                foreach (var entry in p[6].Split(';'))
                {
                    var mp = entry.Split(':');
                    if (mp.Length < 3) continue;
                    g.Members.Add(new GuildMemberData { CharacterId = long.Parse(mp[0]), Name = mp[1], Rank = mp[2] });
                }
            Guild = g;
            OnGuildChanged?.Invoke();
        }

        void OnGuildNameChanged(string oldValue, string newValue) { }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            TOP.UI.GuildUI.Bind(this);
            CmdRefreshGuild();
        }
    }
}
