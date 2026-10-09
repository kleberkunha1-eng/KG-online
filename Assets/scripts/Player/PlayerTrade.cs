using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TOP.Data;

namespace TOP.Player
{
    // Comercio direto entre dois jogadores (Trade), server-autoritativo e em memoria (nao precisa
    // de tabela no banco: a troca e imediata/atomica). Segue o mesmo padrao de convite/aceite de
    // PlayerParty (dicionario estatico de convites pendentes + FindByName) e empurra o estado da
    // oferta via TargetRpc, igual PlayerGuild/PlayerMail.
    public class PlayerTrade : NetworkBehaviour
    {
        const float InviteTimeout = 30f;

        class OfferEntry { public int SlotIndex; public int ItemId; public int Quantity; public InventoryItemData Item; }

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
            if (!CanTradeWith(target))
            { ShowMsg("Comercio exige jogadores vivos, sem duelo, no mesmo mapa e a ate 5 metros.", PlayerMessageType.Warning); return; }

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
            if (!CanTradeWith(inviter))
            { ShowMsg("Comercio indisponivel. Aproximem-se e terminem o duelo.", PlayerMessageType.Warning); return; }

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
            a.GetComponent<PlayerCombat>()?.StopAttack();
            b.GetComponent<PlayerCombat>()?.StopAttack();

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
            if (slot == null || slot.IsEquipped)
            { ShowMsg("Selecione um item disponivel e nao equipado.", PlayerMessageType.Warning); return; }
            quantity = Mathf.Clamp(quantity, 0, slot.Quantity);

            _myOffer.RemoveAll(e => e.SlotIndex == slotIndex);
            if (quantity > 0) _myOffer.Add(new OfferEntry { SlotIndex = slotIndex, ItemId = slot.ItemId,
                Quantity = quantity, Item = _inventory.GetInventoryData().First(i => i.SlotIndex == slotIndex) });

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
            if (!a.CanTradeWith(b))
            { CancelTradeInternal(a, b, "Comercio cancelado: distancia, mapa ou estado invalido."); return; }
            // Revalida posse no exato momento do commit (o jogador pode ter perdido o item/ouro
            // entre a oferta e o lock, por qualquer outro meio).
            if (a._pc.Gold < a._myOfferedGold || b._pc.Gold < b._myOfferedGold)
            { CancelTradeInternal(a, b, "Ouro insuficiente para concluir a troca."); return; }

            var itemsA = a._inventory.GetInventoryData();
            var itemsB = b._inventory.GetInventoryData();
            if (!RemoveOffered(itemsA, a._myOffer) || !RemoveOffered(itemsB, b._myOffer))
            { CancelTradeInternal(a, b, "Itens oferecidos mudaram. Refaca a oferta."); return; }
            if (!ReceiveOffered(itemsA, b._myOffer, a._inventory.totalSlots)
                || !ReceiveOffered(itemsB, a._myOffer, b._inventory.totalSlots))
            { CancelTradeInternal(a, b, "Inventario cheio demais para concluir a troca."); return; }
            ulong goldA = a._pc.Gold - a._myOfferedGold, goldB = b._pc.Gold - b._myOfferedGold;
            if (ulong.MaxValue - goldA < b._myOfferedGold || ulong.MaxValue - goldB < a._myOfferedGold)
            { CancelTradeInternal(a, b, "Limite de ouro excedido."); return; }

            // Both snapshots are fully validated before changing either live inventory.
            a._inventory.InitializeFromData(itemsA);
            b._inventory.InitializeFromData(itemsB);
            a._pc.Gold = goldA + b._myOfferedGold;
            b._pc.Gold = goldB + a._myOfferedGold;

            a.ShowMsg("Comercio concluido.", PlayerMessageType.Success);
            b.ShowMsg("Comercio concluido.", PlayerMessageType.Success);
            EndTradeInternal(a, b, null);
        }

        static bool RemoveOffered(List<InventoryItemData> items, List<OfferEntry> offer)
        {
            foreach (var entry in offer)
            {
                var item = items.Find(i => i.SlotIndex == entry.SlotIndex);
                var expected = entry.Item;
                if (item == null || item.IsEquipped || item.IsLocked || item.ItemId != entry.ItemId
                    || entry.Quantity <= 0 || item.Quantity < entry.Quantity
                    || item.RefineLevel != expected.RefineLevel || item.Durability != expected.Durability
                    || item.GemSlot1 != expected.GemSlot1 || item.GemSlot2 != expected.GemSlot2
                    || item.GemSlot3 != expected.GemSlot3) return false;
                item.Quantity -= entry.Quantity;
                if (item.Quantity == 0) items.Remove(item);
            }
            return true;
        }

        static bool ReceiveOffered(List<InventoryItemData> items, List<OfferEntry> offer, int capacity)
        {
            foreach (var entry in offer)
            {
                int slot = 0;
                while (slot < capacity && items.Any(i => i.SlotIndex == slot)) slot++;
                if (slot == capacity) return false;
                var item = entry.Item;
                items.Add(new InventoryItemData { SlotIndex = (ushort)slot, ItemId = item.ItemId,
                    Quantity = entry.Quantity, Durability = item.Durability, RefineLevel = item.RefineLevel,
                    GemSlot1 = item.GemSlot1, GemSlot2 = item.GemSlot2, GemSlot3 = item.GemSlot3 });
            }
            return true;
        }

        bool CanTradeWith(PlayerTrade other)
        {
            var stats = GetComponent<PlayerStats>();
            var otherStats = other != null ? other.GetComponent<PlayerStats>() : null;
            return other != null && other != this && _pc != null && other._pc != null
                && _inventory != null && other._inventory != null && _pc.IsInitialized && other._pc.IsInitialized
                && !_inventory.HasQuestTransaction && !other._inventory.HasQuestTransaction
                && stats != null && otherStats != null && !stats.IsDead && !otherStats.IsDead
                && _pc.MapName == other._pc.MapName && Vector3.Distance(transform.position, other.transform.position) <= 5
                && GetComponent<PlayerCombat>().DuelOpponentNetId == 0
                && other.GetComponent<PlayerCombat>().DuelOpponentNetId == 0;
        }

        void Update()
        {
            if (isServer && InTrade && _partner != null && !CanTradeWith(_partner))
                CancelTradeInternal(this, _partner, "Comercio cancelado: distancia, mapa ou estado invalido.");
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
