using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TOP.Core;
using TOP.Data;
using TOP.NPC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OriginalNpcServicesImport
{
    const string PrivateScript = @"E:\PrivateTop\meu_servidor\server\GameServer\resource\script\MisScript\NpcScript01.lua";
    const string ReferenceScript = @"E:\ToP Server,client,Db,tools\ToP Server,client,Db,tools\GameServer\resource\script\MisScript\NpcScript01.lua";

    static string Function(string script, string function)
    {
        var match = Regex.Match(script, @"(?ms)^function\s+" + Regex.Escape(function) + @"\s*\(.*?(?=^function\s|\z)");
        if (!match.Success) throw new InvalidDataException("Original NPC function not found: " + function);
        return match.Value;
    }

    static int[] Stock(string script, string function)
    {
        string body = Function(script, function);
        if (!Regex.IsMatch(body, @"(?m)^\s*InitTrade\s*\(\s*\)"))
            throw new InvalidDataException("Original NPC stock not found: " + function);
        var items = Regex.Matches(body, @"(?m)^\s*(?:Weapon|Defence|Other)\s*\(\s*(\d+)\s*\)")
            .Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
        if (items.Length == 0) throw new InvalidDataException("Empty original stock: " + function);
        return items;
    }

    [MenuItem("Tools/PKO/Import Verified Argent NPC Services")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before importing NPC services.");
        string privateSource = File.ReadAllText(PrivateScript);
        string referenceSource = File.ReadAllText(ReferenceScript);
        foreach (string function in new[] { "r_talk03", "r_talk04" })
        {
            Func<string, string[]> calls = source => Regex.Matches(Function(source, function),
                @"(?m)^\s*(?:TriggerCondition|TriggerAction)\s*\([^\r\n]*").Cast<Match>()
                .Select(match => Regex.Replace(match.Value, @"\s", "")).ToArray();
            if (!calls(privateSource).SequenceEqual(calls(referenceSource)))
                throw new InvalidDataException("Original recipe/healing conditions disagree: " + function);
        }
        var recipeMatches = Regex.Matches(Function(privateSource, "r_talk03"),
            @"TriggerAction\s*\(1,\s*TakeMoney,\s*50\)\s*TriggerAction\s*\(1,\s*TakeItem,\s*(\d+),\s*10\)\s*TriggerAction\s*\(1,\s*TakeItem,\s*1779,\s*1\)\s*TriggerAction\s*\(1,\s*GiveItem,\s*(\d+),\s*1\s*,\s*4\)");
        var recipes = recipeMatches.Cast<Match>().Select(match => new NPCRecipe
        {
            MaterialItemId = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
            ResultItemId = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)
        }).ToArray();
        if (!recipes.Select(recipe => recipe.ResultItemId).SequenceEqual(new[] { 3133, 3134, 3135, 3136 }))
            throw new InvalidDataException("Original Ditto recipe structure changed; import aborted.");
        foreach (var recipe in recipes)
            foreach (int item in new[] { recipe.BottleItemId, recipe.MaterialItemId, recipe.ResultItemId })
                if (!PkoTables.Items.ContainsKey(item)) throw new InvalidDataException("Missing original recipe item: " + item);
        var targets = new[]
        {
            (name: "Blacksmith - Goldie", function: "r_trade01", type: NPCType.Blacksmith, count: 78),
            (name: "Tailor - Granny Nila", function: "BT_NpcSale001", type: NPCType.Merchant, count: 39),
            (name: "Physican - Ditto", function: "r_talk03", type: NPCType.Healer, count: 14)
        };
        var stocks = new Dictionary<string, int[]>();
        foreach (var target in targets)
        {
            var stock = Stock(privateSource, target.function);
            if (stock.Length != target.count || !stock.SequenceEqual(Stock(referenceSource, target.function)))
                throw new InvalidDataException("Original sources disagree for " + target.name + "; import aborted.");
            foreach (int item in stock)
                if (!PkoTables.Items.ContainsKey(item))
                    throw new InvalidDataException("Original item absent from active client catalog: " + item);
            stocks.Add(target.name, stock.Distinct().ToArray());
        }

        TOPAutoSave.SaveNow();
        const string scenePath = "Assets/Scenes/GameScene.unity";
        var scene = EditorSceneManager.OpenScene(scenePath);
        var npcs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<NPCInteractable>(true)).ToArray();
        foreach (var target in targets)
        {
            if (npcs.Count(npc => npc.NpcName == target.name) != 1)
                throw new InvalidDataException("Missing/duplicate scene NPC: " + target.name);
            var existing = new SerializedObject(npcs.Single(npc => npc.NpcName == target.name)).FindProperty("shopItems");
            if (existing.arraySize != 0 && !Enumerable.Range(0, existing.arraySize)
                .Select(i => existing.GetArrayElementAtIndex(i).stringValue)
                .SequenceEqual(stocks[target.name].Select(id => id.ToString(CultureInfo.InvariantCulture))))
                throw new InvalidDataException("Existing custom shop would be overwritten: " + target.name);
        }
        var corrections = new Dictionary<string, NPCType>
        {
            ["Castle Guard - Peter"] = NPCType.Other,
            ["Citizen - Margaret"] = NPCType.Other,
            ["Granny Beldi"] = NPCType.Other,
            ["Little Daniel"] = NPCType.Other,
            ["Mysterious Granny"] = NPCType.Other,
            ["Hairstylist - Cartel"] = NPCType.Hairdresser,
            ["Nurse - Gina"] = NPCType.Healer
        };
        foreach (string name in corrections.Keys)
            if (npcs.Count(npc => npc.NpcName == name) != 1)
                throw new InvalidDataException("Missing/duplicate service correction NPC: " + name);
        var ditto = new SerializedObject(npcs.Single(npc => npc.NpcName == "Physican - Ditto"));
        var existingRecipes = ditto.FindProperty("recipes");
        if (existingRecipes.arraySize != 0)
        {
            if (existingRecipes.arraySize != recipes.Length) throw new InvalidDataException("Custom recipes would be overwritten.");
            for (int i = 0; i < recipes.Length; i++)
            {
                var existing = existingRecipes.GetArrayElementAtIndex(i);
                if (existing.FindPropertyRelative("ResultItemId").intValue != recipes[i].ResultItemId
                    || existing.FindPropertyRelative("MaterialItemId").intValue != recipes[i].MaterialItemId
                    || existing.FindPropertyRelative("MaterialQuantity").intValue != 10
                    || existing.FindPropertyRelative("BottleItemId").intValue != 1779
                    || existing.FindPropertyRelative("GoldCost").intValue != 50)
                    throw new InvalidDataException("Custom recipe would be overwritten.");
            }
        }
        var gina = new SerializedObject(npcs.Single(npc => npc.NpcName == "Nurse - Gina"));
        if (gina.FindProperty("fullHealCost").intValue >= 0 && gina.FindProperty("fullHealCost").intValue != 200)
            throw new InvalidDataException("Custom healer cost would be overwritten.");
        string recovery = Path.Combine("Library", "TOPAutosave", "NpcServices",
            DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".unity");
        Directory.CreateDirectory(Path.GetDirectoryName(recovery));
        File.Copy(scenePath, recovery, false);
        var report = new List<string> { "Scene backup: " + recovery, "NPC transforms/visuals/terrain/spawns unchanged." };
        foreach (var target in targets)
        {
            var npc = npcs.Single(n => n.NpcName == target.name);
            var serialized = new SerializedObject(npc);
            serialized.FindProperty("npcType").intValue = (int)target.type;
            var items = serialized.FindProperty("shopItems");
            var stock = stocks[target.name];
            items.arraySize = stock.Length;
            for (int i = 0; i < stock.Length; i++) items.GetArrayElementAtIndex(i).stringValue = stock[i].ToString(CultureInfo.InvariantCulture);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(npc);
            report.Add("Imported " + target.name + ": " + stock.Length + " original items; "
                + npc.ShopItemIds().Length + " purchasable (positive table price and tradeable). Function: " + target.function);
        }
        foreach (var correction in corrections)
        {
            var npc = npcs.Single(n => n.NpcName == correction.Key);
            var serialized = new SerializedObject(npc);
            var property = serialized.FindProperty("npcType");
            report.Add("Service: " + correction.Key + " " + property.intValue + " -> " + (int)correction.Value);
            property.intValue = (int)correction.Value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(npc);
        }
        ditto.Update();
        gina.Update();
        var serializedRecipes = ditto.FindProperty("recipes");
        serializedRecipes.arraySize = recipes.Length;
        for (int i = 0; i < recipes.Length; i++)
        {
            var entry = serializedRecipes.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("ResultItemId").intValue = recipes[i].ResultItemId;
            entry.FindPropertyRelative("MaterialItemId").intValue = recipes[i].MaterialItemId;
            entry.FindPropertyRelative("MaterialQuantity").intValue = 10;
            entry.FindPropertyRelative("BottleItemId").intValue = 1779;
            entry.FindPropertyRelative("GoldCost").intValue = 50;
        }
        ditto.ApplyModifiedPropertiesWithoutUndo();
        gina.FindProperty("fullHealCost").intValue = 200;
        gina.FindProperty("noviceHealWaiver").boolValue = true;
        gina.ApplyModifiedPropertiesWithoutUndo();
        report.Add("Ditto: 4 verified recipes (1 bottle + 10 materials + 50 gold -> 1 potion).");
        report.Add("Gina: full recovery 200 gold; original waiver requires level <6 and completed mission record 500.");
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save imported NPC services.");
        report.Add("Jimberry stock NOT imported: preserved server references differ (67 vs 69 entries).");
        report.Add("No quests, bank balances, teleport destinations or ship services were invented.");
        File.WriteAllLines("Tools/npc-services-import-results.txt", report);
        Debug.Log(string.Join("\n", report));
    }
}
