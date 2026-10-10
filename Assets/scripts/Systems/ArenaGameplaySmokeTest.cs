#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using kcp2k;
using Mirror;
using TOP.Data;
using TOP.NPC;
using TOP.Player;
using TOP.Systems;
using UnityEngine;

namespace TOP.Testing
{
    public static class ArenaGameplaySmokeTest
    {
        public static IEnumerator Run(GameObject first, GameObject other, KcpClient client, KcpClient second, Action tick, Action<bool, string> check)
        {
            var a = first.GetComponent<PlayerController>(); var b = other.GetComponent<PlayerController>();
            var ia = first.GetComponent<PlayerInventory>(); var ib = other.GetComponent<PlayerInventory>();
            var partyA = first.GetComponent<PlayerParty>(); var partyB = other.GetComponent<PlayerParty>();
            var savedA = ia.GetInventoryData(); var savedB = ib.GetInventoryData();
            int levelA = a.Level, levelB = b.Level, partyIdA = partyA.PartyId, partyIdB = partyB.PartyId;
            bool leaderA = partyA.IsLeader, leaderB = partyB.IsLeader;
            ulong goldA = a.Gold, goldB = b.Gold; Vector3 posA = a.transform.position, posB = b.transform.position;
            var npc = NetworkServer.spawned.Values.Select(i => i.GetComponent<NPCInteractable>()).FirstOrDefault(n => n != null && n.IsArenaAdministrator);
            var extra = new List<GameObject>();
            IEnumerator Pump(float seconds = .5f)
            {
                float deadline = Time.realtimeSinceStartup + seconds;
                while (Time.realtimeSinceStartup < deadline) { tick(); second.TickIncoming(); second.TickOutgoing(); yield return null; }
            }
            try
            {
                check(npc != null, "Original NPC 53 (server record 52) Arena Administrator exposes arena registration.");
                if (npc == null) yield break;
                a.RespawnPlayer(); b.RespawnPlayer(); a.Level = b.Level = 26; a.Gold = b.Gold = 50000;
                partyA.PartyId = partyB.PartyId = 0;
                ia.InitializeFromData(new List<InventoryItemData>()); ib.InitializeFromData(new List<InventoryItemData>());
                a.Teleport("garner", npc.transform.position + Vector3.right); b.Teleport("garner", npc.transform.position + Vector3.left);
                first.GetComponent<PlayerMovement>().Stop(); other.GetComponent<PlayerMovement>().Stop();
                first.GetComponent<PlayerMovement>().GetType().GetProperty("ActiveNpc").SetValue(first.GetComponent<PlayerMovement>(), npc);
                other.GetComponent<PlayerMovement>().GetType().GetProperty("ActiveNpc").SetValue(other.GetComponent<PlayerMovement>(), npc);
                SocialGameplaySmokeTest.Send(client, npc, "CmdArenaRegister", w => { w.WriteBool(false); w.WriteBool(false); });
                yield return Pump();
                check(!ArenaCoordinator.Instance.IsRegistered(a) && a.ArenaInstanceId == 0, "Real KCP NPC registration rejects a missing Medal of Valor.");
                check(!OriginalArenaRules.CanObtain(25, 50000, 0, 0) && !OriginalArenaRules.CanObtain(26, 49999, 0, 0)
                    && !OriginalArenaRules.CanObtain(26, 50000, 1, 0), "Original medal grant requires level >25, 50000 gold and no duplicate.");
                SocialGameplaySmokeTest.Send(client, npc, "CmdObtainArenaMedal", _ => { });
                SocialGameplaySmokeTest.Send(second, npc, "CmdObtainArenaMedal", _ => { });
                yield return Pump();
                ia.RemoveItem(0, 1); ib.RemoveItem(0, 1);
                bool medalA = ArenaCoordinator.HasMedal(a, out var ma), medalB = ArenaCoordinator.HasMedal(b, out var mb);
                check(medalA && medalB
                    && ma.MedalHonor == 10 && ma.MedalWins == 10 && ma.MedalEntries == 10 && ma.MedalKills == 10
                    && ma.MedalDeaths == 10 && ma.IsLocked && ma.OwnerCharacterId == a.CharacterId && a.Gold == 0 && b.Gold == 0,
                    "Authoritative permanent NPC medals resist generic removal, charge 50000, bind to the character and initialize all grade-97 attributes to 10.");
                var invalid = new TOP.Inventory.InventoryItem { ItemId = 3849, Quantity = 2, MedalHonor = 10 };
                bool duplicates = OriginalArenaRules.Eligible(new[] { invalid }, out _);
                invalid.Quantity = 1; invalid.MedalHonor = -301;
                check(!duplicates && !OriginalArenaRules.Eligible(new[] { invalid }, out _), "Admission rejects stacked/duplicate medals and honor below -300.");
                invalid.MedalHonor = 30001; bool excessive = OriginalArenaRules.Eligible(new[] { invalid }, out _);
                invalid.MedalHonor = -300;
                check(!excessive && OriginalArenaRules.Eligible(new[] { invalid }, out _), "Original honor admission bounds are inclusive -300..30000.");
                SocialGameplaySmokeTest.Send(client, npc, "CmdArenaRegister", w => { w.WriteBool(false); w.WriteBool(false); }); yield return Pump();
                check(ArenaCoordinator.Instance.IsRegistered(a) && a.ArenaInstanceId == 0, "Registration waits for a consenting opponent instead of teleporting one player.");
                SocialGameplaySmokeTest.Send(second, npc, "CmdArenaRegister", w => { w.WriteBool(false); w.WriteBool(false); }); yield return Pump();
                int room = a.ArenaInstanceId;
                check(room > 0 && room <= 20 && room == b.ArenaInstanceId && a.ArenaSide == 1 && b.ArenaSide == 2
                    && a.MapName == "teampk" && b.MapName == "teampk", "NPC registration starts one of exactly 20 authoritative arena copies.");
                check(Vector3.Distance(a.transform.position, ArenaWorld.Spawn(room, 1)) < .2f
                    && Vector3.Distance(b.transform.position, ArenaWorld.Spawn(room, 2)) < .2f && ma.MedalEntries == 11 && mb.MedalEntries == 11
                    && !ArenaWorld.IsBlocked(room, ArenaWorld.Spawn(room, 1)) && !ArenaWorld.IsBlocked(room, ArenaWorld.Spawn(room, 2)),
                    "Party PVP 1/2 use native (44,21)/(44,66) positions and increment original participation attribute.");
                check(a.GetCharacterData().MapName == "garner" && a.GetCharacterData().Position == OriginalArenaRules.ArgentBar,
                    "Existing save path persists Argent Bar, never transient instance coordinates.");
                check(OriginalArenaRules.KillHonor(21, 26) == 0 && OriginalArenaRules.KillHonor(20, 26) == 2
                    && OriginalArenaRules.KillHonor(36, 26) == 0 && OriginalArenaRules.KillHonor(35, 26) == 1,
                    "Native kill honor preserves the asymmetric -5/+10 thresholds, including the exact -5 gap.");
                foreach (var original in new[] { first, other })
                {
                    var go = UnityEngine.Object.Instantiate(TOP.Network.TOPNetworkManager.Instance.playerPrefab);
                    go.GetComponent<PlayerController>().InitializeFromCharacterData(new CharacterData { Id = 880000 + extra.Count, AccountId = 880000 + extra.Count,
                        Name = "ArenaRoomTest" + extra.Count, MapName = "garner", Level = 26, CurrentHp = 100, CurrentMp = 100, CurrentSp = 100,
                        BaseStr = 5, BaseCon = 5, BaseAgi = 5, BaseSpr = 5, BaseSta = 5 });
                    go.GetComponent<PlayerInventory>().InitializeFromData(new List<InventoryItemData> { new InventoryItemData { ItemId = 3849, Quantity = 1, MedalHonor = 10 } });
                    go.transform.position = OriginalArenaRules.ArgentBar; NetworkServer.Spawn(go, original.GetComponent<NetworkIdentity>().connectionToClient); extra.Add(go);
                }
                var c = extra[0].GetComponent<PlayerController>(); var d = extra[1].GetComponent<PlayerController>();
                bool secondMatch = ArenaCoordinator.Instance.StartMatch(c, d, false, out _);
                var interest = NetworkServer.aoi as TOP.Network.ArenaInterestManagement;
                check(secondMatch && c.ArenaInstanceId != room && ArenaCoordinator.Instance.ActiveRooms == 2
                    && !first.GetComponent<PlayerCombat>().CanPkAttack(extra[0].GetComponent<PlayerCombat>())
                    && interest != null && !interest.OnCheckObserver(c.netIdentity, a.connectionToClient),
                    "Two live instances isolate PK and Mirror observers, including same-map opponents.");
                check(first.GetComponent<PlayerCombat>().CanPkAttack(other.GetComponent<PlayerCombat>())
                    && TOP.UI.GameplayCursor.CanAttackPlayer(a, b), "Native arena land attributes allow opposite sides in the same instance.");
                check(!ia.DropItemOnServer(0, 1, a.transform.position) && !ia.MoveItemOnServer(0, 1), "Active match locks medal/inventory mutation.");
                check(OriginalArenaRules.HonorChange(true, false, 1, 1, 26, 26) == 2
                    && OriginalArenaRules.HonorChange(false, false, 1, 1, 26, 26) == -2
                    && OriginalArenaRules.HonorChange(true, true, 2, 5, 26, 36) == 20
                    && OriginalArenaRules.HonorChange(false, true, 2, 5, 26, 36) == -2,
                    "Honor formula preserves native base 2, enemy/team counts and Lua floor for negative level differences.");
                first.GetComponent<PlayerCombat>().ApplyPlayerDamage(other.GetComponent<PlayerCombat>(), int.MaxValue / 2);
                yield return Pump();
                check(b.CurrentHp == 0 && a.ArenaResult == 1 && b.ArenaResult == -1 && ma.MedalHonor == 13 && mb.MedalHonor == 7
                    && ma.MedalWins == 11 && mb.MedalWins == 10 && ma.MedalKills == 11 && mb.MedalDeaths == 11 && !first.GetComponent<PlayerCombat>().CanPkAttack(other.GetComponent<PlayerCombat>()),
                    "Authoritative elimination resolves once, rewards/penalizes medals and disables post-result combat.");
                yield return Pump(6);
                check(b.ArenaInstanceId == room && b.CurrentHp == 0, "Defeated player does not run generic 5-second respawn before arena closure.");
                yield return Pump(5.5f);
                check(a.ArenaInstanceId == 0 && b.ArenaInstanceId == 0 && a.MapName == "garner" && b.MapName == "garner"
                    && Vector3.Distance(a.transform.position, OriginalArenaRules.ArgentBar) < 1 && Vector3.Distance(b.transform.position, OriginalArenaRules.ArgentBar) < 1
                    && b.CurrentHp > 0 && ma.MedalHonor == 13 && mb.MedalHonor == 7,
                    "Arena closes 11s after result, returns both sides alive to original Argent Bar without double rewards.");
                var oldArena = GameObject.Find("Original_teampk_copy_" + room);
                UnityEngine.Object.DestroyImmediate(oldArena);
                ArenaWorld.Ensure(room);
                var rebuiltArena = GameObject.Find("Original_teampk_copy_" + room);
                check(rebuiltArena != null && rebuiltArena.GetComponent<MeshCollider>() != null,
                    "Destroyed/unloaded native arena terrain is rebuilt on reuse instead of retaining a stale scene cache.");
                ArenaCoordinator.Instance.Leave(c); ArenaCoordinator.Instance.Leave(d);
                a.RespawnPlayer(); b.RespawnPlayer(); c.RespawnPlayer(); d.RespawnPlayer();
                partyA.PartyId = extra[0].GetComponent<PlayerParty>().PartyId = 8801;
                partyB.PartyId = extra[1].GetComponent<PlayerParty>().PartyId = 8802;
                partyA.IsLeader = partyB.IsLeader = true;
                foreach (var pc in new[] { a, b, c, d }) pc.Teleport("garner", OriginalArenaRules.ArgentBar);
                ma.MedalHonor = 30000; mb.MedalHonor = -300;
                bool partyMatch = ArenaCoordinator.Instance.StartMatch(a, b, true, out _);
                check(partyMatch && a.ArenaSide == c.ArenaSide && b.ArenaSide == d.ArenaSide && a.ArenaSide != b.ArenaSide,
                    "Party-mode registration snapshots two actual teams and preserves Party PVP side assignments for every member.");
                first.GetComponent<PlayerCombat>().ApplyPlayerDamage(other.GetComponent<PlayerCombat>(), int.MaxValue / 2);
                yield return Pump();
                check(a.ArenaResult == 0 && b.ArenaResult == 0 && d.CurrentHp > 0,
                    "One eliminated party member cannot end the match while another teammate remains alive.");
                first.GetComponent<PlayerCombat>().ApplyPlayerDamage(extra[1].GetComponent<PlayerCombat>(), int.MaxValue / 2);
                yield return Pump();
                check(a.ArenaResult == 1 && c.ArenaResult == 1 && b.ArenaResult == -1 && d.ArenaResult == -1
                    && ma.MedalHonor == 30006 && mb.MedalHonor == -305,
                    $"Last party elimination awards native team honor exactly once beyond entry bounds: honor={ma.MedalHonor}/{mb.MedalHonor}, results={a.ArenaResult}/{c.ArenaResult}/{b.ArenaResult}/{d.ArenaResult}, levels={a.Level}/{b.Level}/{d.Level}.");
                yield return Pump(11.5f);
                check(new[] { a, b, c, d }.All(pc => pc.ArenaInstanceId == 0 && pc.MapName == "garner" && pc.CurrentHp > 0),
                    "All party members return safely after the native 11-second result window.");
            }
            finally
            {
                foreach (var go in extra) if (go != null) { ArenaCoordinator.Instance.Leave(go.GetComponent<PlayerController>()); NetworkServer.Destroy(go); }
                if (ArenaCoordinator.Instance != null) { ArenaCoordinator.Instance.Leave(a); ArenaCoordinator.Instance.Leave(b); }
                ia.InitializeFromData(savedA); ib.InitializeFromData(savedB);
                a.Level = levelA; b.Level = levelB; a.Gold = goldA; b.Gold = goldB;
                partyA.PartyId = partyIdA; partyB.PartyId = partyIdB; partyA.IsLeader = leaderA; partyB.IsLeader = leaderB;
                a.Teleport("garner", posA); b.Teleport("garner", posB);
            }
        }
    }
}
#endif
