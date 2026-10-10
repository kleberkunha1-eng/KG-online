using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mirror;
using TOP.Data;
using TOP.Services;
using UnityEngine;

namespace TOP.Player
{
    public partial class PlayerQuests
    {
        [Serializable] sealed class OriginalPayload { public OriginalQuestState State; public bool Available; }
        bool clientOriginalAvailable;
        OriginalQuestState clientOriginalState = new OriginalQuestState();
        bool originalTransitionPending, originalProgressDirty;
        Task originalProgressTask = Task.CompletedTask;
        readonly List<(string type, int id, int count)> originalDeferredEvents = new List<(string, int, int)>();
        OriginalQuestState OriginalState => isServer && _pc != null && _pc.GetCharacterData() != null
            ? _pc.GetCharacterData().OriginalQuests : clientOriginalState;
        public bool OriginalQuestsAvailable => isServer ? _pc != null && _pc.GetCharacterData() != null && _pc.GetCharacterData().QuestStateVersion == 1 : clientOriginalAvailable;
        CharacterData OriginalCharacter() => isServer ? _pc.GetCharacterData() : new CharacterData { Job = _pc.Job, Gender = _pc.Gender, Level = _pc.Level, Gold = _pc.Gold };

        internal static void InitializeOriginalState(CharacterData data)
        {
            if (data.OriginalQuests == null) data.OriginalQuests = new OriginalQuestState();
            if (data.QuestStateVersion == 1 && !data.OriginalQuests.HasRecord(1) && !data.OriginalQuests.HasMission(1))
                data.OriginalQuests.Missions.Add(new OriginalMissionState { Id = 1, DefinitionId = 701 });
        }
        public bool HasRecord(int record) => OriginalState != null && OriginalState.HasRecord(record);
        public bool HasFlag(int mission, int flag) => OriginalState != null && OriginalState.HasFlag(mission, flag);

        public bool CanBeginOriginal(int id)
        {
            if (!OriginalQuestCatalog.All.TryGetValue(id, out var d) || d.CompletionOnly || d.BeginActions.Length == 0
                || _pc == null || !OriginalQuestsAvailable) return false;
            return OriginalQuestCatalog.Meets(d.BeginConditions, OriginalState, OriginalCharacter(), OriginalItemCount);
        }
        public bool CanResultOriginal(int id)
        {
            if (!OriginalQuestCatalog.All.TryGetValue(id, out var d) || d.ResultActions.Length == 0
                || _pc == null || !OriginalQuestsAvailable) return false;
            return OriginalQuestCatalog.Meets(d.ResultConditions, OriginalState, _pc.GetCharacterData(), OriginalItemCount);
        }
        long OriginalItemCount(int id) => _inventory != null ? _inventory.GetQuestMaterialCount(id) : 0;
        public int OriginalObjectiveProgress(int id, int index)
        {
            if (!OriginalQuestCatalog.All.TryGetValue(id, out var d)) return 0;
            var needs = d.Needs.Where(n => n.Type != "MIS_NEED_DESP").ToArray();
            if (index >= needs.Length) return CanResultOriginal(id) ? 1 : 0;
            var n = needs[index];
            return n.Type == "MIS_NEED_ITEM" ? (int)Math.Min(n.Count, OriginalItemCount(n.Target))
                : Enumerable.Range(n.Flag, n.Count).Count(flag => HasFlag(d.MissionId, flag));
        }
        IEnumerable<(int questId, int progress)> OriginalActiveQuests() => OriginalState == null
            ? Enumerable.Empty<(int, int)>() : OriginalState.Missions.Where(m => OriginalQuestCatalog.All.ContainsKey(m.DefinitionId + OriginalQuestCatalog.IdOffset))
                .Select(m => (m.DefinitionId + OriginalQuestCatalog.IdOffset, OriginalObjectiveProgress(m.DefinitionId + OriginalQuestCatalog.IdOffset, 0)));

        [Server]
        async Task OriginalTransitionAsync(int id, int action)
        {
            if (_pc == null || _inventory == null || !OriginalQuestsAvailable || !OriginalQuestCatalog.All.TryGetValue(id, out var d)) return;
            if (_refreshPending || originalTransitionPending || _pendingAccepts.Count != 0 || _pendingTurnIns.Count != 0
                || _pendingAbandons.Count != 0 || !originalProgressTask.IsCompleted)
            { _pc.RpcShowMessage("Aguarde a confirmacao da missao original.", PlayerMessageType.Warning); return; }
            if (action != 2 && !CanUseQuestNpc(id, action == 1)) return;
            if (action == 0 && (!_offers.TryGetValue(id, out var offer) || offer.npc != GetComponent<PlayerMovement>().ActiveNpc || Time.time > offer.expires)) return;
            if (action == 0 && !CanBeginOriginal(id) || action == 1 && !CanResultOriginal(id))
            { _pc.RpcShowMessage("Pre-requisitos originais ainda nao atendidos.", PlayerMessageType.Warning); return; }
            if (action == 2 && (!OriginalState.Missions.Any(m => m.DefinitionId == d.Id) || d.CancelActions.Length == 0)) return;
            var commands = action == 0 ? d.BeginActions : action == 1 ? d.ResultActions : d.CancelActions;
            var materials = commands.Where(c => c.Name == "TakeItem").GroupBy(c => c.Int(0))
                .Select(g => new QuestCollectionItem { ItemId = g.Key, Quantity = checked(g.Sum(c => c.Int(1))) }).ToArray();
            var rewards = commands.Where(c => c.Name == "GiveItem")
                .Select(c => new QuestCollectionItem { ItemId = c.Int(0), Quantity = c.Int(1) }).ToArray();
            var transaction = _inventory.PrepareOriginalQuestTransaction(materials, rewards, action == 0 ? d.BeginBagNeed : d.ResultBagNeed, out var error);
            if (transaction == null) { _pc.RpcShowMessage(error, PlayerMessageType.Warning); return; }
            originalTransitionPending = true;
            _pendingTurnIns.Add(id);
            _stateRevision++;
            CharacterData live = null, prepared = null;
            int expGain = 0;
            long moneyDelta = 0;
            try
            {
                var result = await DatabaseService.Instance.PersistCharacterAsync(_pc.CharacterId, () =>
                {
                    if (_pc == null) throw new InvalidOperationException("Player disconnected before original mission transaction.");
                    live = _pc.GetCharacterData();
                    prepared = live.CopySnapshot();
                    prepared.Inventory = transaction.PreparedItems;
                    OriginalQuestCatalog.ApplyStateActions(d, commands, prepared.OriginalQuests);
                    foreach (var reward in rewards)
                        for (int i = 0; i < Math.Min(reward.Quantity, 1024); i++)
                            OriginalQuestCatalog.Notify(prepared.OriginalQuests, "IsItem", reward.ItemId);
                    foreach (var c in commands)
                    {
                        if (c.Name == "AddExp")
                        {
                            int minimum = c.Int(0), maximum = c.Int(1);
                            expGain = checked(expGain + (maximum > minimum ? UnityEngine.Random.Range(minimum, maximum) : minimum));
                        }
                        else if (c.Name == "AddMoney") moneyDelta = checked(moneyDelta + c.Int(0));
                        else if (c.Name == "TakeMoney") moneyDelta = checked(moneyDelta - c.Int(0));
                        else if (c.Name == "SetProfession") prepared.Job = checked((byte)c.Int(0));
                        else if (c.Name == "ClearFightSkill") prepared.Skills.RemoveAll(s => s.SkillId == c.Int(0));
                    }
                    if (expGain < 0 || moneyDelta < 0 && prepared.Gold < (ulong)-moneyDelta) throw new InvalidOperationException("Original reward unavailable.");
                    if (expGain > 0) PlayerController.ApplyExperience(prepared, (ulong)expGain);
                    prepared.Gold = moneyDelta < 0 ? prepared.Gold - (ulong)-moneyDelta : checked(prepared.Gold + (ulong)moneyDelta);
                    return prepared;
                }, Token, 0);
                if (!result.success)
                {
                    if (this != null && connectionToClient != null)
                    {
                        _pc.RpcShowMessage("Missao original nao confirmada: " + result.error, PlayerMessageType.Warning);
                        if (result.error == "RELOAD_REQUIRED" || result.error == "SAVE_CONFLICT" || result.error == "OPERATION_MISMATCH") connectionToClient.Disconnect();
                    }
                    return;
                }
                live.SaveRevision = prepared.SaveRevision;
                if (this == null) return;
                transaction.Commit();
                live.OriginalQuests = prepared.OriginalQuests;
                live.Job = _pc.Job = prepared.Job;
                if (expGain > 0) _pc.AddExp((ulong)expGain);
                if (moneyDelta < 0) _pc.Gold -= (ulong)-moneyDelta;
                else if (moneyDelta > 0) _pc.AddGold((ulong)moneyDelta);
                var stats = GetComponent<PlayerStats>();
                foreach (var command in commands.Where(c => c.Name == "ClearFightSkill"))
                    if (stats != null) stats.ForgetOriginalQuestSkill(command.Int(0));
                if (connectionToClient != null)
                {
                    _pc.RpcShowMessage((action == 0 ? "Missao aceita: " : action == 1 ? "Etapa concluida: " : "Missao abandonada: ") + d.Name, PlayerMessageType.QuestUpdate);
                    PushState();
                }
            }
            finally
            {
                transaction.Dispose();
                _pendingTurnIns.Remove(id);
                originalTransitionPending = false;
                if (this != null)
                {
                    foreach (var pending in originalDeferredEvents.ToArray()) OriginalNotify(pending.type, pending.id, pending.count);
                    originalDeferredEvents.Clear();
                }
            }
        }

        [Server]
        public void ServerNotifyOriginalItem(int itemId, int quantity) => OriginalNotify("IsItem", itemId, quantity);
        [Server]
        void OriginalNotify(string type, int target, int quantity)
        {
            if (!OriginalQuestsAvailable || quantity <= 0) return;
            if (originalTransitionPending) { originalDeferredEvents.Add((type, target, quantity)); return; }
            bool changed = false;
            for (int i = 0; i < Math.Min(quantity, 1024); i++) changed |= OriginalQuestCatalog.Notify(OriginalState, type, target);
            if (!changed) return;
            originalProgressDirty = true;
            _stateRevision++;
            if (originalProgressTask.IsCompleted) originalProgressTask = PersistOriginalProgressAsync();
            if (connectionToClient != null) PushState();
        }
        async Task PersistOriginalProgressAsync()
        {
            while (originalProgressDirty && this != null && connectionToClient != null)
            {
                originalProgressDirty = false;
                CharacterData live = null, saved = null;
                var result = await DatabaseService.Instance.PersistCharacterAsync(_pc.CharacterId, () =>
                {
                    live = _pc.GetCharacterData(); saved = live.CopySnapshot(); return saved;
                }, Token, 0);
                if (!result.success)
                {
                    originalProgressDirty = true;
                    if (this != null && connectionToClient != null)
                    {
                        _pc.RpcShowMessage("Progresso original nao confirmado. Reconecte para recuperar o estado duravel.", PlayerMessageType.Warning);
                        if (result.error == "RELOAD_REQUIRED" || result.error == "SAVE_CONFLICT" || result.error == "OPERATION_MISMATCH") connectionToClient.Disconnect();
                    }
                    return;
                }
                live.SaveRevision = saved.SaveRevision;
            }
        }
    }
}
