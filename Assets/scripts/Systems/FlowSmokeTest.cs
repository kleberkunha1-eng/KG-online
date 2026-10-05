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
        static int PickItem(int type, int race, int job, int level, int skip = 0, EquipmentSlot? want = null, bool offhand = false)
        {
            foreach (var it in TOP.Data.PkoTables.Items.Values)
            {
                if (it.Type != type || it.Level > level || it.EquipSlots.Length == 0 || (offhand && System.Array.IndexOf(it.EquipSlots, 6) < 0)) continue;
                var ds = TOP.Inventory.ItemDatabase.Instance?.GetEquipment(it.Id); if (ds == null || ds.slot != TOP.Data.PkoTables.SlotOf(it) || (want.HasValue && ds.slot != want.Value)) continue;
                if (!TOP.Data.PkoClasses.Allows(it.Classes, testClass) || !TOP.Data.PkoClasses.RaceOk(it, race)) continue;
                string m = (it.RaceModels[race] ?? "").TrimEnd('_');
                bool ok = type >= 20 ? m.Length == 10 && Resources.Load<TOP.Character.PkoPartAsset>("PkoChar/Parts/" + m) != null : Resources.Load<GameObject>("PkoChar/Weapons/" + m) != null;
                if (ok && skip-- <= 0) return it.Id;
            }
            return 0;
        }

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
            var cm = Camera.main; if (cm != null) { foreach (var mb in cm.GetComponents<MonoBehaviour>()) mb.enabled = false; var cf = id.GetComponent<TOP.Player.CameraFollow>(); if (cf != null) cf.enabled = false; }
            var w = PkoUi.Instance != null ? PkoUi.Instance.Get("frmInv") : null;
            Check(w != null, "Inventory window (frmInv) exists");
            if (w == null) yield break;
            w.Open(); yield return new WaitForSeconds(0.5f);
            var grid = w.Grids["grdItem"];

            int race = pv.Race, job = pc.Job, lv = pc.Level; testClass = pc.GetComponent<TOP.Player.PlayerClass>().CurrentClass;
            // One piece per slot: a costume is several separate items.
            var set = new (string label, int item, EquipmentSlot slot, string ui)[]
            {
                ("helmet", PickItem(20, race, job, lv, 0, EquipmentSlot.Helmet), EquipmentSlot.Helmet, "cmdArmet"),
                ("armor", PickItem(22, race, job, lv, 0, EquipmentSlot.Armor), EquipmentSlot.Armor, "cmdBody"),
                ("gloves", PickItem(23, race, job, lv, 0, EquipmentSlot.Gloves), EquipmentSlot.Gloves, "cmdGlove"),
                ("boots", PickItem(24, race, job, lv, 0, EquipmentSlot.Boots), EquipmentSlot.Boots, "cmdShoes"),
                ("weapon", PickItem(1, race, job, lv, 0, EquipmentSlot.Weapon, true), EquipmentSlot.Weapon, "cmdRightHand"),
                ("offhand sword", PickItem(1, race, job, lv, 1, EquipmentSlot.Weapon, true), EquipmentSlot.Shield, "cmdLeftHand"),
            };
            Note("Set: " + string.Join(", ", System.Array.ConvertAll(set, s => s.label + "=" + s.item)));
            int level0 = lv;

            // Add everything to the bag.
            foreach (var s in set) if (s.item > 0) { inv.CmdAddItem(s.item, 1); yield return new WaitForSeconds(0.4f); }
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
                Check(System.Array.IndexOf(pv.Equipped, set[k].item) >= 0, set[k].label + " is shown on the 3D model");
                Check(!grid[bagSlot[k]].Filled, set[k].label + " leaves the bag grid");
                Check(w.Get<PkoSlot>(set[k].ui).Filled, set[k].label + " icon appears in the equipment slot");
            }
            int worn = 0; foreach (var s in set) if (s.item > 0 && eq.GetEquippedItem(s.slot)?.ItemId == s.item) worn++;
            Check(worn == System.Array.FindAll(set, s => s.item > 0).Length, "Full set worn together (" + worn + " pieces)");
            if (pv.Pose != null) { var sb = new System.Text.StringBuilder("wield=" + pv.Pose.Wield + " poses:"); foreach (int p in new[] { 1, 4, 5, 6, 7, 11, 16, 17 }) sb.Append(" " + p + "=" + (pv.Pose.Has(p) ? "ok" : "MISSING")); Note(sb.ToString()); float len = pv.Pose.Once(7); yield return new WaitForSeconds(0.2f); Check(len > 0f, "Attack action plays (" + pv.Pose.CurrentClip + ", " + len.ToString("0.00") + "s)"); yield return new WaitForSeconds(len + 0.3f); }
            if (cm != null) { var look = id.transform.position + Vector3.up * 1.7f; var offs = new[] { new Vector3(0f, .2f, -3.4f), new Vector3(3.4f, .2f, 0f) }; for (int k = 0; k < offs.Length; k++) { cm.transform.position = look + offs[k]; cm.transform.LookAt(look); yield return null; yield return null; yield return Shot("flow-equipset-" + k); } }

            // Take pieces off one by one onto chosen bag slots; the rest stays on.
            int[] target = { 30, 31, 32, 33, 34, 35 };
            for (int k = 0; k < set.Length; k++)
            {
                if (set[k].item == 0 || eq.GetEquippedItem(set[k].slot)?.ItemId != set[k].item) continue;
                Drop(w.Get<PkoSlot>(set[k].ui), grid[target[k]]);
                yield return new WaitForSeconds(1f);
                Check(eq.GetEquippedItem(set[k].slot)?.ItemId is null or 0, "Drag " + set[k].label + " back to the bag unequips it");
                Check(inv.GetSlot(target[k])?.ItemId == set[k].item && !inv.GetSlot(target[k]).IsEquipped, set[k].label + " returns to the chosen bag slot " + target[k]);
                Check(System.Array.IndexOf(pv.Equipped, set[k].item) < 0, set[k].label + " leaves the 3D model");
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
            inv.CmdAddItem(121, 1); yield return new WaitForSeconds(1.2f);
            { int sh = -1; for (int i = 0; i < 40; i++) { var it = inv.GetSlot(i); if (it != null && it.ItemId == 121) { sh = i; break; } }
              if (sh >= 0) { Drop(grid[sh], w.Get<PkoSlot>("cmdLeftHand")); yield return new WaitForSeconds(1f); Check(eq.GetEquippedItem(EquipmentSlot.Shield)?.ItemId is null or 0, "Level 10 shield (121) is refused at level " + lv); } }
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

        IEnumerator Start()
        {
            Application.runInBackground = true;
            File.WriteAllText("Tools/flow-results.txt", "");
            string user = "flowtest", pass = "flowtest1", cname = "Tst" + Random.Range(1000, 9999);

            yield return Until(() => Win("login.clu", "frmAccount") != null && Win("login.clu", "frmAccount").IsOpen, 20);
            var acc = Win("login.clu", "frmAccount");
            Check(acc != null && acc.IsOpen, "Login screen opens (frmAccount)");
            if (acc == null) { Finish(); yield break; }
            yield return Shot("flow-login");

            var reg = Win("login.clu", "frmRegister");
            reg.Get<InputField>("edtRegID").text = user; reg.Get<InputField>("edtRegPassword").text = pass;
            reg.Get<InputField>("edtRegPassword2").text = pass; reg.Get<InputField>("edtRegEmail").text = "flow@test.local";
            Click(reg, "btnRegYes");
            yield return new WaitForSeconds(4);
            Note("Register attempted (ok or already exists)");

            acc.Open(); acc.Get<InputField>("edtID").text = user; acc.Get<InputField>("edtPassword").text = pass;
            Click(acc, "btnYes");
            yield return Until(() => SceneManager.GetActiveScene().name == "CharacterSelectScene", 40);
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
