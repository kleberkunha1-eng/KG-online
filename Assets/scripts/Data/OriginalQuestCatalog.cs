using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TOP.Data
{
    [Serializable] public sealed class OriginalMissionState
    {
        public int Id;
        public int DefinitionId;
        public List<int> Flags = new List<int>();
        public List<int> Triggers = new List<int>();
    }
    [Serializable] public sealed class OriginalQuestState
    {
        public int Version = 1;
        public List<int> Records = new List<int>();
        public List<OriginalMissionState> Missions = new List<OriginalMissionState>();
        public bool HasRecord(int id) => Records.Contains(id);
        public void SetRecord(int id) { if (!HasRecord(id)) Records.Add(id); }
        public bool HasMission(int id) => Missions.Any(m => m.Id == id);
        public bool HasFlag(int id, int flag) => Missions.Any(m => m.Id == id && m.Flags.Contains(flag));
        public void SetFlag(int id, int flag)
        {
            var mission = Missions.FirstOrDefault(m => m.Id == id);
            if (mission == null) throw new InvalidOperationException("Flag without original mission: " + id);
            if (!mission.Flags.Contains(flag)) mission.Flags.Add(flag);
        }
        public OriginalQuestState Copy() => JsonUtility.FromJson<OriginalQuestState>(JsonUtility.ToJson(this));
    }
    [Serializable] public sealed class OriginalQuestCommand
    {
        public string Name;
        public string[] Args;
        public int Int(int index) => index < Args.Length ? int.Parse(Args[index], System.Globalization.CultureInfo.InvariantCulture) : 0;
    }
    [Serializable] public sealed class OriginalQuestNeed { public string Type; public int Target, Count, Flag; public string Description; }
    [Serializable] public sealed class OriginalQuestTrigger { public int Id, Target, MissionId, Flag, Count; public string Type; }
    [Serializable] public sealed class OriginalQuestDefinition
    {
        public int Id, MissionId;
        public string Name, Source, BeginTalk, ResultTalk;
        public string[] NpcNames;
        public bool CompletionOnly;
        public int BeginBagNeed, ResultBagNeed;
        public OriginalQuestCommand[] BeginConditions, ResultConditions, BeginActions, ResultActions, CancelActions;
        public OriginalQuestNeed[] Needs;
        public OriginalQuestTrigger[] Triggers;
        public int RuntimeId => OriginalQuestCatalog.IdOffset + Id;
    }
    [Serializable] public sealed class OriginalMonster { public int Id, Level; public string Name; }
    [Serializable] public sealed class OriginalProfessionPair { public int From, To; }
    [Serializable] public sealed class OriginalQuestCatalogData
    {
        public int Version, Total, Imported;
        public OriginalQuestDefinition[] Definitions;
        public OriginalMonster[] Monsters;
        public OriginalProfessionPair[] ProfessionPairs, RacePairs;
    }
    public static class OriginalQuestCatalog
    {
        // Keep legacy inferred IDs and their already-persisted progress intact.
        public const int IdOffset = 1000000;
        public static readonly OriginalQuestCatalogData Data;
        public static readonly Dictionary<int, OriginalQuestDefinition> All;
        public static readonly Dictionary<int, OriginalQuestTrigger> Triggers;
        static OriginalQuestCatalog()
        {
            var asset = Resources.Load<TextAsset>("OriginalQuests");
            if (asset == null) throw new InvalidOperationException("Original quest catalog missing. Run Tools\\Import-OriginalQuests.cjs.");
            Data = JsonUtility.FromJson<OriginalQuestCatalogData>(asset.text);
            if (Data == null || Data.Version != 1) throw new InvalidOperationException("Unsupported original quest catalog.");
            All = Data.Definitions.ToDictionary(d => d.RuntimeId);
            Triggers = Data.Definitions.SelectMany(d => d.Triggers).GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        }
        public static int MonsterId(string name, int level = 0)
        {
            var matches = Data.Monsters.Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) && (level == 0 || m.Level == level)).ToArray();
            return matches.Length == 1 ? matches[0].Id : 0;
        }
        public static int[] ForNpc(string name, bool completion) => All.Values.Where(d => d.NpcNames.Contains(name)
            && (completion ? d.ResultActions.Length > 0 : !d.CompletionOnly && d.BeginActions.Length > 0)).Select(d => d.RuntimeId).ToArray();
        public static void Register(Dictionary<int, QuestDef> table)
        {
            foreach (var d in All.Values)
            {
                var objectives = d.Needs.Where(n => n.Type != "MIS_NEED_DESP").Select(n => new QuestObjective
                {
                    Type = n.Type == "MIS_NEED_KILL" ? QuestObjectiveType.Kill : QuestObjectiveType.Collect,
                    ItemId = n.Type == "MIS_NEED_ITEM" ? n.Target : 0,
                    Target = n.Type == "MIS_NEED_KILL" ? "#" + n.Target : "", Required = n.Count
                }).ToArray();
                if (objectives.Length == 0) objectives = new[] { new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = string.Join(" / ", d.NpcNames) } };
                var exp = d.ResultActions.FirstOrDefault(a => a.Name == "AddExp");
                var reward = d.ResultActions.FirstOrDefault(a => a.Name == "GiveItem");
                table.Add(d.RuntimeId, new QuestDef { Id = d.RuntimeId, Name = d.Name, Description = d.BeginTalk.Length > 0 ? d.BeginTalk : d.ResultTalk,
                    Objective = objectives[0], AdditionalObjectives = objectives.Skip(1).ToArray(),
                    RewardExp = exp == null ? 0 : exp.Int(0), RewardExpMaximumExclusive = exp == null ? 0 : exp.Int(1),
                    RewardGold = d.ResultActions.Where(a => a.Name == "AddMoney").Sum(a => a.Int(0)),
                    RewardItemId = reward == null ? 0 : reward.Int(0), RewardItemQty = reward == null ? 0 : reward.Int(1) });
            }
        }
        public static bool CanConvert(byte job, byte gender, int next)
        {
            int race = gender == 4 ? 1 : gender + 1;
            return (job == 0 || Data.ProfessionPairs.Any(p => p.From == job && p.To == next))
                && Data.RacePairs.Any(p => p.From == race && p.To == next);
        }
        public static bool Meets(OriginalQuestCommand[] conditions, OriginalQuestState state, CharacterData character, Func<int, long> itemCount)
        {
            foreach (var c in conditions)
            {
                int a = c.Args.Length > 0 && c.Name != "LvCheck" ? c.Int(0) : 0;
                bool ok;
                switch (c.Name)
                {
                    case "AlwaysFailure": ok = false; break;
                    case "AlwaysSuccess": ok = true; break;
                    case "HasMission": ok = state.HasMission(a); break;
                    case "NoMission": ok = !state.HasMission(a); break;
                    case "HasRecord": ok = state.HasRecord(a); break;
                    case "NoRecord": ok = !state.HasRecord(a); break;
                    case "HasFlag": ok = state.HasFlag(a, c.Int(1)); break;
                    case "NoFlag": ok = !state.HasFlag(a, c.Int(1)); break;
                    case "PfEqual": ok = character.Job == a; break;
                    case "NoPfEqual": ok = character.Job != a; break;
                    case "IsChaType": ok = (character.Gender == 4 ? 1 : character.Gender + 1) == a; break;
                    case "NoChaType": ok = (character.Gender == 4 ? 1 : character.Gender + 1) != a; break;
                    case "CTypeCheck": ok = c.Args.Any(v => int.Parse(v) == (character.Gender == 4 ? 1 : character.Gender + 1)); break;
                    case "CheckConvertProfession": ok = CanConvert(character.Job, character.Gender, a); break;
                    case "HasItem": ok = itemCount(a) >= c.Int(1); break;
                    case "NoItem": ok = itemCount(a) < c.Int(1); break;
                    case "HasMoney": ok = character.Gold >= (ulong)a; break;
                    case "LvCheck":
                        int n = c.Int(1);
                        switch (c.Args[0])
                        {
                            case ">": ok = character.Level > n; break;
                            case "<": ok = character.Level < n; break;
                            case ">=": ok = character.Level >= n; break;
                            case "<=": ok = character.Level <= n; break;
                            case "==": case "=": ok = character.Level == n; break;
                            case "~=": case "!=": ok = character.Level != n; break;
                            default: ok = false; break;
                        }
                        break;
                    default: return false;
                }
                if (!ok) return false;
            }
            return true;
        }
        public static void ApplyStateActions(OriginalQuestDefinition definition, OriginalQuestCommand[] actions, OriginalQuestState state)
        {
            foreach (var c in actions)
            {
                if (c.Name == "SystemNotice") continue;
                int a = c.Int(0);
                switch (c.Name)
                {
                    case "AddMission":
                        if (!state.HasMission(a)) state.Missions.Add(new OriginalMissionState { Id = a, DefinitionId = definition.Id });
                        break;
                    case "ClearMission": state.Missions.RemoveAll(m => m.Id == a); break;
                    case "SetRecord": state.SetRecord(a); break;
                    case "ClearRecord": state.Records.Remove(a); break;
                    case "SetFlag": state.SetFlag(a, c.Int(1)); break;
                    case "ClearFlag": state.Missions.FirstOrDefault(m => m.Id == a)?.Flags.Remove(c.Int(1)); break;
                    case "AddTrigger":
                        var trigger = Triggers.TryGetValue(a, out var t) ? t : null;
                        var mission = trigger == null ? null : state.Missions.FirstOrDefault(m => m.Id == trigger.MissionId);
                        if (mission == null) throw new InvalidOperationException("Original trigger not resolvable: " + a);
                        if (!mission.Triggers.Contains(a)) mission.Triggers.Add(a);
                        break;
                    case "ClearTrigger": foreach (var m in state.Missions) m.Triggers.Remove(a); break;
                }
            }
        }
        public static bool Notify(OriginalQuestState state, string type, int target)
        {
            bool changed = false;
            foreach (var mission in state.Missions)
                foreach (int id in mission.Triggers)
                {
                    if (!Triggers.TryGetValue(id, out var trigger) || trigger.Type != type || trigger.Target != target) continue;
                    for (int flag = trigger.Flag; flag < trigger.Flag + trigger.Count; flag++)
                        if (!mission.Flags.Contains(flag)) { mission.Flags.Add(flag); changed = true; break; }
                }
            return changed;
        }
    }
}
