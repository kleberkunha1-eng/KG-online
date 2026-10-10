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
    public partial class PlayerQuests : NetworkBehaviour
    {
        class ActiveQuest
        {
            public int QuestId;
            public int Progress;
            public readonly Dictionary<int, int> Objectives = new Dictionary<int, int>();
            public int GetProgress(int index) => index == 0 ? Progress : Objectives.TryGetValue(index, out int value) ? value : 0;
            public void SetProgress(int index, int value)
            {
                if (index == 0) Progress = value;
                else Objectives[index] = value;
            }
        }

        PlayerController _pc;
        PlayerInventory _inventory;
        bool _refreshPending;
        int _stateRevision;
        readonly HashSet<int> _pendingAccepts = new HashSet<int>();
        readonly HashSet<int> _pendingAbandons = new HashSet<int>();
        readonly HashSet<int> _pendingTurnIns = new HashSet<int>();
        readonly HashSet<(int questId, int objectiveIndex)> _progressPending = new HashSet<(int, int)>();
        readonly Dictionary<(int questId, int objectiveIndex), int> _dirtyProgress = new Dictionary<(int, int), int>();

        readonly List<ActiveQuest> _active = new List<ActiveQuest>();
        readonly HashSet<int> _completed = new HashSet<int>();
        readonly Dictionary<int, (TOP.NPC.NPCInteractable npc, float expires)> _offers =
            new Dictionary<int, (TOP.NPC.NPCInteractable npc, float expires)>();

        public event System.Action OnQuestsChanged;
        public event System.Action<int> OnQuestOffered;
        public List<(int questId, int progress)> ActiveQuests => _active.Select(a => (a.QuestId, a.Progress)).Concat(OriginalActiveQuests()).ToList();
        public IReadOnlyCollection<int> CompletedQuests => _completed;
        public bool HasPendingCompletion => _pendingTurnIns.Count != 0;
        public bool HasUnsavedProgress => _dirtyProgress.Count != 0 || originalProgressDirty || !originalProgressTask.IsCompleted;
        public int GetObjectiveProgress(int questId, int objectiveIndex)
            => OriginalQuestCatalog.All.ContainsKey(questId) ? OriginalObjectiveProgress(questId, objectiveIndex)
                : _active.FirstOrDefault(a => a.QuestId == questId)?.GetProgress(objectiveIndex) ?? 0;

        bool HasPendingProgress(int questId) => _progressPending.Any(key => key.questId == questId);
        void ClearDirtyProgress(int questId)
        {
            foreach (var key in _dirtyProgress.Keys.Where(key => key.questId == questId).ToArray())
                _dirtyProgress.Remove(key);
        }

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
                if (originalProgressDirty && originalProgressTask.IsCompleted) originalProgressTask = PersistOriginalProgressAsync();
                await originalProgressTask;
                if (this == null || connectionToClient == null || originalProgressDirty) return;
                foreach (var key in _dirtyProgress.Keys.ToArray())
                    _ = PersistQuestProgressAsync(key);
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
                foreach (var a in result.active)
                {
                    var entry = new ActiveQuest { QuestId = a.questId, Progress = a.progress };
                    if (a.objectiveProgress != null)
                        foreach (var objective in a.objectiveProgress)
                            entry.SetProgress(objective.objectiveIndex, objective.progress);
                    _active.Add(entry);
                }
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
            if (OriginalQuestCatalog.All.ContainsKey(questId)) { await OriginalTransitionAsync(questId, 0); return; }
            if (_pc == null || !QuestTable.All.TryGetValue(questId, out var def)) return;
            if (!def.HasValidObjectives)
            {
                Debug.LogError("[Quests] Objetivos invalidos no catalogo: " + questId);
                _pc.RpcShowMessage("Configuracao da missao invalida.", PlayerMessageType.Warning);
                return;
            }
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
            if (OriginalQuestCatalog.All.ContainsKey(questId)) { await OriginalTransitionAsync(questId, 2); return; }
            if (_refreshPending || _pendingAccepts.Contains(questId) || _pendingAbandons.Contains(questId)
                || _pendingTurnIns.Count != 0 || HasPendingProgress(questId))
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
                ClearDirtyProgress(questId);
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
            if (OriginalQuestCatalog.All.ContainsKey(questId)) { await OriginalTransitionAsync(questId, 1); return; }
            var entry = _active.FirstOrDefault(a => a.QuestId == questId);
            if (entry == null || !QuestTable.All.TryGetValue(questId, out var def)) return;
            if (_refreshPending || _pendingAccepts.Contains(questId) || _pendingAbandons.Contains(questId)
                || _pendingTurnIns.Count != 0 || HasPendingProgress(questId))
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
                ClearDirtyProgress(questId);
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
            var collected = new List<QuestCollectionItem>();
            materials = collected;
            if (!def.HasValidObjectives) return false;
            for (int i = 0; i < def.ObjectiveCount; i++)
            {
                var objective = def.GetObjective(i);
                switch (objective.Type)
                {
                    case QuestObjectiveType.TalkTo:
                        if (entry.GetProgress(i) < 1) return false;
                        break;
                    case QuestObjectiveType.Kill:
                        if (entry.GetProgress(i) < objective.Required) return false;
                        break;
                    case QuestObjectiveType.Collect:
                        var items = objective.CollectionItems != null && objective.CollectionItems.Length > 0
                            ? objective.CollectionItems
                            : new[] { new QuestCollectionItem { ItemId = objective.ItemId > 0
                                ? objective.ItemId : ResolveItemIdByName(objective.Target), Quantity = objective.Required } };
                        if (items.Any(item => item == null || item.Quantity <= 0 || !PkoTables.Items.ContainsKey(item.ItemId))
                            || items.Select(item => item.ItemId).Distinct().Count() != items.Length) return false;
                        collected.AddRange(items);
                        break;
                    default: return false;
                }
            }
            if (collected.Count == 0) return true;
            if (_inventory == null) return false;
            var combined = new List<QuestCollectionItem>();
            foreach (var group in collected.GroupBy(item => item.ItemId))
            {
                long quantity = group.Sum(item => (long)item.Quantity);
                if (quantity > int.MaxValue) return false;
                combined.Add(new QuestCollectionItem { ItemId = group.Key, Quantity = (int)quantity });
            }
            materials = combined;
            var available = _inventory.GetInventoryData();
            return combined.All(item => PlayerInventory.TryConsumeMaterials(available, item.ItemId, item.Quantity));
        }

        internal static int ResolveItemIdByName(string substring)
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
        public void ServerNotifyKill(string monsterName, int originalMonsterId = 0)
        {
            int nativeId = originalMonsterId > 0 ? originalMonsterId : OriginalQuestCatalog.MonsterId(monsterName);
            if (nativeId > 0) OriginalNotify("IsMonster", nativeId, 1);
            if (string.IsNullOrEmpty(monsterName) || _active.Count == 0) return;
            string lower = monsterName.ToLowerInvariant();
            bool changed = false;
            foreach (var a in _active)
            {
                if (!QuestTable.All.TryGetValue(a.QuestId, out var def) || !def.HasValidObjectives
                    || _pendingAbandons.Contains(a.QuestId) || _pendingTurnIns.Contains(a.QuestId)) continue;
                for (int i = 0; i < def.ObjectiveCount; i++)
                {
                    var objective = def.GetObjective(i);
                    if (objective.Type != QuestObjectiveType.Kill || !lower.Contains(objective.Target.ToLowerInvariant())
                        || a.GetProgress(i) >= objective.Required) continue;
                    int progress = a.GetProgress(i) + 1;
                    a.SetProgress(i, progress);
                    _stateRevision++;
                    changed = true;
                    QueueQuestProgress(a, i);
                    if (progress >= objective.Required) _pc.RpcShowMessage("Objetivo concluido: " + def.Name, PlayerMessageType.QuestUpdate);
                }
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
                if (!QuestTable.All.TryGetValue(a.QuestId, out var def) || !def.HasValidObjectives
                    || _pendingAbandons.Contains(a.QuestId) || _pendingTurnIns.Contains(a.QuestId)) continue;
                for (int i = 0; i < def.ObjectiveCount; i++)
                {
                    var objective = def.GetObjective(i);
                    if (objective.Type != QuestObjectiveType.TalkTo || objective.Target != npcId || a.GetProgress(i) >= 1) continue;
                    a.SetProgress(i, 1);
                    _stateRevision++;
                    changed = true;
                    QueueQuestProgress(a, i);
                }
            }
            if (changed) PushState();
        }

        void QueueQuestProgress(ActiveQuest entry, int objectiveIndex)
        {
            var key = (entry.QuestId, objectiveIndex);
            _dirtyProgress[key] = entry.GetProgress(objectiveIndex);
            _ = PersistQuestProgressAsync(key);
        }

        async Task PersistQuestProgressAsync((int questId, int objectiveIndex) key)
        {
            if (!_progressPending.Add(key)) return;
            try
            {
                long characterId = _pc.CharacterId;
                string token = Token;
                var database = DatabaseService.Instance;
                if (database == null) throw new InvalidOperationException("Servico de progresso indisponivel.");
                while (_dirtyProgress.TryGetValue(key, out int progress))
                {
                    bool confirmed = false;
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        if (await database.SaveQuestProgressAsync(characterId, key.questId, progress, token, objectiveIndex: key.objectiveIndex))
                        { confirmed = true; break; }
                        if (attempt < 2) await Task.Delay(200);
                    }
                    if (!confirmed)
                    {
                        Debug.LogError("[Quests] Progresso nao confirmado apos tres tentativas: " + key);
                        if (this != null && connectionToClient != null)
                            _pc.RpcShowMessage("Nao foi possivel salvar o progresso da missao. Ele foi mantido nesta sessao; tente atualizar novamente.", PlayerMessageType.Warning);
                        return;
                    }
                    if (_dirtyProgress.TryGetValue(key, out int latest) && latest <= progress)
                        _dirtyProgress.Remove(key);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Quests] Falha ao confirmar progresso: " + e.Message);
                if (this != null && connectionToClient != null)
                    _pc.RpcShowMessage("Falha ao confirmar progresso da missao. Tente atualizar novamente.", PlayerMessageType.Warning);
            }
            finally { _progressPending.Remove(key); }
        }

        // Quests disponiveis para oferta automatica por um NPC especifico (usado por NPCInteractable).
        [Server]
        public List<int> GetOfferableQuestsFor(IEnumerable<int> npcQuestIds)
        {
            var result = new List<int>();
            foreach (var id in npcQuestIds)
            {
                if (OriginalQuestCatalog.All.ContainsKey(id)) { if (CanBeginOriginal(id)) result.Add(id); continue; }
                if (!QuestTable.All.TryGetValue(id, out var def) || !def.HasValidObjectives) continue;
                if (_active.Any(a => a.QuestId == id) || _completed.Contains(id)) continue;
                if (!def.CanAcceptAtLevel(_pc.Level)) continue;
                if (!def.MeetsQuestConditions(HasActiveQuest, _completed.Contains)) continue;
                result.Add(id);
            }
            return result;
        }

        [Server]
        public bool HasActiveQuest(int questId) => OriginalQuestCatalog.All.TryGetValue(questId, out var original)
            ? OriginalState != null && OriginalState.HasMission(original.MissionId) : _active.Any(a => a.QuestId == questId);

        [Server]
        public bool CanTurnIn(int questId)
        {
            if (OriginalQuestCatalog.All.ContainsKey(questId)) return CanResultOriginal(questId);
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
                if (_active[i].Objectives.Count > 0)
                    for (int index = 1; index <= _active[i].Objectives.Keys.Max(); index++)
                        sb.Append(',').Append(_active[i].GetProgress(index));
            }
            sb.Append('|');
            sb.Append(string.Join(",", _completed));
            sb.Append('|').Append(JsonUtility.ToJson(new OriginalPayload { State = OriginalState, Available = OriginalQuestsAvailable }));
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
                int originalSep = completedPart.IndexOf('|');
                if (originalSep >= 0)
                {
                    var original = JsonUtility.FromJson<OriginalPayload>(completedPart.Substring(originalSep + 1));
                    clientOriginalState = original.State ?? new OriginalQuestState();
                    clientOriginalAvailable = original.Available;
                    completedPart = completedPart.Substring(0, originalSep);
                }
                if (!string.IsNullOrEmpty(activePart))
                    foreach (var e in activePart.Split(';'))
                    {
                        var p = e.Split(':');
                        if (p.Length < 2) continue;
                        var progress = p[1].Split(',');
                        var entry = new ActiveQuest { QuestId = int.Parse(p[0]), Progress = int.Parse(progress[0]) };
                        for (int i = 1; i < progress.Length; i++) entry.SetProgress(i, int.Parse(progress[i]));
                        _active.Add(entry);
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
