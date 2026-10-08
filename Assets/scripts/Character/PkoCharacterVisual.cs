using System.Collections.Generic;
using System.Text.RegularExpressions;
using TOP.Core;
using TOP.Data;
using UnityEngine;

namespace TOP.Character
{
    // Builds a playable character from the original client data: one skeleton per race (Resources/PkoChar/Rig_000N)
    // plus one skinned part per slot (face, hair, body, gloves, boots) and the weapon/shield on the hand dummies.
    // Used by the character selection preview and by the in-game player.
    public class PkoCharacterVisual : MonoBehaviour
    {
        public const int Races = PkoRaces.OriginalCount; // Original asset families; NewCharacterTest shares Lance equipment.
        const int PartFace = 0, PartHair = 1, PartBody = 2, PartGloves = 3, PartBoots = 4, PartCount = 5;
        const int DummyRightHand = 9, DummyLeftHand = 6;
        static readonly Regex BoneName = new Regex(@"^b(\d+)_");

        public int Race { get; private set; }
        public int Face { get; private set; }
        public int Hair { get; private set; }
        public int[] Equipped { get; private set; } = new int[0];

        GameObject rig;
        Transform[] bones;
        PkoRigFit rigFit;
        readonly Dictionary<string, Transform> dummies = new Dictionary<string, Transform>();
        readonly GameObject[] parts = new GameObject[PartCount];
        readonly List<GameObject> customParts = new List<GameObject>();
        Mesh coveredBody;
        GameObject rightWeapon, leftWeapon, wingVisual;
        Vector3 wingBasePosition;
        Quaternion wingBaseRotation;
        float wingBodyHeight;
        public int WingItemId { get; private set; }
        public Transform WingMount => wingVisual != null ? wingVisual.transform : null;
        public PkoPoseDriver Pose { get; private set; }
        public const string NewCharacterClipFolder = "PkoChar/NewCharacterTest/Animations/";

        public static PkoCharacterVisual Create(Transform parent, int race, int face, int hair, IEnumerable<int> items)
        {
            using var trace = TOP.Diagnostics.GameTrace.Measure("PkoCharacterVisual.Create", race);
            var go = new GameObject("PkoCharacter");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PkoCharacterVisual>();
            v.Apply(race, face, hair, items);
            return v;
        }

        public void Apply(int race, int face, int hair, IEnumerable<int> items)
        {
            race = Mathf.Clamp(race, 0, PkoRaces.Count - 1);
            if (rig == null || race != Race) LoadRig(race);
            Race = race; Face = face; Hair = hair;
            var list = new List<int>(); if (items != null) list.AddRange(items);
            Equipped = list.ToArray();
            if (rig == null) return;

            var models = new string[PartCount];
            int baseRace = PkoRaces.BaseRace(race);
            models[PartFace] = Name(baseRace, face, 0);
            models[PartHair] = Name(baseRace, hair, 1);
            models[PartBody] = Name(baseRace, 0, 2);
            models[PartGloves] = Name(baseRace, 0, 3);
            models[PartBoots] = Name(baseRace, 0, 4);
            string right = null, left = null; PkoItem wing = null; int rightType = 0, leftType = 0;
            var custom = new List<BlueMageSet.Piece>();
            var apparelSlots = new HashSet<EquipmentSlot>();

            // Aparencia vence o equipamento real: processada por ultimo.
            var ordered = new List<int>(Equipped);
            ordered.Sort((x, y) => IsApparelId(x).CompareTo(IsApparelId(y)));
            foreach (int id in ordered)
            {
                if (!PkoTables.Items.TryGetValue(id, out var it)) continue;
                if (BlueMageSet.TryGet(id, out var bluePiece)) { custom.Add(bluePiece); continue; }
                if (PkoTables.SlotOf(it) == EquipmentSlot.Wing)
                {
                    wing = it;
                    continue;
                }
                string m = it.RaceModels != null && baseRace < it.RaceModels.Length ? (it.RaceModels[baseRace] ?? "").TrimEnd('_') : "";
                if (m.Length == 0 || m == "0") continue;
                if (PkoTables.IsApparel(it)) apparelSlots.Add(PkoTables.SlotOf(it));
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

            bool customHead = models[PartFace] == Name(0, 0, PartFace)
                && models[PartHair] == Name(0, 0, PartHair);
            for (int i = 0; i < PartCount; i++)
                SetPart(i, models[i], customHead || (i != PartFace && i != PartHair));
            using (TOP.Diagnostics.GameTrace.Measure("PkoCharacterVisual.Equipment", int.MinValue, 20))
            {
            ApplyCustomParts(custom, apparelSlots);
            SetWeapon(ref rightWeapon, right, DummyRightHand);
            SetWeapon(ref leftWeapon, left, DummyLeftHand);
            SetWing(wing);
            }
            if (Pose != null) Pose.SetWield(PkoPoses.WieldOf(leftType, rightType));
        }

        static bool IsApparelId(int id) => PkoTables.Items.TryGetValue(id, out var it) && PkoTables.IsApparel(it);

        static string Name(int race, int style, int part) => race.ToString("0000") + style.ToString("00") + part.ToString("0000");

        void LoadRig(int race)
        {
            using var trace = TOP.Diagnostics.GameTrace.Measure("PkoCharacterVisual.LoadRig", race, 20);
            if (rig != null) { rig.SetActive(false); Destroy(rig); }
            foreach (Transform c in transform) if (c.gameObject != rig && c.gameObject.activeSelf) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            dummies.Clear();
            var prefab = Resources.Load<GameObject>("PkoChar/Rig_" + race.ToString("0000"));
            if (prefab == null) { Debug.LogError("[PkoCharacterVisual] Rig ausente para a raca " + race + " (execute TOP/Characters/Bake)."); return; }
            rig = Instantiate(prefab, transform);
            rig.name = "Rig";
            rigFit = rig.GetComponent<PkoRigFit>();
            var legacy = rig.GetComponent<LegacyAnimDriver>(); if (legacy != null) { legacy.enabled = false; Destroy(legacy); }
            Pose = rig.AddComponent<PkoPoseDriver>(); Pose.Init(rig.GetComponent<Animation>(), PkoRaces.BaseRace(race).ToString("0000"),
                race == PkoRaces.NewCharacterTest ? NewCharacterClipFolder : null);
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
            rightWeapon = leftWeapon = wingVisual = null;
        }

        void SetPart(int slot, string name, bool useCustomDefault)
        {
            using var trace = TOP.Diagnostics.GameTrace.Measure("PkoCharacterVisual.SetPart", slot, 20);
            if (parts[slot] != null) { parts[slot].SetActive(false); Destroy(parts[slot]); }
            parts[slot] = null;
            bool newDefault = useCustomDefault && Race == PkoRaces.NewCharacterTest && name == Name(0, 0, slot);
            var asset = Resources.Load<PkoPartAsset>(newDefault
                ? "PkoChar/NewCharacterTest/part_" + slot : "PkoChar/Parts/" + name);
            if (newDefault && asset == null)
                Debug.LogError("[PkoCharacterVisual] Missing NewCharacterTest part " + slot + ". Run Tools/PKO/Build NewCharacterTest.");
            if (asset == null) return;
            if (asset.mesh == null || asset.mesh.vertexCount == 0) return;
            parts[slot] = CreatePart(asset, "part_" + name, newDefault);
        }

        // Parts baked for the base race are refitted when the rig's bone lengths belong to a custom body.
        GameObject CreatePart(PkoPartAsset asset, string name, bool rigNative = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rig.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = rigNative || rigFit == null ? asset.mesh : rigFit.Fit(asset.mesh);
            smr.sharedMaterials = asset.materials;
            smr.bones = bones; smr.rootBone = bones[0];
            smr.localBounds = asset.mesh.bounds; smr.updateWhenOffscreen = true;
            // The custom body is weighted for 4 bones; lower quality levels would otherwise fold its joints.
            if (rigFit != null) smr.quality = SkinQuality.Bone4;
            return go;
        }

        void ApplyCustomParts(List<BlueMageSet.Piece> equipped, HashSet<EquipmentSlot> apparel)
        {
            if (coveredBody != null) { Destroy(coveredBody); coveredBody = null; }
            foreach (var old in customParts)
                if (old != null) { old.SetActive(false); Destroy(old); }
            customParts.Clear();
            int coverage = 0;
            foreach (var piece in equipped)
            {
                var appearanceSlot = piece.Slot switch
                {
                    EquipmentSlot.Helmet => EquipmentSlot.ApparelHelmet,
                    EquipmentSlot.Gloves => EquipmentSlot.ApparelGloves,
                    EquipmentSlot.Boots => EquipmentSlot.ApparelBoots,
                    _ => EquipmentSlot.ApparelBody
                };
                if (apparel.Contains(appearanceSlot)) continue;
                var asset = Resources.Load<PkoPartAsset>(BlueMageSet.PartPath(piece.Id, PkoRaces.BaseRace(Race)));
                if (asset == null || asset.mesh == null)
                {
                    Debug.LogError($"[PkoCharacterVisual] Missing Blue Mage part {piece.Id}, race {Race}.");
                    continue;
                }
                int replace = piece.Slot == EquipmentSlot.Helmet ? PartHair
                    : piece.Slot == EquipmentSlot.Gloves ? PartGloves
                    : piece.Slot == EquipmentSlot.Boots ? PartBoots : -1;
                var go = CreatePart(asset, "blue_" + piece.Key);
                if (piece.Slot == EquipmentSlot.Helmet && parts[PartFace] != null)
                    parts[PartFace].SetActive(false);
                if (replace >= 0)
                {
                    if (parts[replace] != null) { parts[replace].SetActive(false); Destroy(parts[replace]); }
                    parts[replace] = go;
                }
                else
                {
                    customParts.Add(go);
                    coverage |= piece.Slot == EquipmentSlot.Armor ? 1 : 2;
                }
            }
            if (coverage != 0 && parts[PartBody] != null)
            {
                var underlay = Resources.Load<PkoPartAsset>($"PkoChar/BlueMageSet/underlay_{PkoRaces.BaseRace(Race)}_{coverage}");
                if (underlay == null)
                {
                    Debug.LogError($"[PkoCharacterVisual] Missing Blue Mage undersuit for race {Race}, coverage {coverage}.");
                    return;
                }
                customParts.Add(CreatePart(underlay, "blue_undersuit"));
                var body = parts[PartBody].GetComponent<SkinnedMeshRenderer>();
                coveredBody = Instantiate(body.sharedMesh);
                coveredBody.hideFlags = HideFlags.DontSave;
                var vertices = coveredBody.vertices;
                float pelvis = coveredBody.bindposes[0].inverse.GetColumn(3).y;
                for (int submesh = 0; submesh < coveredBody.subMeshCount; submesh++)
                {
                    var original = coveredBody.GetTriangles(submesh);
                    var visible = new List<int>();
                    for (int i = 0; i < original.Length; i += 3)
                    {
                        float y = (vertices[original[i]].y + vertices[original[i + 1]].y + vertices[original[i + 2]].y) / 3f;
                        if (!BlueMageSet.Covers(y, pelvis, coverage))
                        { visible.Add(original[i]); visible.Add(original[i + 1]); visible.Add(original[i + 2]); }
                    }
                    coveredBody.SetTriangles(visible, submesh);
                }
                body.sharedMesh = coveredBody;
            }
        }

        void OnDestroy() { if (coveredBody != null) Destroy(coveredBody); }

        // Local rotation of the weapon model inside the grip dummy wrapper.
        void LateUpdate()
        {
            ApplyWeaponPose(rightWeapon, 0); ApplyWeaponPose(leftWeapon, 1);
            ApplyWingPose();
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

        void SetWing(PkoItem item)
        {
            if (wingVisual != null) { wingVisual.SetActive(false); Destroy(wingVisual); }
            wingVisual = null;
            WingItemId = 0;
            if (Pose != null) Pose.SetFlight(null);
            if (rig == null || item == null) return;

            string model = item.Model;
            string resource = item.Id == PkoTables.MeshyMageWingsItemId
                ? "Wings/Animated/MageWings"
                : "Wings/Items/" + item.Id;
            var prefab = Resources.Load<GameObject>(resource);
            if (prefab == null)
            {
                Debug.LogError($"[PkoCharacterVisual] Wing visual for '{item.Name}' ({item.Id}) is missing at Resources/{resource}. Run Tools/PKO/Build Animated Wings.");
                return;
            }

            var mount = new GameObject("wing_" + model).transform;
            mount.SetParent(rig.transform, false);
            var bodyAsset = parts[PartBody] != null ? parts[PartBody].GetComponent<SkinnedMeshRenderer>() : null;
            int chestIndex = System.Array.FindIndex(bones, b => b != null && b.name.EndsWith(" Spine1", System.StringComparison.Ordinal));
            if (chestIndex < 0 || bodyAsset == null || chestIndex >= bodyAsset.sharedMesh.bindposes.Length)
            {
                Debug.LogError($"[PkoCharacterVisual] Cannot attach wing {item.Id}: chest bone or body bind pose is missing.");
                Destroy(mount.gameObject);
                return;
            }
            var bind = bodyAsset.sharedMesh.bindposes[chestIndex].inverse;
            var restBounds = bodyAsset.sharedMesh.bounds;
            Vector3 chest = bind.GetColumn(3);
            // Use the torso's bind pose, not the animated skeleton's changing bounds.
            // Original character models face -Z, so the rear of the torso is +Z.
            Vector3 back = new Vector3(chest.x, chest.y, restBounds.max.z);
            wingBodyHeight = GetBindHeight(bodyAsset.sharedMesh.bindposes);
            mount.SetParent(bones[chestIndex], false);
            wingBasePosition = bind.inverse.MultiplyPoint3x4(back);
            wingBaseRotation = Quaternion.Inverse(bind.rotation);
            mount.localPosition = wingBasePosition;
            mount.localRotation = wingBaseRotation;

            bool originalWing = item.Id != PkoTables.MeshyMageWingsItemId;
            if (originalWing)
            {
                if (!dummies.TryGetValue("dummy_" + item.VisualEffectDummy, out var attachment))
                {
                    Debug.LogError($"[PkoCharacterVisual] Original wing {item.Id} requires missing dummy {item.VisualEffectDummy}.");
                    Destroy(mount.gameObject);
                    return;
                }
                mount.SetParent(attachment, false);
                wingBasePosition = Vector3.zero;
                wingBaseRotation = Quaternion.identity;
                mount.localPosition = wingBasePosition;
                mount.localRotation = wingBaseRotation;
            }

            var instance = Instantiate(prefab, mount);
            instance.name = "model_" + model;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogError($"[PkoCharacterVisual] Wing model '{model}' has no renderers.");
                Destroy(mount.gameObject);
                return;
            }

            if (!originalWing)
            {
                Bounds modelBounds = WingLocalBounds(instance, mount);
                float targetWidth = wingBodyHeight * 0.9f;
                float scale = targetWidth / Mathf.Max(0.0001f, modelBounds.size.x);
                instance.transform.localScale = Vector3.one * scale;
                instance.transform.localPosition = -modelBounds.center * scale;
            }
            wingVisual = mount.gameObject;
            WingItemId = item.Id;
            ApplyWingPose();
            bool flightWing = item.Id == PkoTables.MeshyMageWingsItemId
                || (item.Id >= 128 && item.Id <= 140 && item.Id != 135);
            if (flightWing && Pose != null)
            {
                var wingAnimation = instance.GetComponentInChildren<Animation>();
                if (wingAnimation == null)
                    Debug.LogError($"[PkoCharacterVisual] Flight wing {item.Id} has no legacy animation.");
                else Pose.SetFlight(wingAnimation);
            }
        }

        void ApplyWingPose()
        {
            if (wingVisual == null) return;
            var pose = PkoWingPose.Get(Race, WingItemId);
            wingVisual.transform.localPosition = wingBasePosition
                + wingBaseRotation * (pose.position * wingBodyHeight);
            wingVisual.transform.localRotation = wingBaseRotation * Quaternion.Euler(pose.euler);
            wingVisual.transform.localScale = Vector3.one * pose.scale;
        }

        static float GetBindHeight(Matrix4x4[] bindposes)
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var pose in bindposes)
            {
                float y = pose.inverse.GetColumn(3).y;
                min = Mathf.Min(min, y); max = Mathf.Max(max, y);
            }
            return Mathf.Max(.0001f, max - min);
        }

        static Bounds WingLocalBounds(GameObject instance, Transform mount)
        {
            var savedScale = instance.transform.localScale;
            var scaledMatrix = mount.worldToLocalMatrix * instance.transform.localToWorldMatrix;
            // BakeMesh can include inherited scale; measure unscaled and apply it once.
            instance.transform.localScale = Vector3.one;
            var unscaledMatrix = mount.worldToLocalMatrix * instance.transform.localToWorldMatrix;
            var correction = scaledMatrix * unscaledMatrix.inverse;
            Bounds bounds = new Bounds();
            bool found = false;
            try
            {
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                {
                    Mesh baked = null;
                    try
                    {
                        Bounds local;
                        if (renderer is SkinnedMeshRenderer skin)
                        {
                            baked = new Mesh();
                            skin.BakeMesh(baked);
                            baked.RecalculateBounds();
                            local = baked.bounds;
                        }
                        else
                        {
                            var filter = renderer.GetComponent<MeshFilter>();
                            if (filter == null || filter.sharedMesh == null)
                                throw new System.InvalidOperationException("Wing renderer has no mesh: " + renderer.name);
                            local = filter.sharedMesh.bounds;
                        }
                        var matrix = mount.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 point = correction.MultiplyPoint3x4(matrix.MultiplyPoint3x4(new Vector3(
                                (i & 1) == 0 ? local.min.x : local.max.x,
                                (i & 2) == 0 ? local.min.y : local.max.y,
                                (i & 4) == 0 ? local.min.z : local.max.z)));
                            if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                            else bounds.Encapsulate(point);
                        }
                    }
                    finally
                    {
                        if (baked != null)
                        {
                            if (Application.isPlaying) Destroy(baked);
                            else DestroyImmediate(baked);
                        }
                    }
                }
                return bounds;
            }
            finally { instance.transform.localScale = savedScale; }
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
