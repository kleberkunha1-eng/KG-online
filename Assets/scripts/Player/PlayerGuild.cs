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

        PlayerController _pc;
        public GuildData Guild { get; private set; }
        public event System.Action OnGuildChanged;

        void Awake() => _pc = GetComponent<PlayerController>();

        string Token => TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);

        [Server]
        public async void RefreshGuildInternal()
        {
            if (_pc == null) return;
            var data = await DatabaseService.Instance.GetGuildAsync(_pc.CharacterId, Token);
            GuildName = data != null && data.InGuild ? data.Name : "";
            TargetGuildUpdated(connectionToClient, Serialize(data));
        }

        [Command]
        public void CmdRefreshGuild() => RefreshGuildInternal();

        [Command]
        public async void CmdCreateGuild(string name)
        {
            if (_pc == null || string.IsNullOrWhiteSpace(name)) return;
            var (success, error, _) = await DatabaseService.Instance.CreateGuildAsync(_pc.CharacterId, name.Trim(), Token);
            _pc.RpcShowMessage(success ? "Guilda '" + name + "' fundada!" : FriendlyError(error), success ? PlayerMessageType.Success : PlayerMessageType.Warning);
            if (success) RefreshGuildInternal();
        }

        [Command]
        public async void CmdGuildInvite(string targetName)
        {
            if (_pc == null) return;
            var (success, error) = await DatabaseService.Instance.GuildInviteAsync(_pc.CharacterId, targetName, Token);
            _pc.RpcShowMessage(success ? targetName + " foi adicionado a guilda." : FriendlyError(error), success ? PlayerMessageType.Success : PlayerMessageType.Warning);
            if (success)
            {
                foreach (var kv in NetworkServer.spawned)
                {
                    var targetPc = kv.Value.GetComponent<PlayerController>();
                    if (targetPc != null && targetPc.CharacterName == targetName)
                    {
                        kv.Value.GetComponent<PlayerGuild>()?.RefreshGuildInternal();
                        break;
                    }
                }
            }
        }

        [Command]
        public async void CmdGuildLeave()
        {
            if (_pc == null) return;
            if (await DatabaseService.Instance.GuildLeaveAsync(_pc.CharacterId, Token))
            {
                GuildName = "";
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
            if (success) RefreshGuildInternal();
        }

        [Command]
        public async void CmdGuildSetNotice(string notice)
        {
            if (await DatabaseService.Instance.GuildSetNoticeAsync(_pc.CharacterId, notice ?? "", Token)) RefreshGuildInternal();
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
