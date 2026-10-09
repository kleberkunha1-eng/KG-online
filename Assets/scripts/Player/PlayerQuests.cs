using UnityEngine;
using System;
using Mirror;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TOP.Data;
using TOP.Services;
using TOP.Network;

namespace TOP.Player
{
    // Sistema de quests server-autoritativo. O catalogo (QuestTable) e estatico/local; so o
    // progresso por personagem e persistido via API; a entrega grava quest e personagem juntos.
    // Os ganchos de progresso automatico (matar monstro,
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
        readonly HashSet<int> _progressPending = new HashSet<int>();
        readonly Dictionary<int, int> _dirtyProgress = new Dictionary<int, int>();

        readonly List<ActiveQuest> _active = new List<ActiveQuest>();
        readonly HashSet<int> _completed = new HashSet<int>();
        readonly Dictionary<int, (TOP.NPC.NPCInteractable npc, float expires)> _offers =
            new Dictionary<int, (TOP.NPC.NPCInteractable npc, float expires)>();

        public event System.Action OnQuestsChanged;
        public event System.Action<int> OnQuestOffered;
        public List<(int questId, int progress)> ActiveQuests => _active.Select(a => (a.QuestId, a.Progress)).ToList();
        public IReadOnlyCollection<int> CompletedQuests => _completed;
        public bool HasPendingCompletion => _pendingTurnIns.Count != 0;
        public bool HasUnsavedProgress => _dirtyProgress.Count != 0;

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
            try
            {
                foreach (int questId in _dirtyProgress.Keys.ToArray())
                    _ = PersistQuestProgressAsync(questId);
                while (_progressPending.Count != 0)
                {
                    await Task.Yield();
                    if (this == null) return;
                }
                if (_dirtyProgress.Count != 0)
                {
                    _pc.RpcShowMessage("Progresso ainda nao confirmado. As missoes locais foram preservadas; tente atualizar novamente.", PlayerMessageType.Warning);
                    return;
                }
                int revision = _stateRevision;
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
            if (_refreshPending || _pendingAccepts.Count != 0 || _pendingAbandons.Contains(questId)
                || _pendingTurnIns.Count != 0)
            { _pc.RpcShowMessage("Aguarde a operacao de missao em andamento.", PlayerMessageType.Warning); return; }
            if (!CanUseQuestNpc(questId, false) || !_offers.TryGetValue(questId, out var offer)
                || offer.npc != GetComponent<PlayerMovement>().ActiveNpc || Time.time > offer.expires)
            { _pc.RpcShowMessage("Consulte a missao no NPC correto antes de aceitar.", PlayerMessageType.Warning); return; }
            if (_active.Any(a => a.QuestId == questId) || _completed.Contains(questId))
            { _pc.RpcShowMessage("Voce ja possui ou ja concluiu essa missao.", PlayerMessageType.Warning); return; }
            if (!def.CanAcceptAtLevel(_pc.Level))
            { _pc.RpcShowMessage("Seu nivel esta fora da faixa permitida para essa missao.", PlayerMessageType.Warning); return; }
            if (!def.MeetsQuestConditions(HasActiveQuest, _completed.Contains))
            { _pc.RpcShowMessage("Os pre-requisitos de missao ainda nao foram atendidos.", PlayerMessageType.Warning); return; }

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
                || _pendingTurnIns.Count != 0 || _progressPending.Contains(questId))
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
                _dirtyProgress.Remove(questId);
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
                || _pendingTurnIns.Count != 0 || _progressPending.Contains(questId))
            { _pc.RpcShowMessage("Aguarde a operacao de missao em andamento.", PlayerMessageType.Warning); return; }
            _stateRevision++;
            if (!CanUseQuestNpc(questId, true))
            { _pc.RpcShowMessage("Entregue a missao ao NPC correto, estando perto dele.", PlayerMessageType.Warning); return; }

            if (!IsObjectiveComplete(def, entry, out var collectItems))
            { _pc.RpcShowMessage("Objetivo ainda nao concluido.", PlayerMessageType.Warning); return; }
            if (def.RewardGold < 0 || !def.HasValidExperienceReward || def.RewardItemQty < 0
                || _pc.Gold > DatabaseService.MaxExactApiInteger || _pc.Exp > DatabaseService.MaxExactApiInteger
                || DatabaseService.MaxExactApiInteger - _pc.Gold < (ulong)def.RewardGold
                || DatabaseService.MaxExactApiInteger - _pc.Exp < (ulong)def.MaximumExperienceReward)
            { _pc.RpcShowMessage("Recompensa invalida ou limite de ouro/experiencia excedido.", PlayerMessageType.Warning); return; }

            if (_inventory == null)
            { _pc.RpcShowMessage("Inventario indisponivel para entregar a missao.", PlayerMessageType.Warning); return; }
            int rewardId = def.RewardItemQty > 0
                ? (def.RewardItemId > 0 ? def.RewardItemId : ResolveItemIdByName(def.RewardItemName)) : 0;
            var inventoryTransaction = _inventory.PrepareQuestTransaction(collectItems, rewardId, def.RewardItemQty, out string error);
            if (inventoryTransaction == null)
            { _pc.RpcShowMessage(error, PlayerMessageType.Warning); return; }
            _pendingTurnIns.Add(questId);
            try
            {
                int experienceReward = def.RollExperienceReward();
                CharacterData liveData = null;
                CharacterData rewardedData = null;
                var result = await DatabaseService.Instance.PersistCharacterAsync(_pc.CharacterId, () =>
                {
                    if (_pc == null) throw new InvalidOperationException("Jogador desconectou antes de iniciar a entrega.");
                    liveData = _pc.GetCharacterData();
                    rewardedData = liveData.CopySnapshot();
                    if (inventoryTransaction != null) rewardedData.Inventory = inventoryTransaction.PreparedItems;
                    if (experienceReward > 0) PlayerController.ApplyExperience(rewardedData, (ulong)experienceReward);
                    rewardedData.Gold = checked(rewardedData.Gold + (ulong)def.RewardGold);
                    return rewardedData;
                }, Token, questId);
                if (!result.success)
                {
                    if (this != null && connectionToClient != null)
                    {
                        if (result.error == "RELOAD_REQUIRED" || result.error == "SAVE_CONFLICT" || result.error == "OPERATION_MISMATCH")
                        {
                            _pc.RpcShowMessage("Reconecte para recuperar a confirmacao da missao com seguranca.", PlayerMessageType.Warning);
                            connectionToClient.Disconnect();
                        }
                        else _pc.RpcShowMessage("Nao foi possivel concluir a missao. Seus itens foram preservados.", PlayerMessageType.Warning);
                    }
                    return;
                }
                liveData.SaveRevision = rewardedData.SaveRevision;
                if (this == null)
                { Debug.Log("[Quests] Entrega persistida apos desconexao; inventario e recompensas serao recuperados no login."); return; }
                inventoryTransaction?.Commit();

                _active.Remove(entry);
                _dirtyProgress.Remove(questId);
                _completed.Add(questId);

                if (experienceReward > 0) _pc.AddExp((ulong)experienceReward);
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
        bool IsObjectiveComplete(QuestDef def, ActiveQuest entry, out IReadOnlyList<QuestCollectionItem> materials)
        {
            materials = Array.Empty<QuestCollectionItem>();
            switch (def.Objective.Type)
            {
                case QuestObjectiveType.TalkTo: return entry.Progress >= 1;
                case QuestObjectiveType.Kill: return entry.Progress >= def.Objective.Required;
                case QuestObjectiveType.Collect:
                    materials = def.Objective.CollectionItems != null && def.Objective.CollectionItems.Length > 0
                        ? def.Objective.CollectionItems
                        : new[] { new QuestCollectionItem { ItemId = def.Objective.ItemId > 0
                            ? def.Objective.ItemId : ResolveItemIdByName(def.Objective.Target), Quantity = def.Objective.Required } };
                    if (_inventory == null || materials.Any(item => item == null || item.Quantity <= 0 || !PkoTables.Items.ContainsKey(item.ItemId))
                        || materials.Select(item => item.ItemId).Distinct().Count() != materials.Count) return false;
                    var available = _inventory.GetInventoryData();
                    return materials.All(item => PlayerInventory.TryConsumeMaterials(available, item.ItemId, item.Quantity));
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
                QueueQuestProgress(a);
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
                QueueQuestProgress(a);
            }
            if (changed) PushState();
        }

        void QueueQuestProgress(ActiveQuest entry)
        {
            _dirtyProgress[entry.QuestId] = entry.Progress;
            _ = PersistQuestProgressAsync(entry.QuestId);
        }

        async Task PersistQuestProgressAsync(int questId)
        {
            if (!_progressPending.Add(questId)) return;
            try
            {
                long characterId = _pc.CharacterId;
                string token = Token;
                var database = DatabaseService.Instance;
                if (database == null) throw new InvalidOperationException("Servico de progresso indisponivel.");
                while (_dirtyProgress.TryGetValue(questId, out int progress))
                {
                    bool confirmed = false;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (await database.SaveQuestProgressAsync(characterId, questId, progress, token))
                        { confirmed = true; break; }
                        if (attempt < 2) await Task.Delay(200);
                    }
                    if (!confirmed)
                    {
                        Debug.LogError("[Quests] Progresso nao confirmado apos tres tentativas: " + questId);
                        if (this != null && connectionToClient != null)
                            _pc.RpcShowMessage("Nao foi possivel salvar o progresso da missao. Ele foi mantido nesta sessao; tente atualizar novamente.", PlayerMessageType.Warning);
                        return;
                    }
                    if (_dirtyProgress.TryGetValue(questId, out int latest) && latest <= progress)
                        _dirtyProgress.Remove(questId);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Quests] Falha ao confirmar progresso: " + e.Message);
                if (this != null && connectionToClient != null)
                    _pc.RpcShowMessage("Falha ao confirmar progresso da missao. Tente atualizar novamente.", PlayerMessageType.Warning);
            }
            finally { _progressPending.Remove(questId); }
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
                if (!def.CanAcceptAtLevel(_pc.Level)) continue;
                if (!def.MeetsQuestConditions(HasActiveQuest, _completed.Contains)) continue;
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
            ApplyQuestState(data);
        }

        internal void ApplyQuestState(string data)
        {
            if (isServer)
            {
                OnQuestsChanged?.Invoke();
                return;
            }
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
