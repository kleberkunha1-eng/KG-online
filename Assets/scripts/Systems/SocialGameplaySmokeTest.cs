#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using kcp2k;
using Mirror;
using TOP.Data;
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
                    functionHash = AdminGenerationSmokeTest.FunctionHash(command),
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
