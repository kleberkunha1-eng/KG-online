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

        public static ushort FunctionHash(string name, Type declaringType = null)
        {
            var calls = typeof(Mirror.RemoteCalls.RemoteProcedureCalls);
            var delegates = (IDictionary)calls.GetField("remoteCallDelegates",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            foreach (DictionaryEntry entry in delegates)
            {
                var function = (Delegate)entry.Value.GetType().GetField("function").GetValue(entry.Value);
                if (declaringType != null && function.Method.DeclaringType != declaringType) continue;
                if (function.Method.Name == "InvokeUserCode_" + name
                    || function.Method.Name.StartsWith("InvokeUserCode_" + name + "__", StringComparison.Ordinal))
                    return (ushort)entry.Key;
            }
            throw new InvalidOperationException("Mirror function not registered: " + declaringType + "." + name);
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
                    var controller = player.GetComponent<PlayerController>();
                    var otherPlayer = new GameObject("OtherCharacterInventoryFixture");
                    try
                    {
                        int originalCount = inventory.GetInventoryData().Count;
                        var otherInventory = otherPlayer.AddComponent<PlayerInventory>();
                        otherInventory.InitializeFromData(new List<InventoryItemData>
                        {
                            new InventoryItemData { SlotIndex = 7, ItemId = BlueMageSet.Pieces[0].Id, Quantity = 1 }
                        });
                        check(otherInventory.GetSlot(7)?.ItemId == BlueMageSet.Pieces[0].Id
                            && inventory.GetInventoryData().Count == originalCount
                            && otherInventory.GetInventoryData().Count == 1,
                            "Two character inventory instances retain independent items and slots.");
                        otherInventory.InitializeFromData(null);
                        check(otherInventory.GetInventoryData().Count == 0 && inventory.GetInventoryData().Count == originalCount,
                            "Loading a character with null inventory clears its old items without changing another character.");
                    }
                    finally { UnityEngine.Object.Destroy(otherPlayer); }
                    var equipment = player.GetComponent<PlayerEquipment>();
                    if (controller == null || equipment == null)
                        throw new InvalidOperationException("Blue Mage fixture requires player and equipment components.");
                    int previousLevel = controller.Level;
                    var stats = player.GetComponent<PlayerStats>();
                    var movement = player.GetComponent<PlayerMovement>();
                    if (stats == null || movement == null)
                        throw new InvalidOperationException("Equipment fixture requires stats and movement.");
                    try
                    {
                        controller.Level = Mathf.Max(previousLevel, 10);
                        int baseHp = stats.MaxHp;
                        int baseDefense = stats.PhysicalDefense;
                        foreach (var piece in BlueMageSet.Pieces)
                        {
                            var definition = PkoTables.Items[piece.Id];
                            check(definition.Con >= 0 && definition.Hp >= 0 && definition.Agi >= 0 && definition.Def >= 0,
                                piece.Name + " has no negative vitality, movement or defense bonuses.");
                            yield return Request("CmdAdminGenerate", new[] { piece.Id, 0, 0, 0, 0, 0 }, send, tick);
                            check(success && inventory.GetItemCount(piece.Id) == 1,
                                "Authorized KCP generation creates " + piece.Name + ": " + message);
                            ushort itemSlot = ushort.MaxValue;
                            foreach (var generated in inventory.GetInventoryData())
                                if (generated.ItemId == piece.Id) { itemSlot = generated.SlotIndex; break; }
                            check(itemSlot != ushort.MaxValue && equipment.EquipFromInventory(itemSlot, piece.Slot)
                                && equipment.GetEquippedItem(piece.Slot)?.ItemId == piece.Id,
                                piece.Name + " equips into its intended slot with normal requirements.");
                            check(stats.CurrentHp > 0 && controller.CurrentHp == stats.CurrentHp
                                && controller.MaxHp == stats.MaxHp && stats.MaxHp >= baseHp,
                                piece.Name + " preserves positive, synchronized HP.");
                        }
                        int expectedDefense = baseDefense;
                        foreach (var piece in BlueMageSet.Pieces) expectedDefense += PkoTables.Items[piece.Id].Def;
                        check(stats.PhysicalDefense == expectedDefense, "Full set grants exactly its catalog defense bonuses.");
                        int equippedHp = stats.CurrentHp, equippedMaxHp = stats.MaxHp, equippedDefense = stats.PhysicalDefense;
                        var saved = controller.GetCharacterData();
                        controller.InitializeFromCharacterData(saved);
                        check(stats.CurrentHp == equippedHp && stats.MaxHp == equippedMaxHp
                            && stats.PhysicalDefense == equippedDefense,
                            "Saving and loading equipped Blue Mage set preserves HP and rebuilds bonuses exactly once.");
                        equipment.LoadEquippedFromInventory(saved.Inventory);
                        check(stats.PhysicalDefense == equippedDefense && stats.CurrentHp == equippedHp,
                            "Repeated equipment restore does not stack bonuses or lose HP.");
                        var start = player.transform.position;
                        movement.SetDestination(start + Vector3.right * 2f);
                        float moveDeadline = Time.realtimeSinceStartup + .5f;
                        while (Time.realtimeSinceStartup < moveDeadline) { tick(); yield return null; }
                        movement.Stop();
                        check(Vector3.Distance(player.transform.position, start) > .1f,
                            "Character actually moves with the full Blue Mage set equipped.");

                        foreach (var piece in BlueMageSet.Pieces) equipment.UnequipItem(piece.Slot);
                        check(stats.PhysicalDefense == baseDefense && stats.MaxHp == baseHp && stats.CurrentHp > 0,
                            "Unequipping restored equipment returns to baseline without negative stat drift.");
                        foreach (var piece in BlueMageSet.Pieces)
                        {
                            foreach (var entry in inventory.GetInventoryData())
                                if (entry.ItemId == piece.Id)
                                { equipment.EquipFromInventory(entry.SlotIndex, piece.Slot); break; }
                        }
                        check(stats.PhysicalDefense == equippedDefense,
                            "Re-equipping after reload restores the same full-set bonuses.");
                        var chest = BlueMageSet.Pieces[1];
                        int vitalityGem = 0;
                        foreach (var gem in PkoGems.GemsFor(PkoTables.Items[chest.Id]))
                        {
                            var bonus = PkoGems.GemEffect(gem.ItemId);
                            if (bonus.Con > 0 || bonus.Hp > 0) { vitalityGem = gem.ItemId; break; }
                        }
                        if (vitalityGem == 0)
                            throw new InvalidOperationException("Reload regression requires a compatible vitality gem.");
                        var chestItem = equipment.GetEquippedItem(chest.Slot);
                        ushort chestSlot = chestItem.SlotIndex;
                        equipment.UnequipItem(chest.Slot);
                        chestItem.RefineLevel = 3;
                        chestItem.Gems[0] = vitalityGem;
                        equipment.EquipFromInventory(chestSlot, chest.Slot);
                        stats.SetCurrentHpMpSp(stats.MaxHp, stats.MaxMp, stats.MaxSp);
                        int boostedHp = stats.MaxHp, boostedDefense = stats.PhysicalDefense;
                        check(boostedHp > baseHp && boostedDefense > equippedDefense,
                            "Vitality gem and refinement actually increase HP and defense before reload.");
                        saved = controller.GetCharacterData();
                        controller.InitializeFromCharacterData(saved);
                        check(stats.CurrentHp == boostedHp && stats.MaxHp == boostedHp
                            && stats.PhysicalDefense == boostedDefense,
                            "Reload restores instance gem/refinement bonuses before clamping saved HP.");
                        equipment.UnequipItem(chest.Slot);
                        check(stats.MaxHp == baseHp && stats.CurrentHp == baseHp
                            && stats.PhysicalDefense == equippedDefense - PkoTables.Items[chest.Id].Def,
                            "Removing restored gemmed armor clamps HP safely and never subtracts unapplied bonuses.");
                        chestItem = inventory.GetSlot(chestSlot);
                        chestItem.RefineLevel = 0;
                        chestItem.Gems[0] = -1;
                        equipment.EquipFromInventory(chestSlot, chest.Slot);
                        saved = controller.GetCharacterData();
                        saved.CurrentHp = 0;
                        controller.InitializeFromCharacterData(saved);
                        check(stats.IsDead && !movement.IsMoving,
                            "Loading a saved dead character stops movement and starts normal respawn.");
                        float respawnDeadline = Time.realtimeSinceStartup + 7f;
                        while (stats.IsDead && Time.realtimeSinceStartup < respawnDeadline) { tick(); yield return null; }
                        check(!stats.IsDead && controller.CurrentHp == stats.MaxHp,
                            "Saved HP zero recovers through normal respawn instead of permanently locking movement.");
                        start = player.transform.position;
                        movement.SetDestination(start + Vector3.right * 2f);
                        moveDeadline = Time.realtimeSinceStartup + .5f;
                        while (Time.realtimeSinceStartup < moveDeadline) { tick(); yield return null; }
                        movement.Stop();
                        check(Vector3.Distance(player.transform.position, start) > .1f,
                            "Character moves again after reconnecting with saved zero HP.");
                    }
                    finally
                    {
                        foreach (var piece in BlueMageSet.Pieces) equipment.UnequipItem(piece.Slot);
                        controller.Level = previousLevel;
                    }
                    foreach (var piece in BlueMageSet.Pieces)
                        check(inventory.GetItemCount(piece.Id) == 1,
                            piece.Name + " returns to inventory after unequip without duplication.");
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
