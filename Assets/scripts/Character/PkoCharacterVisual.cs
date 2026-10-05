using System.Collections.Generic;
using System.Text.RegularExpressions;
using TOP.Data;
using UnityEngine;

namespace TOP.Character
{
    // Builds a playable character from the original client data: one skeleton per race (Resources/PkoChar/Rig_000N)
    // plus one skinned part per slot (face, hair, body, gloves, boots) and the weapon/shield on the hand dummies.
    // Used by the character selection preview and by the in-game player.
    public class PkoCharacterVisual : MonoBehaviour
    {
        public const int Races = 4; // Lance, Carsise, Phyllis, Ami
        const int PartFace = 0, PartHair = 1, PartBody = 2, PartGloves = 3, PartBoots = 4, PartCount = 5;
        const int DummyRightHand = 9, DummyLeftHand = 6;
        static readonly Regex BoneName = new Regex(@"^b(\d+)_");

        public int Race { get; private set; }
        public int Face { get; private set; }
        public int Hair { get; private set; }
        public int[] Equipped { get; private set; } = new int[0];

        GameObject rig;
        Transform[] bones;
        readonly Dictionary<string, Transform> dummies = new Dictionary<string, Transform>();
        readonly GameObject[] parts = new GameObject[PartCount];
        GameObject rightWeapon, leftWeapon;
        public PkoPoseDriver Pose { get; private set; }

        public static PkoCharacterVisual Create(Transform parent, int race, int face, int hair, IEnumerable<int> items)
        {
            var go = new GameObject("PkoCharacter");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PkoCharacterVisual>();
            v.Apply(race, face, hair, items);
            return v;
        }

        public void Apply(int race, int face, int hair, IEnumerable<int> items)
        {
            race = Mathf.Clamp(race, 0, Races - 1);
            if (rig == null || race != Race) LoadRig(race);
            Race = race; Face = face; Hair = hair;
            var list = new List<int>(); if (items != null) list.AddRange(items);
            Equipped = list.ToArray();
            if (rig == null) return;

            var models = new string[PartCount];
            models[PartFace] = Name(race, face, 0);
            models[PartHair] = Name(race, hair, 1);
            models[PartBody] = Name(race, 0, 2);
            models[PartGloves] = Name(race, 0, 3);
            models[PartBoots] = Name(race, 0, 4);
            string right = null, left = null; int rightType = 0, leftType = 0;

            // Aparencia vence o equipamento real: processada por ultimo.
            var ordered = new List<int>(Equipped);
            ordered.Sort((x, y) => IsApparelId(x).CompareTo(IsApparelId(y)));
            foreach (int id in ordered)
            {
                if (!PkoTables.Items.TryGetValue(id, out var it)) continue;
                string m = it.RaceModels != null && race < it.RaceModels.Length ? (it.RaceModels[race] ?? "").TrimEnd('_') : "";
                if (m.Length == 0 || m == "0") continue;
                switch (it.Type)
                {
                    case 20: case 28: if (m.Length == 10) models[PartHair] = m; break;
                    case 21: if (m.Length == 10) models[PartFace] = m; break;
                    case 22: if (m.Length == 10) models[PartBody] = m; break;
                    case 23: if (m.Length == 10) models[PartGloves] = m; break;
                    case 24: if (m.Length == 10) models[PartBoots] = m; break;
                    case 3: case 11: left = m; leftType = it.Type; break; // bows (and shields) sit in the left hand
                    case 1 when PkoTables.IsApparel(it): right = m; rightType = 1; if (leftType == 1) left = m; break; // apparel sword covers both hands when dual wielding
                    case 1 when rightType == 1 && leftType != 1: left = m; leftType = 1; break; // second sword: dual wield
                    case 1: case 2: case 4: case 5: case 6: case 7: case 8: case 9: case 10: case 18: case 19: right = m; rightType = it.Type; break;
                }
            }

            for (int i = 0; i < PartCount; i++) SetPart(i, models[i]);
            SetWeapon(ref rightWeapon, right, DummyRightHand);
            SetWeapon(ref leftWeapon, left, DummyLeftHand);
            if (Pose != null) Pose.SetWield(PkoPoses.WieldOf(leftType, rightType));
        }

        static bool IsApparelId(int id) => PkoTables.Items.TryGetValue(id, out var it) && PkoTables.IsApparel(it);

        static string Name(int race, int style, int part) => race.ToString("0000") + style.ToString("00") + part.ToString("0000");

        void LoadRig(int race)
        {
            if (rig != null) { rig.SetActive(false); Destroy(rig); }
            foreach (Transform c in transform) if (c.gameObject != rig && c.gameObject.activeSelf) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            dummies.Clear();
            var prefab = Resources.Load<GameObject>("PkoChar/Rig_" + race.ToString("0000"));
            if (prefab == null) { Debug.LogError("[PkoCharacterVisual] Rig ausente para a raca " + race + " (execute TOP/Characters/Bake)."); return; }
            rig = Instantiate(prefab, transform);
            rig.name = "Rig";
            var legacy = rig.GetComponent<LegacyAnimDriver>(); if (legacy != null) { legacy.enabled = false; Destroy(legacy); }
            Pose = rig.AddComponent<PkoPoseDriver>(); Pose.Init(rig.GetComponent<Animation>(), race.ToString("0000"));
            var found = new List<(int, Transform)>();
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                var m = BoneName.Match(t.name);
                if (m.Success) found.Add((int.Parse(m.Groups[1].Value), t));
                else if (t.name.StartsWith("dummy_")) dummies[t.name] = t;
            }
            bones = new Transform[found.Count];
            foreach (var (i, t) in found) if (i < bones.Length) bones[i] = t;
            for (int i = 0; i < parts.Length; i++) parts[i] = null;
            rightWeapon = leftWeapon = null;
        }

        void SetPart(int slot, string name)
        {
            if (parts[slot] != null) Destroy(parts[slot]);
            parts[slot] = null;
            var asset = Resources.Load<PkoPartAsset>("PkoChar/Parts/" + name);
            if (asset == null) return;
            var go = new GameObject("part_" + name);
            go.transform.SetParent(rig.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = asset.mesh; smr.sharedMaterials = asset.materials;
            smr.bones = bones; smr.rootBone = bones[0];
            smr.localBounds = asset.mesh.bounds; smr.updateWhenOffscreen = true;
            parts[slot] = go;
        }

        // Local rotation of the weapon model inside the grip dummy wrapper.
        void LateUpdate()
        {
            ApplyWeaponPose(rightWeapon, 0); ApplyWeaponPose(leftWeapon, 1);
        }

        void ApplyWeaponPose(GameObject wrap, int hand)
        {
            if (wrap == null || wrap.transform.childCount == 0) return;
            int k = PkoWeaponPose.Key(Race, hand);
            var inst = wrap.transform.GetChild(0);
            inst.localPosition = PkoWeaponPose.Pos[k]; inst.localRotation = Quaternion.Euler(PkoWeaponPose.Euler[k]);
        }
        void SetWeapon(ref GameObject slot, string model, int dummyId)
        {
            if (slot != null) Destroy(slot);
            slot = null;
            if (string.IsNullOrEmpty(model) || model == "0") return;
            var prefab = Resources.Load<GameObject>("PkoChar/Weapons/" + model);
            if (prefab == null || !dummies.TryGetValue("dummy_" + dummyId, out var anchor)) return;
            // Item models keep the original Z-up axes; characters use (-x,z,y), so swap Y/Z (rotation + mirror).
            var wrap = new GameObject("weapon_" + model).transform;
            wrap.SetParent(anchor, false);
            wrap.localPosition = Vector3.zero; wrap.localRotation = Quaternion.Euler(90f, 0f, 0f); wrap.localScale = new Vector3(1f, 1f, -1f);
            var inst = Instantiate(prefab, wrap);
            inst.transform.localPosition = Vector3.zero;
            slot = wrap.gameObject;
        }

        // Height of the skeleton (feet to head) in world units, for camera framing.
        public bool TryGetBodyBounds(out Bounds bounds)
        {
            bounds = new Bounds(); bool any = false;
            if (bones == null) return false;
            foreach (var b in bones)
            {
                if (b == null) continue;
                if (!any) { bounds = new Bounds(b.position, Vector3.zero); any = true; } else bounds.Encapsulate(b.position);
            }
            if (any) bounds.Expand(new Vector3(0.5f, 0.3f, 0.5f));
            return any;
        }
    }
}
