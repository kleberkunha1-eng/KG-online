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

        readonly List<ActiveQuest> _active = new List<ActiveQuest>();
        readonly HashSet<int> _completed = new HashSet<int>();

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
            if (_pc == null) return;
            var (active, completed) = await DatabaseService.Instance.GetQuestsAsync(_pc.CharacterId, Token);
            _active.Clear();
            foreach (var a in active) _active.Add(new ActiveQuest { QuestId = a.questId, Progress = a.progress });
            _completed.Clear();
            foreach (var c in completed) _completed.Add(c);
            PushState();
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
            if (_active.Any(a => a.QuestId == questId) || _completed.Contains(questId))
            { _pc.RpcShowMessage("Voce ja possui ou ja concluiu essa missao.", PlayerMessageType.Warning); return; }
            if (_pc.Level < def.RequiredLevel)
            { _pc.RpcShowMessage("Nivel insuficiente para essa missao.", PlayerMessageType.Warning); return; }
            if (def.PrerequisiteId != 0 && !_completed.Contains(def.PrerequisiteId))
            { _pc.RpcShowMessage("Voce precisa concluir uma missao anterior primeiro.", PlayerMessageType.Warning); return; }

            bool ok = await DatabaseService.Instance.AcceptQuestAsync(_pc.CharacterId, questId, Token);
            if (!ok) { _pc.RpcShowMessage("Nao foi possivel aceitar a missao.", PlayerMessageType.Warning); return; }
            _active.Add(new ActiveQuest { QuestId = questId, Progress = 0 });
            _pc.RpcShowMessage("Missao aceita: " + def.Name, PlayerMessageType.QuestUpdate);
            PushState();
        }

        [Command]
        public void CmdAbandonQuest(int questId) => ServerAbandonQuest(questId);

        [Server]
        public async void ServerAbandonQuest(int questId)
        {
            if (!_active.Any(a => a.QuestId == questId)) return;
            if (await DatabaseService.Instance.AbandonQuestAsync(_pc.CharacterId, questId, Token))
            {
                _active.RemoveAll(a => a.QuestId == questId);
                PushState();
            }
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

            if (!IsObjectiveComplete(def, entry, out int itemIdForCollect))
            { _pc.RpcShowMessage("Objetivo ainda nao concluido.", PlayerMessageType.Warning); return; }

            // Para quests de coleta, consome os itens no momento da entrega.
            if (def.Objective.Type == QuestObjectiveType.Collect && itemIdForCollect > 0)
            {
                if (_inventory == null || !_inventory.RemoveItemById(itemIdForCollect, def.Objective.Required))
                { _pc.RpcShowMessage("Os itens exigidos nao estao mais no seu inventario.", PlayerMessageType.Warning); return; }
            }

            bool ok = await DatabaseService.Instance.CompleteQuestAsync(_pc.CharacterId, questId, Token);
            if (!ok) return;

            _active.Remove(entry);
            _completed.Add(questId);

            if (def.RewardExp > 0) _pc.AddExp((ulong)def.RewardExp);
            if (def.RewardGold > 0) _pc.AddGold((ulong)def.RewardGold);
            if (!string.IsNullOrEmpty(def.RewardItemName) && def.RewardItemQty > 0 && _inventory != null)
            {
                int rewardItemId = ResolveItemIdByName(def.RewardItemName);
                if (rewardItemId > 0)
                {
                    int slot = _inventory.FindEmptySlot();
                    if (slot >= 0) _inventory.AddItem(rewardItemId, def.RewardItemQty, (ushort)slot);
                }
            }

            _pc.RpcShowMessage("Missao concluida: " + def.Name, PlayerMessageType.QuestUpdate);
            PushState();
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
                    collectItemId = ResolveItemIdByName(def.Objective.Target);
                    if (collectItemId <= 0 || _inventory == null) return false;
                    int have = 0;
                    for (int i = 0; i < _inventory.totalSlots; i++)
                    {
                        var s = _inventory.GetSlot(i);
                        if (s != null && s.ItemId == collectItemId && !s.IsEquipped) have += s.Quantity;
                    }
                    return have >= def.Objective.Required;
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
        public void ServerOfferQuest(int questId) => TargetOfferQuest(connectionToClient, questId);

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
