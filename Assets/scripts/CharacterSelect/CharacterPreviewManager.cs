using System.Collections.Generic;
using TOP.Character;
using TOP.Data;
using UnityEngine;

namespace TOP.CharacterSelect
{
    // Shows the 3D characters of the selection / creation screens. Each slot renders through its own camera,
    // which is re-framed from the model's real size every time the look changes.
    public class CharacterPreviewManager : MonoBehaviour
    {
        public static CharacterPreviewManager Instance { get; private set; }

        // Original client models face -Y in model space; rotated so they look at the camera.
        const float FacingYaw = 180f;
        const float FramingMargin = 1.12f;
        // Previews live on their own layer and are only drawn by the slot cameras, so a networked player (spawned in this
        // scene while entering the world, at the same position as slot 2) can never show up inside a preview.
        public const int PreviewLayer = 31;

        [SerializeField] private Transform[] previewSlots = new Transform[3];

        readonly PkoCharacterVisual[] visuals = new PkoCharacterVisual[3];
        readonly Camera[] cameras = new Camera[3];

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        public Transform GetSlotTransform(int slot) { return slot >= 0 && slot < previewSlots.Length ? previewSlots[slot] : null; }

        public void RegisterCamera(int slot, Camera cam)
        {
            if (slot < 0 || slot >= 3) return;
            cameras[slot] = cam; cam.cullingMask = 1 << PreviewLayer; Frame(slot);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        public void ShowExistingCharacter(int slot, CharacterPreviewData d)
        {
            if (d == null) { ClearPreview(slot); return; }
            Show(slot, d.Job, d.HairStyle, d.FaceStyle, d.Equipped);
        }

        public void ShowCreatePreview(int slot, byte race, byte gender, int hair = 0, int face = 0)
        {
            Show(slot, race, hair, face, null);
        }

        public void ChangeLook(int slot, byte race, int hair, int face) { Show(slot, race, hair, face, null); }

        void Show(int slot, int race, int hair, int face, IEnumerable<int> items)
        {
            if (!Valid(slot)) return;
            if (visuals[slot] == null)
            {
                foreach (var stray in previewSlots[slot].GetComponentsInChildren<PkoCharacterVisual>(true)) { stray.gameObject.SetActive(false); Destroy(stray.gameObject); }
                visuals[slot] = PkoCharacterVisual.Create(previewSlots[slot], race, face, hair, items);
                visuals[slot].transform.localRotation = Quaternion.Euler(0f, FacingYaw, 0f);
            }
            else visuals[slot].Apply(race, face, hair, items);
            SetLayer(visuals[slot].transform, PreviewLayer);
            Frame(slot);
        }

        public void ClearPreview(int slot)
        {
            if (!Valid(slot) || visuals[slot] == null) return;
            Destroy(visuals[slot].gameObject);
            visuals[slot] = null;
        }

        public void ClearAllPreviews() { for (int i = 0; i < 3; i++) ClearPreview(i); }

        public void RotateCharacter(int slot, float deltaY)
        {
            if (Valid(slot) && visuals[slot] != null) visuals[slot].transform.Rotate(0f, deltaY, 0f, Space.World);
        }

        // Positions the slot camera so the whole body (feet to head) fits the render texture.
        void Frame(int slot)
        {
            var cam = cameras[slot]; var v = visuals[slot];
            if (cam == null || v == null || !v.TryGetBodyBounds(out Bounds b)) return;
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distV = b.extents.y * FramingMargin / tan;
            float distH = Mathf.Max(b.extents.x, b.extents.z) * FramingMargin / (tan * cam.aspect);
            float dist = Mathf.Max(distV, distH);
            Vector3 center = b.center;
            cam.transform.position = center + new Vector3(0f, 0f, -dist);
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = Mathf.Max(0.05f, dist - b.extents.magnitude * 1.5f);
            cam.farClipPlane = dist + b.extents.magnitude * 3f;
        }

        bool Valid(int slot) { return slot >= 0 && slot < 3 && previewSlots != null && slot < previewSlots.Length && previewSlots[slot] != null; }
    }
}