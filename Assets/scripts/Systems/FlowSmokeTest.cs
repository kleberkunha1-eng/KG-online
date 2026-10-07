#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using TOP.Core;
using TOP.UI.Pko;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TOP.Testing
{
    // Teste ponta a ponta do fluxo real: login -> criar personagem -> selecionar -> entrar na GameScene.
    // Executa apenas quando o editor pede via Tools/validate-flow.request.
    [InitializeOnLoad]
    public class FlowSmokeTest : MonoBehaviour
    {
        const string Request = "Tools/validate-flow.request";
        readonly List<string> lines = new List<string>();

        static FlowSmokeTest() { EditorApplication.update += Poll; }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            File.Delete(Request);
            SessionState.SetBool("TOP.RunFlowTest", true);
            EditorSceneManager.OpenScene("Assets/Scenes/LoginScene.unity");
            EditorApplication.EnterPlaymode();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (!SessionState.GetBool("TOP.RunFlowTest", false)) return;
            SessionState.SetBool("TOP.RunFlowTest", false);
            var go = new GameObject("FlowSmokeTest"); DontDestroyOnLoad(go); go.AddComponent<FlowSmokeTest>();
        }

        static string AnimInfo(TOP.Character.PkoCharacterVisual v)
        {
            var a = v.GetComponentInChildren<Animation>(true);
            if (a == null) return " anim=NONE";
            int n = 0; foreach (AnimationState s in a) n++;
            return " anim clips=" + n + " playing=" + a.isPlaying + " enabled=" + a.enabled + " driver=" + (a.GetComponent<LegacyAnimDriver>() != null) + " active=" + a.gameObject.activeInHierarchy;
        }

        void DumpVisuals(string tag)
        {
            var vs = FindObjectsByType<TOP.Character.PkoCharacterVisual>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Note(tag + ": visuals=" + vs.Length + " skinned=" + FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length);
            foreach (var v in vs) Note("  " + v.transform.parent.name + " race=" + v.Race + " pos=" + v.transform.position + " rigs=" + v.GetComponentsInChildren<Animation>(true).Length + AnimInfo(v));
        }

        static void Drop(PkoSlot from, PkoSlot to)
        {
            to.OnDrop(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { pointerDrag = from.gameObject });
        }

        // Picks a wearable of the original table for the character: right level/class, model for its race that exists as baked asset.
        static TOP.Core.CharacterClass testClass = TOP.Core.CharacterClass.Swordsman;
        static bool IsRing(EquipmentSlot s) { return s == EquipmentSlot.Ring1 || s == EquipmentSlot.Ring2; }
        static bool IsOffhandSword(TOP.Data.PkoItem it) { return it.Type == 1 && System.Array.IndexOf(it.EquipSlots, 6) >= 0; }
        static bool SlotMatches(TOP.Data.PkoItem it, EquipmentSlot itemSlot, EquipmentSlot want, bool offhand)
        {
            if (itemSlot == want) return true;
            if (IsRing(itemSlot) && IsRing(want)) return true;
            return offhand && want == EquipmentSlot.Shield && itemSlot == EquipmentSlot.Weapon && IsOffhandSword(it);
        }

        static bool HasRaceModel(TOP.Data.PkoItem it, int race)
        {
            string m = it.RaceModels != null && race >= 0 && race < it.RaceModels.Length ? (it.RaceModels[race] ?? "").TrimEnd('_') : "";
            if (string.IsNullOrEmpty(m) || m == "0") return false;
            return it.Type >= 20 ? m.Length == 10 && Resources.Load<TOP.Character.PkoPartAsset>("PkoChar/Parts/" + m) != null
                                 : Resources.Load<GameObject>("PkoChar/Weapons/" + m) != null;
        }

        static int PickItem(int type, int race, int job, int level, int skip = 0, EquipmentSlot? want = null, bool offhand = false)
        {
            foreach (var it in TOP.Data.PkoTables.Items.Values)
            {
                if (it.Type != type || it.Level > level || it.EquipSlots.Length == 0 || (offhand && System.Array.IndexOf(it.EquipSlots, 6) < 0)) continue;
                var ds = TOP.Inventory.ItemDatabase.Instance?.GetEquipment(it.Id); if (ds == null || ds.slot != TOP.Data.PkoTables.SlotOf(it) || (want.HasValue && !SlotMatches(it, ds.slot, want.Value, offhand))) continue;
                if (!TOP.Data.PkoClasses.Allows(it.Classes, testClass) || !TOP.Data.PkoClasses.RaceOk(it, race)) continue;
                if (HasRaceModel(it, race) && skip-- <= 0) return it.Id;
            }
            return 0;
        }

        static int PickSlotItem(EquipmentSlot want, int race, int job, int level, HashSet<int> used, bool requireVisible = false, bool offhand = false)
        {
            foreach (var it in TOP.Data.PkoTables.Items.Values)
            {
                if (used.Contains(it.Id) || it.Level > level || it.EquipSlots.Length == 0) continue;
                var ds = TOP.Inventory.ItemDatabase.Instance?.GetEquipment(it.Id);
                if (ds == null || ds.slot != TOP.Data.PkoTables.SlotOf(it) || !SlotMatches(it, ds.slot, want, offhand)) continue;
                if (!TOP.Data.PkoClasses.Allows(it.Classes, testClass) || !TOP.Data.PkoClasses.RaceOk(it, race)) continue;
                if (requireVisible && !HasRaceModel(it, race)) continue;
                used.Add(it.Id);
                return it.Id;
            }
            return 0;
        }

        static readonly EquipmentSlot[] TestSlots =
        {
            EquipmentSlot.Helmet, EquipmentSlot.Armor, EquipmentSlot.Weapon, EquipmentSlot.Shield, EquipmentSlot.Gloves, EquipmentSlot.Boots,
            EquipmentSlot.Necklace, EquipmentSlot.Ring1, EquipmentSlot.Ring2, EquipmentSlot.Earring, EquipmentSlot.Belt, EquipmentSlot.Tattoo,
            EquipmentSlot.Cape, EquipmentSlot.Wing, EquipmentSlot.Pet, EquipmentSlot.Mount, EquipmentSlot.ApparelBody, EquipmentSlot.ApparelHelmet,
            EquipmentSlot.ApparelGloves, EquipmentSlot.ApparelBoots, EquipmentSlot.ApparelShield, EquipmentSlot.ApparelSword, EquipmentSlot.ApparelGreatSword,
            EquipmentSlot.ApparelGun, EquipmentSlot.ApparelDagger, EquipmentSlot.ApparelStaff, EquipmentSlot.ApparelBow, EquipmentSlot.ApparelPet,
            EquipmentSlot.ApparelGlow
        };

        IEnumerator AdminTest(Mirror.NetworkIdentity id)
        {
            Check(TOP.Network.LoginNetworkClient.IsAdmin, "Account is admin");
            var inv = id.GetComponent<TOP.Player.PlayerInventory>(); var eq = id.GetComponent<TOP.Player.PlayerEquipment>();
            var pc = id.GetComponent<TOP.Player.PlayerController>(); var st = id.GetComponent<TOP.Player.PlayerStats>();
            TOP.Data.PkoItem weapon = null;
            foreach (var it in TOP.Data.PkoTables.Items.Values)
            {
                if (it.Type != 1 || it.Level > pc.Level || it.EquipSlots.Length == 0) continue;
                if (!TOP.Data.PkoClasses.Allows(it.Classes, id.GetComponent<TOP.Player.PlayerClass>().CurrentClass) || !TOP.Data.PkoClasses.RaceOk(it, pc.Job)) continue;
                var ds = TOP.Inventory.ItemDatabase.Instance?.GetEquipment(it.Id); if (ds == null || ds.slot != EquipmentSlot.Weapon) continue;
                weapon = it; break;
            }
            Check(weapon != null, "Admin test: wearable sword found");
            if (weapon == null) yield break;
            var gemList = TOP.Data.PkoGems.GemsFor(weapon);
            var rage = gemList.Find(g => g.Name.Contains("Rage")) ?? gemList[0];
            Check(gemList.Count > 0 && TOP.Data.PkoGems.GemEffect(rage.ItemId).Str > 0, "Gem list for sword has Rage gems (" + gemList.Count + ")");

            int expected = inv.FindEmptySlot();
            inv.CmdAdminGenerate(weapon.Id, 7, 2, rage.ItemId, 0, 0); yield return new WaitForSeconds(1.5f);
            var gen = inv.GetSlot(expected);
            Check(gen != null && gen.ItemId == weapon.Id, "Generated item lands in first empty slot " + expected);
            if (gen == null) yield break;
            Check(gen.RefineLevel == 7 && gen.SocketCount == 2 && gen.Gems[0] == rage.ItemId && gen.Gems[1] == 0 && gen.Gems[2] == -1, "Refine/sockets/gems stored on the instance");
            var data = inv.GetInventoryData().Find(d => d.SlotIndex == expected);
            Check(data != null && data.RefineLevel == 7 && data.GemSlot1 == rage.ItemId && data.GemSlot2 == 0 && data.GemSlot3 == null, "Instance maps to DB columns");

            int str0 = st.Strength, atk0 = st.PhysicalAttack;
            inv.CmdEquipItem((ushort)expected, EquipmentSlot.Weapon); yield return new WaitForSeconds(1f);
            var extra = TOP.Data.PkoGems.InstanceBonus(gen);
            var ed = TOP.Inventory.ItemDatabase.Instance.GetEquipment(weapon.Id);
            Check(eq.GetEquippedItem(EquipmentSlot.Weapon)?.ItemId == weapon.Id, "Generated sword equips");
            Check(st.Strength - str0 >= extra.Str && st.PhysicalAttack - atk0 >= extra.Atk + ed.bonusAttack, "Equip adds gem/refine bonus (STR +" + (st.Strength - str0) + ", ATK +" + (st.PhysicalAttack - atk0) + ")");
            inv.CmdUnequipItem(EquipmentSlot.Weapon); yield return new WaitForSeconds(1f);
            Check(st.Strength == str0 && st.PhysicalAttack == atk0, "Unequip removes the bonus again");
            inv.CmdAdminGenerate(weapon.Id, 3, 1, 999999, 0, 0); yield return new WaitForSeconds(1.2f);
            int bad = inv.FindItemSlot(weapon.Id); var last = default(TOP.Inventory.InventoryItem);
            for (int i = 0; i < 40; i++) { var x = inv.GetSlot(i); if (x != null && x.ItemId == weapon.Id && x != gen) last = x; }
            Check(last != null && last.Gems[0] == 0, "Incompatible gem id is rejected by the server");
        }
        IEnumerator EquipTest(Mirror.NetworkIdentity id, TOP.Character.PkoCharacterVisual pv)
        {
            var inv = id.GetComponent<TOP.Player.PlayerInventory>(); var eq = id.GetComponent<TOP.Player.PlayerEquipment>(); var pc = id.GetComponent<TOP.Player.PlayerController>();
            if (inv == null || eq == null || pv == null || pc == null) { Check(false, "Equip test: components present"); yield break; }
            var cls = pc.GetComponent<TOP.Player.PlayerClass>();
            var cm = Camera.main; if (cm != null) { foreach (var mb in cm.GetComponents<MonoBehaviour>()) mb.enabled = false; var cf = id.GetComponent<TOP.Player.CameraFollow>(); if (cf != null) cf.enabled = false; }
            var w = PkoUi.Instance != null ? PkoUi.Instance.Get("frmInv") : null;
            Check(w != null, "Inventory window (frmInv) exists");
            if (w == null) yield break;
            w.Open(); yield return new WaitForSeconds(0.5f);
            var grid = w.Grids["grdItem"];

            if (TOP.Network.LoginNetworkClient.IsAdmin)
            {
                Note("STEP prepare: set test character to level 100 / Champion for full equipment coverage");
                inv.CmdAdminSetLevel(100);
                inv.CmdAdminSetClass((int)CharacterClass.Champion);
                yield return new WaitForSeconds(1.5f);
            }

            Note("STEP prepare: clear test equipment and bag space");
            foreach (var slot in TestSlots) inv.CmdUnequipItem(slot);
            yield return new WaitForSeconds(1f);
            for (int i = 0; i < inv.totalSlots; i++)
            {
                var item = inv.GetSlot(i);
                if (item != null && !item.IsEmpty && !item.IsEquipped) inv.CmdDeleteItem((ushort)i);
            }
            yield return new WaitForSeconds(1f);

            int race = pv.Race, job = pc.Job, lv = pc.Level; testClass = cls != null ? cls.CurrentClass : CharacterClass.Swordsman;
            var used = new HashSet<int>();
            // One different item per equipment slot. Visible body/weapon slots require baked assets;
            // accessory/apparel slots only need a valid equipment entry and slot mapping.
            var set = new (string label, int item, EquipmentSlot slot, string ui, bool visible)[]
            {
                ("helmet", PickSlotItem(EquipmentSlot.Helmet, race, job, lv, used, true), EquipmentSlot.Helmet, "cmdArmet", true),
                ("armor", PickSlotItem(EquipmentSlot.Armor, race, job, lv, used, true), EquipmentSlot.Armor, "cmdBody", true),
                ("gloves", PickSlotItem(EquipmentSlot.Gloves, race, job, lv, used, true), EquipmentSlot.Gloves, "cmdGlove", true),
                ("boots", PickSlotItem(EquipmentSlot.Boots, race, job, lv, used, true), EquipmentSlot.Boots, "cmdShoes", true),
                ("weapon", PickSlotItem(EquipmentSlot.Weapon, race, job, lv, used, true), EquipmentSlot.Weapon, "cmdRightHand", true),
                ("shield", PickSlotItem(EquipmentSlot.Shield, race, job, lv, used), EquipmentSlot.Shield, "cmdLeftHand", false),
                ("necklace", PickSlotItem(EquipmentSlot.Necklace, race, job, lv, used), EquipmentSlot.Necklace, "cmdNecklace", false),
                ("ring 1", PickSlotItem(EquipmentSlot.Ring1, race, job, lv, used), EquipmentSlot.Ring1, "cmdJewelry1", false),
                ("ring 2", PickSlotItem(EquipmentSlot.Ring2, race, job, lv, used), EquipmentSlot.Ring2, "cmdJewelry2", false),
                ("earring", PickSlotItem(EquipmentSlot.Earring, race, job, lv, used), EquipmentSlot.Earring, "cmdJewelry3", false),
                ("belt", PickSlotItem(EquipmentSlot.Belt, race, job, lv, used), EquipmentSlot.Belt, "cmdJewelry4", false),
                ("tattoo", PickSlotItem(EquipmentSlot.Tattoo, race, job, lv, used), EquipmentSlot.Tattoo, "cmdCirclet1", false),
                ("cape", PickSlotItem(EquipmentSlot.Cape, race, job, lv, used), EquipmentSlot.Cape, "cmdCloak", false),
                ("wing", PickSlotItem(EquipmentSlot.Wing, race, job, lv, used), EquipmentSlot.Wing, "cmdWing", false),
                ("pet", PickSlotItem(EquipmentSlot.Pet, race, job, lv, used), EquipmentSlot.Pet, "cmdPet", false),
                ("mount", PickSlotItem(EquipmentSlot.Mount, race, job, lv, used), EquipmentSlot.Mount, "cmdMount", false),
                ("apparel body", PickSlotItem(EquipmentSlot.ApparelBody, race, job, lv, used), EquipmentSlot.ApparelBody, "cmdBodyApp", false),
                ("apparel helmet", PickSlotItem(EquipmentSlot.ApparelHelmet, race, job, lv, used), EquipmentSlot.ApparelHelmet, "cmdArmetApp", false),
                ("apparel gloves", PickSlotItem(EquipmentSlot.ApparelGloves, race, job, lv, used), EquipmentSlot.ApparelGloves, "cmdGloveApp", false),
                ("apparel boots", PickSlotItem(EquipmentSlot.ApparelBoots, race, job, lv, used), EquipmentSlot.ApparelBoots, "cmdShoesApp", false),
                ("apparel shield", PickSlotItem(EquipmentSlot.ApparelShield, race, job, lv, used), EquipmentSlot.ApparelShield, "cmdShieldApp", false),
                ("apparel sword", PickSlotItem(EquipmentSlot.ApparelSword, race, job, lv, used), EquipmentSlot.ApparelSword, "cmdSword1App", false),
                ("apparel greatsword", PickSlotItem(EquipmentSlot.ApparelGreatSword, race, job, lv, used), EquipmentSlot.ApparelGreatSword, "cmdGreatSwordApp", false),
                ("apparel gun", PickSlotItem(EquipmentSlot.ApparelGun, race, job, lv, used), EquipmentSlot.ApparelGun, "cmdGunApp", false),
                ("apparel dagger", PickSlotItem(EquipmentSlot.ApparelDagger, race, job, lv, used), EquipmentSlot.ApparelDagger, "cmdDaggerApp", false),
                ("apparel staff", PickSlotItem(EquipmentSlot.ApparelStaff, race, job, lv, used), EquipmentSlot.ApparelStaff, "cmdStaffApp", false),
                ("apparel bow", PickSlotItem(EquipmentSlot.ApparelBow, race, job, lv, used), EquipmentSlot.ApparelBow, "cmdBowApp", false),
                ("apparel pet", PickSlotItem(EquipmentSlot.ApparelPet, race, job, lv, used), EquipmentSlot.ApparelPet, "cmdPetApp", false),
                ("apparel glow", PickSlotItem(EquipmentSlot.ApparelGlow, race, job, lv, used), EquipmentSlot.ApparelGlow, "cmdGlowApp", false),
            };
            Note("Set: " + string.Join(", ", System.Array.ConvertAll(set, s => s.label + "=" + s.item)));
            int level0 = lv;

            // Add everything to the bag.
            foreach (var s in set) if (s.item > 0) { Note("STEP add " + s.label + " item " + s.item); inv.CmdAddItem(s.item, 1); yield return new WaitForSeconds(0.25f); }
            yield return new WaitForSeconds(1f);
            int[] bagSlot = new int[set.Length];
            for (int k = 0; k < set.Length; k++) { bagSlot[k] = -1; for (int i = 0; i < 40; i++) { var it = inv.GetSlot(i); if (it != null && it.ItemId == set[k].item && !it.IsEquipped) { bagSlot[k] = i; break; } } Check(set[k].item == 0 || bagSlot[k] >= 0, "Bag has " + set[k].label + " " + set[k].item); }

            // Wrong slot: a helmet dropped on the boots slot must be refused.
            if (bagSlot[0] >= 0) { Drop(grid[bagSlot[0]], w.Get<PkoSlot>("cmdShoes")); yield return new WaitForSeconds(0.8f); Check(eq.GetEquippedItem(EquipmentSlot.Boots)?.ItemId is null or 0 && eq.GetEquippedItem(EquipmentSlot.Helmet)?.ItemId is null or 0, "Helmet refused on the boots slot"); }

            // Drag every piece onto its own slot.
            for (int k = 0; k < set.Length; k++)
            {
                if (bagSlot[k] < 0) continue;
                { var fs = grid[bagSlot[k]]; var ts = w.Get<PkoSlot>(set[k].ui); Note("drop " + set[k].label + " from(group=" + fs.Group + ",idx=" + fs.Index + ",bag=" + bagSlot[k] + ") to(" + (ts != null ? ts.Group + ",idx=" + ts.Index + ",dropped=" + (ts.Dropped != null) : "NULL") + ") lvl=" + pc.Level + " job=" + pc.Job); }
                Drop(grid[bagSlot[k]], w.Get<PkoSlot>(set[k].ui));
                yield return new WaitForSeconds(1f);
                Check(eq.GetEquippedItem(set[k].slot)?.ItemId == set[k].item, "Drag " + set[k].label + " " + set[k].item + " onto " + set[k].ui + " equips it");
                Check(System.Array.IndexOf(pv.Equipped, set[k].item) >= 0, set[k].label + " syncs to the visual equipment list");
                Check(!grid[bagSlot[k]].Filled, set[k].label + " leaves the bag grid");
                Check(w.Get<PkoSlot>(set[k].ui).Filled, set[k].label + " icon appears in the equipment slot");
            }
            int worn = 0; foreach (var s in set) if (s.item > 0 && eq.GetEquippedItem(s.slot)?.ItemId == s.item) worn++;
            Check(worn == System.Array.FindAll(set, s => s.item > 0).Length, "Full set worn together (" + worn + " pieces)");
            if (pv.Pose != null) { var sb = new System.Text.StringBuilder("wield=" + pv.Pose.Wield + " poses:"); foreach (int p in new[] { 1, 4, 5, 6, 7, 11, 16, 17 }) sb.Append(" " + p + "=" + (pv.Pose.Has(p) ? "ok" : "MISSING")); Note(sb.ToString()); float len = pv.Pose.Once(7); yield return new WaitForSeconds(0.2f); Check(len > 0f, "Attack action plays (" + pv.Pose.CurrentClip + ", " + len.ToString("0.00") + "s)"); yield return new WaitForSeconds(len + 0.3f); }
            if (cm != null) { var look = id.transform.position + Vector3.up * 1.7f; var offs = new[] { new Vector3(0f, .2f, -3.4f), new Vector3(3.4f, .2f, 0f) }; for (int k = 0; k < offs.Length; k++) { cm.transform.position = look + offs[k]; cm.transform.LookAt(look); yield return null; yield return null; yield return Shot("flow-equipset-" + k); } }

            // Take pieces off one by one onto their reserved bag slot; the rest stays on.
            for (int k = 0; k < set.Length; k++)
            {
                if (set[k].item == 0 || eq.GetEquippedItem(set[k].slot)?.ItemId != set[k].item) continue;
                Drop(w.Get<PkoSlot>(set[k].ui), grid[bagSlot[k]]);
                yield return new WaitForSeconds(1f);
                Check(eq.GetEquippedItem(set[k].slot)?.ItemId is null or 0, "Drag " + set[k].label + " back to the bag unequips it");
                Check(inv.GetSlot(bagSlot[k])?.ItemId == set[k].item && !inv.GetSlot(bagSlot[k]).IsEquipped, set[k].label + " returns to the original bag slot " + bagSlot[k]);
                Check(System.Array.IndexOf(pv.Equipped, set[k].item) < 0, set[k].label + " leaves the visual equipment list");
                int still = 0; for (int j = k + 1; j < set.Length; j++) if (set[j].item > 0 && eq.GetEquippedItem(set[j].slot)?.ItemId == set[j].item) still++;
                int expect = 0; for (int j = k + 1; j < set.Length; j++) if (set[j].item > 0) expect++;
                Check(still == expect, "Other pieces stay worn (" + still + "/" + expect + ")");
            }

            // Double click / right click path and swap of an occupied slot.
            int second = PickItem(22, race, job, lv, 1, EquipmentSlot.Armor);
            if (second > 0 && set[1].item > 0)
            {
                inv.CmdAddItem(set[1].item, 0 + 1); inv.CmdAddItem(second, 1); yield return new WaitForSeconds(1.2f);
                int a = -1, b = -1; for (int i = 0; i < 40; i++) { var it = inv.GetSlot(i); if (it == null || it.IsEquipped) continue; if (it.ItemId == second) b = i; else if (it.ItemId == set[1].item && a < 0) a = i; }
                Note("swap test armors a=" + a + " b=" + b + " second=" + second);
                if (a >= 0 && b >= 0)
                {
                    Drop(grid[a], w.Get<PkoSlot>("cmdBody")); yield return new WaitForSeconds(1f);
                    Drop(grid[b], w.Get<PkoSlot>("cmdBody")); yield return new WaitForSeconds(1f);
                    Check(eq.GetEquippedItem(EquipmentSlot.Armor)?.ItemId == second, "Equipping over a worn armor swaps it (worn=" + eq.GetEquippedItem(EquipmentSlot.Armor)?.ItemId + ")");
                    Check(inv.GetSlot(a) != null && !inv.GetSlot(a).IsEquipped && inv.GetSlot(a).ItemId == set[1].item, "Replaced armor returns to the bag");
                }
            }
            if (pc.Level < 10)
            {
                inv.CmdAddItem(121, 1); yield return new WaitForSeconds(1.2f);
                int sh = -1; for (int i = 0; i < 40; i++) { var it = inv.GetSlot(i); if (it != null && it.ItemId == 121) { sh = i; break; } }
                if (sh >= 0) { Drop(grid[sh], w.Get<PkoSlot>("cmdLeftHand")); yield return new WaitForSeconds(1f); Check(eq.GetEquippedItem(EquipmentSlot.Shield)?.ItemId is null or 0, "Level 10 shield (121) is refused at level " + lv); }
            }
            Check(pc.Level == level0, "Level unchanged");
        }
        void Note(string s) { lines.Add(s); File.WriteAllLines("Tools/flow-results.txt", lines); }
        void Check(bool ok, string s) { Note((ok ? "PASS " : "FAIL ") + s); }

        static PkoWindow Win(string file, string name)
        {
            foreach (var w in FindObjectsByType<PkoWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (w.Def != null && w.Def.name == name && w.Def.file == file) return w;
            return null;
        }

        IEnumerator Until(System.Func<bool> cond, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!cond() && Time.realtimeSinceStartup < end) { EditorApplication.isPaused = false; yield return null; }
        }

        IEnumerator Shot(string n)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot("Tools/" + n + ".png");
            yield return null;
        }

        static void Click(PkoWindow w, string n) { var b = w.Get<Button>(n); if (b != null) b.onClick.Invoke(); }

        static InputField LoginField(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<InputField>() : null;
        }

        static InputField FieldObject(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<InputField>() : null;
        }

        static Button ButtonObject(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<Button>() : null;
        }

        static Text StatusText()
        {
            var go = GameObject.Find("Status");
            return go != null ? go.GetComponent<Text>() : null;
        }

        static Button DirectChildButton(GameObject root, int index)
        {
            if (root == null) return null;
            int seen = 0;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var b = root.transform.GetChild(i).GetComponent<Button>();
                if (b == null) continue;
                if (seen++ == index) return b;
            }
            return null;
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            File.WriteAllText("Tools/flow-results.txt", "");
            string user = "flowtest", pass = "flowtest1", cname = "Tst" + Random.Range(1000, 9999);

            GameObject loginCard = null;
            yield return Until(() => { loginCard = GameObject.Find("LoginCard"); return loginCard != null && loginCard.activeInHierarchy && LoginField("Field_Account") != null && LoginField("Field_Password") != null; }, 20);
            Check(loginCard != null && loginCard.activeInHierarchy, "Login screen opens (LoginCard)");
            if (loginCard == null) { Finish(); yield break; }
            yield return Shot("flow-login");

            LoginField("Field_Account").text = user;
            LoginField("Field_Password").text = pass;
            var loginButton = DirectChildButton(loginCard, 0);
            Check(loginButton != null, "Login button exists");
            if (loginButton == null) { Finish(); yield break; }
            loginButton.onClick.Invoke();
            yield return Until(() => SceneManager.GetActiveScene().name == "CharacterSelectScene" || (StatusText() != null && StatusText().color.r > .9f), 18);
            if (SceneManager.GetActiveScene().name != "CharacterSelectScene")
            {
                Note("Login with existing flowtest account did not enter CharacterSelectScene. Status=" + (StatusText() != null ? StatusText().text : "-"));
                user = "flow" + Random.Range(100000, 999999);
                DirectChildButton(loginCard, 1)?.onClick.Invoke();
                yield return Until(() => GameObject.Find("RegisterCard") != null && GameObject.Find("RegisterCard").activeInHierarchy, 5);
                var regUser = FieldObject("Input_Conta (3-16 caracteres)");
                var regPass = FieldObject("Input_Senha (min. 4)");
                var regPass2 = FieldObject("Input_Confirmar senha");
                var regMail = FieldObject("Input_E-mail (opcional)");
                var regButton = ButtonObject("Btn_Registrar");
                Check(regUser != null && regPass != null && regPass2 != null && regButton != null, "Register form fields exist");
                if (regUser == null || regPass == null || regPass2 == null || regButton == null) { Finish(); yield break; }
                regUser.text = user;
                regPass.text = pass;
                regPass2.text = pass;
                if (regMail != null) regMail.text = "flow@test.local";
                regButton.onClick.Invoke();
                yield return Until(() => loginCard != null && loginCard.activeInHierarchy && LoginField("Field_Account") != null && LoginField("Field_Password") != null, 20);
                Check(loginCard != null && loginCard.activeInHierarchy && LoginField("Field_Account") != null && LoginField("Field_Password") != null, "Register returns to login form");
                if (loginCard == null || !loginCard.activeInHierarchy || LoginField("Field_Account") == null || LoginField("Field_Password") == null) { Finish(); yield break; }
                LoginField("Field_Account").text = user;
                LoginField("Field_Password").text = pass;
                loginButton.onClick.Invoke();
                yield return Until(() => SceneManager.GetActiveScene().name == "CharacterSelectScene", 40);
            }
            Check(SceneManager.GetActiveScene().name == "CharacterSelectScene", "Login via API + Mirror reaches CharacterSelectScene");
            if (SceneManager.GetActiveScene().name != "CharacterSelectScene") { Finish(); yield break; }

            PkoWindow sel = null;
            yield return Until(() => { sel = Win("selectcha.clu", "frmUserselect"); return sel != null && sel.IsOpen && sel.Label("labCha1") != null && sel.Label("labCha1").text != "Nil"; }, 30);
            Check(sel != null && sel.Label("labCha1").text != "Nil", "Character list loaded from server/DB");
            yield return new WaitForSeconds(1);
            DumpVisuals("select"); yield return Shot("flow-select");

            int slot = -1; for (int i = 0; i < 3; i++) if (sel.Label("labCha" + (i + 1)).text.StartsWith("(vazio)")) { slot = i; break; }
            if (slot < 0) { Note("No free slot; skipping creation"); }
            else
            {
                Click(sel, "btnCreate");
                var found = Win("selectcha.clu", "frmUserfound");
                yield return Until(() => found != null && found.IsOpen, 5);
                Check(found != null && found.IsOpen, "Create-character window opens");
                found.Get<InputField>("edtName").text = cname;
                found.Get<Button>("btnRightStyle").onClick.Invoke();
                yield return new WaitForSeconds(.5f);
                found.Get<Button>("btnRightStyle").onClick.Invoke(); found.Get<Button>("btnLeftStyle").onClick.Invoke();
                yield return new WaitForSeconds(.5f);
                DumpVisuals("create"); yield return Shot("flow-create");
                Click(found, "btnYes");
                yield return Until(() => sel.IsOpen && sel.Label("labCha" + (slot + 1)).text.Contains(cname), 15);
                Check(sel.Label("labCha" + (slot + 1)).text.Contains(cname), "Character created in DB and listed (" + cname + ", slot " + (slot + 1) + ")");
                yield return new WaitForSeconds(1);
                yield return Shot("flow-select2");
            }

            Click(sel, "btnYes");
            yield return Until(() => NetworkClient.localPlayer != null, 10);
            yield return new WaitForSeconds(0.3f); yield return Shot("flow-entering");
            { int stray = 0; foreach (var v in FindObjectsByType<TOP.Character.PkoCharacterVisual>(FindObjectsSortMode.None)) if (v.gameObject.layer != TOP.CharacterSelect.CharacterPreviewManager.PreviewLayer && SceneManager.GetActiveScene().name == "CharacterSelectScene") stray++; Check(stray == 0, "No non-preview character visual leaks into the select scene (" + stray + ")"); }
            yield return Until(() => SceneManager.GetActiveScene().name == "GameScene" && NetworkClient.localPlayer != null, 40);
            Check(SceneManager.GetActiveScene().name == "GameScene", "Selecting a character loads GameScene");
            var id = NetworkClient.localPlayer;
            Check(id != null, "Local player exists in GameScene");
            if (id != null) { yield return new WaitForSeconds(3); Note("Player=" + id.name + " pos=" + id.transform.position);
                var pv = id.GetComponentInChildren<TOP.Character.PkoCharacterVisual>();
                Check(pv != null, "Player uses the original-client character model");
                if (pv != null) { DumpVisuals("game"); var ra = pv.GetComponentInChildren<Animation>(true); Transform bt = null; if (ra != null) foreach (var tr in ra.GetComponentsInChildren<Transform>()) if (tr.name.StartsWith("b5_")) { bt = tr; break; } Vector3 p0 = bt != null ? bt.localEulerAngles : Vector3.zero; yield return new WaitForSeconds(0.4f); Vector3 p1 = bt != null ? bt.localEulerAngles : Vector3.zero; Check((p1 - p0).sqrMagnitude > 0.0001f, "Idle animation moves bones in game (" + (bt != null ? bt.name : "-") + " " + p0 + "->" + p1 + ")"); }
                if (pv != null && pv.TryGetBodyBounds(out var pb)) Note("Model bounds center=" + pb.center + " size=" + pb.size + " parts=" + pv.GetComponentsInChildren<SkinnedMeshRenderer>().Length);
                { var cc = id.GetComponent<CharacterController>(); float minY = float.MaxValue; if (pv != null) foreach (var s in pv.GetComponentsInChildren<SkinnedMeshRenderer>()) minY = Mathf.Min(minY, s.bounds.min.y);
                  float gy = float.NaN; RaycastHit gh = default; foreach (var h in Physics.RaycastAll(id.transform.position + Vector3.up * 5f, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore)) { if (h.transform.IsChildOf(id.transform)) continue; if (float.IsNaN(gy)) { gy = h.point.y; gh = h; } }
                  Note("CC center=" + (cc != null ? cc.center.ToString() : "-") + " h=" + (cc != null ? cc.height : 0) + " bottomY=" + (cc != null ? cc.bounds.min.y : 0) + " meshMinY=" + minY + " groundRay=" + gy + " (" + (gh.collider != null ? gh.collider.name : "-") + ") visualY=" + (pv != null ? pv.transform.position.y : 0) + " scale=" + id.transform.lossyScale); }
                var peq = id.GetComponent<TOP.Player.PlayerEquipment>();
                Note("Equipped ids=" + (peq != null ? string.Join(",", peq.GetEquippedItemIds()) : "n/a") + " visual=" + (pv != null ? string.Join(",", pv.Equipped) : "n/a") + " db295=" + (TOP.Inventory.ItemDatabase.Instance?.GetEquipment(295) != null));
                yield return AdminTest(id);
                yield return EquipTest(id, pv);
                var cam = Camera.main; if (cam != null) Note("Camera=" + cam.transform.position + " dist=" + Vector3.Distance(cam.transform.position, id.transform.position));
            }
            yield return Shot("flow-game");
            if (id != null && Camera.main != null)
            {
                var cm = Camera.main; foreach (var mb in cm.GetComponents<MonoBehaviour>()) mb.enabled = false; var cf = id.GetComponent<TOP.Player.CameraFollow>(); if (cf != null) cf.enabled = false;
                var look = id.transform.position + Vector3.up * 0.5f;
                for (int f = 0; f < 3; f++)
                {
                    cm.transform.position = look + new Vector3(0f, 1.2f, 4.5f);
                    cm.transform.LookAt(look);
                    yield return null;
                }
                yield return Shot("flow-player");
            }
            Finish();
        }

        void Finish()
        {
            Note("DONE");
            EditorApplication.isPlaying = false;
        }
    }
}
#endif
