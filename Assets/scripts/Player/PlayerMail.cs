using UnityEngine;
using Mirror;
using System.Collections.Generic;
using TOP.Data;
using TOP.Services;
using TOP.Network;

namespace TOP.Player
{
    // Correio (mail) persistido via API (tabela mails). Suporta ouro e um item anexado por carta.
    // O item anexado so e de fato entregue no inventario quando o jogador clica em "Resgatar"
    // (claim), espelhando o fluxo do cliente original.
    public class PlayerMail : NetworkBehaviour
    {
        PlayerController _pc;
        PlayerInventory _inventory;
        public List<MailData> Mails { get; private set; } = new List<MailData>();
        public event System.Action OnMailChanged;

        void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _inventory = GetComponent<PlayerInventory>();
        }

        string Token => TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);

        [Server]
        async void RefreshMailInternal()
        {
            if (_pc == null) return;
            var list = await DatabaseService.Instance.GetMailAsync(_pc.CharacterId, Token);
            TargetMailUpdated(connectionToClient, Serialize(list));
        }

        [Command]
        public void CmdRefreshMail() => RefreshMailInternal();

        [Command]
        public void CmdSendMail(string targetName, string subject, string body, long gold, int itemId, int itemQuantity, int itemRefine)
        {
            if (_pc != null) _pc.RpcShowMessage("Original: apenas correio do GM/Item Mall. Correio com anexos entre jogadores desativado.", PlayerMessageType.Warning);
        }

        public bool InventoryRequestPending { get; private set; }

        async System.Threading.Tasks.Task SendMailInternal(string targetName, string subject, string body,
            long gold, int itemId, int itemQuantity, int itemRefine)
        {
            if (_pc == null || string.IsNullOrWhiteSpace(targetName)) return;
            gold = System.Math.Max(0, gold);

            // Se houver item anexado, remove do inventario do remetente ANTES de enviar (evita duplicacao).
            if (itemId >= 0 && itemQuantity > 0)
            {
                if (_inventory == null || !_inventory.RemoveItemById(itemId, itemQuantity))
                {
                    _pc.RpcShowMessage("Voce nao possui esse item para anexar.", PlayerMessageType.Warning);
                    return;
                }
            }

            var (success, error) = await DatabaseService.Instance.SendMailAsync(_pc.CharacterId, targetName.Trim(), subject ?? "", body ?? "", gold, itemId, itemQuantity, itemRefine, Token);
            if (!success)
            {
                // Falhou no servidor (ex: jogador nao encontrado) - devolve o item/ouro ao remetente.
                if (itemId >= 0 && itemQuantity > 0) _inventory?.AddItem(itemId, itemQuantity, (ushort)(_inventory.FindEmptySlot() is int s && s >= 0 ? s : 0));
                _pc.RpcShowMessage(FriendlyError(error), PlayerMessageType.Warning);
                return;
            }
            _pc.RpcShowMessage("Correio enviado para " + targetName + ".", PlayerMessageType.Success);
        }

        [Command]
        public void CmdClaimMail(long mailId)
        {
            if (_pc != null) _pc.RpcShowMessage("Resgate de correio legado desativado: requer entrega atomica do GM/Item Mall.", PlayerMessageType.Warning);
        }

        async System.Threading.Tasks.Task ClaimMailInternal(long mailId)
        {
            if (_pc == null) return;
            var (success, gold, itemId, itemQuantity, itemRefine) = await DatabaseService.Instance.ClaimMailAsync(_pc.CharacterId, mailId, Token);
            if (!success) return;
            if (gold > 0) _pc.Gold += (ulong)gold;
            if (itemId >= 0 && itemQuantity > 0 && _inventory != null)
            {
                int slot = _inventory.FindEmptySlot();
                if (slot >= 0) _inventory.AddItem(itemId, itemQuantity, (ushort)slot);
                else _pc.RpcShowMessage("Inventario cheio! O item continua no correio.", PlayerMessageType.Warning);
            }
            RefreshMailInternal();
        }

        [Command]
        public async void CmdDeleteMail(long mailId)
        {
            await DatabaseService.Instance.DeleteMailAsync(_pc.CharacterId, mailId, Token);
            RefreshMailInternal();
        }

        static string FriendlyError(string error) => error switch
        {
            "PLAYER_NOT_FOUND" => "Jogador nao encontrado.",
            "INSUFFICIENT_GOLD" => "Ouro insuficiente.",
            _ => "Nao foi possivel enviar o correio.",
        };

        static string Serialize(List<MailData> list)
        {
            var parts = new List<string>();
            foreach (var m in list)
                parts.Add(string.Join("|", m.Id, Escape(m.SenderName), Escape(m.Subject), Escape(m.Body), m.Gold, m.ItemId, m.ItemQuantity, m.ItemRefine,
                    m.IsRead ? 1 : 0, m.IsClaimed ? 1 : 0, new System.DateTimeOffset(m.CreatedAt).ToUnixTimeSeconds()));
            return string.Join(";", parts);
        }

        static string Escape(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("|", "/").Replace(";", ",");

        [TargetRpc]
        void TargetMailUpdated(NetworkConnectionToClient conn, string data)
        {
            Mails.Clear();
            if (!string.IsNullOrEmpty(data))
            {
                foreach (var entry in data.Split(';'))
                {
                    var p = entry.Split('|');
                    if (p.Length < 11) continue;
                    Mails.Add(new MailData
                    {
                        Id = long.Parse(p[0]), SenderName = p[1], Subject = p[2], Body = p[3], Gold = long.Parse(p[4]),
                        ItemId = int.Parse(p[5]), ItemQuantity = int.Parse(p[6]), ItemRefine = int.Parse(p[7]),
                        IsRead = p[8] == "1", IsClaimed = p[9] == "1",
                        CreatedAt = System.DateTimeOffset.FromUnixTimeSeconds(long.Parse(p[10])).UtcDateTime,
                    });
                }
            }
            OnMailChanged?.Invoke();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            TOP.UI.MailUI.Bind(this);
            CmdRefreshMail();
        }
    }
}
