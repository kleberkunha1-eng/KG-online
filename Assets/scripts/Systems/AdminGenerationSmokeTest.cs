#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Mirror;
using TOP.Data;
using TOP.Network;
using TOP.Player;
using TOP.Services;
using UnityEngine;

namespace TOP.Testing
{
    public static class AdminGenerationSmokeTest
    {
        public static int Responses { get; private set; }
        static bool success;
        static string message;
        static int requestId, responseRequestId;

        public static ushort FunctionHash(string name)
        {
            var calls = typeof(Mirror.RemoteCalls.RemoteProcedureCalls);
            var delegates = (IDictionary)calls.GetField("remoteCallDelegates",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (DictionaryEntry entry in delegates)
            {
                var function = (Delegate)entry.Value.GetType().GetField("function").GetValue(entry.Value);
                if (function.Method.Name.StartsWith("InvokeUserCode_" + name + "__", StringComparison.Ordinal))
                    return (ushort)entry.Key;
            }
            throw new InvalidOperationException("Mirror function not registered: " + name);
        }

        public static void Receive(RpcMessage rpc)
        {
            bool correlated = rpc.functionHash == FunctionHash("TargetAdminRequestResult");
            if (!correlated && rpc.functionHash != FunctionHash("TargetAdminGenerationResult")) return;
            using (var reader = NetworkReaderPool.Get(rpc.payload))
            {
                responseRequestId = correlated ? reader.Read<int>() : 0;
                success = reader.ReadBool();
                message = reader.ReadString();
                Responses++;
            }
        }

        public static IEnumerator Run(PlayerConnection connection, GameObject player,
            Action<string, int[]> send, Action tick, Action<bool, string> check)
        {
            var root = typeof(ApiConfig).GetField("root", BindingFlags.Static | BindingFlags.NonPublic);
            string previousRoot = (string)root.GetValue(null);
            string previousToken = connection.SessionToken;
            Responses = 0;
            using (var api = new AdminApiFixture())
            {
                root.SetValue(null, api.Url);
                connection.SessionToken = "fixture-admin";
                var inventory = player.GetComponent<PlayerInventory>();
                GameObject databaseObject = null;
                if (DatabaseService.Instance == null)
                {
                    databaseObject = new GameObject("AdminFixtureDatabase");
                    databaseObject.AddComponent<DatabaseService>();
                }
                int itemId = PkoTables.MeshyMageWingsItemId;
                try
                {
                    check(PkoTables.Items.ContainsKey(itemId), "Fixture server catalog includes Mage Wings.");
                    check(TOPNetworkManager.Instance != null && inventory.connectionToClient != null,
                        "Fixture inventory has manager=" + (TOPNetworkManager.Instance != null)
                        + ", owner connection=" + (inventory.connectionToClient != null) + ".");
                    int before = inventory.GetItemCount(itemId);
                    yield return Request("CmdAdminGive", new[] { itemId, 1 }, send, tick);
                    check(!success && inventory.GetItemCount(itemId) == before
                        && message.Contains("Permissao"), "Non-admin request is rejected with an explicit response: " + message);
                    api.Admin = true;
                    yield return Request("CmdAdminGive", new[] { itemId, 1 }, send, tick);
                    check(success && inventory.GetItemCount(itemId) == before + 1,
                        "Authorized KCP command actually adds Mage Wings: " + message);
                    int equipmentId = 0;
                    foreach (var definition in PkoTables.Items.Values)
                        if (PkoGems.CanSocket(definition)) { equipmentId = definition.Id; break; }
                    if (equipmentId == 0) throw new InvalidOperationException("Fixture requires socket-capable equipment.");
                    yield return Request("CmdAdminGenerate", new[] { equipmentId, 3, 0, 0, 0, 0 }, send, tick);
                    bool refined = false;
                    foreach (var item in inventory.GetInventoryData())
                        if (item.ItemId == equipmentId && item.RefineLevel == 3) refined = true;
                    check(success && refined, "Equipment command preserves requested refinement: " + message);
                    yield return Request("CmdAdminGive", new[] { itemId, 1 }, send, tick, false);
                    check(success && responseRequestId == 0, "Legacy admin command remains compatible.");
                    yield return Request("CmdAdminGive", new[] { -999, 1 }, send, tick);
                    check(!success && message.Contains("invalida"), "Unknown item is explicitly rejected.");
                    yield return Request("CmdAdminGive", new[] { itemId, 0 }, send, tick);
                    check(!success && message.Contains("invalida"), "Invalid quantity is explicitly rejected.");
                    connection.SessionToken = null;
                    yield return Request("CmdAdminGive", new[] { itemId, 1 }, send, tick);
                    check(!success && message.Contains("expirada"), "Missing session reports re-login, not fake success: " + message);
                    connection.SessionToken = "fixture-admin";
                    var full = new List<InventoryItemData>();
                    for (ushort slot = 0; slot < inventory.totalSlots - 1; slot++)
                        full.Add(new InventoryItemData { SlotIndex = slot, ItemId = itemId, Quantity = 1 });
                    inventory.InitializeFromData(full);
                    yield return Request("CmdAdminGive", new[] { itemId, 2 }, send, tick);
                    check(!success && message.Contains("1 de 2") && inventory.FindEmptySlot() < 0,
                        "Partial delivery reports the exact received quantity: " + message);
                    yield return Request("CmdAdminGenerate", new[] { itemId, 0, 0, 0, 0, 0 }, send, tick);
                    check(!success && message.Contains("cheio"), "Full inventory explicitly rejects equipment generation: " + message);
                    check(api.TokenObserved, "Admin authorization uses this connection's bearer token.");
                }
                finally
                {
                    root.SetValue(null, previousRoot);
                    connection.SessionToken = previousToken;
                    if (databaseObject != null) UnityEngine.Object.Destroy(databaseObject);
                }
            }
        }

        static IEnumerator Request(string command, int[] arguments, Action<string, int[]> send, Action tick, bool correlate = true)
        {
            int before = Responses;
            success = false;
            message = "No response";
            if (correlate)
            {
                requestId++;
                var correlatedArguments = new int[arguments.Length + 1];
                correlatedArguments[0] = requestId;
                Array.Copy(arguments, 0, correlatedArguments, 1, arguments.Length);
                send(command.Replace("CmdAdmin", "CmdRequestAdmin"), correlatedArguments);
            }
            else send(command, arguments);
            float deadline = Time.realtimeSinceStartup + 6f;
            while (Responses == before && Time.realtimeSinceStartup < deadline)
            {
                tick();
                yield return null;
            }
            if (Responses == before) throw new TimeoutException(command + " received no acknowledgement.");
            if (correlate && responseRequestId != requestId)
                throw new InvalidOperationException("Generation response belongs to another request.");
        }

        sealed class AdminApiFixture : IDisposable
        {
            readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            readonly Task loop;
            volatile bool disposed;
            public volatile bool Admin, TokenObserved;
            public string Url { get; }

            public AdminApiFixture()
            {
                listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
                loop = Serve();
            }

            async Task Serve()
            {
                try
                {
                    while (!disposed)
                    {
                        using (var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                        using (var stream = client.GetStream())
                        using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                        {
                            string line;
                            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                                if (line.Equals("Authorization: Bearer fixture-admin", StringComparison.OrdinalIgnoreCase))
                                    TokenObserved = true;
                            string body = "{\"success\":true,\"admin\":" + (Admin ? "true" : "false") + "}";
                            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                                + "Connection: close\r\nContent-Length: " + body.Length + "\r\n\r\n" + body);
                            await stream.WriteAsync(response, 0, response.Length).ConfigureAwait(false);
                        }
                    }
                }
                catch (ObjectDisposedException) when (disposed) { }
                catch (SocketException) when (disposed) { }
            }

            public void Dispose()
            {
                disposed = true;
                listener.Stop();
                loop.GetAwaiter().GetResult();
            }
        }
    }
}
#endif
