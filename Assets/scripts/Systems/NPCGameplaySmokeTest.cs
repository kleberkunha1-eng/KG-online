#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using kcp2k;
using Mirror;
using TOP.Core;
using TOP.Data;
using TOP.NPC;
using TOP.Player;
using UnityEngine;

namespace TOP.Testing
{
    public static class NPCGameplaySmokeTest
    {
        static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        static uint openedNpc;
        static int[] openedItems;

        public static void Receive(RpcMessage rpc)
        {
            if (rpc.functionHash != AdminGenerationSmokeTest.FunctionHash("TargetOpenDialogue")) return;
            using (var reader = NetworkReaderPool.Get(rpc.payload))
            {
                reader.Read<string[]>();
                openedItems = reader.Read<int[]>();
                reader.Read<int[]>();
                reader.Read<int[]>();
                openedNpc = rpc.netId;
            }
        }

        static IEnumerator Pump(Action tick, float seconds = .6f)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) { tick(); yield return null; }
        }

        public static IEnumerator Run(GameObject player, KcpClient client, Action tick, Action<bool, string> check)
        {
            var actual = UnityEngine.Object.FindObjectsByType<NPCInteractable>(FindObjectsSortMode.None)
                .Where(npc => npc.gameObject.scene.name == "GameScene").ToArray();
            check(actual.Length > 0, "World contains interactable original NPCs: " + actual.Length);
            check(actual.Single(n => n.NpcName == "Blacksmith - Goldie").ShopItemIds().Length == 78
                && actual.Single(n => n.NpcName == "Tailor - Granny Nila").ShopItemIds().Length == 39
                && actual.Single(n => n.NpcName == "Physican - Ditto").ShopItemIds().Length == 14,
                "Saved GameScene has exactly 78/39/14 verified original shop items at Goldie/Nila/Ditto.");
            check(actual.Single(n => n.NpcName == "Hairstylist - Cartel").NpcType == NPCType.Hairdresser
                && actual.Where(n => new[] { "Castle Guard - Peter", "Citizen - Margaret", "Granny Beldi", "Little Daniel",
                    "Mysterious Granny" }.Contains(n.NpcName)).All(n => n.NpcType == NPCType.Other),
                "Saved GameScene routes Cartel to hair salon and ordinary NPCs to dialogue, not accidental salons.");
            check(actual.Single(n => n.NpcName == "Physican - Ditto").Recipes.Count == 4
                && actual.Single(n => n.NpcName == "Nurse - Gina").FullHealCost == 200
                && actual.Single(n => n.NpcName == "Nurse - Gina").NpcType == NPCType.Healer,
                "Saved GameScene includes four verified Ditto recipes and Gina's original 200-gold recovery.");
            var catalogReport = actual.Select(npc => npc.NpcId + " | " + npc.NpcName + " | " + npc.NpcType
                + " | shop-items=" + npc.ShopItemIds().Length + " | active=" + npc.isActiveAndEnabled).ToArray();
            System.IO.File.WriteAllLines("Tools/npc-catalog-audit.txt", catalogReport);
            var root = new GameObject("Synthetic Merchant", typeof(NetworkIdentity), typeof(NPCInteractable));
            var npc = root.GetComponent<NPCInteractable>();
            var controller = player.GetComponent<PlayerController>();
            var inventory = player.GetComponent<PlayerInventory>();
            var movement = player.GetComponent<PlayerMovement>();
            var stats = player.GetComponent<PlayerStats>();
            bool statsEnabled = stats.enabled;
            var quests = player.GetComponent<PlayerQuests>();
            var item = PkoTables.Items.Values.First(i => i.Price >= 2 && i.Stack >= 2 && i.Tradeable);
            try
            {
                stats.enabled = false;
                openedNpc = 0;
                openedItems = null;
                typeof(NPCInteractable).GetField("npcId", Fields).SetValue(npc, "synthetic-merchant");
                typeof(NPCInteractable).GetField("npcName", Fields).SetValue(npc, "Synthetic Merchant");
                typeof(NPCInteractable).GetField("npcType", Fields).SetValue(npc, NPCType.Merchant);
                typeof(NPCInteractable).GetField("shopItems", Fields).SetValue(npc,
                    new[] { item.Id.ToString(), item.Id.ToString(), "invalid", "-1" });
                typeof(NPCInteractable).GetField("availableQuests", Fields).SetValue(npc, new[] { "1" });
                typeof(NPCInteractable).GetField("completesQuests", Fields).SetValue(npc, new[] { "1" });
                root.transform.position = player.transform.position + Vector3.right * 6;
                typeof(NetworkIdentity).GetMethod("InitializeNetworkBehaviours", Fields).Invoke(npc.netIdentity ?? root.GetComponent<NetworkIdentity>(), null);
                NetworkServer.Spawn(root);
                stats.SetCurrentHpMpSp(stats.MaxHp, stats.MaxMp, stats.MaxSp);
                player.GetComponent<PlayerCombat>().StopAttack();
                movement.Stop();
                inventory.InitializeFromData(new List<InventoryItemData>());
                controller.Gold = (ulong)item.Price * 10;
                ulong gold = controller.Gold;
                check(npc.ShopItemIds().SequenceEqual(new[] { item.Id }), "NPC catalog rejects invalid IDs and removes duplicate entries.");
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.FindEmptySlot() == 0,
                    "Buying before talking to the merchant is rejected without charging gold.");
                SocialGameplaySmokeTest.Send(client, movement, "CmdInteractWithNpc", w => w.WriteNetworkIdentity(npc.netIdentity));
                yield return Pump(tick, 1.8f);
                check(movement.ActiveNpc == npc && !movement.IsMoving
                    && Vector3.Distance(player.transform.position, root.transform.position) <= npc.InteractionRange,
                    "Clicking a nearby NPC actually approaches, stops inside range and opens that NPC.");
                check(openedNpc == npc.netId && openedItems != null && openedItems.SequenceEqual(new[] { item.Id }),
                    "NPC dialogue and authoritative catalog arrive over a real owner-targeted KCP RPC.");
                check(quests.CanUseQuestNpc(1, false) && quests.CanUseQuestNpc(1, true),
                    "Quest context recognizes only configured offers/turn-ins of the current nearby NPC.");
                check(!quests.CanUseQuestNpc(704, false), "Unlisted quest cannot be offered by the current NPC.");
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(2); });
                yield return Pump(tick);
                check(inventory.GetSlot(0)?.ItemId == item.Id && inventory.GetSlot(0)?.Quantity == 2
                    && controller.Gold == gold - (ulong)item.Price * 2, "Real KCP purchase gives exact quantity and charges the original table price once.");
                gold = controller.Gold;
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(-1); });
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(item.Stack + 1); });
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(int.MaxValue); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.GetInventoryData().Count == 1,
                    "Negative/excess quantities and non-catalog items cannot change inventory or gold.");
                SocialGameplaySmokeTest.Send(client, npc, "CmdSellItem", w => { w.Write(0); w.Write(1); });
                yield return Pump(tick);
                check(inventory.GetSlot(0)?.Quantity == 1 && controller.Gold == gold + (ulong)(item.Price / 2),
                    "Sale uses original half-price rule and removes only the requested quantity.");
                gold = controller.Gold;
                inventory.SetItemRefine(0, 2);
                SocialGameplaySmokeTest.Send(client, npc, "CmdSellItem", w => { w.Write(0); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.GetSlot(0)?.RefineLevel == 2,
                    "Refined gear cannot accidentally be sold for its base price.");
                var snapshot = inventory.GetInventoryData();
                snapshot[0].RefineLevel = 0;
                snapshot[0].GemSlot1 = 10;
                inventory.InitializeFromData(snapshot);
                SocialGameplaySmokeTest.Send(client, npc, "CmdSellItem", w => { w.Write(0); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.GetSlot(0)?.Gems[0] == 10, "Socketed items are protected from NPC sale.");
                snapshot[0].GemSlot1 = null;
                snapshot[0].IsEquipped = true;
                inventory.InitializeFromData(snapshot);
                SocialGameplaySmokeTest.Send(client, npc, "CmdSellItem", w => { w.Write(0); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.GetSlot(0)?.IsEquipped == true, "Equipped items cannot be sold.");
                inventory.InitializeFromData(new List<InventoryItemData>());
                controller.Gold = 0;
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == 0 && inventory.FindEmptySlot() == 0, "Insufficient gold purchase leaves inventory untouched.");
                controller.Gold = gold;
                var full = Enumerable.Range(0, inventory.totalSlots).Select(i => new InventoryItemData
                    { SlotIndex = (ushort)i, ItemId = item.Id, Quantity = 1 }).ToList();
                inventory.InitializeFromData(full);
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.GetInventoryData().Count == 40,
                    "Full inventory purchase does not charge gold.");
                inventory.InitializeFromData(new List<InventoryItemData>());
                player.transform.position = root.transform.position + Vector3.right * 3;
                check(npc.CanInteract(movement), "NPC interaction allows exactly the configured 3-meter range.");
                player.transform.position = root.transform.position + Vector3.right * 3.01f;
                check(!npc.CanInteract(movement) && !quests.CanUseQuestNpc(1, false),
                    "NPC/quest interaction rejects distances above the configured range.");
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.FindEmptySlot() == 0,
                    "Leaving the shop range revokes purchase permission server-side.");
                player.transform.position = root.transform.position + Vector3.right;
                controller.MapName = "different-fixture-map";
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.FindEmptySlot() == 0 && !quests.CanUseQuestNpc(1, false),
                    "NPC shop and quests reject same coordinates on a different map.");
                controller.MapName = "garner";
                stats.SetCurrentHpMpSp(0, stats.MaxMp, stats.MaxSp);
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(1); });
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.FindEmptySlot() == 0 && !npc.CanInteract(movement),
                    "Dead players cannot interact or buy even with a previously opened shop.");
                stats.SetCurrentHpMpSp(stats.MaxHp, stats.MaxMp, stats.MaxSp);
                SocialGameplaySmokeTest.Send(client, npc, "CmdBuyItem", w => { w.Write(item.Id); w.Write(1); });
                yield return Pump(tick);
                check(inventory.GetSlot(0)?.Quantity == 1, "Valid purchase continues working after rejected requests.");

                var recipe = new NPCRecipe { MaterialItemId = 3129, ResultItemId = 3133 };
                typeof(NPCInteractable).GetField("recipes", Fields).SetValue(npc, new[] { recipe });
                inventory.InitializeFromData(new List<InventoryItemData>
                {
                    new InventoryItemData { SlotIndex = 0, ItemId = 1779, Quantity = 2 },
                    new InventoryItemData { SlotIndex = 1, ItemId = 3129, Quantity = 4 },
                    new InventoryItemData { SlotIndex = 2, ItemId = 3129, Quantity = 6 },
                    new InventoryItemData { SlotIndex = 3, ItemId = item.Id, Quantity = 1, RefineLevel = 3,
                        GemSlot1 = 10, Durability = 17 }
                });
                controller.Gold = 100;
                SocialGameplaySmokeTest.Send(client, npc, "CmdCraftRecipe", w => w.Write(3133));
                yield return Pump(tick);
                check(controller.Gold == 50 && inventory.GetItemCount(1779) == 1 && inventory.GetItemCount(3129) == 0
                    && inventory.GetItemCount(3133) == 1, "Recipe atomically consumes one bottle, ten materials across stacks and exactly 50 gold.");
                check(inventory.GetSlot(3)?.RefineLevel == 3 && inventory.GetSlot(3)?.Gems[0] == 10
                    && inventory.GetSlot(3)?.Durability == 17, "Recipe preserves attributes and slots of unrelated equipment.");
                gold = controller.Gold;
                SocialGameplaySmokeTest.Send(client, npc, "CmdCraftRecipe", w => w.Write(3133));
                SocialGameplaySmokeTest.Send(client, npc, "CmdCraftRecipe", w => w.Write(int.MaxValue));
                yield return Pump(tick);
                check(controller.Gold == gold && inventory.GetItemCount(1779) == 1 && inventory.GetItemCount(3133) == 1,
                    "Missing materials or unconfigured recipe cannot consume the bottle, gold or existing output.");
                var recipeItems = new List<InventoryItemData>
                {
                    new InventoryItemData { SlotIndex = 0, ItemId = 1779, Quantity = 2 },
                    new InventoryItemData { SlotIndex = 1, ItemId = 3129, Quantity = 20 }
                };
                inventory.InitializeFromData(recipeItems);
                controller.Gold = 49;
                SocialGameplaySmokeTest.Send(client, npc, "CmdCraftRecipe", w => w.Write(3133));
                yield return Pump(tick);
                check(controller.Gold == 49 && inventory.GetItemCount(1779) == 2 && inventory.GetItemCount(3129) == 20,
                    "Insufficient recipe gold leaves all materials untouched.");
                for (int i = 2; i < inventory.totalSlots; i++)
                    recipeItems.Add(new InventoryItemData { SlotIndex = (ushort)i, ItemId = item.Id, Quantity = 1 });
                inventory.InitializeFromData(recipeItems);
                controller.Gold = 100;
                SocialGameplaySmokeTest.Send(client, npc, "CmdCraftRecipe", w => w.Write(3133));
                yield return Pump(tick);
                check(controller.Gold == 100 && inventory.GetItemCount(1779) == 2 && inventory.GetItemCount(3129) == 20,
                    "Full recipe output inventory never partially consumes ingredients or gold.");
                inventory.InitializeFromData(new List<InventoryItemData>());
                typeof(NPCInteractable).GetField("fullHealCost", Fields).SetValue(npc, 200);
                typeof(NPCInteractable).GetField("noviceHealWaiver", Fields).SetValue(npc, true);
                stats.SetCurrentHpMpSp(1, 1, 1);
                controller.Gold = 199;
                SocialGameplaySmokeTest.Send(client, npc, "CmdFullHeal", w => { });
                yield return Pump(tick);
                check(stats.CurrentHp == 1 && controller.Gold == 199, "Original full-heal cost is enforced before healing.");
                controller.Gold = 300;
                SocialGameplaySmokeTest.Send(client, npc, "CmdFullHeal", w => { });
                yield return Pump(tick);
                check(stats.CurrentHp == stats.MaxHp && stats.CurrentMp == stats.MaxMp && stats.CurrentSp == stats.MaxSp
                    && controller.Gold == 100 && controller.CurrentHp == stats.MaxHp,
                    "Paid full recovery restores and synchronizes all resources, charging exactly 200 gold.");
                SocialGameplaySmokeTest.Send(client, npc, "CmdFullHeal", w => { });
                yield return Pump(tick);
                check(controller.Gold == 100, "No healing charge when all resources are already full.");
                var completed = (HashSet<int>)typeof(PlayerQuests).GetField("_completed", Fields).GetValue(quests);
                completed.Add(500);
                int level = controller.Level;
                controller.Level = 5;
                stats.SetCurrentHpMpSp(1, 1, 1);
                controller.Gold = 0;
                SocialGameplaySmokeTest.Send(client, npc, "CmdFullHeal", w => { });
                yield return Pump(tick);
                check(stats.CurrentHp == stats.MaxHp && controller.Gold == 0,
                    "Original novice waiver requires completed record 500 and permits healing at level 5 without gold.");
                controller.Level = 6;
                stats.SetCurrentHpMpSp(1, 1, 1);
                SocialGameplaySmokeTest.Send(client, npc, "CmdFullHeal", w => { });
                yield return Pump(tick);
                check(stats.CurrentHp == 1 && controller.Gold == 0, "Novice recovery waiver expires exactly at level 6.");
                completed.Remove(500);
                controller.Level = level;
                stats.SetCurrentHpMpSp(stats.MaxHp, stats.MaxMp, stats.MaxSp);
                player.transform.position = root.transform.position + Vector3.right * 10;
                SocialGameplaySmokeTest.Send(client, movement, "CmdInteractWithNpc", w => w.WriteNetworkIdentity(npc.netIdentity));
                yield return Pump(tick, .2f);
                SocialGameplaySmokeTest.Send(client, movement, "CmdMoveTo", w => w.WriteVector3(player.transform.position));
                yield return Pump(tick, .5f);
                check(movement.ActiveNpc == null && !movement.IsMoving,
                    "A new ground movement command cancels the pending NPC approach.");
            }
            finally { stats.enabled = statsEnabled; movement.Stop(); NetworkServer.Destroy(root); }
        }
    }
}
#endif
