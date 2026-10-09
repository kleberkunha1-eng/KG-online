#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using TOP.Data;
using TOP.NPC;
using TOP.Player;
using TOP.Services;
using UnityEngine;

namespace TOP.Testing
{
    public static class QuestPersistenceSmokeTest
    {
        [Serializable]
        sealed class RewardRequest { public CharacterData Character; }

        sealed class ApiFixture : IDisposable
        {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly ConcurrentQueue<string> replies = new ConcurrentQueue<string>();
            readonly Task serving;
            volatile bool stopped;
            int requests;
            public int Requests => Volatile.Read(ref requests);
            public string Root { get; }
            public readonly ConcurrentQueue<string> Bodies = new ConcurrentQueue<string>();
            public readonly ConcurrentQueue<string> RequestLines = new ConcurrentQueue<string>();

            public ApiFixture()
            {
                listener.Start();
                Root = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                serving = Task.Run(Serve);
            }

            public void Reply(string json) => replies.Enqueue(json);

            async Task Serve()
            {
                while (!stopped)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(); }
                    catch (ObjectDisposedException) when (stopped) { break; }
                    catch (SocketException) when (stopped) { break; }
                    using (client)
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    {
                        string first = await reader.ReadLineAsync();
                        if (first == null) throw new IOException("Quest fixture received an empty HTTP request.");
                        int contentLength = 0;
                        string header;
                        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
                            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                contentLength = int.Parse(header.Substring("Content-Length:".Length).Trim());
                        var body = new char[contentLength];
                        int received = 0;
                        while (received < body.Length)
                        {
                            int count = await reader.ReadAsync(body, received, body.Length - received);
                            if (count == 0) throw new IOException("Quest fixture received an incomplete HTTP body.");
                            received += count;
                        }
                        Bodies.Enqueue(new string(body));
                        RequestLines.Enqueue(first);
                        Interlocked.Increment(ref requests);
                        if (!replies.TryDequeue(out string json))
                            throw new InvalidOperationException("Unexpected quest fixture request: " + first);
                        await Task.Delay(350);
                        byte[] data = Encoding.UTF8.GetBytes(json);
                        byte[] headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: "
                            + data.Length + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(headers, 0, headers.Length);
                        await stream.WriteAsync(data, 0, data.Length);
                    }
                }
            }

            public void Dispose()
            {
                stopped = true;
                listener.Stop();
                serving.GetAwaiter().GetResult();
                if (!replies.IsEmpty) throw new InvalidOperationException("Quest fixture has unconsumed responses.");
            }
        }

        static IEnumerator Pump(Action tick, float seconds = .8f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { tick(); yield return null; }
        }

        public static IEnumerator Run(PlayerQuests quests, Action tick, Action<bool, string> check)
        {
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var rootField = typeof(ApiConfig).GetField("root", BindingFlags.NonPublic | BindingFlags.Static);
            var refresh = typeof(PlayerQuests).GetMethod("RefreshQuestsInternal", fields);
            string oldRoot = (string)rootField.GetValue(null);
            GameObject service = null;
            using (var fixture = new ApiFixture())
            {
                try
                {
                    rootField.SetValue(null, fixture.Root);
                    if (DatabaseService.Instance == null)
                    {
                        service = new GameObject("Isolated Quest API");
                        service.AddComponent<DatabaseService>();
                    }
                    fixture.Reply("{\"success\":true,\"active\":[{\"questId\":704,\"progress\":2}],\"completed\":[500]}");
                    refresh.Invoke(quests, null);
                    refresh.Invoke(quests, null);
                    yield return Pump(tick);
                    check(fixture.Requests == 1 && quests.HasActiveQuest(704) && quests.CompletedQuests.Count == 1,
                        "Quest refresh loads persisted state and duplicate refresh sends only one isolated HTTP request.");

                    fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_FAILURE\"}");
                    refresh.Invoke(quests, null);
                    yield return Pump(tick);
                    check(quests.HasActiveQuest(704) && quests.CompletedQuests.Count == 1,
                        "Rejected quest refresh preserves both active progress and completed records.");

                    fixture.Reply("{\"success\":true,\"active\":[{\"questId\":704,\"progress\":2}],\"completed\":[500]}");
                    refresh.Invoke(quests, null);
                    yield return Pump(tick, .1f);
                    fixture.Reply("{\"success\":true}");
                    quests.ServerNotifyKill("rat");
                    yield return Pump(tick, 1.2f);
                    check(quests.ActiveQuests.Find(q => q.questId == 704).progress == 3,
                        "Delayed refresh cannot overwrite newer kill progress while its HTTP response is in flight.");

                    int oldRequired = QuestTable.All[704].Objective.Required;
                    try
                    {
                        QuestTable.All[704].Objective.Required = 6;
                        int progressRequests = fixture.Requests;
                        for (int attempt = 0; attempt < 3; attempt++)
                            fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_PROGRESS_FAILURE\"}");
                        quests.ServerNotifyKill("rat");
                        yield return Pump(tick, 1.9f);
                        var progressBodies = fixture.Bodies.ToArray();
                        check(fixture.Requests == progressRequests + 3 && quests.HasUnsavedProgress
                            && quests.ActiveQuests.Find(q => q.questId == 704).progress == 4
                            && progressBodies[progressBodies.Length - 1] == progressBodies[progressBodies.Length - 2]
                            && progressBodies[progressBodies.Length - 2] == progressBodies[progressBodies.Length - 3],
                            "Rejected kill progress retries the same monotonic value three times and retains the unconfirmed count locally.");
                        quests.ApplyQuestState("|");
                        check(quests.HasActiveQuest(704) && quests.HasUnsavedProgress
                            && quests.ActiveQuests.Find(q => q.questId == 704).progress == 4 && quests.CompletedQuests.Contains(500),
                            "Host-targeted quest updates do not rebuild or clear the authoritative server state and unconfirmed progress.");

                        progressRequests = fixture.Requests;
                        for (int attempt = 0; attempt < 3; attempt++)
                            fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_PROGRESS_FAILURE\"}");
                        refresh.Invoke(quests, null);
                        refresh.Invoke(quests, null);
                        yield return Pump(tick, 1.9f);
                        check(fixture.Requests == progressRequests + 3 && quests.HasUnsavedProgress
                            && fixture.RequestLines.ToArray().Last().Contains("/progress ")
                            && quests.ActiveQuests.Find(q => q.questId == 704).progress == 4
                            && quests.CompletedQuests.Contains(500),
                            "Refresh retries dirty progress first and never fetches an obsolete quest snapshot when confirmation still fails.");

                        progressRequests = fixture.Requests;
                        fixture.Reply("{\"success\":true}");
                        fixture.Reply("{\"success\":true,\"active\":[{\"questId\":704,\"progress\":4}],\"completed\":[500]}");
                        refresh.Invoke(quests, null);
                        yield return Pump(tick, 1.2f);
                        check(fixture.Requests == progressRequests + 2 && !quests.HasUnsavedProgress
                            && quests.ActiveQuests.Find(q => q.questId == 704).progress == 4
                            && fixture.RequestLines.ToArray().Last().StartsWith("GET ", StringComparison.Ordinal),
                            "After recovery, refresh confirms retained progress before reloading the authoritative quest list.");

                        progressRequests = fixture.Requests;
                        fixture.Reply("{\"success\":true}");
                        fixture.Reply("{\"success\":true}");
                        quests.ServerNotifyKill("rat");
                        yield return Pump(tick, .1f);
                        quests.ServerNotifyKill("rat");
                        quests.ServerAbandonQuest(704);
                        yield return Pump(tick, 1.2f);
                        progressBodies = fixture.Bodies.ToArray();
                        check(fixture.Requests == progressRequests + 2 && !quests.HasUnsavedProgress
                            && quests.ActiveQuests.Find(q => q.questId == 704).progress == 6
                            && progressBodies[progressBodies.Length - 2].Contains("\"progress\":5")
                            && progressBodies.Last().Contains("\"progress\":6"),
                            "Kills during an in-flight save coalesce into the latest count without overlapping writes or abandonment.");
                    }
                    finally { QuestTable.All[704].Objective.Required = oldRequired; }

                    quests.ServerOfferQuest(1);
                    fixture.Reply("{\"success\":true}");
                    int before = fixture.Requests;
                    quests.ServerAcceptQuest(1);
                    quests.ServerAcceptQuest(1);
                    refresh.Invoke(quests, null);
                    quests.ServerAbandonQuest(1);
                    yield return Pump(tick);
                    check(fixture.Requests == before + 1 && quests.ActiveQuests.FindAll(q => q.questId == 1).Count == 1,
                        "Repeated acceptance and overlapping refresh/abandon cannot duplicate the quest or its API request.");

                    fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_FAILURE\"}");
                    quests.ServerAbandonQuest(1);
                    yield return Pump(tick);
                    check(quests.HasActiveQuest(1), "Failed abandonment retains the accepted quest.");

                    fixture.Reply("{\"success\":true}");
                    quests.ServerNotifyTalk("town1_guide");
                    yield return Pump(tick);
                    check(quests.ActiveQuests.Find(q => q.questId == 1).progress == 1,
                        "Talking to the configured quest target advances its persisted objective.");

                    var controller = quests.GetComponent<PlayerController>();
                    ulong exp5 = PkoTables.ExpToNextLevel(5), exp6 = PkoTables.ExpToNextLevel(6);
                    var rewardSnapshot = new CharacterData { Level = 5, Exp = 0, StatPoints = 3, SkillPoints = 2,
                        CurrentHp = 1, MaxHp = 100, CurrentMp = 1, MaxMp = 50, CurrentSp = 1, MaxSp = 100 };
                    PlayerController.ApplyExperience(rewardSnapshot, exp5 + exp6 + 1);
                    check(rewardSnapshot.Level == 7 && rewardSnapshot.Exp == 1 && rewardSnapshot.StatPoints == 13
                        && rewardSnapshot.SkillPoints == 4 && rewardSnapshot.CurrentHp == 100
                        && rewardSnapshot.CurrentMp == 50 && rewardSnapshot.CurrentSp == 100,
                        "Durable reward snapshots share the exact multi-level XP, points and refill rules with live level-up.");
                    ulong gold = controller.Gold;
                    fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_FAILURE\"}");
                    quests.ServerTurnInQuest(1);
                    yield return Pump(tick);
                    check(quests.HasActiveQuest(1) && controller.Gold == gold,
                        "Rejected talk-quest completion retains the quest and grants no gold.");

                    fixture.Reply("");
                    fixture.Reply("{\"success\":true,\"revision\":1}");
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(1);
                    quests.ServerTurnInQuest(1);
                    quests.ServerAbandonQuest(1);
                    refresh.Invoke(quests, null);
                    yield return Pump(tick, 1.4f);
                    check(fixture.Requests == before + 2 && !quests.HasActiveQuest(1)
                        && controller.Gold == gold + 50 && quests.CompletedQuests.Count == 2,
                        "Repeated talk-quest completion grants the exact reward once and blocks overlapping abandonment/refresh.");
                    var requests = fixture.Bodies.ToArray();
                    check(requests[requests.Length - 1] == requests[requests.Length - 2]
                        && fixture.RequestLines.ToArray().Last().StartsWith("PUT /api/game/characters/", StringComparison.Ordinal)
                        && requests.Last().Contains("\"QuestId\":1") && controller.GetCharacterData().SaveRevision == 1,
                        "A lost completion response retries the identical atomic character/quest operation and acknowledges its revision once.");

                    fixture.Reply("{\"success\":true}");
                    before = fixture.Requests;
                    quests.ServerAbandonQuest(704);
                    quests.ServerAbandonQuest(704);
                    refresh.Invoke(quests, null);
                    yield return Pump(tick);
                    check(fixture.Requests == before + 1 && !quests.HasActiveQuest(704),
                        "Successful abandonment removes the quest once and suppresses overlapping refresh.");

                    fixture.Reply("{\"success\":true,\"active\":[],\"completed\":[]}");
                    refresh.Invoke(quests, null);
                    yield return Pump(tick);
                    check(quests.ActiveQuests.Count == 0 && quests.CompletedQuests.Count == 0,
                        "An explicit successful empty quest response clears state normally.");
                    yield return CheckCollectDelivery(quests, fixture, tick, check);
                    var uncertainData = new CharacterData { Id = 990003, AccountId = 9, Level = 1, Gold = 13 };
                    before = fixture.Requests;
                    uncertainData.Gold = DatabaseService.MaxExactApiInteger + 1;
                    var invalidSave = DatabaseService.Instance.SaveCharacterAsync(uncertainData, "isolated-token");
                    yield return Pump(tick, .1f);
                    check(invalidSave.IsCompleted && !invalidSave.Result && fixture.Requests == before
                        && !DatabaseService.Instance.RequiresCharacterReload(uncertainData.Id),
                        "Gold outside the API exact numeric range is explicitly rejected before HTTP rather than rounded or saved.");
                    uncertainData.Gold = 13;
                    fixture.Reply("");
                    fixture.Reply("");
                    fixture.Reply("");
                    var uncertain = DatabaseService.Instance.PersistCharacterAsync(uncertainData.Id, () => uncertainData, "isolated-token", 0);
                    yield return Pump(tick, 1.5f);
                    requests = fixture.Bodies.ToArray();
                    check(uncertain.IsCompleted && !uncertain.Result.success && uncertain.Result.error == "RELOAD_REQUIRED"
                        && fixture.Requests == before + 3 && requests[requests.Length - 1] == requests[requests.Length - 2]
                        && requests[requests.Length - 2] == requests[requests.Length - 3]
                        && DatabaseService.Instance.RequiresCharacterReload(uncertainData.Id),
                        "Three lost save acknowledgements block the character and reuse one operation ID/payload on every attempt.");
                    before = fixture.Requests;
                    var blocked = DatabaseService.Instance.SaveCharacterAsync(uncertainData, "isolated-token");
                    yield return Pump(tick, .1f);
                    check(blocked.IsCompleted && !blocked.Result && fixture.Requests == before,
                        "An uncertain character cannot overwrite a possibly committed reward with a new save.");
                    fixture.Reply("{\"success\":true,\"Character\":{\"Id\":990003,\"AccountId\":9,\"SaveRevision\":1,\"Gold\":13},\"Inventory\":[],\"Skills\":[]}");
                    var reloaded = DatabaseService.Instance.LoadCharacterAsync(uncertainData.Id, 9, "isolated-token");
                    yield return Pump(tick);
                    check(reloaded.IsCompleted && reloaded.Result != null && reloaded.Result.SaveRevision == 1
                        && reloaded.Result.Gold == 13 && !DatabaseService.Instance.RequiresCharacterReload(uncertainData.Id),
                        "Authoritative character reload restores its committed revision and releases the uncertain-save block.");
                }
                finally
                {
                    rootField.SetValue(null, oldRoot);
                    if (service != null) UnityEngine.Object.Destroy(service);
                }
            }

            static IEnumerator CheckCollectDelivery(PlayerQuests quests, ApiFixture fixture, Action tick, Action<bool, string> check)
            {
                const int questId = 990001;
                const int multiQuestId = 990002;
                const int rivalQuestId = 990004;
                const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
                var inventory = quests.GetComponent<PlayerInventory>();
                var npc = quests.GetComponent<PlayerMovement>().ActiveNpc;
                var available = typeof(NPCInteractable).GetField("availableQuests", fields);
                var receives = typeof(NPCInteractable).GetField("completesQuests", fields);
                var oldAvailable = available.GetValue(npc);
                var oldReceives = receives.GetValue(npc);
                var oldInventory = inventory.GetInventoryData();
                var material = PkoTables.Items.Values.First(i => i.Stack >= 20);
                var reward = PkoTables.Items.Values.First(i => i.Id != material.Id && i.Stack >= 1);
                var secondMaterial = PkoTables.Items.Values.First(i => i.Id != material.Id && i.Id != reward.Id && i.Stack >= 5);
                var controller = quests.GetComponent<PlayerController>();
                int oldLevel = controller.Level;
                if (QuestTable.All.ContainsKey(questId) || QuestTable.All.ContainsKey(multiQuestId) || QuestTable.All.ContainsKey(rivalQuestId))
                    throw new InvalidOperationException("Synthetic quest ID already exists.");
                QuestTable.All.Add(questId, new QuestDef { Id = questId, Name = "Synthetic collection", RewardItemId = reward.Id,
                    RewardItemQty = 1, Objective = new QuestObjective { Type = QuestObjectiveType.Collect, ItemId = material.Id, Required = 10 } });
                var multi = new QuestDef { Id = multiQuestId, Name = "Synthetic multiple materials", RequiredLevel = 5, MaximumLevel = 6,
                    RewardGold = 7, RewardExp = 40, RewardExpMaximumExclusive = 70, RewardItemId = reward.Id, RewardItemQty = 1,
                    RequiredCompletedQuests = new[] { questId }, ExcludedActiveQuests = new[] { 704 },
                    Objective = new QuestObjective { Type = QuestObjectiveType.Collect, CollectionItems = new[]
                    {
                        new QuestCollectionItem { ItemId = material.Id, Quantity = 10 },
                        new QuestCollectionItem { ItemId = secondMaterial.Id, Quantity = 3 }
                    } } };
                QuestTable.All.Add(multiQuestId, multi);
                QuestTable.All.Add(rivalQuestId, new QuestDef { Id = rivalQuestId, Name = "Synthetic excluded quest",
                    ExcludedActiveQuests = new[] { questId }, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = npc.NpcId } });
                try
                {
                    available.SetValue(npc, new[] { questId.ToString(), multiQuestId.ToString(), rivalQuestId.ToString() });
                    receives.SetValue(npc, new[] { questId.ToString(), multiQuestId.ToString() });
                    quests.ServerOfferQuest(questId);
                    quests.ServerOfferQuest(rivalQuestId);
                    int initialRequests = fixture.Requests;
                    fixture.Reply("{\"success\":true}");
                    quests.ServerAcceptQuest(questId);
                    quests.ServerAcceptQuest(rivalQuestId);
                    yield return Pump(tick);
                    check(fixture.Requests == initialRequests + 1 && quests.HasActiveQuest(questId) && !quests.HasActiveQuest(rivalQuestId)
                        && quests.GetOfferableQuestsFor(new[] { rivalQuestId }).Count == 0,
                        "Concurrent acceptance cannot bypass an excluded active quest while its first acceptance is awaiting HTTP.");
                    var items = new List<InventoryItemData>
                    {
                        new InventoryItemData { SlotIndex = 0, ItemId = material.Id, Quantity = 6, UniqueItemId = "fixture-a", Durability = 11 },
                        new InventoryItemData { SlotIndex = 1, ItemId = material.Id, Quantity = 7, UniqueItemId = "fixture-b", Durability = 12 },
                        new InventoryItemData { SlotIndex = 2, ItemId = material.Id, Quantity = 20, UniqueItemId = "fixture-refined", RefineLevel = 3, GemSlot1 = 10 },
                        new InventoryItemData { SlotIndex = 3, ItemId = material.Id, Quantity = 20, UniqueItemId = "fixture-locked", IsLocked = true, OwnerCharacterId = 99 }
                    };
                    inventory.InitializeFromData(items);
                    check(inventory.PrepareQuestTransaction(material.Id, -1, reward.Id, 1, out _) == null
                        && inventory.PrepareQuestTransaction(material.Id, 10, int.MaxValue, 1, out _) == null
                        && !inventory.HasQuestTransaction && inventory.GetSlot(0)?.Quantity == 6,
                        "Invalid material quantity or missing exact reward ID cannot reserve or mutate inventory.");
                    int before = fixture.Requests;
                    fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_FAILURE\"}");
                    quests.ServerTurnInQuest(questId);
                    check(inventory.HasQuestTransaction && inventory.GetItemCount(material.Id) == 53,
                        "Collection reserves inventory without consuming any materials before API acknowledgement.");
                    check(!inventory.MoveItemOnServer(0, 5) && !inventory.AddItem(reward.Id, 1)
                        && !inventory.RemoveItemById(material.Id, 1)
                        && !inventory.DropItemOnServer(0, 1, quests.transform.position),
                        "Moving, adding, removing and dropping items are rejected while collection delivery is pending.");
                    inventory.CmdDeleteItem(0);
                    check(inventory.GetSlot(0)?.Quantity == 6, "Deleting a pending mission material does not alter the reserved inventory.");
                    ulong pendingGold = quests.GetComponent<PlayerController>().Gold;
                    npc.CmdBuyItem(reward.Id, 1, quests.connectionToClient);
                    npc.CmdSellItem(0, 1, quests.connectionToClient);
                    npc.CmdCraftRecipe(reward.Id, quests.connectionToClient);
                    quests.GetComponent<PlayerForge>().CmdForgeItem(0);
                    quests.GetComponent<PlayerMail>().CmdClaimMail(1);
                    inventory.InitializeFromData(new List<InventoryItemData>());
                    check(quests.GetComponent<PlayerController>().Gold == pendingGold
                        && !quests.GetComponent<PlayerMail>().InventoryRequestPending && inventory.GetSlot(0)?.Quantity == 6,
                        "NPC purchase/sale/recipe, forge, mail claim and snapshot replacement cannot change a pending quest inventory or charge gold.");
                    yield return Pump(tick);
                    check(fixture.Requests == before + 1 && quests.HasActiveQuest(questId) && !inventory.HasQuestTransaction
                        && inventory.GetSlot(0)?.Quantity == 6 && inventory.GetSlot(1)?.Quantity == 7
                        && inventory.GetInventoryData()[0].UniqueItemId == "fixture-a",
                        "Rejected collection completion releases inventory and preserves exact slots, quantities and instance IDs.");

                    var full = Enumerable.Range(0, inventory.totalSlots).Select(slot =>
                        new InventoryItemData { SlotIndex = (ushort)slot, ItemId = material.Id, Quantity = 20 }).ToList();
                    inventory.InitializeFromData(full);
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(questId);
                    yield return Pump(tick, .2f);
                    check(fixture.Requests == before && !inventory.HasQuestTransaction && inventory.GetItemCount(material.Id) == 800,
                        "Full inventory with no freed material slot rejects the reward before any API call or consumption.");
                    inventory.InitializeFromData(new List<InventoryItemData> { items[2], items[3] });
                    quests.ServerTurnInQuest(questId);
                    yield return Pump(tick, .2f);
                    check(fixture.Requests == before && quests.HasActiveQuest(questId) && !inventory.HasQuestTransaction,
                        "Refined, socketed and locked materials cannot satisfy consumable quest requirements.");
                    check(!quests.CanTurnIn(questId), "Quest completion availability uses the same protected-material rules as delivery.");
                    check(inventory.GetQuestMaterialCount(material.Id) == 0
                        && TOP.UI.QuestLogUI.CollectionLabel(inventory, material.Id, "", 10).EndsWith(" 0/10"),
                        "Protected materials do not produce false collection progress in the diary.");
                    check(PlayerInventory.IsUsableQuestMaterial(false, false, 0, false)
                        && !PlayerInventory.IsUsableQuestMaterial(true, false, 0, false)
                        && !PlayerInventory.IsUsableQuestMaterial(false, true, 0, false)
                        && !PlayerInventory.IsUsableQuestMaterial(false, false, 1, false)
                        && !PlayerInventory.IsUsableQuestMaterial(false, false, 0, true),
                        "Each equipment, lock, refinement and gem restriction independently excludes quest material.");

                    inventory.InitializeFromData(items);
                    check(inventory.GetQuestMaterialCount(material.Id) == 13
                        && TOP.UI.QuestLogUI.CollectionLabel(inventory, material.Id, "", 10).EndsWith(" 13/10"),
                        "Collection diary displays usable quantities across slots without counting refined or locked material.");
                    var clientInventoryObject = new GameObject("Isolated collection client");
                    try
                    {
                        var clientInventory = clientInventoryObject.AddComponent<PlayerInventory>();
                        string payload = (string)typeof(PlayerInventory).GetField("_inventoryData",
                            BindingFlags.NonPublic | BindingFlags.Instance).GetValue(inventory);
                        var deserialize = typeof(PlayerInventory).GetMethod("DeserializeInventory", BindingFlags.NonPublic | BindingFlags.Instance);
                        deserialize.Invoke(clientInventory, new object[] { payload });
                        check(clientInventory.GetSlot(3).IsLocked && clientInventory.GetQuestMaterialCount(material.Id) == 13,
                            "Remote-client inventory retains locks and displays the same usable collection count as the server.");
                        deserialize.Invoke(clientInventory, new object[] { "0:" + material.Id + ":3:0:0:-1:-1:-1;1:"
                            + material.Id + ":4:0:0:-1:-1:-1:12" });
                        check(clientInventory.GetQuestMaterialCount(material.Id) == 7 && clientInventory.GetSlot(1).Durability == 12,
                            "Legacy eight/nine-field inventory messages remain readable after lock synchronization.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(clientInventoryObject); }
                    fixture.Reply("{\"success\":true,\"revision\":2}");
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(questId);
                    quests.ServerTurnInQuest(questId);
                    yield return Pump(tick);
                    var result = inventory.GetInventoryData();
                    check(fixture.Requests == before + 1 && !quests.HasActiveQuest(questId) && !inventory.HasQuestTransaction
                        && inventory.GetSlot(0)?.ItemId == reward.Id && inventory.GetSlot(0)?.Quantity == 1
                        && inventory.GetSlot(1)?.Quantity == 3,
                        "Confirmed collection consumes exactly ten materials across slots and grants its exact ID reward once.");
                    check(quests.GetComponent<PlayerController>().GetCharacterData().SaveRevision == 2
                        && fixture.Bodies.ToArray().Last().Contains("\"QuestId\":" + questId),
                        "Collection advances the character revision and uses the same durable save endpoint as its rewards.");
                    check(result.Single(i => i.SlotIndex == 1).UniqueItemId == "fixture-b"
                        && result.Single(i => i.SlotIndex == 1).Durability == 12
                        && inventory.GetSlot(2)?.RefineLevel == 3 && inventory.GetSlot(2)?.Gems[0] == 10
                        && result.Single(i => i.SlotIndex == 3).IsLocked
                        && result.Single(i => i.SlotIndex == 3).OwnerCharacterId == 99,
                        "Collection commit preserves remaining item IDs, durability, gems, refinement and ownership locks.");

                    check(!multi.CanAcceptAtLevel(4) && multi.CanAcceptAtLevel(5) && multi.CanAcceptAtLevel(6)
                        && !multi.CanAcceptAtLevel(7), "Quest acceptance uses inclusive original-style minimum and maximum level bands.");
                    var conditions = new QuestDef { PrerequisiteId = 1, RequiredCompletedQuests = new[] { 701, 702 },
                        ExcludedActiveQuests = new[] { 739 }, ExcludedCompletedQuests = new[] { 740 } };
                    var completed = new HashSet<int> { 1, 701, 702 };
                    var active = new HashSet<int>();
                    check(conditions.MeetsQuestConditions(active.Contains, completed.Contains),
                        "Legacy prerequisite and all required completion records combine without changing old quest IDs.");
                    completed.Remove(702);
                    bool missing = !conditions.MeetsQuestConditions(active.Contains, completed.Contains);
                    completed.Add(702);
                    active.Add(739);
                    bool exclusive = !conditions.MeetsQuestConditions(active.Contains, completed.Contains);
                    active.Clear();
                    completed.Add(740);
                    check(missing && exclusive && !conditions.MeetsQuestConditions(active.Contains, completed.Contains),
                        "Missing completion records, competing active quests and excluded completed records each reject acceptance.");
                    var randomState = UnityEngine.Random.state;
                    try
                    {
                        UnityEngine.Random.InitState(721);
                        var rolls = Enumerable.Range(0, 10000).Select(_ => multi.RollExperienceReward()).ToArray();
                        check(rolls.All(value => value >= 40 && value < 70) && rolls.Min() == 40 && rolls.Max() == 69,
                            "Original AddExp minimum is inclusive and maximum exclusive across ten thousand deterministic draws.");
                    }
                    finally { UnityEngine.Random.state = randomState; }
                    var fixedReward = new QuestDef { RewardExp = 70, RewardExpMaximumExclusive = 40 };
                    check(fixedReward.RollExperienceReward() == 70 && new QuestDef { RewardExp = 40 }.RollExperienceReward() == 40
                        && !new QuestDef { RewardExp = -1 }.HasValidExperienceReward
                        && TOP.UI.QuestLogUI.ExperienceLabel(multi) == "40-69"
                        && TOP.UI.QuestLogUI.ExperienceLabel(fixedReward) == "70",
                        "Fixed legacy XP and inverted/equal native ranges keep the minimum; quest log shows the actual attainable range.");
                    check(TOP.UI.QuestLogUI.RowHeight(multi) == 114
                        && TOP.UI.QuestLogUI.RowHeight(QuestTable.All[questId]) == 96,
                        "Quest log allocates one objective line per material without overlapping rewards or delivery buttons.");
                    controller.Level = 5;
                    quests.ServerOfferQuest(multiQuestId);
                    before = fixture.Requests;
                    controller.Level = 7;
                    quests.ServerAcceptQuest(multiQuestId);
                    check(quests.GetOfferableQuestsFor(new[] { multiQuestId }).Count == 0,
                        "An NPC cannot offer a quest above its configured maximum level.");
                    controller.Level = 4;
                    quests.ServerAcceptQuest(multiQuestId);
                    check(fixture.Requests == before && !quests.HasActiveQuest(multiQuestId),
                        "Acceptance outside either level boundary makes no persistence request.");
                    controller.Level = 5;
                    multi.RequiredCompletedQuests = new[] { questId, 999999 };
                    check(quests.GetOfferableQuestsFor(new[] { multiQuestId }).Count == 0,
                        "NPC offers are suppressed while any required quest record is missing.");
                    quests.ServerAcceptQuest(multiQuestId);
                    check(fixture.Requests == before && !quests.HasActiveQuest(multiQuestId),
                        "An earlier NPC offer cannot bypass changed completion-record requirements.");
                    multi.RequiredCompletedQuests = new[] { questId };
                    fixture.Reply("{\"success\":true}");
                    quests.ServerAcceptQuest(multiQuestId);
                    yield return Pump(tick);
                    check(quests.HasActiveQuest(multiQuestId), "A previously offered multi-material quest accepts at the exact minimum level.");

                    var multiItems = new List<InventoryItemData>
                    {
                        new InventoryItemData { SlotIndex = 0, ItemId = material.Id, Quantity = 10, UniqueItemId = "multi-primary" },
                        new InventoryItemData { SlotIndex = 1, ItemId = secondMaterial.Id, Quantity = 5, UniqueItemId = "multi-secondary", Durability = 13 }
                    };
                    inventory.InitializeFromData(new List<InventoryItemData>
                    { new InventoryItemData { SlotIndex = 0, ItemId = material.Id, Quantity = 10 } });
                    string generatedId = inventory.GetInventoryData()[0].UniqueItemId;
                    check(Guid.TryParse(generatedId, out _) && inventory.GetInventoryData()[0].UniqueItemId == generatedId,
                        "New inventory instances retain one valid UUID across repeated persistence snapshots.");
                    inventory.InitializeFromData(new List<InventoryItemData> { multiItems[0] });
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(multiQuestId);
                    yield return Pump(tick, .2f);
                    check(!quests.CanTurnIn(multiQuestId) && fixture.Requests == before && !inventory.HasQuestTransaction
                        && inventory.GetItemCount(material.Id) == 10,
                        "A missing secondary collection material cannot consume the primary material or call the API.");
                    check(inventory.PrepareQuestTransaction(new[]
                    {
                        new QuestCollectionItem { ItemId = material.Id, Quantity = 1 },
                        new QuestCollectionItem { ItemId = material.Id, Quantity = 1 }
                    }, 0, 0, out _) == null && !inventory.HasQuestTransaction,
                        "Duplicate material IDs in a quest definition are explicitly rejected without reserving inventory.");
                    inventory.InitializeFromData(multiItems);
                    ulong beforeGold = controller.Gold;
                    fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_FAILURE\"}");
                    quests.ServerTurnInQuest(multiQuestId);
                    yield return Pump(tick);
                    check(quests.HasActiveQuest(multiQuestId) && !inventory.HasQuestTransaction
                        && inventory.GetItemCount(material.Id) == 10 && inventory.GetItemCount(secondMaterial.Id) == 5
                        && controller.Gold == beforeGold,
                        "Rejected multi-material delivery preserves every material and grants no reward.");
                    controller.Level = 7;
                    check(quests.CanTurnIn(multiQuestId), "An already accepted quest remains deliverable after leaving its acceptance level band.");
                    ulong beforeExp = controller.Exp;
                    int beforeLevel = controller.Level, beforeStatPoints = controller.StatPoints, beforeSkillPoints = controller.SkillPoints;
                    fixture.Reply("");
                    fixture.Reply("{\"success\":true,\"revision\":3}");
                    quests.ServerTurnInQuest(multiQuestId);
                    yield return Pump(tick, 1.4f);
                    var bodies = fixture.Bodies.ToArray();
                    var persisted = JsonUtility.FromJson<RewardRequest>(bodies.Last()).Character;
                    bool matchesRolledExperience = Enumerable.Range(40, 30).Any(value =>
                    {
                        var expected = new CharacterData { Exp = beforeExp, Level = beforeLevel,
                            StatPoints = beforeStatPoints, SkillPoints = beforeSkillPoints };
                        PlayerController.ApplyExperience(expected, (ulong)value);
                        return expected.Exp == persisted.Exp && expected.Level == persisted.Level
                            && expected.StatPoints == persisted.StatPoints && expected.SkillPoints == persisted.SkillPoints;
                    });
                    check(bodies[bodies.Length - 1] == bodies[bodies.Length - 2] && matchesRolledExperience
                        && controller.Exp == persisted.Exp && controller.Level == persisted.Level
                        && controller.StatPoints == persisted.StatPoints && controller.SkillPoints == persisted.SkillPoints,
                        "A random quest reward is sampled once, retained through lost-response retries and applied identically to persisted/live XP.");
                    check(!quests.HasActiveQuest(multiQuestId) && controller.GetCharacterData().SaveRevision == 3
                        && controller.Gold == beforeGold + 7 && inventory.GetItemCount(material.Id) == 0
                        && inventory.GetItemCount(secondMaterial.Id) == 2 && inventory.GetItemCount(reward.Id) == 1
                        && inventory.GetInventoryData().Single(i => i.ItemId == secondMaterial.Id).UniqueItemId == "multi-secondary",
                        "Atomic delivery consumes each exact material quantity, preserves the remainder and grants gold/item rewards once.");

                    var mixed = new QuestDef { Id = rivalQuestId, Name = "Synthetic mixed objectives", RewardGold = 3,
                        Objective = new QuestObjective { Type = QuestObjectiveType.Kill, Target = "rat", Required = 2 },
                        AdditionalObjectives = new[]
                        {
                            new QuestObjective { Type = QuestObjectiveType.Kill, Target = "wolf", Required = 3 },
                            new QuestObjective { Type = QuestObjectiveType.Collect, ItemId = material.Id, Required = 4 },
                            new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = npc.NpcId }
                        } };
                    QuestTable.All[rivalQuestId] = mixed;
                    receives.SetValue(npc, new[] { questId.ToString(), multiQuestId.ToString(), rivalQuestId.ToString() });
                    check(mixed.HasValidObjectives && TOP.UI.QuestLogUI.RowHeight(mixed) == 150,
                        "Mixed kill/kill/collection/talk definitions allocate every objective line in the diary.");
                    check(!new QuestDef { Objective = mixed.Objective, AdditionalObjectives = new QuestObjective[16] }.HasValidObjectives
                        && !new QuestDef { Objective = mixed.Objective, AdditionalObjectives = new QuestObjective[] { null } }.HasValidObjectives,
                        "Null objectives and catalogs above the sixteen-objective persistence limit are rejected.");
                    quests.ServerOfferQuest(rivalQuestId);
                    fixture.Reply("{\"success\":true}");
                    quests.ServerAcceptQuest(rivalQuestId);
                    yield return Pump(tick);
                    inventory.InitializeFromData(new List<InventoryItemData>
                    { new InventoryItemData { SlotIndex = 0, ItemId = material.Id, Quantity = 4, UniqueItemId = "mixed-material" } });
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(rivalQuestId);
                    check(quests.HasActiveQuest(rivalQuestId) && !quests.CanTurnIn(rivalQuestId) && fixture.Requests == before,
                        "Collection inventory alone cannot bypass unfinished kill and talk objectives.");
                    // Reload independently persisted counters to exercise the real HTTP DTO before later events.
                    fixture.Reply("{\"success\":true,\"active\":[{\"questId\":" + rivalQuestId
                        + ",\"progress\":2,\"objectiveProgress\":[{\"objectiveIndex\":1,\"progress\":2}]}],\"completed\":[]}");
                    typeof(PlayerQuests).GetMethod("RefreshQuestsInternal", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(quests, null);
                    yield return Pump(tick);
                    check(quests.GetObjectiveProgress(rivalQuestId, 0) == 2 && quests.GetObjectiveProgress(rivalQuestId, 1) == 2
                        && quests.GetObjectiveProgress(rivalQuestId, 3) == 0 && !quests.CanTurnIn(rivalQuestId),
                        "Refresh restores independent objective counters without treating missing talk progress as complete.");
                    fixture.Reply("{\"success\":true}");
                    fixture.Reply("{\"success\":true}");
                    before = fixture.Requests;
                    quests.ServerNotifyKill("wolf");
                    quests.ServerNotifyTalk(npc.NpcId);
                    quests.ServerTurnInQuest(rivalQuestId);
                    yield return Pump(tick, 1.4f);
                    bodies = fixture.Bodies.ToArray();
                    var objectiveBodies = bodies.Skip(bodies.Length - 2).ToArray();
                    check(fixture.Requests == before + 2 && objectiveBodies.Any(body => body.Contains("\"objectiveIndex\":1"))
                        && objectiveBodies.Any(body => body.Contains("\"objectiveIndex\":3")) && !quests.HasUnsavedProgress
                        && quests.GetObjectiveProgress(rivalQuestId, 0) == 2
                        && quests.GetObjectiveProgress(rivalQuestId, 1) == 3 && quests.GetObjectiveProgress(rivalQuestId, 3) == 1,
                        "Kill and talk write separate objective indices; pending progress prevents premature mixed delivery.");
                    var clientQuestObject = new GameObject("Isolated mixed quest client");
                    try
                    {
                        clientQuestObject.SetActive(false);
                        clientQuestObject.AddComponent<Mirror.NetworkIdentity>();
                        var clientQuests = clientQuestObject.AddComponent<PlayerQuests>();
                        clientQuestObject.SetActive(true);
                        clientQuests.ApplyQuestState(rivalQuestId + ":2,3,0,1|500");
                        check(clientQuests.GetObjectiveProgress(rivalQuestId, 0) == 2
                            && clientQuests.GetObjectiveProgress(rivalQuestId, 1) == 3
                            && clientQuests.GetObjectiveProgress(rivalQuestId, 3) == 1 && clientQuests.CompletedQuests.Contains(500),
                            "Remote quest state retains all objective counters and independent completion records.");
                        clientQuests.ApplyQuestState(rivalQuestId + ":2|500");
                        check(clientQuests.GetObjectiveProgress(rivalQuestId, 0) == 2
                            && clientQuests.GetObjectiveProgress(rivalQuestId, 1) == 0,
                            "Legacy single-counter quest state remains compatible with the mixed-objective client.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(clientQuestObject); }
                    mixed.AdditionalObjectives[2] = new QuestObjective { Type = QuestObjectiveType.Collect, ItemId = material.Id, Required = 2 };
                    check(!quests.CanTurnIn(rivalQuestId),
                        "Repeated material IDs across distinct objectives require their combined quantity, not the same stack twice.");
                    var remainingMaterials = new Dictionary<int, long>();
                    TOP.UI.QuestLogUI.CollectionLabel(inventory, material.Id, "", 4, remainingMaterials);
                    check(TOP.UI.QuestLogUI.CollectionLabel(inventory, material.Id, "", 2, remainingMaterials).EndsWith(" 0/2"),
                        "Mixed quest diary allocates repeated material requirements without counting a single stack twice.");
                    inventory.InitializeFromData(new List<InventoryItemData>
                    { new InventoryItemData { SlotIndex = 0, ItemId = material.Id, Quantity = 7, UniqueItemId = "mixed-material" } });
                    check(quests.CanTurnIn(rivalQuestId), "All independent kill and combined collection requirements must pass before delivery.");
                    beforeGold = controller.Gold;
                    fixture.Reply("{\"success\":true,\"revision\":4}");
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(rivalQuestId);
                    quests.ServerTurnInQuest(rivalQuestId);
                    yield return Pump(tick);
                    check(fixture.Requests == before + 1 && !quests.HasActiveQuest(rivalQuestId)
                        && controller.Gold == beforeGold + 3 && inventory.GetQuestMaterialCount(material.Id) == 1
                        && controller.GetCharacterData().SaveRevision == 4,
                        "Mixed delivery atomically consumes summed materials and awards gold once after every objective is complete.");
                }
                finally
                {
                    available.SetValue(npc, oldAvailable);
                    receives.SetValue(npc, oldReceives);
                    QuestTable.All.Remove(questId);
                    QuestTable.All.Remove(multiQuestId);
                    QuestTable.All.Remove(rivalQuestId);
                    inventory.InitializeFromData(oldInventory);
                    controller.Level = oldLevel;
                }

            }

        }
    }
}
#endif
