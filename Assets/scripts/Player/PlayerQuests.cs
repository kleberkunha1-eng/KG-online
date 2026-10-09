using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TOP.Data;
using TOP.Services;
using TOP.Network;

namespace TOP.Player
{
    // Sistema de quests server-autoritativo. O catalogo (QuestTable) e estatico/local; so o
    // progresso por personagem e persistido via API (tabela quest_progress), no mesmo padrao de
    // PlayerFriends/PlayerMail/PlayerGuild. Os ganchos de progresso automatico (matar monstro,
    // falar com NPC) sao chamados pelo EnemyStats/NPCInteractable via metodos [Server] publicos.
    public class PlayerQuests : NetworkBehaviour
    {
        class ActiveQuest { public int QuestId; public int Progress; }

        PlayerController _pc;
        PlayerInventory _inventory;
        bool _refreshPending;
        int _stateRevision;
        readonly HashSet<int> _pendingAccepts = new HashSet<int>();
        readonly HashSet<int> _pendingAbandons = new HashSet<int>();
        readonly HashSet<int> _pendingTurnIns = new HashSet<int>();

        readonly List<ActiveQuest> _active = new List<ActiveQuest>();
        readonly HashSet<int> _completed = new HashSet<int>();
        readonly Dictionary<int, (TOP.NPC.NPCInteractable npc, float expires)> _offers =
            new Dictionary<int, (TOP.NPC.NPCInteractable npc, float expires)>();

        public event System.Action OnQuestsChanged;
        public event System.Action<int> OnQuestOffered;
        public List<(int questId, int progress)> ActiveQuests => _active.Select(a => (a.QuestId, a.Progress)).ToList();
        public IReadOnlyCollection<int> CompletedQuests => _completed;

        void Awake()
        {
            _pc = GetComponent<PlayerController>();
            _inventory = GetComponent<PlayerInventory>();
        }

        string Token => TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId);

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            TOP.UI.QuestLogUI.Bind(this);
            CmdRefreshQuests();
        }

        [Server]
        async void RefreshQuestsInternal()
        {
            if (_pc == null || _refreshPending || _pendingAccepts.Count != 0 || _pendingAbandons.Count != 0
                || _pendingTurnIns.Count != 0) return;
            if (DatabaseService.Instance == null)
            {
                _pc.RpcShowMessage("Servico de missoes indisponivel.", PlayerMessageType.Warning);
                return;
            }
            _refreshPending = true;
            int revision = _stateRevision;
            try
            {
                var result = await DatabaseService.Instance.GetQuestsAsync(_pc.CharacterId, Token);
                if (this == null || connectionToClient == null) return;
                if (!result.success)
                {
                    _pc.RpcShowMessage("Nao foi possivel atualizar as missoes. O progresso atual foi preservado.", PlayerMessageType.Warning);
                    return;
                }
                if (revision != _stateRevision) return;
                _active.Clear();
                foreach (var a in result.active) _active.Add(new ActiveQuest { QuestId = a.questId, Progress = a.progress });
                _completed.Clear();
                foreach (var c in result.completed) _completed.Add(c);
                PushState();
            }
            finally { _refreshPending = false; }
        }

        [Command]
        public void CmdRefreshQuests() => RefreshQuestsInternal();

        // ------------------------------------------------------------------------------
        // Aceitar / abandonar
        // ------------------------------------------------------------------------------
        [Command]
        public void CmdAcceptQuest(int questId) => ServerAcceptQuest(questId);

        [Server]
        public async void ServerAcceptQuest(int questId)
        {
            if (_pc == null || !QuestTable.All.TryGetValue(questId, out var def)) return;
            if (_refreshPending || _pendingAccepts.Contains(questId) || _pendingAbandons.Contains(questId)
                || _pendingTurnIns.Contains(questId))
            { _pc.RpcShowMessage("Aguarde a operacao de missao em andamento.", PlayerMessageType.Warning); return; }
            if (!CanUseQuestNpc(questId, false) || !_offers.TryGetValue(questId, out var offer)
                || offer.npc != GetComponent<PlayerMovement>().ActiveNpc || Time.time > offer.expires)
            { _pc.RpcShowMessage("Consulte a missao no NPC correto antes de aceitar.", PlayerMessageType.Warning); return; }
            if (_active.Any(a => a.QuestId == questId) || _completed.Contains(questId))
            { _pc.RpcShowMessage("Voce ja possui ou ja concluiu essa missao.", PlayerMessageType.Warning); return; }
            if (_pc.Level < def.RequiredLevel)
            { _pc.RpcShowMessage("Nivel insuficiente para essa missao.", PlayerMessageType.Warning); return; }
            if (def.PrerequisiteId != 0 && !_completed.Contains(def.PrerequisiteId))
            { _pc.RpcShowMessage("Voce precisa concluir uma missao anterior primeiro.", PlayerMessageType.Warning); return; }

            _pendingAccepts.Add(questId);
            _stateRevision++;
            try
            {
                bool ok = await DatabaseService.Instance.AcceptQuestAsync(_pc.CharacterId, questId, Token);
                if (this == null || connectionToClient == null) return;
                if (!ok) { _pc.RpcShowMessage("Nao foi possivel aceitar a missao.", PlayerMessageType.Warning); return; }
                _active.Add(new ActiveQuest { QuestId = questId, Progress = 0 });
                _offers.Remove(questId);
                _pc.RpcShowMessage("Missao aceita: " + def.Name, PlayerMessageType.QuestUpdate);
                PushState();
            }
            finally { _pendingAccepts.Remove(questId); }
        }

        [Command]
        public void CmdAbandonQuest(int questId) => ServerAbandonQuest(questId);

        [Server]
        public async void ServerAbandonQuest(int questId)
        {
            if (_refreshPending || _pendingAccepts.Contains(questId) || _pendingAbandons.Contains(questId)
                || _pendingTurnIns.Contains(questId))
            { _pc.RpcShowMessage("Aguarde a operacao de missao em andamento.", PlayerMessageType.Warning); return; }
            if (!_active.Any(a => a.QuestId == questId)) return;
            _pendingAbandons.Add(questId);
            _stateRevision++;
            try
            {
                bool ok = await DatabaseService.Instance.AbandonQuestAsync(_pc.CharacterId, questId, Token);
                if (this == null || connectionToClient == null) return;
                if (!ok)
                { _pc.RpcShowMessage("Nao foi possivel abandonar a missao. O progresso foi preservado.", PlayerMessageType.Warning); return; }
                _active.RemoveAll(a => a.QuestId == questId);
                PushState();
            }
            finally { _pendingAbandons.Remove(questId); }
        }

        // ------------------------------------------------------------------------------
        // Entrega (turn-in)
        // ------------------------------------------------------------------------------
        [Command]
        public void CmdTurnInQuest(int questId) => ServerTurnInQuest(questId);

        [Server]
        public async void ServerTurnInQuest(int questId)
        {
            var entry = _active.FirstOrDefault(a => a.QuestId == questId);
            if (entry == null || !QuestTable.All.TryGetValue(questId, out var def)) return;
            if (_refreshPending || _pendingAccepts.Contains(questId) || _pendingAbandons.Contains(questId)
                || _pendingTurnIns.Contains(questId))
            { _pc.RpcShowMessage("Aguarde a operacao de missao em andamento.", PlayerMessageType.Warning); return; }
            _stateRevision++;
            if (!CanUseQuestNpc(questId, true))
            { _pc.RpcShowMessage("Entregue a missao ao NPC correto, estando perto dele.", PlayerMessageType.Warning); return; }

            if (!IsObjectiveComplete(def, entry, out int itemIdForCollect))
            { _pc.RpcShowMessage("Objetivo ainda nao concluido.", PlayerMessageType.Warning); return; }
            if (def.RewardGold < 0 || def.RewardExp < 0 || def.RewardItemQty < 0
                || ulong.MaxValue - _pc.Gold < (ulong)def.RewardGold || ulong.MaxValue - _pc.Exp < (ulong)def.RewardExp)
            { _pc.RpcShowMessage("Recompensa invalida ou limite de ouro/experiencia excedido.", PlayerMessageType.Warning); return; }

            PlayerInventory.QuestInventoryTransaction inventoryTransaction = null;
            if (def.Objective.Type == QuestObjectiveType.Collect || def.RewardItemQty > 0)
            {
                if (_inventory == null)
                { _pc.RpcShowMessage("Inventario indisponivel para entregar a missao.", PlayerMessageType.Warning); return; }
                int rewardId = def.RewardItemQty > 0
                    ? (def.RewardItemId > 0 ? def.RewardItemId : ResolveItemIdByName(def.RewardItemName)) : 0;
                inventoryTransaction = _inventory.PrepareQuestTransaction(itemIdForCollect,
                    def.Objective.Type == QuestObjectiveType.Collect ? def.Objective.Required : 0,
                    rewardId, def.RewardItemQty, out string error);
                if (inventoryTransaction == null)
                { _pc.RpcShowMessage(error, PlayerMessageType.Warning); return; }
            }
            _pendingTurnIns.Add(questId);
            try
            {
                bool ok = await DatabaseService.Instance.CompleteQuestAsync(_pc.CharacterId, questId, Token);
                if (!ok)
                {
                    if (this != null && connectionToClient != null)
                        _pc.RpcShowMessage("Nao foi possivel concluir a missao. Seus itens foram preservados.", PlayerMessageType.Warning);
                    return;
                }
                if (this == null)
                { Debug.LogError("[Quests] Confirmacao recebida apos destruir o jogador; entrega requer reconciliacao."); return; }
                inventoryTransaction?.Commit();

                _active.Remove(entry);
                _completed.Add(questId);

                if (def.RewardExp > 0) _pc.AddExp((ulong)def.RewardExp);
                if (def.RewardGold > 0) _pc.AddGold((ulong)def.RewardGold);
                if (connectionToClient != null)
                {
                    _pc.RpcShowMessage("Missao concluida: " + def.Name, PlayerMessageType.QuestUpdate);
                    PushState();
                }
            }
            finally { inventoryTransaction?.Dispose(); _pendingTurnIns.Remove(questId); }
        }

        [Server]
        bool IsObjectiveComplete(QuestDef def, ActiveQuest entry, out int collectItemId)
        {
            collectItemId = -1;
            switch (def.Objective.Type)
            {
                case QuestObjectiveType.TalkTo: return entry.Progress >= 1;
                case QuestObjectiveType.Kill: return entry.Progress >= def.Objective.Required;
                case QuestObjectiveType.Collect:
                    collectItemId = def.Objective.ItemId > 0 ? def.Objective.ItemId : ResolveItemIdByName(def.Objective.Target);
                    if (collectItemId <= 0 || _inventory == null) return false;
                    return PlayerInventory.TryConsumeMaterials(_inventory.GetInventoryData(), collectItemId, def.Objective.Required);
                default: return false;
            }
        }

        static int ResolveItemIdByName(string substring)
        {
            if (string.IsNullOrEmpty(substring)) return -1;
            foreach (var kv in PkoTables.Items)
                if (!string.IsNullOrEmpty(kv.Value.Name) && kv.Value.Name.ToLowerInvariant().Contains(substring.ToLowerInvariant()))
                    return kv.Key;
            return -1;
        }

        // ------------------------------------------------------------------------------
        // Ganchos de progresso automatico (chamados pelo servidor a partir de outros sistemas)
        // ------------------------------------------------------------------------------
        [Server]
        public void ServerNotifyKill(string monsterName)
        {
            if (string.IsNullOrEmpty(monsterName) || _active.Count == 0) return;
            string lower = monsterName.ToLowerInvariant();
            bool changed = false;
            foreach (var a in _active)
            {
                if (!QuestTable.All.TryGetValue(a.QuestId, out var def) || def.Objective.Type != QuestObjectiveType.Kill) continue;
                if (!lower.Contains(def.Objective.Target.ToLowerInvariant())) continue;
                if (a.Progress >= def.Objective.Required) continue;
                a.Progress++;
                _stateRevision++;
                changed = true;
                _ = DatabaseService.Instance.SaveQuestProgressAsync(_pc.CharacterId, a.QuestId, a.Progress, Token);
                if (a.Progress >= def.Objective.Required) _pc.RpcShowMessage("Objetivo concluido: " + def.Name, PlayerMessageType.QuestUpdate);
            }
            if (changed) PushState();
        }

        [Server]
        public void ServerNotifyTalk(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || _active.Count == 0) return;
            bool changed = false;
            foreach (var a in _active)
            {
                if (!QuestTable.All.TryGetValue(a.QuestId, out var def) || def.Objective.Type != QuestObjectiveType.TalkTo) continue;
                if (def.Objective.Target != npcId || a.Progress >= 1) continue;
                a.Progress = 1;
                _stateRevision++;
                changed = true;
                _ = DatabaseService.Instance.SaveQuestProgressAsync(_pc.CharacterId, a.QuestId, a.Progress, Token);
            }
            if (changed) PushState();
        }

        // Quests disponiveis para oferta automatica por um NPC especifico (usado por NPCInteractable).
        [Server]
        public List<int> GetOfferableQuestsFor(IEnumerable<int> npcQuestIds)
        {
            var result = new List<int>();
            foreach (var id in npcQuestIds)
            {
                if (!QuestTable.All.TryGetValue(id, out var def)) continue;
                if (_active.Any(a => a.QuestId == id) || _completed.Contains(id)) continue;
                if (_pc.Level < def.RequiredLevel) continue;
                if (def.PrerequisiteId != 0 && !_completed.Contains(def.PrerequisiteId)) continue;
                result.Add(id);
            }
            return result;
        }

        [Server]
        public bool HasActiveQuest(int questId) => _active.Any(a => a.QuestId == questId);

        [Server]
        public bool CanTurnIn(int questId)
        {
            var entry = _active.FirstOrDefault(a => a.QuestId == questId);
            if (entry == null || !QuestTable.All.TryGetValue(questId, out var def)) return false;
            return IsObjectiveComplete(def, entry, out _);
        }

        // Empurra um popup de "nova missao" para o dono (chamado por NPCInteractable).
        [Server]
        public void ServerOfferQuest(int questId)
        {
            if (!CanUseQuestNpc(questId, false)) return;
            _offers[questId] = (GetComponent<PlayerMovement>().ActiveNpc, Time.time + 60);
            TargetOfferQuest(connectionToClient, questId);
        }

        [Server]
        public bool CanUseQuestNpc(int questId, bool turnIn)
        {
            if (!QuestTable.All.TryGetValue(questId, out var def)) return false;
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            if (npc == null || !npc.CanInteract(movement)) return false;
            string expected = turnIn ? def.TurnInNpcId : def.GiverNpcId;
            return (string.IsNullOrEmpty(expected) || npc.NpcId == expected)
                && (turnIn ? npc.ReceivesQuest(questId) : npc.OffersQuest(questId));
        }

        // ------------------------------------------------------------------------------
        // Sincronizacao com o dono
        // ------------------------------------------------------------------------------
        [Server]
        void PushState()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _active.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(_active[i].QuestId).Append(':').Append(_active[i].Progress);
            }
            sb.Append('|');
            sb.Append(string.Join(",", _completed));
            TargetQuestsUpdated(connectionToClient, sb.ToString());
        }

        [TargetRpc]
        void TargetQuestsUpdated(NetworkConnectionToClient conn, string data)
        {
            _active.Clear();
            _completed.Clear();
            if (!string.IsNullOrEmpty(data))
            {
                int sep = data.IndexOf('|');
                string activePart = sep >= 0 ? data.Substring(0, sep) : data;
                string completedPart = sep >= 0 ? data.Substring(sep + 1) : "";
                if (!string.IsNullOrEmpty(activePart))
                    foreach (var e in activePart.Split(';'))
                    {
                        var p = e.Split(':');
                        if (p.Length < 2) continue;
                        _active.Add(new ActiveQuest { QuestId = int.Parse(p[0]), Progress = int.Parse(p[1]) });
                    }
                if (!string.IsNullOrEmpty(completedPart))
                    foreach (var e in completedPart.Split(','))
                        if (int.TryParse(e, out int cid)) _completed.Add(cid);
            }
            OnQuestsChanged?.Invoke();
        }

        [TargetRpc]
        public void TargetOfferQuest(NetworkConnectionToClient conn, int questId) => OnQuestOffered?.Invoke(questId);
    }
}
