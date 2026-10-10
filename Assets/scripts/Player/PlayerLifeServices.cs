using System;
using System.Linq;
using Mirror;
using TOP.Data;
using UnityEngine;
namespace TOP.Player
{
    public partial class PlayerLifeServices : NetworkBehaviour
    {
        [SyncVar] public int FairyGrowth, FairyStamina, FairyLevel;
        [SyncVar] public string StallName = "";
        [SyncVar] public string StallData = "";
        PlayerController pc;
        PlayerInventory inventory;
        double nextFairyTick;
        void Awake() { pc = GetComponent<PlayerController>(); inventory = GetComponent<PlayerInventory>(); }
        public bool StallActive => !string.IsNullOrEmpty(StallName);
        public static int GrowthCap(int level) => Math.Min(6480, 240 * Math.Max(1, level));
        public static int StaminaCap(int level) { int n = Math.Min(32000, 5000 + 1000 * level); return n == 25000 ? 25001 : n; }
        public static double FairyChance(int level, int attribute) => Math.Min(1, 1 / (Math.Floor((1 + Math.Pow(level / 10.0, 3)) * 10) / 10 * Math.Max(0.01, 1 - attribute * 0.05)));
        public static FairyState EquippedFairy(CharacterData data)
        {
            var item = data.Inventory.Find(i => i.IsEquipped && PkoTables.Items.TryGetValue(i.ItemId, out var definition) && definition.Type == 59);
            if (item == null || string.IsNullOrEmpty(item.UniqueItemId)) return null;
            var state = data.Gameplay.Fairies.Find(i => i.ItemKey == item.UniqueItemId);
            if (state == null) { state = new FairyState { ItemKey = item.UniqueItemId, Stamina = Math.Min(5000, Math.Max(49, (int)item.Durability * 50)) }; data.Gameplay.Fairies.Add(state); }
            return state;
        }
        [Server]
        public void Synchronize()
        {
            if (pc == null || !pc.IsInitialized) return;
            var data = pc.GetCharacterData();
            var fairy = EquippedFairy(data);
            FairyGrowth = fairy != null ? fairy.Growth : 0;
            FairyStamina = fairy != null ? fairy.Stamina : 0;
            FairyLevel = fairy != null ? fairy.Level : 0;
            StallName = data.Gameplay.StallName ?? "";
            StallData = JsonUtility.ToJson(data.Gameplay);
        }
        void Update()
        {
            if (!isServer || pc == null || !pc.IsInitialized || inventory.HasQuestTransaction || StallActive) return;
            Synchronize();
            if (nextFairyTick == 0) { nextFairyTick = NetworkTime.time + 60 + Math.Max(0, FairyLevel - 27) * 5; return; }
            if (NetworkTime.time < nextFairyTick) return;
            nextFairyTick = NetworkTime.time + 60 + Math.Max(0, FairyLevel - 27) * 5;
            TickFairy();
        }
        [Server]
        async void TickFairy()
        {
            var stats = GetComponent<PlayerStats>();
            if (stats == null || stats.IsDead || pc.IsAboardBoat || EquippedFairy(pc.GetCharacterData()) == null || FairyStamina <= 49) return;
            await PlayerDurableAction.Run(pc, data =>
            {
                var fairy = EquippedFairy(data);
                if (fairy == null) return;
                fairy.Stamina = Math.Max(49, fairy.Stamina - 50);
                if (fairy.Stamina > 49) fairy.Growth = Math.Min(GrowthCap(fairy.Level), fairy.Growth + 1);
            }, data => Synchronize());
        }
        [Command]
        public async void CmdFeedFairy(int foodSlot)
        {
            await PlayerDurableAction.Run(pc, data =>
            {
                var fairy = EquippedFairy(data);
                if (fairy == null) throw new InvalidOperationException("Equipe uma fairy no slot Pet.");
                var food = PlayerForge.Available(data, foodSlot);
                int ration = food.ItemId == 227 || food.ItemId == 2312 ? 50 : food.ItemId == 3152 ? 5 : food.ItemId == 6841 || food.ItemId == 6842 ? 100 : 0;
                if (ration > 0) fairy.Stamina = Math.Min(StaminaCap(fairy.Level), fairy.Stamina + ration * 50);
                else
                {
                    int attribute = food.ItemId - 222, gain = 1;
                    if (food.ItemId >= 276 && food.ItemId <= 280) { attribute = food.ItemId - 276; gain = 2; }
                    if (attribute < 0 || attribute > 4 || fairy.Level + gain > 42 || fairy.Growth < GrowthCap(fairy.Level) || fairy.Stamina <= 49) throw new InvalidOperationException("Fruta normal/grande exige growth cheio e fairy abaixo do nivel 42.");
                    int[] attributes = { fairy.Strength, fairy.Agility, fairy.Accuracy, fairy.Constitution, fairy.Spirit };
                    bool success = UnityEngine.Random.value < FairyChance(fairy.Level, attributes[attribute]);
                    fairy.Growth = success ? 0 : fairy.Growth / 2;
                    if (success) attributes[attribute] += gain;
                    fairy.Strength = attributes[0]; fairy.Agility = attributes[1]; fairy.Accuracy = attributes[2]; fairy.Constitution = attributes[3]; fairy.Spirit = attributes[4];
                }
                if (--food.Quantity == 0) data.Inventory.Remove(food);
            }, data => Synchronize());
        }
    }
}
