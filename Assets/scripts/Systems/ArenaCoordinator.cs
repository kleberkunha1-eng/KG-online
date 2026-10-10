using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TOP.Data;
using TOP.Network;
using TOP.Player;
using UnityEngine;

namespace TOP.Systems
{
    public sealed class ArenaCoordinator : MonoBehaviour
    {
        public static ArenaCoordinator Instance { get; private set; }
        sealed class Registration { public PlayerController Leader; public bool Party; public float Expires; }
        sealed class Room
        {
            public int Id, Winner, Count1, Count2, Average1, Average2;
            public bool Party; public double CloseAt;
            public readonly List<PlayerController> Players = new List<PlayerController>();
        }
        readonly List<Registration> waiting = new List<Registration>();
        readonly Dictionary<int, Room> rooms = new Dictionary<int, Room>();
        public int ActiveRooms => rooms.Count;
        void Awake() { Instance = this; }
        public static bool Busy(PlayerController pc) => pc != null && pc.ArenaInstanceId != 0;
        public bool IsRegistered(PlayerController pc) => waiting.Any(r => r.Leader == pc);
        static IEnumerable<TOP.Inventory.InventoryItem> Items(PlayerInventory bag)
        { for (int i = 0; i < bag.totalSlots; i++) yield return bag.GetSlot(i); }
        public static bool HasMedal(PlayerController pc, out TOP.Inventory.InventoryItem medal)
        {
            medal = null; var bag = pc != null ? pc.GetComponent<PlayerInventory>() : null;
            return bag != null && OriginalArenaRules.Eligible(Items(bag), out medal);
        }
        static bool GetMedal(PlayerController pc, out TOP.Inventory.InventoryItem medal)
            => OriginalArenaRules.UniqueMedal(Items(pc.GetComponent<PlayerInventory>()), out medal);
        public void ResetServer() { waiting.Clear(); rooms.Clear(); }
        static bool Available(PlayerController pc)
        {
            if (pc == null || !pc.IsInitialized || pc.connectionToClient == null || !pc.connectionToClient.isReady
                || pc.CurrentHp <= 0 || pc.MapName != "garner" || Busy(pc) || pc.IsAboardBoat || pc.BoatOperationPending) return false;
            var bag = pc.GetComponent<PlayerInventory>(); var combat = pc.GetComponent<PlayerCombat>();
            var trade = pc.GetComponent<PlayerTrade>(); var quests = pc.GetComponent<PlayerQuests>(); var mail = pc.GetComponent<PlayerMail>();
            return bag != null && !bag.HasQuestTransaction && combat != null && combat.DuelOpponentNetId == 0
                && (trade == null || !trade.InTrade) && (quests == null || !quests.HasPendingCompletion)
                && (mail == null || !mail.InventoryRequestPending) && HasMedal(pc, out _);
        }
        static List<PlayerController> Team(PlayerController leader, bool party)
        {
            if (!party) return new List<PlayerController> { leader };
            var membership = leader.GetComponent<PlayerParty>();
            if (membership == null || membership.PartyId == 0 || !membership.IsLeader) return null;
            return NetworkServer.spawned.Values.Select(i => i.GetComponent<PlayerController>())
                .Where(p => p != null && p.GetComponent<PlayerParty>() != null && p.GetComponent<PlayerParty>().PartyId == membership.PartyId).ToList();
        }
        static bool ValidTeam(List<PlayerController> team) => team != null && team.Count > 0 && team.Count <= 5
            && team.All(p => Available(p) && Vector3.Distance(p.transform.position, OriginalArenaRules.ArgentBar) <= 25);
        public bool Register(PlayerController pc, bool party, out string error)
        {
            error = null;
            if (!ValidTeam(Team(pc, party))) { error = "Inscricao exige uma medalha 3849 (honra -300..30000), jogadores vivos no Argent Bar, livres de operacoes; grupo: lider e ate 5 membros."; return false; }
            waiting.RemoveAll(r => r.Leader == pc);
            waiting.Add(new Registration { Leader = pc, Party = party, Expires = Time.time + 120 });
            MatchWaiting(); return true;
        }
        public void Cancel(PlayerController pc) { waiting.RemoveAll(r => r.Leader == pc); }
        void MatchWaiting()
        {
            waiting.RemoveAll(r => r.Leader == null || Time.time > r.Expires || !ValidTeam(Team(r.Leader, r.Party)));
            foreach (bool party in new[] { false, true })
            {
                var candidates = waiting.Where(r => r.Party == party).ToArray();
                for (int i = 0; i + 1 < candidates.Length; i += 2)
                    if (StartMatch(candidates[i].Leader, candidates[i + 1].Leader, party, out _))
                    { waiting.Remove(candidates[i]); waiting.Remove(candidates[i + 1]); }
            }
        }
        public bool StartMatch(PlayerController first, PlayerController second, bool party, out string error)
        {
            error = null; var team1 = Team(first, party); var team2 = Team(second, party);
            if (!ValidTeam(team1) || !ValidTeam(team2) || team1.Intersect(team2).Any()
                || (first.GetComponent<PlayerParty>().PartyId != 0 && first.GetComponent<PlayerParty>().PartyId == second.GetComponent<PlayerParty>().PartyId))
            { error = "Equipes invalidas ou aliadas."; return false; }
            int id = Enumerable.Range(1, OriginalArenaRules.Copies).FirstOrDefault(n => !rooms.ContainsKey(n));
            if (id == 0) { error = "As 20 copias estao ocupadas."; return false; }
            var room = new Room { Id = id, Party = party, Count1 = team1.Count, Count2 = team2.Count,
                Average1 = (int)Math.Floor(team1.Average(p => p.Level)), Average2 = (int)Math.Floor(team2.Average(p => p.Level)) };
            room.Players.AddRange(team1); room.Players.AddRange(team2); rooms.Add(id, room); ArenaWorld.Ensure(id);
            foreach (var pc in room.Players)
            {
                pc.ArenaInstanceId = id; pc.ArenaSide = team1.Contains(pc) ? 1 : 2; pc.ArenaResult = 0;
                pc.GetComponent<PlayerMovement>().Stop(); pc.GetComponent<PlayerCombat>().StopAttack();
                var respawn = pc.GetComponent<PlayerRespawn>(); if (respawn != null) respawn.CancelPendingRespawn();
                pc.Teleport("teampk", ArenaWorld.Spawn(id, pc.ArenaSide));
                if (GetMedal(pc, out var medal)) { medal.MedalEntries++; pc.GetComponent<PlayerInventory>().SerializeInventory(); }
                pc.RpcShowMessage("Party PVP " + pc.ArenaSide + " - copia " + id + ". Batalha iniciada.", PlayerMessageType.Info);
                Save(pc);
            }
            Refresh(); return true;
        }
        public void PlayerKilled(PlayerController attacker, PlayerController defeated)
        {
            if (!Opponents(attacker, defeated)) return;
            int honor = OriginalArenaRules.KillHonor(attacker.Level, defeated.Level);
            if (GetMedal(attacker, out var winnerMedal)) { winnerMedal.MedalHonor += honor; winnerMedal.MedalKills++; attacker.GetComponent<PlayerInventory>().SerializeInventory(); }
            if (GetMedal(defeated, out var loserMedal)) { loserMedal.MedalHonor -= honor; loserMedal.MedalDeaths++; defeated.GetComponent<PlayerInventory>().SerializeInventory(); }
            attacker.RpcShowMessage("Honra por morte: +" + honor + ".", PlayerMessageType.Info);
            defeated.RpcShowMessage("Honra por morte: -" + honor + ".", PlayerMessageType.Warning);
            Save(attacker); Save(defeated);
        }
        public static bool Opponents(PlayerController a, PlayerController b) => a != null && b != null
            && a.ArenaInstanceId > 0 && a.ArenaInstanceId == b.ArenaInstanceId && a.ArenaSide != b.ArenaSide
            && a.ArenaResult == 0 && b.ArenaResult == 0;
        void Update()
        {
            if (NetworkServer.active)
            {
                MatchWaiting();
                foreach (var room in rooms.Values.ToArray())
                {
                    if (room.Winner != 0) { if (NetworkTime.time >= room.CloseAt) Close(room); continue; }
                    bool live1 = room.Players.Any(p => p != null && p.ArenaSide == 1 && p.CurrentHp > 0);
                    bool live2 = room.Players.Any(p => p != null && p.ArenaSide == 2 && p.CurrentHp > 0);
                    if (!live1 && live2) Resolve(room, 2); else if (live1 && !live2) Resolve(room, 1);
                    else if (!live1 && !live2) { room.Winner = -1; room.CloseAt = NetworkTime.time + 11; foreach (var p in room.Players) if (p != null) p.ArenaResult = -1; }
                }
            }
            if (NetworkClient.localPlayer != null)
            {
                var pc = NetworkClient.localPlayer.GetComponent<PlayerController>();
                if (pc != null && pc.ArenaInstanceId > 0) ArenaWorld.Ensure(pc.ArenaInstanceId);
            }
        }
        void Resolve(Room room, int winner)
        {
            if (room.Winner != 0) return;
            room.Winner = winner; room.CloseAt = NetworkTime.time + OriginalArenaRules.CloseSeconds;
            foreach (var pc in room.Players) if (pc != null) Award(room, pc, pc.ArenaSide == winner);
        }
        static void Award(Room room, PlayerController pc, bool won)
        {
            if (pc.ArenaResult != 0) return;
            pc.ArenaResult = won ? 1 : -1;
            pc.GetComponent<PlayerCombat>().StopAttack(); pc.GetComponent<PlayerMovement>().Stop();
            if (GetMedal(pc, out var medal))
            {
                int ownCount = pc.ArenaSide == 1 ? room.Count1 : room.Count2, enemyCount = pc.ArenaSide == 1 ? room.Count2 : room.Count1;
                int ownAverage = pc.ArenaSide == 1 ? room.Average1 : room.Average2, enemyAverage = pc.ArenaSide == 1 ? room.Average2 : room.Average1;
                int honor = OriginalArenaRules.HonorChange(won, room.Party, ownCount, enemyCount, ownAverage, enemyAverage);
                medal.MedalHonor += honor; if (won) medal.MedalWins++;
                pc.GetComponent<PlayerInventory>().SerializeInventory();
                pc.RpcShowMessage((won ? "Vitoria!" : "Derrota!") + " Honra " + honor + " (total " + medal.MedalHonor + "). Arena fecha em 11 segundos.", won ? PlayerMessageType.Success : PlayerMessageType.Warning);
            }
            Save(pc);
        }
        public void Leave(PlayerController pc)
        {
            Cancel(pc);
            if (pc == null || !rooms.TryGetValue(pc.ArenaInstanceId, out var room)) return;
            if (room.Winner == 0)
            {
                if (room.Players.Any(p => p != null && p != pc && p.ArenaSide == pc.ArenaSide && p.CurrentHp > 0)) Award(room, pc, false);
                else Resolve(room, pc.ArenaSide == 1 ? 2 : 1);
            }
            room.Players.Remove(pc); Return(pc);
            if (room.Players.Count == 0) rooms.Remove(room.Id);
        }
        void Close(Room room)
        { foreach (var pc in room.Players.ToArray()) if (pc != null) Return(pc); rooms.Remove(room.Id); Refresh(); }
        void Return(PlayerController pc)
        {
            pc.ArenaInstanceId = pc.ArenaSide = pc.ArenaResult = 0;
            if (pc.CurrentHp <= 0) pc.RespawnPlayer();
            pc.Teleport("garner", OriginalArenaRules.ArgentBar);
            pc.GetComponent<PlayerMovement>().Stop(); Save(pc);
        }
        static void Save(PlayerController pc) { if (TOPNetworkManager.Instance != null) TOPNetworkManager.Instance.SavePlayerAfterDeath(pc); }
        static void Refresh() { if (NetworkServer.aoi is ArenaInterestManagement interest) interest.Refresh(); }
        void OnDestroy() { if (Instance == this) Instance = null; ArenaWorld.Reset(); }
    }
}
