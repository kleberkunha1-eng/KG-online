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
        sealed class ApiFixture : IDisposable
        {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly ConcurrentQueue<string> replies = new ConcurrentQueue<string>();
            readonly Task serving;
            volatile bool stopped;
            int requests;
            public int Requests => Volatile.Read(ref requests);
            public string Root { get; }

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
                    ulong gold = controller.Gold;
                    fixture.Reply("{\"success\":false,\"error\":\"FIXTURE_FAILURE\"}");
                    quests.ServerTurnInQuest(1);
                    yield return Pump(tick);
                    check(quests.HasActiveQuest(1) && controller.Gold == gold,
                        "Rejected talk-quest completion retains the quest and grants no gold.");

                    fixture.Reply("{\"success\":true}");
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(1);
                    quests.ServerTurnInQuest(1);
                    quests.ServerAbandonQuest(1);
                    refresh.Invoke(quests, null);
                    yield return Pump(tick);
                    check(fixture.Requests == before + 1 && !quests.HasActiveQuest(1)
                        && controller.Gold == gold + 50 && quests.CompletedQuests.Count == 2,
                        "Repeated talk-quest completion grants the exact reward once and blocks overlapping abandonment/refresh.");

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
                if (QuestTable.All.ContainsKey(questId)) throw new InvalidOperationException("Synthetic quest ID already exists.");
                QuestTable.All.Add(questId, new QuestDef { Id = questId, Name = "Synthetic collection", RewardItemId = reward.Id,
                    RewardItemQty = 1, Objective = new QuestObjective { Type = QuestObjectiveType.Collect, ItemId = material.Id, Required = 10 } });
                try
                {
                    available.SetValue(npc, new[] { questId.ToString() });
                    receives.SetValue(npc, new[] { questId.ToString() });
                    quests.ServerOfferQuest(questId);
                    fixture.Reply("{\"success\":true}");
                    quests.ServerAcceptQuest(questId);
                    yield return Pump(tick);
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

                    inventory.InitializeFromData(items);
                    fixture.Reply("{\"success\":true}");
                    before = fixture.Requests;
                    quests.ServerTurnInQuest(questId);
                    quests.ServerTurnInQuest(questId);
                    yield return Pump(tick);
                    var result = inventory.GetInventoryData();
                    check(fixture.Requests == before + 1 && !quests.HasActiveQuest(questId) && !inventory.HasQuestTransaction
                        && inventory.GetSlot(0)?.ItemId == reward.Id && inventory.GetSlot(0)?.Quantity == 1
                        && inventory.GetSlot(1)?.Quantity == 3,
                        "Confirmed collection consumes exactly ten materials across slots and grants its exact ID reward once.");
                    check(result.Single(i => i.SlotIndex == 1).UniqueItemId == "fixture-b"
                        && result.Single(i => i.SlotIndex == 1).Durability == 12
                        && inventory.GetSlot(2)?.RefineLevel == 3 && inventory.GetSlot(2)?.Gems[0] == 10
                        && result.Single(i => i.SlotIndex == 3).IsLocked
                        && result.Single(i => i.SlotIndex == 3).OwnerCharacterId == 99,
                        "Collection commit preserves remaining item IDs, durability, gems, refinement and ownership locks.");
                }
                finally
                {
                    available.SetValue(npc, oldAvailable);
                    receives.SetValue(npc, oldReceives);
                    QuestTable.All.Remove(questId);
                    inventory.InitializeFromData(oldInventory);
                }
            }
        }
    }
}
#endif
