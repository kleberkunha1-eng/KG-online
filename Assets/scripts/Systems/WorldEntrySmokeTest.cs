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
using TOP.Network;
using TOP.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace TOP.Testing
{
    public class WorldEntrySmokeTest : MonoBehaviour
    {
        const string Active = "TOP.WorldEntrySmokeActive";
        readonly List<string> checks = new List<string>();
        TOPNetworkManager manager;
        KcpClient client;
        PlayerConnection playerConnection;
        GameObject player;
        bool connected;
        string transportError;
        int spawns;
        int worldEntryErrors;
        bool ownerSpawned;
        readonly Unbatcher unbatcher = new Unbatcher();
        TOP.World.EnvironmentSnapshot environmentSnapshot;
        TOP.World.EnvironmentReply environmentReply;
        int environmentSnapshots, environmentReplies;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequested()
        {
            if (!SessionState.GetBool(Active, false)) return;
            new GameObject("WorldEntrySmokeTest").AddComponent<WorldEntrySmokeTest>();
        }

        void CheckBankStorageRules()
        {
            var pile = TOP.Data.PkoTables.Items.Values.First(item => item.Stack >= 4);
            var source = new TOP.Data.InventoryItemData
            {
                UniqueItemId = Guid.NewGuid().ToString(), SlotIndex = 0, ItemId = pile.Id, Quantity = 10,
                Durability = 73, RefineLevel = 0, GemSlot1 = null, GemSlot2 = null, GemSlot3 = null
            };
            var data = new TOP.Data.CharacterData { Id = 7001,
                Inventory = new List<TOP.Data.InventoryItemData> { source },
                BankItems = new List<TOP.Data.InventoryItemData>() };
            var probe = new GameObject("Bank storage rule fixture");
            try
            {
                var inventory = probe.AddComponent<TOP.Player.PlayerInventory>();
                bool deposited = TOP.NPC.NPCInteractable.Deposit(data, inventory, 0, 4, out _);
                Check(deposited && data.Inventory[0].Quantity == 6 && data.BankItems.Count == 1
                    && data.BankItems[0].Quantity == 4 && data.BankItems[0].Durability == 73
                    && data.BankItems[0].UniqueItemId != source.UniqueItemId,
                    "Bank deposits split item stacks while preserving item attributes.");
                bool withdrew = TOP.NPC.NPCInteractable.Withdraw(data, data.BankItems[0].SlotIndex, 2, out _);
                Check(withdrew && data.Inventory.Sum(item => item.Quantity) == 8
                    && data.BankItems.Count == 1 && data.BankItems[0].Quantity == 2,
                    "Bank withdrawals partially restore stacks and merge compatible inventory items.");
                Check(TOP.NPC.NPCInteractable.OriginalBankSlotCount == 32,
                    "Original personal bank has 32 slots on one page.");
                data.Inventory.Add(new TOP.Data.InventoryItemData { SlotIndex = 1, ItemId = 2520, Quantity = 1 });
                Check(!TOP.NPC.NPCInteractable.Deposit(data, inventory, 1, 1, out _),
                    "Original banker item blacklist is enforced server-side.");
                data.Inventory[1].ItemId = pile.Id;
                data.Inventory[1].OwnerCharacterId = data.Id + 1;
                Check(!TOP.NPC.NPCInteractable.Deposit(data, inventory, 1, 1, out _),
                    "Character-bound bank items cannot be moved to another character.");
                data.Inventory[1].OwnerCharacterId = data.Id;
                data.Inventory[1].IsEquipped = true;
                Check(!TOP.NPC.NPCInteractable.Deposit(data, inventory, 1, 1, out _),
                    "Equipped items cannot be deposited.");
                data.Inventory[1].IsEquipped = false;
                data.Inventory[1].IsLocked = true;
                Check(!TOP.NPC.NPCInteractable.Deposit(data, inventory, 1, 1, out _),
                    "Locked items cannot be deposited.");

                var fullBank = new TOP.Data.CharacterData { Id = 7002,
                    Inventory = new List<TOP.Data.InventoryItemData>
                    {
                        new TOP.Data.InventoryItemData { UniqueItemId = Guid.NewGuid().ToString(), SlotIndex = 0, ItemId = pile.Id, Quantity = 1 }
                    },
                    BankItems = Enumerable.Range(0, 32).Select(slot => new TOP.Data.InventoryItemData
                    {
                        UniqueItemId = Guid.NewGuid().ToString(), SlotIndex = (ushort)slot, ItemId = 40000 + slot, Quantity = 1
                    }).ToList() };
                Check(!TOP.NPC.NPCInteractable.Deposit(fullBank, inventory, 0, 1, out _)
                    && fullBank.Inventory[0].Quantity == 1 && fullBank.BankItems.Count == 32,
                    "A full bank rejects deposits without consuming inventory items.");

                var fullInventory = new TOP.Data.CharacterData { Id = 7003,
                    Inventory = Enumerable.Range(0, 40).Select(slot => new TOP.Data.InventoryItemData
                    {
                        UniqueItemId = Guid.NewGuid().ToString(), SlotIndex = (ushort)slot, ItemId = pile.Id + 1000 + slot, Quantity = 1
                    }).ToList(),
                    BankItems = new List<TOP.Data.InventoryItemData>
                    {
                        new TOP.Data.InventoryItemData { UniqueItemId = Guid.NewGuid().ToString(), SlotIndex = 0, ItemId = pile.Id, Quantity = 1 }
                    } };
                Check(!TOP.NPC.NPCInteractable.Withdraw(fullInventory, 0, 1, out _)
                    && fullInventory.Inventory.Count == 40 && fullInventory.BankItems[0].Quantity == 1,
                    "A full inventory rejects withdrawals without removing bank items.");
            }
            finally { Destroy(probe); }
        }
        void Check(bool ok, string message)
        {
            checks.Add((ok ? "PASS " : "FAIL ") + message);
            if (!ok) Debug.LogError("[WorldEntrySmoke] " + message);
        }

        IEnumerator Start()
        {
            Application.logMessageReceived += ObserveLog;
            var pending = new Stack<IEnumerator>();
            pending.Push(Run());
            try
            {
                while (pending.Count > 0)
                {
                    var run = pending.Peek();
                    bool next;
                    try { next = run.MoveNext(); }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        Check(false, e.Message);
                        break;
                    }
                    if (!next)
                    {
                        pending.Pop();
                        (run as IDisposable)?.Dispose();
                    }
                    else if (run.Current is IEnumerator nested) pending.Push(nested);
                    else yield return run.Current;
                }
            }
            finally
            {
                while (pending.Count > 0) (pending.Pop() as IDisposable)?.Dispose();
                Finish();
            }
        }

        IEnumerator Run()
        {
            // Let scene Start callbacks finish before beginning the timed network handshake.
            yield return null;
            var centralTerrain = Array.Find(Terrain.activeTerrains, terrain =>
                terrain.name == "Garner_Argent" && terrain.gameObject.scene.name == "GameScene");
            Check(centralTerrain != null && centralTerrain.drawHeightmap
                && centralTerrain.GetComponent<TerrainCollider>() != null
                && centralTerrain.GetComponent<TerrainCollider>().enabled,
                "Central city terrain is active, visible and has an enabled collider.");
            Check(centralTerrain != null
                && Physics.Raycast(new Vector3(-47.42f, 8f, 1.73f), Vector3.down, out var cityGround, 20f,
                    LayerMask.GetMask("Terrain"), QueryTriggerInteraction.Ignore)
                && cityGround.collider == centralTerrain.GetComponent<TerrainCollider>()
                && Mathf.Abs(cityGround.point.y - .6f) < .1f,
                "City click/ground ray hits the central terrain at city height, not the KillPlane.");
            if (centralTerrain != null)
            {
                var pivot = new Vector3(-47.42f, 2.8f, 1.73f);
                for (int pitch = -35; pitch <= 75; pitch += 5)
                {
                    var desired = pivot + Quaternion.Euler(pitch, 45, 0) * Vector3.back * 14;
                    var safe = CameraFollow.ProtectGround(pivot, desired, LayerMask.GetMask("Terrain", "Ground"), .5f, 50);
                    Check(Physics.Raycast(safe + Vector3.up * 50, Vector3.down, out var cameraGround, 100,
                        LayerMask.GetMask("Terrain", "Ground"), QueryTriggerInteraction.Ignore)
                        && safe.y - cameraGround.point.y >= .49f, "Camera stays above ground at pitch " + pitch);
                }
            }
            if (NetworkServer.active || NetworkClient.active)
            {
                Check(false, $"Test requires isolation: server={NetworkServer.active}, client={NetworkClient.active}, "
                    + $"manager={(NetworkManager.singleton != null ? NetworkManager.singleton.name : "none")}.");
                yield break;
            }
            manager = NetworkManager.singleton as TOPNetworkManager;
            KcpTransport transport;
            if (manager == null)
            {
                if (NetworkManager.singleton != null)
                {
                    Check(false, "Existing manager is not TOPNetworkManager: " + NetworkManager.singleton.GetType().Name);
                    yield break;
                }
                var root = new GameObject("WorldEntryTestServer");
                transport = root.AddComponent<KcpTransport>();
                manager = root.AddComponent<TOPNetworkManager>();
                manager.transport = transport;
            }
            else
            {
                transport = manager.transport as KcpTransport;
                if (transport == null) { Check(false, "Fixture requires KCP transport."); yield break; }
            }
            transport.Port = 17892;
            manager.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            manager.autoCreatePlayer = false;
            manager.StartServer();
            manager.CancelInvoke("AutoSaveAll");
            client = new KcpClient(() => connected = true, Receive, () => connected = false,
                (code, error) => transportError = code + ": " + error, new KcpConfig(Timeout: 8000));
            client.Connect("127.0.0.1", 17892);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!connected && transportError == null && Time.realtimeSinceStartup < deadline)
            {
                Tick();
                yield return null;
            }
            Check(connected && transportError == null, "Synthetic client connects to isolated TOPNetworkManager."
                + (connected && transportError == null ? "" : " " + (transportError ?? "Handshake deadline exceeded.")));
            if (!connected) yield break;
            var connections = (Dictionary<int, PlayerConnection>)typeof(TOPNetworkManager)
                .GetField("_connections", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            foreach (var connection in connections.Values) { playerConnection = connection; break; }
            Check(playerConnection != null, "Server tracks the connected synthetic client.");
            if (playerConnection == null) yield break;
            if (SessionState.GetBool("TOP.EnvironmentSmoke", false))
            {
                Check(TOP.World.WorldEnvironment.Instance != null && Camera.main != null
                    && Camera.main.clearFlags == CameraClearFlags.Skybox,
                    "Gameplay camera draws the synchronized sky instead of its former solid background.");
                yield return CheckEnvironment();
            }
            SendReady();
            deadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < deadline) { Tick(); yield return null; }
            Check(spawns == 0 && !playerConnection.Connection.isReady,
                "Ready during login produces zero world spawns.");

            player = Instantiate(manager.playerPrefab);
            player.transform.position = new Vector3(0f, .6f, 0f);
            var controller = player.GetComponent<PlayerController>();
            bool newCharacter = SessionState.GetBool("TOP.NewCharacterSmoke", false);
            controller.InitializeFromCharacterData(new CharacterData
            {
                Id = 999999, AccountId = 999999, Name = "WorldEntryTest", MapName = "garner", Level = 1,
                Job = (byte)(newCharacter ? PkoRaces.NewCharacterTest : 0),
                BaseStr = 10, BaseAgi = 8, BaseCon = 10, BaseSpr = 10, BaseSta = 10,
                CurrentHp = 220, MaxHp = 220, CurrentMp = 150, MaxMp = 150, CurrentSp = 96, MaxSp = 96
            });
            playerConnection.PlayerController = controller;
            playerConnection.State = ConnectionState.InGame;
            Check(controller.IsInitialized && playerConnection.Connection.identity == null,
                "Selected synthetic character is initialized but not sent before Ready.");
            SendReady();
            deadline = Time.realtimeSinceStartup + 5f;
            while ((!ownerSpawned || spawns == 0) && transportError == null && Time.realtimeSinceStartup < deadline)
            {
                Tick();
                yield return null;
            }
            Tick();
            Check(playerConnection.Connection.isReady && playerConnection.Connection.identity != null,
                "World Ready associates the selected player with its connection.");
            Check(ownerSpawned && spawns > 0, $"World Ready delivers owner={ownerSpawned}, spawns={spawns}, "
                + $"observing={playerConnection.Connection.observing.Count}, server spawned={NetworkServer.spawned.Count}, "
                + $"send rate={NetworkServer.sendRate}, ticks={NetworkServer.actualTickRate}, "
                + $"loading={NetworkServer.isLoadingScene}.");
            Check(transportError == null, "World entry completes without transport error.");
            Vector3 movementStart = player.transform.position;
            var movement = player.GetComponent<PlayerMovement>();
            movement.SetDestination(movementStart + Vector3.right * 2f);
            deadline = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < deadline) { Tick(); yield return null; }
            Check(player.transform.position.x > movementStart.x + .5f
                && Mathf.Abs(player.transform.position.y - movementStart.y) < .5f,
                "Player actually moves under server authority and stays on city ground.");
            if (newCharacter)
            {
                Check(controller.Job == PkoRaces.NewCharacterTest && controller.CurrentHp > 0
                    && controller.GetCharacterData().Job == PkoRaces.NewCharacterTest,
                    "NewCharacterTest preserves race identity and live stats through world entry/save data.");
                yield return null;
                var visual = player.GetComponentInChildren<TOP.Character.PkoCharacterVisual>();
                Check(visual != null && visual.Race == PkoRaces.NewCharacterTest
                    && visual.Pose.Has(TOP.Character.PkoPoses.Run), "Gameplay binder loads new model and Lance movement poses.");
                Vector3 start = player.transform.position;
                player.GetComponent<PlayerMovement>().SetDestination(start + Vector3.right * 2f);
                deadline = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < deadline) { Tick(); yield return null; }
                Check(Vector3.Distance(start, player.transform.position) > .5f,
                    "NewCharacterTest actually moves under server-authoritative movement.");
            }

            int navigating = 0;
            foreach (var identity in NetworkServer.spawned.Values)
            {
                var ai = identity.GetComponent<EnemyAI>();
                if (ai != null && ai.GetComponent<NavMeshAgent>().isOnNavMesh)
                {
                    navigating++;
                    if (Physics.Raycast(ai.transform.position + Vector3.up * 10, Vector3.down, out var mobGround, 30,
                        LayerMask.GetMask("Ground", "Terrain"), QueryTriggerInteraction.Ignore))
                    {
                        foreach (var skin in ai.GetComponentsInChildren<SkinnedMeshRenderer>())
                        {
                            var baked = new Mesh();
                            try
                            {
                                skin.BakeMesh(baked);
                                float bottom = float.PositiveInfinity;
                                foreach (var vertex in baked.vertices)
                                    bottom = Mathf.Min(bottom, skin.transform.TransformPoint(vertex).y);
                                Check(bottom - mobGround.point.y < .5f && bottom - mobGround.point.y > -.5f,
                                    "Monster visual stays at ground height: " + ai.name + " gap=" + (bottom - mobGround.point.y));
                            }
                            finally { Destroy(baked); }
                        }
                    }
                    else Check(false, "Monster has no ground: " + ai.name);
                }
            }
            Check(navigating > 0, "Server monsters navigate on the baked world NavMesh.");
            var enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Mob_Slime.prefab");
            var replica = Instantiate(enemyPrefab);
            try
            {
                var ai = replica.GetComponent<EnemyAI>();
                ai.OnStartClient();
                Check(!ai.GetComponent<NavMeshAgent>().enabled,
                    "An unspawned client replica never activates server navigation.");
            }
            finally { Destroy(replica); }
            Check(worldEntryErrors == 0, "No missing-scene spawns or invalid NavMesh agent creation occurred.");
            CheckBankStorageRules();
            if (SessionState.GetBool("TOP.SocialSmoke", false))
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "--boat-only") < 0)
                {
                    yield return SocialGameplaySmokeTest.Run(player, client, Tick, Check);
                    yield return NPCGameplaySmokeTest.Run(player, client, Tick, Check);
                }
                yield return QuestPersistenceSmokeTest.RunBoatConstruction(player, client, Tick, Check);
                yield return OriginalQuestSmokeTest.Run(player, client, Tick, Check);
            }
            if (SessionState.GetBool("TOP.AdminGenerationSmoke", false))
            {
                var ui = TOP.UI.Pko.PkoUi.Instance;
                Check(ui != null && !ui.Windows.ContainsKey("frmBlackTrade"),
                    "Unused windows are not constructed during actual world startup.");
                if (ui != null)
                {
                    int count = ui.Windows.Count;
                    var window = ui.Get("frmBlackTrade");
                    Check(ui.Windows.Count == count + 1 && ui.Get("frmBlackTrade") == window,
                        $"On-demand UI builds exactly one window and reuses it; startup windows={count}.");
                }
                yield return AdminGenerationSmokeTest.Run(playerConnection, player, SendAdminCommand, Tick, Check);
            }
        }

        void ObserveLog(string message, string stack, LogType type)
        {
            if (message.Contains("Failed to create agent") || message.Contains("Spawn scene object not found")
                || message.Contains("Could not spawn assetId"))
                worldEntryErrors++;
        }

        void Receive(ArraySegment<byte> data, KcpChannel channel)
        {
            if (channel != KcpChannel.Reliable) return;
            if (!unbatcher.AddBatch(data)) { transportError = "Invalid batch"; return; }
            while (unbatcher.GetNextMessage(out var message, out _))
            {
                using (var reader = NetworkReaderPool.Get(message))
                {
                    ushort id = reader.ReadUShort();
                    if (id == NetworkMessageId<RpcMessage>.Id && SessionState.GetBool("TOP.SocialSmoke", false))
                    {
                        var rpc = reader.Read<RpcMessage>();
                        NPCGameplaySmokeTest.Receive(rpc);
                        OriginalQuestSmokeTest.Receive(rpc);
                        if (rpc.functionHash == AdminGenerationSmokeTest.FunctionHash("RpcShowMessage"))
                            using (var payload = NetworkReaderPool.Get(rpc.payload))
                                Debug.Log("[SocialFixture] " + payload.ReadString());
                        continue;
                    }
                    if (id == NetworkMessageId<TOP.World.EnvironmentSnapshot>.Id)
                    {
                        environmentSnapshot = reader.Read<TOP.World.EnvironmentSnapshot>();
                        environmentSnapshots++;
                        continue;
                    }
                    if (id == NetworkMessageId<TOP.World.EnvironmentReply>.Id)
                    {
                        environmentReply = reader.Read<TOP.World.EnvironmentReply>();
                        environmentReplies++;
                        continue;
                    }
                    if (id == NetworkMessageId<RpcMessage>.Id && SessionState.GetBool("TOP.AdminGenerationSmoke", false))
                    {
                        AdminGenerationSmokeTest.Receive(reader.Read<RpcMessage>());
                        continue;
                    }
                    if (id != NetworkMessageId<SpawnMessage>.Id) continue;
                    var spawn = reader.Read<SpawnMessage>();
                    spawns++;
                    ownerSpawned |= spawn.isLocalPlayer;
                }
            }
        }

        void SendReady()
        {
            var batcher = new Batcher(1200);
            using (var message = NetworkWriterPool.Get())
            using (var batch = NetworkWriterPool.Get())
            {
                NetworkMessages.Pack(new ReadyMessage(), message);
                batcher.AddMessage(message.ToArraySegment(), Time.realtimeSinceStartupAsDouble);
                if (batcher.GetBatch(batch)) client.Send(batch.ToArraySegment(), KcpChannel.Reliable);
            }
        }

        void Tick() { client.TickIncoming(); client.TickOutgoing(); }

        IEnumerator CheckEnvironment()
        {
            var control = Resources.Load<TOP.World.WorldEnvironmentSettings>("WorldEnvironment").defaults;
            using (var message = NetworkWriterPool.Get())
            using (var batch = NetworkWriterPool.Get())
            {
                var batcher = new Batcher(1200);
                NetworkMessages.Pack(new TOP.World.EnvironmentChange
                    { controls = control, hour = 12 }, message);
                batcher.AddMessage(message.ToArraySegment(), Time.realtimeSinceStartupAsDouble);
                if (batcher.GetBatch(batch)) client.Send(batch.ToArraySegment(), KcpChannel.Reliable);
            }
            float deadline = Time.realtimeSinceStartup + 3;
            while (environmentReplies == 0 && Time.realtimeSinceStartup < deadline) { Tick(); yield return null; }
            Check(environmentReplies > 0 && !environmentReply.success
                && environmentReply.message == "NOT_AUTHENTICATED", "Unauthenticated environment change is rejected over KCP.");
            var pending = (System.Collections.IDictionary)typeof(TOPNetworkManager)
                .GetField("_pendingAuths", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            object auth = pending[playerConnection.ConnectionId];
            var field = auth.GetType().GetField("IsAuthenticated");
            field.SetValue(auth, true);
            try
            {
                typeof(TOPNetworkManager).GetMethod("BroadcastEnvironment", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(manager, null);
                deadline = Time.realtimeSinceStartup + 3;
                while (environmentSnapshots == 0 && Time.realtimeSinceStartup < deadline) { Tick(); yield return null; }
                var now = DateTimeOffset.Now;
                double expected = now.ToUnixTimeMilliseconds() / 1000d + now.Offset.TotalSeconds;
                Check(environmentSnapshots > 0 && Math.Abs(environmentSnapshot.localSeconds - expected) < 4
                    && environmentSnapshot.utcOffsetMinutes == (int)now.Offset.TotalMinutes,
                    "Server clock and timezone arrive through a real KCP snapshot, independently of client clock.");
                Check(environmentSnapshots > 0 && environmentSnapshot.controls.IsValid
                    && environmentSnapshot.controls.weather == control.weather
                    && Mathf.Abs(environmentSnapshot.controls.moonDiameter - control.moonDiameter) < .0001f,
                    "Environment controls survive Mirror serialization.");
            }
            finally { field.SetValue(auth, false); }
        }

        void SendAdminCommand(string name, int[] arguments)
        {
            var inventory = player.GetComponent<PlayerInventory>();
            var batcher = new Batcher(1200);
            using (var payload = NetworkWriterPool.Get())
            using (var message = NetworkWriterPool.Get())
            using (var batch = NetworkWriterPool.Get())
            {
                foreach (int argument in arguments) payload.Write(argument);
                NetworkMessages.Pack(new CommandMessage
                {
                    netId = inventory.netId,
                    componentIndex = (byte)Array.IndexOf(player.GetComponents<NetworkBehaviour>(), inventory),
                    functionHash = AdminGenerationSmokeTest.FunctionHash(name),
                    payload = payload.ToArraySegment()
                }, message);
                batcher.AddMessage(message.ToArraySegment(), Time.realtimeSinceStartupAsDouble);
                if (batcher.GetBatch(batch)) client.Send(batch.ToArraySegment(), KcpChannel.Reliable);
            }
        }

        void Finish()
        {
            if (playerConnection != null)
            {
                playerConnection.State = ConnectionState.Login;
                playerConnection.PlayerController = null;
            }
            client?.Disconnect();
            client?.TickOutgoing();
            if (manager != null) manager.StopServer();
            if (player != null) Destroy(player);
            Application.logMessageReceived -= ObserveLog;
            checks.Add("No account authentication or database writes were performed.");
            File.WriteAllLines("Tools/world-entry-smoke-results.txt", checks);
            CheckBankStorageRules();
            if (SessionState.GetBool("TOP.SocialSmoke", false))
                File.WriteAllLines("Tools/social-gameplay-results.txt", checks);
            if (SessionState.GetBool("TOP.EnvironmentSmoke", false))
                File.WriteAllLines("Tools/environment-network-results.txt", checks);
            if (SessionState.GetBool("TOP.NewCharacterSmoke", false))
                File.WriteAllLines("Tools/new-character-gameplay-results.txt", checks);
            SessionState.SetBool(Active, false);
            SessionState.SetBool("TOP.AdminGenerationSmoke", false);
            SessionState.SetBool("TOP.NewCharacterSmoke", false);
            SessionState.SetBool("TOP.EnvironmentSmoke", false);
            SessionState.SetBool("TOP.SocialSmoke", false);
            EditorApplication.isPlaying = false;
        }

        void OnDestroy()
        {
            if (SessionState.GetBool(Active, false))
            {
                checks.Add("FAIL World-entry test was interrupted.");
                Finish();
            }
        }
    }
}
#endif
