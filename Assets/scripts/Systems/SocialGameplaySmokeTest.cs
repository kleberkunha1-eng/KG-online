#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using kcp2k;
using Mirror;
using TOP.Data;
using TOP.Core;
using TOP.Player;
using UnityEngine;

namespace TOP.Testing
{
    public static class SocialGameplaySmokeTest
    {
        internal static void Send(KcpClient client, NetworkBehaviour component, string command, Action<NetworkWriter> arguments)
        {
            var batcher = new Batcher(1200);
            using (var payload = NetworkWriterPool.Get())
            using (var message = NetworkWriterPool.Get())
            using (var batch = NetworkWriterPool.Get())
            {
                arguments(payload);
                NetworkMessages.Pack(new CommandMessage
                {
                    netId = component.netId,
                    componentIndex = (byte)Array.IndexOf(component.GetComponents<NetworkBehaviour>(), component),
                    functionHash = AdminGenerationSmokeTest.FunctionHash(command, component.GetType()),
                    payload = payload.ToArraySegment()
                }, message);
                batcher.AddMessage(message.ToArraySegment(), Time.realtimeSinceStartupAsDouble);
                if (batcher.GetBatch(batch)) client.Send(batch.ToArraySegment(), KcpChannel.Reliable);
            }
        }

        static IEnumerator Pump(Action tick, KcpClient second, float seconds = 1f)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                tick();
                second.TickIncoming();
                second.TickOutgoing();
                yield return null;
            }
        }

        public static IEnumerator Run(GameObject first, KcpClient client, Action tick, Action<bool, string> check)
        {
            RemainingSystemsSmokeTest.Run(check);
            check(!OriginalPvpRules.AreEnemies(1, 0, 0, 0, 0, 0, 0)
                && !OriginalPvpRules.AreEnemies(0, 0, 0, 0, 0, 0, 0)
                && !OriginalPvpRules.AreEnemies(99, 0, 0, 0, 0, 0, 0), "Original normal/unknown maps never allow PK.");
            check(OriginalPvpRules.AreEnemies(2, 7, 7, 0, 0, 0, 0)
                && !OriginalPvpRules.AreEnemies(2, 0, 0, 9, 9, 0, 0), "Original type 2 protects guilds, not parties.");
            check(!OriginalPvpRules.AreEnemies(3, 7, 7, 0, 0, 0, 0)
                && OriginalPvpRules.AreEnemies(3, 0, 0, 9, 9, 0, 0), "Original teampk type 3 protects parties, not guilds.");
            check(!OriginalPvpRules.AreEnemies(4, 7, 7, 0, 0, 0, 0)
                && !OriginalPvpRules.AreEnemies(4, 0, 0, 9, 9, 0, 0)
                && OriginalPvpRules.AreEnemies(4, 0, 0, 0, 0, 0, 0), "Original type 4 protects party and nonzero guild without treating unguilded players as allies.");
            check(!OriginalPvpRules.AreEnemies(5, 0, 0, 0, 0, 1, 1)
                && OriginalPvpRules.AreEnemies(5, 0, 0, 0, 0, 1, 2), "Original side-based map type protects its own side.");
            check(OriginalPvpRules.IsSafe(2) && !OriginalPvpRules.IsSafe(1)
                && OriginalPvpRules.IsLand(1) && OriginalPvpRules.IsLand(8) && !OriginalPvpRules.IsLand(128),
                "Original safe, land and bridge masks match native CompCommand.h.");
            check(OriginalPvpRules.IsPvpMap("puzzleworld") && !OriginalPvpRules.IsPvpMap("garner")
                && OriginalPvpRules.RequiresGuildData("abandonedcity") && !OriginalPvpRules.RequiresGuildData("teampk")
                && !OriginalPvpRules.CanFightInArea(4, true, 2, 1, 0, 0, 0, 0, 0, 0)
                && OriginalPvpRules.CanFightInArea(4, true, 1, 8, 0, 0, 0, 0, 0, 0),
                "PK requires an enabled native map, safe-zone cells block combat, and guild membership is required only by map types 2/4.");
            var pveDeath = new CharacterData { Level = 20, Exp = 1000, CurrentSp = 40, MapName = "garner", Inventory = new List<InventoryItemData>() };
            OriginalDeathPenaltyResult pvePenalty = OriginalDeathPenalty.Apply(pveDeath, false, 12);
            check(pvePenalty.ExperienceLost == Math.Min(PkoTables.ExpToNextLevel(20) / 50UL, 1000UL)
                && pveDeath.Exp == 1000UL - pvePenalty.ExperienceLost && pveDeath.CurrentSp == 0,
                "Original PvE death removes 2% of current-level EXP (capped by current EXP) and clears SP.");
            var protectedDeath = new CharacterData { Level = 20, Exp = 1000, CurrentSp = 40, MapName = "garner", Inventory = new List<InventoryItemData> { new InventoryItemData { ItemId = 3846, Quantity = 1 } } };
            OriginalDeathPenaltyResult protectedPenalty = OriginalDeathPenalty.Apply(protectedDeath, false, 12);
            check(protectedPenalty.ConsumedProtectionItem == 3846 && protectedDeath.Exp == 1000 && protectedDeath.CurrentSp == 0
                && protectedDeath.Inventory.Count == 0, "Original protection item consumes one stack unit and suppresses EXP loss, not SP loss.");
            var playerDeath = new CharacterData { Level = 50, Exp = 1000, CurrentSp = 40, MapName = "teampk", Inventory = new List<InventoryItemData>() };
            OriginalDeathPenaltyResult playerPenalty = OriginalDeathPenalty.Apply(playerDeath, true, 12);
            check(playerPenalty.LostStamina && playerDeath.Exp == 1000 && playerDeath.CurrentSp == 0,
                "Original teampk player kill clears SP but exempts EXP and durability.");
            var pkMapDeath = new CharacterData { Level = 20, Exp = 250000, CurrentSp = 40, MapName = "puzzleworld", Inventory = new List<InventoryItemData>() };
            OriginalDeathPenaltyResult pkMapPenalty = OriginalDeathPenalty.Apply(pkMapDeath, true, 12);
            ulong expectedPkLoss = Math.Min(400UL * 20UL, Math.Min(PkoTables.ExpToNextLevel(20) / 50UL, 250000UL));
            check(pkMapPenalty.LostStamina && pkMapDeath.CurrentSp == 0 && pkMapPenalty.ExperienceLost == expectedPkLoss
                && pkMapDeath.Exp == 250000UL - expectedPkLoss, "Original player kill on native PK maps applies the capped level-squared EXP loss plus SP loss.");
            var highLevelPkDeath = new CharacterData { Level = 80, Exp = 250000, CurrentSp = 40, MapName = "hell", Inventory = new List<InventoryItemData>() };
            OriginalDeathPenaltyResult highLevelPkPenalty = OriginalDeathPenalty.Apply(highLevelPkDeath, true, 12);
            ulong expectedHighLevelPkLoss = Math.Min(80UL * 80UL * 20UL, Math.Min(PkoTables.ExpToNextLevel(80) / 50UL, 250000UL)) / 50UL;
            check(highLevelPkPenalty.ExperienceLost == expectedHighLevelPkLoss
                && highLevelPkDeath.Exp == 250000UL - expectedHighLevelPkLoss, "Original level-80+ player-kill EXP loss is scaled down by 50 after the native cap.");            var pkProtectedDeath = new CharacterData { Level = 20, Exp = 250000, CurrentSp = 40, MapName = "hell", Inventory = new List<InventoryItemData> { new InventoryItemData { ItemId = 3846, Quantity = 1, IsLocked = true } } };
            OriginalDeathPenaltyResult pkProtectedPenalty = OriginalDeathPenalty.Apply(pkProtectedDeath, true, 12);
            check(pkProtectedPenalty.ConsumedProtectionItem == 3846 && pkProtectedDeath.Exp == 250000 && pkProtectedDeath.CurrentSp == 0
                && pkProtectedDeath.Inventory.Count == 0, "Original PvP voodoo doll suppresses EXP/equipment loss but does not suppress SP loss, including when bound.");
            PkoItem repairableHelmet = PkoTables.Items.Values.First(item => item.Type == 20 && item.Durability >= 100 && PkoTables.SlotOf(item) == EquipmentSlot.Helmet);
            ushort startingDurability = (ushort)repairableHelmet.Durability;
            int expectedDurability = Math.Max(49, startingDurability - (int)Math.Floor(startingDurability * 0.05f));
            var wornDeath = new CharacterData { Level = 21, Exp = 500000, CurrentSp = 40, MapName = "garner", Inventory = new List<InventoryItemData> { new InventoryItemData { ItemId = repairableHelmet.Id, Quantity = 1, Durability = startingDurability, IsEquipped = true } } };
            OriginalDeathPenaltyResult wearPenalty = OriginalDeathPenalty.Apply(wornDeath, false, 12);
            check(wearPenalty.EquipmentWorn == 1 && wornDeath.Inventory[0].Durability == expectedDurability,
                "Original PvE death reduces each eligible equipped repair item by 5%, stopping at durability 49.");
            var teampkPveDeath = new CharacterData { Level = 20, Exp = 250000, CurrentSp = 40, MapName = "teampk", Inventory = new List<InventoryItemData>() };
            OriginalDeathPenaltyResult teampkPvePenalty = OriginalDeathPenalty.Apply(teampkPveDeath, false, 12);
            check(teampkPvePenalty.LostStamina && teampkPveDeath.CurrentSp == 0 && teampkPveDeath.Exp == 250000,
                "Original PvE teampk death is exempt from EXP and durability penalties but clears SP.");
            var directDeathSet = new List<InventoryItemData> { new InventoryItemData { ItemId = 2817, IsEquipped = true }, new InventoryItemData { ItemId = 2818, IsEquipped = true }, new InventoryItemData { ItemId = 2819, IsEquipped = true } };
            var nightDeathSet = new CharacterData { Level = 75, Exp = 250000, CurrentSp = 40, MapName = "garner", Inventory = directDeathSet };
            OriginalDeathPenaltyResult nightSetPenalty = OriginalDeathPenalty.Apply(nightDeathSet, false, 18);
            var lowDeathSet = new CharacterData { Level = 74, Exp = 250000, CurrentSp = 40, MapName = "garner", Inventory = new List<InventoryItemData> { new InventoryItemData { ItemId = 2817, IsEquipped = true }, new InventoryItemData { ItemId = 2818, IsEquipped = true }, new InventoryItemData { ItemId = 2819, IsEquipped = true } } };
            OriginalDeathPenaltyResult lowSetPenalty = OriginalDeathPenalty.Apply(lowDeathSet, false, 18);
            var pirateSet = new CharacterData { Level = 70, Exp = 250000, CurrentSp = 40, MapName = "garner", Inventory = new List<InventoryItemData> { new InventoryItemData { ItemId = 5964, FusionItemId = 2530, IsEquipped = true }, new InventoryItemData { ItemId = 6145, FusionItemId = 2531, IsEquipped = true }, new InventoryItemData { ItemId = 6146, FusionItemId = 2532, IsEquipped = true } } };
            OriginalDeathPenaltyResult pirateSetPenalty = OriginalDeathPenalty.Apply(pirateSet, false, 6);
            check(!nightSetPenalty.Applied && nightDeathSet.Exp == 250000 && nightDeathSet.CurrentSp == 40
                && lowSetPenalty.LostStamina && lowDeathSet.CurrentSp == 0
                && !pirateSetPenalty.Applied && pirateSet.Exp == 250000 && pirateSet.CurrentSp == 40,
                "Original nighttime Death/Pirate set exemptions honor minimum levels and persisted fusion IDs.");            var exemptDeath = new CharacterData { Level = 20, Exp = 1000, CurrentSp = 40, MapName = "secretgarden", Inventory = new List<InventoryItemData>() };
            OriginalDeathPenaltyResult exemptPenalty = OriginalDeathPenalty.Apply(exemptDeath, false, 12);
            check(exemptDeath.Exp == 1000 && exemptDeath.CurrentSp == 0 && exemptPenalty.ExperienceLost == 0,
                "Original secretgarden death loses SP only and skips EXP and equipment wear.");
            var attributes = Resources.Load<TextAsset>("PKO/teampk.atr").bytes;
            check(OriginalPvpRules.TryReadAttributes(attributes, 20, 20, out ushort area) && area == 1
                && !OriginalPvpRules.TryReadAttributes(attributes, 95, 20, out _)
                && !OriginalPvpRules.TryReadAttributes(attributes, -1, 20, out _)
                && !OriginalPvpRules.TryReadAttributes(new byte[8], 0, 0, out _),
                "Packed arena attributes use native row-major indexing and fail closed for malformed/boundary cells.");
            var quote = BoatCatalog.Quote(1, 15, 8, 53, 73);
            check(quote.Price == 9990 && quote.Health == 2280 && quote.Fuel == 500 && quote.Defense == 46
                && quote.Speed == 450 && quote.FuelConsumption == 1 && BoatCatalog.HullModelId(new BoatData { TypeId = 1 }) == 2004000000
                && new[] { 2004000000, 2004020000, 2004010000, 2004040000 }.All(id => Resources.Load<GameObject>("PKOShips/Models/model/character/" + id) != null)
                && quote.MinimumAttack == 167 && quote.MaximumAttack == 250 && quote.Capacity == 24,
                "Original default Guppy costs exactly 9990 gold with native hull model, BSREC, fuel, speed and equipment statistics.");
            check(!BoatCatalog.CanBuild(1, 14, 0) && BoatCatalog.CanBuild(1, 15, 0)
                && !BoatCatalog.CanBuild(3, 29, 0) && BoatCatalog.CanBuild(3, 30, 0)
                && !BoatCatalog.IsArgentOffering(4) && BoatCatalog.IsArgentOffering(6),
                "Original Argent offerings and ship level thresholds are preserved.");
            check(!BoatCatalog.ValidName("a") && BoatCatalog.ValidName("ab") && BoatCatalog.ValidName(new string('a', 16))
                && !BoatCatalog.ValidName(new string('a', 17)) && !BoatCatalog.ValidName("<boat>") && !BoatCatalog.ValidName("barco\n"),
                "Boat names enforce the native 2-16 byte limit and reject control/markup characters.");
            var existing = new HashSet<int>(NetworkServer.connections.Keys);
            bool connected = false;
            string error = null;
            int duelInvites = 0;
            var batches = new Unbatcher();
            var second = new KcpClient(() => connected = true, (bytes, channel) =>
            {
                if (channel != KcpChannel.Reliable || !batches.AddBatch(bytes)) return;
                while (batches.GetNextMessage(out var message, out _))
                    using (var reader = NetworkReaderPool.Get(message))
                        if (reader.ReadUShort() == NetworkMessageId<RpcMessage>.Id)
                        {
                            var rpc = reader.Read<RpcMessage>();
                            if (rpc.functionHash == AdminGenerationSmokeTest.FunctionHash("TargetDuelInvite"))
                            {
                                using (var payload = NetworkReaderPool.Get(rpc.payload))
                                    if (payload.Read<uint>() == first.GetComponent<NetworkIdentity>().netId
                                        && payload.ReadString() == "WorldEntryTest") duelInvites++;
                            }
                        }
            }, () => connected = false, (code, message) => error = code + ": " + message, new KcpConfig(Timeout: 8000));
            GameObject other = null;
            NetworkConnectionToClient connection = null;
            try
            {
                second.Connect("127.0.0.1", 17892);
                yield return Pump(tick, second, 2);
                check(connected && error == null, "Second synthetic player connects over KCP"
                    + (error == null ? "." : ": " + error));
                if (!connected) yield break;
                connection = NetworkServer.connections.Values.First(c => !existing.Contains(c.connectionId));
                var pending = (IDictionary)typeof(TOP.Network.TOPNetworkManager)
                    .GetField("_pendingAuths", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(TOP.Network.TOPNetworkManager.Instance);
                foreach (DictionaryEntry entry in pending)
                    entry.Value.GetType().GetField("IsAuthenticated").SetValue(entry.Value, true);
                foreach (var identity in NetworkServer.spawned.Values)
                {
                    var ai = identity.GetComponent<EnemyAI>();
                    if (ai != null) { ai.enabled = false; ai.CancelInvoke(); ai.StopAllCoroutines(); }
                }
                foreach (var spawner in UnityEngine.Object.FindObjectsByType<TOP.Core.EnemySpawner>(FindObjectsSortMode.None))
                {
                    spawner.enabled = false;
                    spawner.CancelInvoke();
                    spawner.StopAllCoroutines();
                }
                other = UnityEngine.Object.Instantiate(TOP.Network.TOPNetworkManager.Instance.playerPrefab);
                other.GetComponent<PlayerController>().InitializeFromCharacterData(new CharacterData
                {
                    Id = 999998, AccountId = 999998, Name = "SocialTest", MapName = "garner", Level = 1,
                    BaseStr = 5, BaseAgi = 5, BaseCon = 5, BaseSpr = 5, BaseSta = 5,
                    CurrentHp = 100, CurrentMp = 100, CurrentSp = 100
                });
                other.transform.position = first.transform.position + Vector3.right;
                NetworkServer.AddPlayerForConnection(connection, other);
                var a = first.GetComponent<PlayerCombat>();
                var b = other.GetComponent<PlayerCombat>();
                var targetCollider = other.GetComponentsInChildren<Collider>().FirstOrDefault(collider => collider.enabled && !collider.isTrigger);
                check(targetCollider != null, "Remote player has a live collider available to actual pointer raycasts.");
                var sa = first.GetComponent<PlayerStats>();
                var sb = other.GetComponent<PlayerStats>();
                var ia = first.GetComponent<PlayerInventory>();
                var ib = other.GetComponent<PlayerInventory>();
                var ta = first.GetComponent<PlayerTrade>();
                var tb = other.GetComponent<PlayerTrade>();
                var pa = first.GetComponent<PlayerParty>();
                var pb = other.GetComponent<PlayerParty>();
                a.StopAttack();
                first.GetComponent<PlayerMovement>().Stop();
                yield return Pump(tick, second);
                check(sa.CurrentHp > 0 && sb.CurrentHp > 0 && a.connectionToClient != null && b.connectionToClient != null
                    && a.GetComponent<PlayerController>().IsInitialized && b.GetComponent<PlayerController>().IsInitialized
                    && a.GetComponent<PlayerController>().MapName == b.GetComponent<PlayerController>().MapName
                    && Vector3.Distance(a.transform.position, b.transform.position) <= 5
                    && a.connectionToClient.isReady && b.connectionToClient.isReady,
                    $"Social fixture ready: hp={sa.CurrentHp}/{sb.CurrentHp}, ready={a.connectionToClient?.isReady}/{b.connectionToClient?.isReady}, distance={Vector3.Distance(a.transform.position, b.transform.position)}.");
                check((bool)typeof(PlayerCombat).GetMethod("AvailableForDuel", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(a, new object[] { b }), "Server validates fixture players as available for duel.");

                Send(client, first.GetComponent<PlayerController>(), "CmdAttackTarget",
                    w => { w.WriteNetworkIdentity(b.netIdentity); w.Write(0); });
                yield return Pump(tick, second);
                check(a.CurrentTargetNetId == 0, "Player attack is rejected without an accepted duel.");

                Send(client, a, "CmdRequestDuel", w => w.WriteNetworkIdentity(b.netIdentity));
                yield return Pump(tick, second);
                check(duelInvites == 1 && a.DuelOpponentNetId == 0, "Duel invitation arrives over real KCP; no attack permission before acceptance.");
                Send(second, b, "CmdRespondDuel", w => { w.Write(a.netId); w.WriteBool(true); });
                yield return Pump(tick, second);
                check(a.CanDuelAttack(b) && b.CanDuelAttack(a), "Accepted duel grants reciprocal server-only attack permission.");
                Send(client, b, "CmdCancelDuel", w => { });
                yield return Pump(tick, second);
                check(a.CanDuelAttack(b), "Mirror rejects commands on the other player's component (authority).");

                int hp = sb.CurrentHp;
                Send(client, first.GetComponent<PlayerController>(), "CmdAttackTarget",
                    w => { w.WriteNetworkIdentity(b.netIdentity); w.Write(0); });
                yield return Pump(tick, second, .5f);
                check(sb.CurrentHp < hp && sb.CurrentHp > 0, "Actual server auto-attack damages consenting opponent.");
                a.StopAttack();
                a.ApplyDuelDamage(b, int.MaxValue / 2);
                check(sb.CurrentHp == 1 && !sb.IsDead && a.DuelOpponentNetId == 0 && b.DuelOpponentNetId == 0,
                    "Lethal duel damage ends both sides at 1 HP without death.");
                hp = sb.CurrentHp;
                a.ApplyDuelDamage(b, 9999);
                check(sb.CurrentHp == hp, "No player damage after duel termination.");

                sa.SetCurrentHpMpSp(sa.MaxHp, sa.MaxMp, sa.MaxSp);
                sb.SetCurrentHpMpSp(sb.MaxHp, sb.MaxMp, sb.MaxSp);
                yield return Pump(tick, second, 3);
                Send(client, a, "CmdRequestDuel", w => w.WriteNetworkIdentity(b.netIdentity));
                yield return Pump(tick, second);
                Send(second, b, "CmdRespondDuel", w => { w.Write(a.netId); w.WriteBool(false); });
                yield return Pump(tick, second);
                check(a.DuelOpponentNetId == 0 && b.DuelOpponentNetId == 0, "Declined duel never grants PvP permission.");

                Send(client, pa, "CmdPartyInvite", w => w.WriteString("SocialTest"));
                yield return Pump(tick, second);
                Send(second, pb, "CmdPartyAccept", w => w.WriteString("WorldEntryTest"));
                yield return Pump(tick, second);
                check(pa.PartyId != 0 && pa.PartyId == pb.PartyId && pa.IsLeader && !pb.IsLeader,
                    "Existing party invite/accept works between two real KCP connections.");
                Send(second, pb, "CmdPartyLeave", w => { });
                yield return Pump(tick, second);
                check(pa.PartyId == 0 && pb.PartyId == 0, "Two-player party cleanly dissolves on leave.");

                int itemId = PkoTables.Items.Keys.First();
                ia.InitializeFromData(new List<InventoryItemData>
                {
                    new InventoryItemData { ItemId = itemId, SlotIndex = 1, Quantity = 2, Durability = 17,
                        RefineLevel = 4, GemSlot1 = 10, GemSlot2 = 20, GemSlot3 = 30 }
                });
                ib.InitializeFromData(new List<InventoryItemData>());
                first.GetComponent<PlayerController>().Gold = 100;
                other.GetComponent<PlayerController>().Gold = 200;
                Send(client, ta, "CmdRequestTrade", w => w.WriteString("SocialTest"));
                yield return Pump(tick, second);
                Send(second, tb, "CmdRespondTrade", w => { w.WriteString("WorldEntryTest"); w.WriteBool(true); });
                yield return Pump(tick, second);
                check(ta.InTrade && tb.InTrade, "Trade request/accept opens both server participants.");
                Send(client, ta, "CmdSetOfferItem", w => { w.Write(1); w.Write(1); });
                Send(client, ta, "CmdSetOfferGold", w => w.Write(25UL));
                yield return Pump(tick, second);
                Send(client, ta, "CmdToggleLock", w => { });
                Send(second, tb, "CmdToggleLock", w => { });
                yield return Pump(tick, second);
                var received = ib.GetSlot(0);
                check(!ta.InTrade && !tb.InTrade && ia.GetSlot(1)?.Quantity == 1 && received?.Quantity == 1
                    && received.RefineLevel == 4 && received.Durability == 17 && received.Gems.SequenceEqual(new[] { 10, 20, 30 }),
                    "Partial-stack trade preserves quantity, refinement, durability and all gems.");
                check(first.GetComponent<PlayerController>().Gold == 75 && other.GetComponent<PlayerController>().Gold == 225,
                    "Gold is transferred exactly once when both lock.");

                Send(client, ta, "CmdRequestTrade", w => w.WriteString("SocialTest"));
                yield return Pump(tick, second);
                Send(second, tb, "CmdRespondTrade", w => { w.WriteString("WorldEntryTest"); w.WriteBool(true); });
                yield return Pump(tick, second);
                Send(client, ta, "CmdSetOfferItem", w => { w.Write(1); w.Write(1); });
                yield return Pump(tick, second);
                ia.SetItemRefine(1, 5);
                Send(client, ta, "CmdToggleLock", w => { });
                Send(second, tb, "CmdToggleLock", w => { });
                yield return Pump(tick, second);
                check(!ta.InTrade && !tb.InTrade && ia.GetSlot(1)?.RefineLevel == 5 && ib.GetSlot(0)?.Quantity == 1,
                    "Changing an offered item cancels trade without removing anything from either live inventory.");

                var keptItems = ib.GetInventoryData();
                var fullItems = ib.GetInventoryData();
                for (int i = 1; i < ib.totalSlots; i++)
                    fullItems.Add(new InventoryItemData { SlotIndex = (ushort)i, ItemId = itemId, Quantity = 1 });
                ib.InitializeFromData(fullItems);
                Send(client, ta, "CmdRequestTrade", w => w.WriteString("SocialTest"));
                yield return Pump(tick, second);
                Send(second, tb, "CmdRespondTrade", w => { w.WriteString("WorldEntryTest"); w.WriteBool(true); });
                yield return Pump(tick, second);
                Send(client, ta, "CmdSetOfferItem", w => { w.Write(1); w.Write(1); });
                yield return Pump(tick, second);
                Send(client, ta, "CmdToggleLock", w => { });
                Send(second, tb, "CmdToggleLock", w => { });
                yield return Pump(tick, second);
                check(!ta.InTrade && !tb.InTrade && ia.GetSlot(1)?.Quantity == 1 && ib.GetInventoryData().Count == 40,
                    "Full destination inventory rejects the complete trade before removing source items.");
                ib.InitializeFromData(keptItems);

                var duelAvailability = typeof(PlayerCombat).GetMethod("AvailableForDuel", BindingFlags.Instance | BindingFlags.NonPublic);
                var tradeAvailability = typeof(PlayerTrade).GetMethod("CanTradeWith", BindingFlags.Instance | BindingFlags.NonPublic);
                other.transform.position = first.transform.position + Vector3.right * 20;
                check((bool)duelAvailability.Invoke(a, new object[] { b }), "Duel allows exactly 20 meters.");
                other.transform.position = first.transform.position + Vector3.right * 20.01f;
                check(!(bool)duelAvailability.Invoke(a, new object[] { b }), "Duel rejects distances above 20 meters.");
                other.transform.position = first.transform.position + Vector3.right * 5;
                check((bool)tradeAvailability.Invoke(ta, new object[] { tb }), "Trade allows exactly 5 meters.");
                other.transform.position = first.transform.position + Vector3.right * 5.01f;
                check(!(bool)tradeAvailability.Invoke(ta, new object[] { tb }), "Trade rejects distances above 5 meters.");

                other.transform.position = first.transform.position + Vector3.right * 25;
                Send(client, ta, "CmdRequestTrade", w => w.WriteString("SocialTest"));
                Send(client, a, "CmdRequestDuel", w => w.WriteNetworkIdentity(b.netIdentity));
                yield return Pump(tick, second);
                check(!ta.InTrade && !tb.InTrade && a.DuelOpponentNetId == 0,
                    "Out-of-range trade and duel requests are rejected.");
                other.transform.position = first.transform.position + Vector3.right;
                yield return Pump(tick, second, 3);
                Send(client, a, "CmdRequestDuel", w => w.WriteNetworkIdentity(b.netIdentity));
                yield return Pump(tick, second);
                Send(second, b, "CmdRespondDuel", w => { w.Write(a.netId); w.WriteBool(true); });
                yield return Pump(tick, second);
                check(a.CanDuelAttack(b), "Second duel starts cleanly after the previous one ended.");
                check(TOP.UI.GameplayCursor.CanAttackPlayer(a.GetComponent<PlayerController>(), b.GetComponent<PlayerController>())
                    && TOP.UI.GameplayCursor.Classify(targetCollider, a.GetComponent<PlayerController>()) == TOP.UI.GameplayCursorKind.Sword,
                    "Accepted duel opponent has the sword cursor using the same permission as left-click targeting.");
                Physics.SyncTransforms();
                check(targetCollider != null && Physics.Raycast(targetCollider.bounds.center
                        + Vector3.up * (targetCollider.bounds.extents.y + 2), Vector3.down, out var playerHit, 10)
                    && playerHit.collider.GetComponentInParent<PlayerController>() == b.GetComponent<PlayerController>()
                    && TOP.UI.GameplayCursor.Classify(playerHit.collider, a.GetComponent<PlayerController>()) == TOP.UI.GameplayCursorKind.Sword,
                    "Real pointer raycast hits the live opponent body and selects the sword, not the disabled prefab collider.");
                var characterField = typeof(PlayerController).GetField("_characterData", BindingFlags.Instance | BindingFlags.NonPublic);
                var localController = a.GetComponent<PlayerController>();
                var remoteController = b.GetComponent<PlayerController>();
                var localData = characterField.GetValue(localController);
                var remoteData = characterField.GetValue(remoteController);
                try
                {
                    characterField.SetValue(localController, null);
                    characterField.SetValue(remoteController, null);
                    check(!localController.IsInitialized && !remoteController.IsInitialized
                        && TOP.UI.GameplayCursor.CanAttackPlayer(localController, remoteController),
                        "Client-style synchronized player state selects attack cursor without server-only character initialization.");
                }
                finally
                {
                    characterField.SetValue(localController, localData);
                    characterField.SetValue(remoteController, remoteData);
                }
                Send(client, ta, "CmdRequestTrade", w => w.WriteString("SocialTest"));
                yield return Pump(tick, second);
                check(!ta.InTrade && a.CanDuelAttack(b), "Trade cannot interrupt an active duel.");
                var skills = first.GetComponent<PlayerSkills>();
                var skill = ScriptableObject.CreateInstance<TOP.Core.SkillData>();
                try
                {
                    skill.skillId = 999887;
                    skill.skillName = "Synthetic Duel Skill";
                    skill.targetType = TOP.Core.SkillTargetType.SingleEnemy;
                    skill.range = 3;
                    skill.baseDamage = 10;
                    skill.damageMultiplier = skill.damagePerLevel = skill.elementMultiplier = 1;
                    skill.cooldown = 0;
                    skill.mpCost = skill.spCost = 0;
                    typeof(PlayerSkills).GetField("_allSkills", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(skills, new[] { skill });
                    skills.LearnSkill(skill.skillId);
                    sb.SetCurrentHpMpSp(sb.MaxHp, sb.MaxMp, sb.MaxSp);
                    hp = sb.CurrentHp;
                    skills.UseSkill(skill.skillId, b.transform.position, b.netId);
                    check(sb.CurrentHp < hp && sb.CurrentHp > 0, "Single-target skill damages only the accepted duel opponent.");
                    a.ApplyDuelDamage(b, int.MaxValue / 2);
                    hp = sb.CurrentHp;
                    skills.UseSkill(skill.skillId, b.transform.position, b.netId);
                    check(sb.CurrentHp == hp, "Offensive skill is rejected after duel permission expires.");
                }
                finally { UnityEngine.Object.Destroy(skill); }
                sa.SetCurrentHpMpSp(sa.MaxHp, sa.MaxMp, sa.MaxSp);
                sb.SetCurrentHpMpSp(sb.MaxHp, sb.MaxMp, sb.MaxSp);
                yield return Pump(tick, second, 3);
                Send(client, a, "CmdRequestDuel", w => w.WriteNetworkIdentity(b.netIdentity));
                yield return Pump(tick, second);
                Send(second, b, "CmdRespondDuel", w => { w.Write(a.netId); w.WriteBool(true); });
                yield return Pump(tick, second);
                check(a.CanDuelAttack(b), "Duel can restart after a skill-assisted finish.");
                other.GetComponent<PlayerController>().MapName = "different-fixture-map";
                yield return Pump(tick, second);
                check(a.DuelOpponentNetId == 0 && b.DuelOpponentNetId == 0, "Map changes terminate both duel participants.");
                other.GetComponent<PlayerController>().MapName = "garner";
                yield return Pump(tick, second, 3);
                Send(client, a, "CmdRequestDuel", w => w.WriteNetworkIdentity(b.netIdentity));
                yield return Pump(tick, second);
                var expires = typeof(PlayerCombat).GetField("challengeExpires", BindingFlags.Instance | BindingFlags.NonPublic);
                check((float)expires.GetValue(b) > Time.time + 27 && (float)expires.GetValue(b) <= Time.time + 30,
                    "Invitation expiration is scheduled for 30 seconds (not an unlimited challenge).");
                expires.SetValue(b, Time.time - 1);
                yield return Pump(tick, second);
                Send(second, b, "CmdRespondDuel", w => { w.Write(a.netId); w.WriteBool(true); });
                yield return Pump(tick, second);
                check(a.DuelOpponentNetId == 0 && b.DuelOpponentNetId == 0, "Expired challenge cannot be accepted.");
                yield return Pump(tick, second, 3);
                Send(client, a, "CmdRequestDuel", w => w.WriteNetworkIdentity(b.netIdentity));
                yield return Pump(tick, second);
                Send(second, b, "CmdRespondDuel", w => { w.Write(a.netId); w.WriteBool(true); });
                yield return Pump(tick, second);
                check(a.CanDuelAttack(b), "A fresh challenge works after an expired invitation.");
                Send(client, a, "CmdCancelDuel", _ => { });
                yield return Pump(tick, second);
                var ca = a.GetComponent<PlayerController>();
                var cb = b.GetComponent<PlayerController>();
                Vector3 positionA = a.transform.position, positionB = b.transform.position;
                int partyA = pa.PartyId, partyB = pb.PartyId;
                int originalLevel = cb.Level;
                int deaths = 0;
                Action died = () => deaths++;
                cb.OnDeath += died;
                try
                {
                    check(!a.CanPkAttack(b), "Garner remains protected from lethal PK even after an accepted duel.");
                    ca.MapName = cb.MapName = "teampk";
                    cb.Level = 15;
                    a.transform.position = new Vector3(20 - WorldBlockGrid.OriginX, positionA.y, WorldBlockGrid.OriginZ - 20);
                    b.transform.position = a.transform.position + Vector3.right;
                    pa.PartyId = pb.PartyId = 0;
                    check(a.CanPkAttack(b), "Server allows unrelated players in the original land-only teampk arena.");
                    check(TOP.UI.GameplayCursor.CanAttackPlayer(ca, cb)
                        && TOP.UI.GameplayCursor.Classify(targetCollider, ca) == TOP.UI.GameplayCursorKind.Sword,
                        "Allowed arena opponent uses the sword cursor, not the interaction hand.");
                    pa.PartyId = pb.PartyId = 77;
                    hp = sb.CurrentHp;
                    a.ApplyPlayerDamage(b, 20);
                    check(!a.CanPkAttack(b) && sb.CurrentHp == hp, "Same-party protection is rechecked at damage application.");
                    check(!TOP.UI.GameplayCursor.CanAttackPlayer(ca, cb)
                        && TOP.UI.GameplayCursor.Classify(targetCollider, ca) == TOP.UI.GameplayCursorKind.Hand,
                        "Protected party mate changes to the hand cursor for social interaction rather than an attack.");
                    pa.PartyId = pb.PartyId = 0;
                    cb.MapName = "garner";
                    check(!a.CanPkAttack(b), "PK cannot cross map boundaries.");
                    cb.MapName = "teampk";
                    b.transform.position = new Vector3(95 - WorldBlockGrid.OriginX, positionB.y, WorldBlockGrid.OriginZ - 20);
                    check(!a.CanPkAttack(b), "Unknown/boundary arena cells deny lethal PK.");
                    b.transform.position = a.transform.position + Vector3.right;
                    ca.BoatOperationPending = true;
                    check(!a.CanPkAttack(b), "Atomic boat purchase cannot overlap lethal PK.");
                    ca.BoatOperationPending = false;
                    var pkSkills = a.GetComponent<PlayerSkills>();
                    var allSkills = typeof(PlayerSkills).GetField("_allSkills", BindingFlags.Instance | BindingFlags.NonPublic);
                    var previousSkills = allSkills.GetValue(pkSkills);
                    var pkSkill = ScriptableObject.CreateInstance<TOP.Core.SkillData>();
                    var additionalCollider = b.gameObject.AddComponent<BoxCollider>();
                    try
                    {
                        pkSkill.skillId = 999888;
                        pkSkill.skillName = "Synthetic PK Skill";
                        pkSkill.targetType = TOP.Core.SkillTargetType.SingleEnemy;
                        pkSkill.range = 3;
                        pkSkill.baseDamage = 12;
                        pkSkill.damageMultiplier = pkSkill.damagePerLevel = pkSkill.elementMultiplier = 1;
                        pkSkill.mpCost = pkSkill.spCost = 1;
                        pkSkill.cooldown = 0;
                        pkSkill.areaRadius = 2;
                        pkSkill.maxTargets = 2;
                        allSkills.SetValue(pkSkills, new[] { pkSkill });
                        pkSkills.LearnSkill(pkSkill.skillId);
                        sa.SetCurrentHpMpSp(sa.MaxHp, sa.MaxMp, sa.MaxSp);
                        sb.SetCurrentHpMpSp(sb.MaxHp, sb.MaxMp, sb.MaxSp);
                        pa.PartyId = pb.PartyId = 77;
                        hp = sb.CurrentHp;
                        int mana = sa.CurrentMp, stamina = sa.CurrentSp;
                        pkSkills.UseSkill(pkSkill.skillId, b.transform.position, b.netId);
                        check(sb.CurrentHp == hp && sa.CurrentMp == mana && sa.CurrentSp == stamina,
                            "Protected PK skill target cannot take damage or consume caster resources.");
                        pa.PartyId = pb.PartyId = 0;
                        int skillDamage = Mathf.Max(1, 12 + sa.PhysicalAttack - sb.PhysicalDefense);
                        pkSkills.UseSkill(pkSkill.skillId, b.transform.position, b.netId);
                        check(sb.CurrentHp == hp - skillDamage && sa.CurrentMp == mana - 1,
                            "Single-target PK skill uses the shared original arena permission and charges its cost once.");
                        pkSkill.targetType = TOP.Core.SkillTargetType.AreaEnemy;
                        Physics.SyncTransforms();
                        hp = sb.CurrentHp;
                        pkSkills.UseSkill(pkSkill.skillId, b.transform.position, b.netId);
                        check(sb.CurrentHp == hp - skillDamage,
                            "Area PK skill damages an allowed player once despite multiple colliders.");
                        mana = sa.CurrentMp;
                        hp = sb.CurrentHp;
                        pkSkills.UseSkill(pkSkill.skillId, a.transform.position + Vector3.right * 10, b.netId);
                        pkSkills.UseSkill(pkSkill.skillId, new Vector3(float.NaN, 0, 0), b.netId);
                        check(sb.CurrentHp == hp && sa.CurrentMp == mana,
                            "Out-of-range and non-finite area casts are rejected before costs and player damage.");
                    }
                    finally
                    {
                        allSkills.SetValue(pkSkills, previousSkills);
                        UnityEngine.Object.Destroy(pkSkill);
                        UnityEngine.Object.Destroy(additionalCollider);
                    }
                    ulong experience = cb.Exp, gold = cb.Gold;
                    int points = ca.PkPoints;
                    sb.SetCurrentHpMpSp(sb.MaxHp, sb.MaxMp, sb.MaxSp);
                    a.ApplyPlayerDamage(b, 25);
                    check(sb.CurrentHp == sb.MaxHp - Mathf.Max(1, 25 - sb.PhysicalDefense),
                        "Lethal PK damage subtracts physical defense exactly once.");
                    a.ApplyPlayerDamage(b, int.MaxValue / 2);
                    a.ApplyPlayerDamage(b, int.MaxValue / 2);
                    check(sb.IsDead && cb.CurrentHp == 0 && sb.CurrentSp == 0 && deaths == 1 && !a.CanPkAttack(b),
                        "Arena death reaches zero HP once, blocks duplicate damage and terminates attack permission.");
                    check(cb.Exp == experience && cb.Gold == gold && ca.PkPoints == points,
                        "Original teampk death does not lose EXP/gold or invent red-name/crime points.");
                }
                finally
                {
                    cb.OnDeath -= died;
                    ca.BoatOperationPending = false;
                    ca.MapName = cb.MapName = "garner";
                    cb.Level = originalLevel;
                    pa.PartyId = partyA; pb.PartyId = partyB;
                    a.transform.position = positionA; b.transform.position = positionB;
                    sb.SetCurrentHpMpSp(sb.MaxHp, sb.MaxMp, sb.MaxSp);
                }
                yield return ArenaGameplaySmokeTest.Run(first, other, client, second, tick, check);
                second.Disconnect();
                yield return Pump(tick, second);
                check(a.DuelOpponentNetId == 0 && a.CurrentTargetNetId == 0,
                    "Disconnect removes the opponent and stops combat without stale duel permission.");
            }
            finally
            {
                second.Disconnect();
                second.TickOutgoing();
                if (other != null) NetworkServer.Destroy(other);
            }
        }
    }
}
#endif
