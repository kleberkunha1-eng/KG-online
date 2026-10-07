using UnityEngine;
using Mirror;
using System.Collections.Generic;
using TOP.Data;
using TOP.Services;
using TOP.Network;

namespace TOP.Player
{
    // Lista de amigos persistida via API (tabela friendships). O servidor consulta o banco sob
    // demanda (sem cache constante) e empurra o resultado para o dono via TargetRpc, no mesmo
    // formato serializado ja usado por PlayerParty (id:name:level:job:lastOnlineUnix;...).
    public class PlayerFriends : NetworkBehaviour
    {
        PlayerController _pc;
        public List<FriendData> Friends { get; private set; } = new List<FriendData>();
        public event System.Action OnFriendsChanged;

        void Awake() => _pc = GetComponent<PlayerController>();

        string Token => TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);

        [Server]
        async void RefreshFriendsInternal()
        {
            if (_pc == null) return;
            var list = await DatabaseService.Instance.GetFriendsAsync(_pc.CharacterId, Token);
            TargetFriendsUpdated(connectionToClient, Serialize(list));
        }

        [Command]
        public void CmdRefreshFriends() => RefreshFriendsInternal();

        [Command]
        public async void CmdAddFriend(string targetName)
        {
            if (_pc == null || string.IsNullOrWhiteSpace(targetName)) return;
            var (success, error) = await DatabaseService.Instance.AddFriendAsync(_pc.CharacterId, targetName.Trim(), Token);
            _pc.RpcShowMessage(success ? "Amigo adicionado: " + targetName : FriendlyError(error), success ? PlayerMessageType.Success : PlayerMessageType.Warning);
            if (success) RefreshFriendsInternal();
        }

        [Command]
        public async void CmdRemoveFriend(long friendId)
        {
            if (_pc == null) return;
            bool success = await DatabaseService.Instance.RemoveFriendAsync(_pc.CharacterId, friendId, Token);
            if (success) RefreshFriendsInternal();
        }

        static string FriendlyError(string error) => error switch
        {
            "PLAYER_NOT_FOUND" => "Jogador nao encontrado.",
            "ALREADY_FRIENDS" => "Voces ja sao amigos.",
            "CANNOT_ADD_SELF" => "Voce nao pode adicionar a si mesmo.",
            _ => "Nao foi possivel adicionar o amigo.",
        };

        static string Serialize(List<FriendData> list)
        {
            var parts = new List<string>();
            foreach (var f in list)
                parts.Add(f.Id + ":" + f.Name + ":" + f.Level + ":" + f.Job + ":" + (f.LastOnline.HasValue ? new System.DateTimeOffset(f.LastOnline.Value).ToUnixTimeSeconds() : 0));
            return string.Join(";", parts);
        }

        [TargetRpc]
        void TargetFriendsUpdated(NetworkConnectionToClient conn, string data)
        {
            Friends.Clear();
            if (!string.IsNullOrEmpty(data))
            {
                foreach (var entry in data.Split(';'))
                {
                    var p = entry.Split(':');
                    if (p.Length < 5) continue;
                    Friends.Add(new FriendData
                    {
                        Id = long.Parse(p[0]),
                        Name = p[1],
                        Level = int.Parse(p[2]),
                        Job = int.Parse(p[3]),
                        LastOnline = long.TryParse(p[4], out long t) && t > 0 ? System.DateTimeOffset.FromUnixTimeSeconds(t).UtcDateTime : (System.DateTime?)null,
                    });
                }
            }
            OnFriendsChanged?.Invoke();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            TOP.UI.FriendsUI.Bind(this);
        }
    }
}
