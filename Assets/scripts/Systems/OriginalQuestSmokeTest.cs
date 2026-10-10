#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using kcp2k;
using Mirror;
using TOP.Data;
using TOP.NPC;
using TOP.Player;
using TOP.Services;
using UnityEngine;

namespace TOP.Testing
{
    public static class OriginalQuestSmokeTest
    {
        static string lastPayload;
        internal static void Receive(RpcMessage rpc)
        {
            if (rpc.functionHash != AdminGenerationSmokeTest.FunctionHash("TargetQuestsUpdated", typeof(PlayerQuests))) return;
            using (var reader = NetworkReaderPool.Get(rpc.payload)) lastPayload = reader.ReadString();
        }
        [Serializable] sealed class Request { public CharacterData Character; public List<InventoryItemData> Inventory; public int QuestId; }
        static IEnumerator Pump(Action tick, float seconds = .3f)
        { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) { tick(); yield return null; } }
        static IEnumerator AwaitSave(Action tick, PlayerQuests quests, QuestPersistenceSmokeTest.ApiFixture fixture, int requests)
        {
            float until = Time.realtimeSinceStartup + 12;
            while (Time.realtimeSinceStartup < until && (fixture.Requests < requests || quests.HasPendingCompletion || quests.HasUnsavedProgress))
            { tick(); yield return null; }
            if (fixture.Requests < requests || quests.HasPendingCompletion || quests.HasUnsavedProgress) throw new TimeoutException("Original quest save did not complete.");
        }
        public static IEnumerator Run(GameObject player, KcpClient client, Action tick, Action<bool, string> check)
        {
            var catalog = OriginalQuestCatalog.Data;
            var resource = Resources.Load<TextAsset>("OriginalQuests");
            var packaged = resource == null ? null : JsonUtility.FromJson<OriginalQuestCatalogData>(resource.text);
            check(packaged != null && packaged.Version == 1
                && packaged.Definitions.Select(d => d.RuntimeId).OrderBy(id => id).SequenceEqual(OriginalQuestCatalog.All.Keys.OrderBy(id => id))
                && packaged.Definitions.All(d => QuestTable.All.ContainsKey(d.RuntimeId)),
                "Runtime loads the packaged OriginalQuests resource and registers every imported definition in QuestTable.");
            var names = UnityEngine.Object.FindObjectsByType<NPCInteractable>(FindObjectsSortMode.None).Select(n => n.NpcName).ToHashSet();
            int present = catalog.Definitions.Count(d => d.NpcNames.Any(names.Contains));
            File.WriteAllText("Tools/original-quest-world-coverage.txt", "Imported " + catalog.Imported + "/" + catalog.Total
                + "\nDefinitions bound to an active world NPC: " + present + "\nOther imported definitions: " + (catalog.Imported - present)
                + "\nUnsupported definitions: " + (catalog.Total - catalog.Imported) + "\nActive NPC names: " + string.Join(" | ", names));
            check(catalog.Imported >= 900 && catalog.Total == 1508 && OriginalQuestCatalog.All.Count == catalog.Imported,
                "Declarative importer preserves broad original coverage and reports every unsupported definition fail-closed.");
            check(QuestTable.All.ContainsKey(1) && QuestTable.All.ContainsKey(704) && QuestTable.All.ContainsKey(1000702),
                "Native definition namespace preserves existing inferred quest IDs and their saved progress.");
            check(OriginalQuestCatalog.MonsterId("Mystic Shrub") > 0 && OriginalQuestCatalog.MonsterId("not an original monster") == 0,
                "Native kill triggers resolve exact original monster IDs, never legacy substring matches.");
            check(OriginalQuestCatalog.MonsterId("Baby Scorpion") == 0 && OriginalQuestCatalog.MonsterId("Baby Scorpion", 3) == 188
                && OriginalQuestCatalog.MonsterId("Baby Scorpion", 1) == 1551,
                "Original monster variants with identical names resolve by native level or explicit numeric ID, never by ambiguous names alone.");
            var state = new OriginalQuestState();
            state.SetRecord(701); state.SetRecord(701);
            check(state.Records.Count == 1 && state.HasRecord(701) && !state.HasRecord(702), "Native records are independent, idempotent and exact.");
            var letter = OriginalQuestCatalog.All[1000702];
            OriginalQuestCatalog.ApplyStateActions(letter, letter.BeginActions, state);
            check(state.HasMission(701) && state.HasFlag(701, 1) && !state.HasFlag(701, 10), "Accept actions create a native mission and its independent flags.");
            state.SetFlag(701, 10); state.SetFlag(701, 10);
            check(state.Missions[0].Flags.Count == 2, "SetFlag is idempotent and does not confuse a flag with a record.");
            var copy = state.Copy(); copy.SetRecord(2); copy.SetFlag(701, 11);
            check(!state.HasRecord(2) && !state.HasFlag(701, 11), "Original state snapshots deeply isolate records, missions and flags.");
            OriginalQuestCatalog.ApplyStateActions(letter, letter.ResultActions, state);
            check(state.HasRecord(701) && !state.HasMission(701) && !state.HasFlag(701, 10), "ClearMission discards its flags while completion records survive.");
            var character = new CharacterData { Level = 9, Job = 0, Gender = 0, Gold = 10 };
            OriginalQuestCommand[] rules = { new OriginalQuestCommand { Name = "LvCheck", Args = new[] { ">", "9" } },
                new OriginalQuestCommand { Name = "PfEqual", Args = new[] { "0" } },
                new OriginalQuestCommand { Name = "HasRecord", Args = new[] { "701" } },
                new OriginalQuestCommand { Name = "HasItem", Args = new[] { "1847", "2" } } };
            check(!OriginalQuestCatalog.Meets(rules, state, character, item => 2), "Native strict level comparison rejects the inclusive boundary.");
            character.Level = 10;
            check(!OriginalQuestCatalog.Meets(new[] { new OriginalQuestCommand { Name = "UnsupportedPrerequisite", Args = new string[0] } },
                state, character, item => 2), "Unknown native prerequisites fail closed instead of unlocking a quest.");
            check(OriginalQuestCatalog.Meets(rules, state, character, item => 2), "Level, class, previous-record and item prerequisites combine conjunctively.");
            character.Job = 2;
            check(!OriginalQuestCatalog.Meets(rules, state, character, item => 2), "Wrong original class cannot bypass prerequisites.");
            character.Job = 0;
            check(!OriginalQuestCatalog.Meets(rules, state, character, item => 1), "Insufficient original item quantity blocks acceptance.");
            check(OriginalQuestCatalog.CanConvert(1, 1, 8) && !OriginalQuestCatalog.CanConvert(1, 1, 9)
                && OriginalQuestCatalog.CanConvert(1, 0, 9) && !OriginalQuestCatalog.CanConvert(2, 0, 9),
                "Original profession and race tables enforce valid Champion/Crusader conversion paths.");
            var combat = OriginalQuestCatalog.All.Values.First(d => d.Triggers.Any(t => t.Type == "IsMonster") && d.BeginActions.Any(a => a.Name == "AddMission"));
            var trigger = combat.Triggers.First(t => t.Type == "IsMonster");
            var combatState = new OriginalQuestState();
            OriginalQuestCatalog.ApplyStateActions(combat, combat.BeginActions, combatState);
            check(!OriginalQuestCatalog.Notify(combatState, "IsMonster", int.MaxValue), "Unrelated kills cannot advance native triggers.");
            for (int i = 0; i < trigger.Count + 2; i++) OriginalQuestCatalog.Notify(combatState, "IsMonster", trigger.Target);
            check(Enumerable.Range(trigger.Flag, trigger.Count).All(f => combatState.HasFlag(trigger.MissionId, f)),
                "Native kill counters set consecutive mission flags and clamp at the original trigger count.");

            var controller = player.GetComponent<PlayerController>(); var quests = player.GetComponent<PlayerQuests>();
            var inventory = player.GetComponent<PlayerInventory>(); var movement = player.GetComponent<PlayerMovement>();
            var data = controller.GetCharacterData(); var oldState = data.OriginalQuests; int oldVersion = data.QuestStateVersion;
            byte oldJob = controller.Job; int oldLevel = controller.Level; ulong oldExp = controller.Exp, oldGold = controller.Gold;
            long oldRevision = data.SaveRevision; var oldInventory = inventory.GetInventoryData(); Vector3 oldPosition = player.transform.position;
            var rootField = typeof(ApiConfig).GetField("root", BindingFlags.NonPublic | BindingFlags.Static);
            string oldRoot = (string)rootField.GetValue(null); GameObject service = null, sennaFixture = null;
            using (var fixture = new QuestPersistenceSmokeTest.ApiFixture())
            {
                try
                {
                    rootField.SetValue(null, fixture.Root);
                    if (DatabaseService.Instance == null) service = new GameObject("Original quest isolated API", typeof(DatabaseService));
                    sennaFixture = new GameObject("Original Senna synthetic KCP fixture", typeof(NetworkIdentity), typeof(NPCInteractable));
                    var senna = sennaFixture.GetComponent<NPCInteractable>();
                    typeof(NPCInteractable).GetField("npcName", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(senna, "Newbie Guide - Senna");
                    typeof(NPCInteractable).GetField("npcId", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(senna, "original_senna_fixture");
                    sennaFixture.transform.position = player.transform.position;
                    typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sennaFixture.GetComponent<NetworkIdentity>(), null);
                    NetworkServer.Spawn(sennaFixture);
                    var goldie = UnityEngine.Object.FindObjectsByType<NPCInteractable>(FindObjectsSortMode.None).Single(n => n.NpcName == "Blacksmith - Goldie");
                    void Reply() => fixture.Reply("{\"success\":true,\"revision\":" + (data.SaveRevision + 1) + ",\"questStateVersion\":1}");
                    void Talk(NPCInteractable npc)
                    {
                        player.transform.position = npc.transform.position + Vector3.right;
                        SocialGameplaySmokeTest.Send(client, movement, "CmdInteractWithNpc", w => w.WriteNetworkIdentity(npc.netIdentity));
                    }
                    void Result(int id, NPCInteractable npc) => SocialGameplaySmokeTest.Send(client, npc, "CmdQuestAction", w => { w.Write(id); w.Write(true); });
                    data.OriginalQuests = new OriginalQuestState(); data.QuestStateVersion = 0;
                    Talk(senna); yield return Pump(tick);
                    Result(1000701, senna); yield return Pump(tick);
                    check(fixture.Requests == 0, "Old APIs without native quest capability cannot grant memory-only records or rewards.");
                    data.QuestStateVersion = 1; controller.Job = 0; controller.Level = 50;
                    controller.Exp = 0; controller.Gold = 0; inventory.InitializeFromData(new List<InventoryItemData>());
                    PlayerQuests.InitializeOriginalState(data);
                    check(quests.CanResultOriginal(1000701) && !quests.CanBeginOriginal(1000702), "Original Welcome episode gates the first Senna letter chain.");
                    Reply(); Result(1000701, senna); Result(1000701, senna);
                    yield return AwaitSave(tick, quests, fixture, 1);
                    check(fixture.Requests == 1 && quests.HasRecord(1) && controller.Exp == 6 && quests.CanBeginOriginal(1000702),
                        "Actual KCP Welcome completion durably records mission 1, awards six XP once and unlocks the next episode.");
                    check(senna.OffersQuest(1000702) && goldie.ReceivesQuest(1000703) && !goldie.OffersQuest(1000702),
                        "Native NPC bindings distinguish Senna's acceptance from Goldie's completion-only delivery.");
                    SocialGameplaySmokeTest.Send(client, senna, "CmdQuestAction", w => { w.Write(1000702); w.Write(false); });
                    yield return Pump(tick);
                    Reply(); SocialGameplaySmokeTest.Send(client, quests, "CmdAcceptQuest", w => w.Write(1000702));
                    yield return AwaitSave(tick, quests, fixture, 2);
                    check(quests.HasFlag(701, 1) && inventory.GetQuestMaterialCount(3950) == 1 && quests.HasActiveQuest(1000702),
                        "Original acceptance atomically persists mission flags and the letter item using the character save receipt path.");
                    var body = JsonUtility.FromJson<Request>(fixture.Bodies.ToArray().Last());
                    check(body.QuestId == 0 && body.Character.QuestStateVersion == 1 && body.Character.OriginalQuests.HasFlag(701, 1)
                        && body.Inventory.Any(i => i.ItemId == 3950), "Native mission, letter and capability share one immutable atomic save snapshot.");
                    Result(1000703, goldie); yield return Pump(tick);
                    check(fixture.Requests == 2 && inventory.GetQuestMaterialCount(3950) == 1, "Remote-NPC delivery is rejected before any HTTP transaction or item consumption.");
                    Talk(goldie); yield return Pump(tick);
                    fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_REJECTED\"}"); Result(1000703, goldie);
                    yield return AwaitSave(tick, quests, fixture, 3);
                    check(!quests.HasFlag(701, 10) && inventory.GetQuestMaterialCount(3950) == 1 && !inventory.HasQuestTransaction,
                        "Rejected native delivery preserves its item and flags and releases the inventory reservation.");
                    fixture.Reply("{\"success\":false,\"error\":\"DB_ERROR\"}"); Reply(); Result(1000703, goldie); Result(1000703, goldie);
                    yield return AwaitSave(tick, quests, fixture, 5);
                    var bodies = fixture.Bodies.ToArray();
                    check(bodies[3] == bodies[4] && fixture.Requests == 5 && quests.HasFlag(701, 10) && inventory.GetQuestMaterialCount(3950) == 0
                        && quests.HasActiveQuest(1000702) && !quests.HasRecord(701),
                        "Completion-only Goldie episode retries the same receipt, consumes the letter once and sets a flag without closing the parent mission.");
                    Talk(senna); yield return Pump(tick); Reply(); Result(1000702, senna);
                    yield return AwaitSave(tick, quests, fixture, 6);
                    check(quests.HasRecord(701) && !quests.HasFlag(701, 10) && !quests.HasActiveQuest(1000702)
                        && controller.Exp == 15 && quests.CanBeginOriginal(1000704),
                        "Returning to Senna completes the parent, clears transient flags, awards nine XP and unlocks the next record-dependent letter.");
                    var restored = JsonUtility.FromJson<OriginalQuestState>(JsonUtility.ToJson(data.OriginalQuests));
                    check(restored.HasRecord(1) && restored.HasRecord(701) && restored.Missions.Count == 0,
                        "Native chain records survive serialization without retaining cleared mission flags.");
                    data.OriginalQuests = new OriginalQuestState(); data.OriginalQuests.SetRecord(58);
                    controller.Level = 50; controller.Job = 0; inventory.InitializeFromData(new List<InventoryItemData>());
                    var peter = UnityEngine.Object.FindObjectsByType<NPCInteractable>(FindObjectsSortMode.None).Single(n => n.NpcName == "Castle Guard - Peter");
                    var william = UnityEngine.Object.FindObjectsByType<NPCInteractable>(FindObjectsSortMode.None).Single(n => n.NpcName == "General - William");
                    void Offer(int id, NPCInteractable npc) => SocialGameplaySmokeTest.Send(client, npc, "CmdQuestAction", w => { w.Write(id); w.Write(false); });
                    void Accept(int id) => SocialGameplaySmokeTest.Send(client, quests, "CmdAcceptQuest", w => w.Write(id));
                    Talk(peter); yield return Pump(tick); Offer(1000067, peter); yield return Pump(tick); Reply(); Accept(1000067);
                    yield return AwaitSave(tick, quests, fixture, 7);
                    check(quests.HasActiveQuest(1000067) && !quests.CanResultOriginal(1000067), "Existing Peter NPC accepts the original twelve-Piglet episode only after its previous mission record.");
                    Reply(); fixture.Reply("{\"success\":true,\"revision\":" + (data.SaveRevision + 2) + ",\"questStateVersion\":1}");
                    for (int i = 0; i < 14; i++) quests.ServerNotifyKill("Piglet", 237);
                    yield return AwaitSave(tick, quests, fixture, 9);
                    body = JsonUtility.FromJson<Request>(fixture.Bodies.ToArray().Last());
                    check(quests.HasFlag(62, 21) && quests.GetObjectiveProgress(1000067, 0) == 12 && body.Character.OriginalQuests.HasFlag(62, 21),
                        "Authoritative numeric kills coalesce to durable original flag counters and stop exactly at twelve.");
                    yield return Pump(tick);
                    var clientStateRoot = new GameObject("Original readonly client state", typeof(NetworkIdentity), typeof(PlayerQuests));
                    try
                    {
                        typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(clientStateRoot.GetComponent<NetworkIdentity>(), null);
                        var remote = clientStateRoot.GetComponent<PlayerQuests>(); remote.ApplyQuestState(lastPayload);
                        check(remote.HasRecord(58) && remote.HasFlag(62, 21) && remote.GetObjectiveProgress(1000067, 0) == 12,
                            "Actual owner-targeted KCP payload reconstructs independent original records and flag counters on a remote client.");
                    }
                    finally { UnityEngine.Object.Destroy(clientStateRoot); }
                    Reply(); Result(1000067, peter); yield return AwaitSave(tick, quests, fixture, 10);
                    check(quests.HasRecord(62) && !quests.HasFlag(62, 21) && controller.Exp == 315 && quests.CanBeginOriginal(1000068),
                        "Original kill episode awards 300 XP, records its completion and unlocks the next story letter.");
                    Offer(1000068, peter); yield return Pump(tick); Reply(); Accept(1000068); yield return AwaitSave(tick, quests, fixture, 11);
                    check(inventory.GetQuestMaterialCount(4120) == 1 && quests.HasActiveQuest(1000068), "Peter's next story episode durably gives the original Letter to General William.");
                    Talk(william); yield return Pump(tick); Reply(); Result(1000069, william); yield return AwaitSave(tick, quests, fixture, 12);
                    check(quests.HasRecord(63) && inventory.GetQuestMaterialCount(4120) == 0 && quests.CanBeginOriginal(1000070) && controller.Exp == 415,
                        "Existing General William consumes the story letter, gives 100 XP and unlocks the item-collection episode.");
                    Offer(1000070, william); yield return Pump(tick); Reply(); Accept(1000070); yield return AwaitSave(tick, quests, fixture, 13);
                    Reply(); inventory.AddItem(1678, 3); yield return AwaitSave(tick, quests, fixture, 14);
                    body = JsonUtility.FromJson<Request>(fixture.Bodies.ToArray().Last());
                    check(quests.HasFlag(64, 12) && body.Character.OriginalQuests.HasFlag(64, 12) && quests.CanResultOriginal(1000070),
                        "Original item-acquisition trigger persists all three consecutive flags together with the collected items.");
                    Reply(); Result(1000070, william); yield return AwaitSave(tick, quests, fixture, 15);
                    check(quests.HasRecord(64) && inventory.GetQuestMaterialCount(1678) == 0 && controller.Exp == 715,
                        "Original collection delivery consumes exactly three items and awards its original 300 XP.");
                    Offer(1000071, william); yield return Pump(tick); Reply(); Accept(1000071); yield return AwaitSave(tick, quests, fixture, 16);
                    check(inventory.GetQuestMaterialCount(3953) == 1 && !quests.HasRecord(65), "The completed collection episode unlocks William's original Courage Certificate delivery.");
                    Talk(peter); yield return Pump(tick); Reply(); Result(1000072, peter); yield return AwaitSave(tick, quests, fixture, 17);
                    body = JsonUtility.FromJson<Request>(fixture.Bodies.ToArray().Last());
                    check(controller.Job == 1 && data.Job == 1 && body.Character.Job == 1 && quests.HasRecord(65)
                        && inventory.GetQuestMaterialCount(3953) == 0 && inventory.GetQuestMaterialCount(1) == 1
                        && inventory.GetQuestMaterialCount(3164) == 1 && controller.Exp == 815,
                        "Actual original Swordsman class-change episode atomically consumes its certificate, changes profession and grants XP plus multiple items.");
                    check(data.SaveRevision == oldRevision + 15, "Rejected operations and retry receipts cannot advance the native chain save revision twice.");
                }
                finally
                {
                    rootField.SetValue(null, oldRoot); data.OriginalQuests = oldState; data.QuestStateVersion = oldVersion;
                    data.SaveRevision = oldRevision; controller.Job = data.Job = oldJob; controller.Level = oldLevel;
                    controller.Exp = oldExp; controller.Gold = oldGold; inventory.InitializeFromData(oldInventory); player.transform.position = oldPosition;
                    if (service != null) UnityEngine.Object.Destroy(service);
                    if (sennaFixture != null) NetworkServer.Destroy(sennaFixture);
                }
            }
        }
    }
}
#endif
