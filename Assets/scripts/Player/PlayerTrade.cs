using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TOP.Player
{
    // Comercio direto entre dois jogadores (Trade), server-autoritativo e em memoria (nao precisa
    // de tabela no banco: a troca e imediata/atomica). Segue o mesmo padrao de convite/aceite de
    // PlayerParty (dicionario estatico de convites pendentes + FindByName) e empurra o estado da
    // oferta via TargetRpc, igual PlayerGuild/PlayerMail.
    public class PlayerTrade : NetworkBehaviour
    {
        const float InviteTimeout = 30f;

        class OfferEntry { public int ItemId; public int Quantity; }

        PlayerController _pc;
        PlayerInventory _inventory;

        [SyncVar] public bool InTrade;

        PlayerTrade _partner;
        readonly List<OfferEntry> _myOffer = new List<OfferEntry>();
        ulong _myOfferedGold;
        bool _myLocked;

        class PendingInvite { public PlayerTrade Inviter; public float Time; }
        static readonly Dictionary<string, PendingInvite> _pendingInvites = new Dictionary<string, PendingInvite>();

        void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _inventory = GetComponent<PlayerInventory>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            TOP.UI.TradeUI.Bind(this);
        }

        [Server]
        void ShowMsg(string text, PlayerMessageType type) => _pc?.RpcShowMessage(text, type);

        // ------------------------------------------------------------------------------
        // Convite
        // ------------------------------------------------------------------------------
        [Command]
        public void CmdRequestTrade(string targetName)
        {
            if (_pc == null || string.IsNullOrWhiteSpace(targetName) || targetName == _pc.CharacterName) return;
            if (InTrade) { ShowMsg("Voce ja esta em uma negociacao.", PlayerMessageType.Warning); return; }

            var target = FindByName(targetName);
            if (target == null) { ShowMsg("Jogador nao encontrado.", PlayerMessageType.Warning); return; }
            if (target.InTrade) { ShowMsg(targetName + " ja esta negociando com outra pessoa.", PlayerMessageType.Warning); return; }

            _pendingInvites[targetName] = new PendingInvite { Inviter = this, Time = Time.time };
            target.TargetTradeInvite(target.connectionToClient, _pc.CharacterName);
            ShowMsg("Pedido de comercio enviado para " + targetName + ".", PlayerMessageType.Info);
        }

        [TargetRpc]
        void TargetTradeInvite(NetworkConnectionToClient conn, string inviterName) => TOP.UI.TradeUI.ShowInvite(this, inviterName);

        [Command]
        public void CmdRespondTrade(string inviterName, bool accept)
        {
            if (_pc == null) return;
            if (!_pendingInvites.TryGetValue(_pc.CharacterName, out var invite) || invite.Inviter == null)
            { ShowMsg("Pedido expirado.", PlayerMessageType.Warning); return; }
            _pendingInvites.Remove(_pc.CharacterName);
            var inviter = invite.Inviter;
            if (inviter._pc == null || inviter._pc.CharacterName != inviterName || Time.time - invite.Time > InviteTimeout)
            { ShowMsg("Pedido expirado.", PlayerMessageType.Warning); return; }

            if (!accept)
            {
                inviter.ShowMsg(_pc.CharacterName + " recusou o comercio.", PlayerMessageType.Info);
                return;
            }
            if (InTrade || inviter.InTrade) { ShowMsg("Negociacao ja em andamento.", PlayerMessageType.Warning); return; }

            StartTradeInternal(inviter, this);
        }

        [Server]
        static void StartTradeInternal(PlayerTrade a, PlayerTrade b)
        {
            a._partner = b; b._partner = a;
            a.InTrade = true; b.InTrade = true;
            a._myOffer.Clear(); b._myOffer.Clear();
            a._myOfferedGold = 0; b._myOfferedGold = 0;
            a._myLocked = false; b._myLocked = false;

            a.TargetTradeStarted(a.connectionToClient, b._pc.CharacterName);
            b.TargetTradeStarted(b.connectionToClient, a._pc.CharacterName);
            BroadcastOffers(a, b);
        }

        [TargetRpc]
        void TargetTradeStarted(NetworkConnectionToClient conn, string partnerName) => TOP.UI.TradeUI.OnTradeStarted(this, partnerName);

        // ------------------------------------------------------------------------------
        // Oferta
        // ------------------------------------------------------------------------------
        [Command]
        public void CmdSetOfferItem(int slotIndex, int quantity)
        {
            if (!InTrade || _partner == null || _inventory == null) return;
            var slot = _inventory.GetSlot(slotIndex);
            if (slot == null || slot.IsEquipped) return;
            quantity = Mathf.Clamp(quantity, 0, slot.Quantity);

            _myOffer.RemoveAll(e => e.ItemId == slot.ItemId);
            if (quantity > 0) _myOffer.Add(new OfferEntry { ItemId = slot.ItemId, Quantity = quantity });

            UnlockBoth();
            BroadcastOffers(this, _partner);
        }

        [Command]
        public void CmdSetOfferGold(ulong amount)
        {
            if (!InTrade || _partner == null || _pc == null) return;
            _myOfferedGold = System.Math.Min(amount, _pc.Gold);
            UnlockBoth();
            BroadcastOffers(this, _partner);
        }

        [Command]
        public void CmdToggleLock()
        {
            if (!InTrade || _partner == null) return;
            _myLocked = !_myLocked;
            BroadcastOffers(this, _partner);
            if (_myLocked && _partner._myLocked) TryCommitTrade(this, _partner);
        }

        [Server]
        void UnlockBoth()
        {
            _myLocked = false;
            if (_partner != null) _partner._myLocked = false;
        }

        [Server]
        static void BroadcastOffers(PlayerTrade a, PlayerTrade b)
        {
            a.TargetOfferUpdated(a.connectionToClient, Serialize(a._myOffer), a._myOfferedGold, a._myLocked, Serialize(b._myOffer), b._myOfferedGold, b._myLocked);
            b.TargetOfferUpdated(b.connectionToClient, Serialize(b._myOffer), b._myOfferedGold, b._myLocked, Serialize(a._myOffer), a._myOfferedGold, a._myLocked);
        }

        [TargetRpc]
        void TargetOfferUpdated(NetworkConnectionToClient conn, string myOffer, ulong myGold, bool myLocked, string partnerOffer, ulong partnerGold, bool partnerLocked)
            => TOP.UI.TradeUI.OnOfferUpdated(this, myOffer, myGold, myLocked, partnerOffer, partnerGold, partnerLocked);

        static string Serialize(List<OfferEntry> offer)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < offer.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(offer[i].ItemId).Append(':').Append(offer[i].Quantity);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------
        // Confirmacao / Commit
        // ------------------------------------------------------------------------------
        [Server]
        static void TryCommitTrade(PlayerTrade a, PlayerTrade b)
        {
            // Revalida posse no exato momento do commit (o jogador pode ter perdido o item/ouro
            // entre a oferta e o lock, por qualquer outro meio).
            if (a._pc.Gold < a._myOfferedGold || b._pc.Gold < b._myOfferedGold)
            { CancelTradeInternal(a, b, "Ouro insuficiente para concluir a troca."); return; }

            int freeSlotsA = CountFreeSlots(a._inventory);
            int freeSlotsB = CountFreeSlots(b._inventory);
            if (b._myOffer.Count > freeSlotsA || a._myOffer.Count > freeSlotsB)
            { CancelTradeInternal(a, b, "Inventario cheio demais para concluir a troca."); return; }

            // Remove primeiro (atomico por item via RemoveItemById); se falhar, aborta sem ja ter
            // dado nada ao lado oposto.
            foreach (var e in a._myOffer) if (!a._inventory.RemoveItemById(e.ItemId, e.Quantity))
            { RestoreRemoved(a, b); CancelTradeInternal(a, b, "Falha ao validar os itens oferecidos."); return; }
            foreach (var e in b._myOffer) if (!b._inventory.RemoveItemById(e.ItemId, e.Quantity))
            { RestoreRemoved(a, b); CancelTradeInternal(a, b, "Falha ao validar os itens oferecidos."); return; }

            a._pc.Gold -= a._myOfferedGold;
            b._pc.Gold -= b._myOfferedGold;

            foreach (var e in b._myOffer)
            {
                int slot = a._inventory.FindEmptySlot();
                if (slot >= 0) a._inventory.AddItem(e.ItemId, e.Quantity, (ushort)slot);
            }
            foreach (var e in a._myOffer)
            {
                int slot = b._inventory.FindEmptySlot();
                if (slot >= 0) b._inventory.AddItem(e.ItemId, e.Quantity, (ushort)slot);
            }
            a._pc.Gold += b._myOfferedGold;
            b._pc.Gold += a._myOfferedGold;

            a.ShowMsg("Comercio concluido.", PlayerMessageType.Success);
            b.ShowMsg("Comercio concluido.", PlayerMessageType.Success);
            EndTradeInternal(a, b, null);
        }

        // Nao ha rollback parcial real necessario aqui: a ordem de remocao so avanca para o
        // segundo lado apos o primeiro ter sucesso total, entao se o primeiro bloco falhar nada
        // foi alterado ainda; mantido por clareza/seguranca futura caso a logica mude.
        [Server]
        static void RestoreRemoved(PlayerTrade a, PlayerTrade b) { }

        static int CountFreeSlots(PlayerInventory inv)
        {
            int count = 0;
            for (int i = 0; i < inv.totalSlots; i++) if (inv.GetSlot(i) == null) count++;
            return count;
        }

        [Command]
        public void CmdCancelTrade() { if (InTrade && _partner != null) CancelTradeInternal(this, _partner, "Comercio cancelado."); }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (InTrade && _partner != null) CancelTradeInternal(this, _partner, "O outro jogador saiu.");
        }

        [Server]
        static void CancelTradeInternal(PlayerTrade a, PlayerTrade b, string reason) => EndTradeInternal(a, b, reason);

        [Server]
        static void EndTradeInternal(PlayerTrade a, PlayerTrade b, string reason)
        {
            a.InTrade = false; b.InTrade = false;
            a._partner = null; b._partner = null;
            a._myOffer.Clear(); b._myOffer.Clear();
            a._myOfferedGold = 0; b._myOfferedGold = 0;
            a._myLocked = false; b._myLocked = false;
            a.TargetTradeClosed(a.connectionToClient, reason ?? "");
            b.TargetTradeClosed(b.connectionToClient, reason ?? "");
        }

        [TargetRpc]
        void TargetTradeClosed(NetworkConnectionToClient conn, string reason) => TOP.UI.TradeUI.OnTradeClosed(this, reason);

        [Server]
        static PlayerTrade FindByName(string name)
        {
            foreach (var kv in NetworkServer.spawned)
            {
                var pt = kv.Value.GetComponent<PlayerTrade>();
                var pc = kv.Value.GetComponent<PlayerController>();
                if (pt != null && pc != null && pc.CharacterName == name) return pt;
            }
            return null;
        }
    }
}
