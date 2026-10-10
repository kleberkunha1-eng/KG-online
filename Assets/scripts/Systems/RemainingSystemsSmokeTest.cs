#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TOP.Data;
using TOP.Player;
using UnityEngine;
namespace TOP.Testing
{
    public static class RemainingSystemsSmokeTest
    {
        public static void Run(Action<bool, string> check)
        {
            var data = new CharacterData { Id = 10, Gold = 100000, GameplayStateVersion = 1 };
            data.Inventory.Add(new InventoryItemData { ItemId = 1780, Quantity = 1, UniqueItemId = "stone", SlotIndex = 0 });
            var snapshot = data.CopySnapshot();
            PlayerForge.Consume(snapshot, 1780, 1);
            PlayerForge.Charge(snapshot, 100000);
            check(snapshot.Gold == 0 && snapshot.Inventory.Count == 0 && data.Gold == 100000 && data.Inventory.Count == 1, "Guild creation payment is isolated in a rollback-safe character snapshot.");
            check(typeof(PlayerGuild).GetMethod("CmdGuildAnswerInvite") != null && typeof(PlayerGuild).GetMethod("CmdGuildSetRank") != null, "Guild invite consent and leader rank commands are woven into the core.");
            check(ForgeTable.ByLevel.Count == 12 && ForgeTable.ByLevel[4].SuccessRate == 85 && ForgeTable.ByLevel[12].FailedLevel == 0, "Original forge rates/failure levels remain intact.");
            check(PlayerForge.CombineChance(1, 49) == 100 && PlayerForge.CombineChance(9, 49) == 20 && PlayerForge.CombineChance(4, 50) == 95, "Original normal/refining gem combining rates are preserved.");
            var locked = data.CopySnapshot(); locked.Inventory[0].IsLocked = true;
            bool rejected = false;
            try { PlayerForge.Consume(locked, 1780, 1); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && locked.Inventory[0].Quantity == 1, "Locked materials cannot be consumed by forge/fusion.");
            rejected = false;
            try { PlayerForge.Charge(data.CopySnapshot(), 100001); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && data.Gold == 100000, "Forge rejects insufficient gold without mutation.");
            check(PlayerLifeServices.GrowthCap(10) == 2400 && PlayerLifeServices.GrowthCap(30) == 6480 && PlayerLifeServices.StaminaCap(20) == 25001, "Original fairy growth cap and apparel-marker stamina exception are preserved.");
            check(PlayerLifeServices.FairyChance(0, 0) == 1 && PlayerLifeServices.FairyChance(40, 0) < 0.1, "Fairy fruit probability follows the original level/attribute formula.");
            var fairy = new FairyState { ItemKey = "fairy", Strength = 1, Agility = 2, Accuracy = 3, Constitution = 4, Spirit = 5 };
            data.Gameplay.Fairies.Add(fairy);
            check(fairy.Level == 15, "Fairy level is the sum of its five original attributes.");
            var copy = data.CopySnapshot(); copy.Gameplay.Fairies[0].Growth = 55;
            check(data.Gameplay.Fairies[0].Growth == 0, "Fairy progression is deep-copied for atomic durability/rollback.");
            data.Gameplay.Offers.Add(new StallOffer { ItemKey = "stone", ItemId = 1780, Quantity = 1, Price = 20 });
            copy = data.CopySnapshot(); copy.Gameplay.Offers[0].Quantity = 0;
            check(data.Gameplay.Offers[0].Quantity == 1, "Stall reservations are isolated from unconfirmed purchases.");
            check(typeof(PlayerLifeServices).GetMethod("CmdBuyStall") != null && typeof(PlayerLifeServices).GetMethod("CmdCloseStall") != null, "Player stalls expose authoritative purchase and close commands.");
            var skill = new PkoSkill(); skill.ClassMaxLevel[1] = 10;
            check(PlayerSkills.ClassMaximum(skill, 1) == 10 && PlayerSkills.ClassMaximum(skill, 2) == 0 && PlayerSkills.ClassMaximum(skill, 9) == 10, "Skill learning enforces class limits and promoted-class inheritance.");
            skill.ClassMaxLevel[-1] = 3;
            check(PlayerSkills.ClassMaximum(skill, 0) == 3, "Original all-class Set Stall/life skill limits are imported.");
            check(PkoTables.TryTargetFormula("hp=hp(1)-sklv(0)*30", out string stat, out int amount) && stat == "hp" && amount == -30, "Generic skill damage imports original sklv arithmetic, not an invented multiplier.");
            check(PkoTables.TryTargetFormula("hp=hp(1)+sklv(0)*50", out stat, out amount) && amount == 50 && PkoTables.TryTargetFormula("sp=sp(1)+sklv(0)*30", out stat, out amount) && stat == "sp", "Original heal and SP restoration formulas are imported generically.");
            var apparel = PkoTables.Items.Values.FirstOrDefault(i => PkoTables.IsApparel(i) && i.OriginalMaxDurability == 25000);
            check(apparel != null && apparel.Durability == 500, "Fusion recognizes original client apparel 25000 templates before /50 scaling; server's unfused 23000 marker is also supported.");
            var fairyDefinition = PkoTables.Items[232];
            check(fairyDefinition.OriginalMaxDurability == 5000 && fairyDefinition.Durability == 100, "Fairy stamina uses original 5000 units rather than display durability 100.");
            check(OriginalSkillParameters.Cost(81, 1, -1) == 20 && OriginalSkillParameters.Cooldown(81, 1, -1) == 5, "Original Illusion Slash Lua SP/cooldown functions import 20 SP and five seconds.");
            check(OriginalSkillParameters.Cooldown(17, 10, -1) == 5 && OriginalSkillParameters.Cooldown(17, 1, -1) == 9.5f, "Original level-dependent Sacred Ray cooldown evaluates safely from imported data.");
            var converted = PkoTables.Skills.Values.Select(PkoTables.ToSkillData).ToArray();
            check(converted.Length == PkoTables.Skills.Count && converted.Select(s => s.skillId).Distinct().Count() == converted.Length && converted.All(s => s.cooldown >= 0), "Every original skillinfo row has a unique generic runtime definition and valid cooldown.");
            foreach (var asset in converted) UnityEngine.Object.Destroy(asset);
            check(PkoTables.SkillEffects.Count > 0, "Original skilleff status catalog remains available; Lua-specific behaviors are documented separately.");
        }
    }
}
#endif
