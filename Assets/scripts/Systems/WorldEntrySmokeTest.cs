#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequested()
        {
            if (!SessionState.GetBool(Active, false)) return;
            new GameObject("WorldEntrySmokeTest").AddComponent<WorldEntrySmokeTest>();
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
                if (ai != null && ai.GetComponent<NavMeshAgent>().isOnNavMesh) navigating++;
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
            if (SessionState.GetBool("TOP.NewCharacterSmoke", false))
                File.WriteAllLines("Tools/new-character-gameplay-results.txt", checks);
            SessionState.SetBool(Active, false);
            SessionState.SetBool("TOP.AdminGenerationSmoke", false);
            SessionState.SetBool("TOP.NewCharacterSmoke", false);
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
