using TOP.Character;
using TOP.Data;
using UnityEngine;

namespace TOP.Player
{
    // Replaces the placeholder model of the player prefab with the original-client character
    // (race skeleton + face/hair/body/gloves/boots + weapon) and keeps it in sync with the synchronized
    // appearance and equipment.
    [RequireComponent(typeof(PlayerController))]
    public class PlayerVisualBinder : MonoBehaviour
    {
        PlayerController pc;
        PlayerEquipment equipment;
        PkoCharacterVisual visual;
        string appliedKey;
        GameObject boatVisual;
        string appliedBoatId;
        bool placeholderHidden;

        void Awake()
        {
            pc = GetComponent<PlayerController>();
            equipment = GetComponent<PlayerEquipment>();
        }

        void Update()
        {
            if (pc == null || string.IsNullOrEmpty(pc.CharacterName)) return;
            if (pc.IsAboardBoat)
            {
                if (visual != null) visual.gameObject.SetActive(false);
                else HidePlaceholder();
                if (boatVisual == null || appliedBoatId != pc.ActiveBoatId) CreateBoatVisual();
                if (boatVisual != null) boatVisual.SetActive(true);
                return;
            }
            if (boatVisual != null) boatVisual.SetActive(false);
            if (visual != null) visual.gameObject.SetActive(true);
            var items = equipment != null ? equipment.GetEquippedItemIds() : new System.Collections.Generic.List<int>();
            string key = pc.Job + "|" + pc.HairStyle + "|" + pc.FaceStyle + "|" + string.Join(",", items);
            if (key == appliedKey) return;
            appliedKey = key;

            if (visual == null)
            {
                var root = transform.Find("CharacterVisual") ?? transform;
                HidePlaceholder();
                visual = PkoCharacterVisual.Create(transform, pc.Job, pc.FaceStyle, pc.HairStyle, items);
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
                visual.transform.localPosition = Vector3.zero;
            }
            else visual.Apply(pc.Job, pc.FaceStyle, pc.HairStyle, items);
        }

        void HidePlaceholder()
        {
            if (placeholderHidden) return;
            var root = transform.Find("CharacterVisual");
            if (root == null) root = transform;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            placeholderHidden = true;
        }

        void CreateBoatVisual()
        {
            if (boatVisual != null) Destroy(boatVisual);
            appliedBoatId = pc.ActiveBoatId;
            var boat = pc.ActiveBoat;
            if (boat == null) return;
            boatVisual = new GameObject("BoatVisual_" + boat.TypeId);
            boatVisual.transform.SetParent(transform, false);
            GameObject originalHull = null;
            try { originalHull = Resources.Load<GameObject>("PKOShips/Models/model/character/" + BoatCatalog.HullModelId(boat)); }
            catch (System.InvalidOperationException) { }
            if (originalHull != null)
            {
                var hull = Instantiate(originalHull, boatVisual.transform, false);
                hull.name = "OriginalHull_" + boat.TypeId;
                hull.transform.localPosition = new Vector3(0f, -.5f, 0f);
                hull.transform.localRotation = Quaternion.identity;
                hull.transform.localScale = Vector3.one;
            }
            else
            {
                float scale = boat.TypeId == 1 ? 1f : boat.TypeId == 2 ? 1.15f : boat.TypeId == 3 ? 1.3f : 1.1f;
                AddBoatPart(boatVisual.transform, "Hull", PrimitiveType.Cube, new Vector3(0f, -.35f, 0f),
                    new Vector3(1.55f * scale, .55f, 3.2f * scale), new Color(.36f, .19f, .08f));
                AddBoatPart(boatVisual.transform, "Deck", PrimitiveType.Cube, new Vector3(0f, -.02f, -.15f),
                    new Vector3(1.25f * scale, .12f, 2.4f * scale), new Color(.58f, .38f, .17f));
                AddBoatPart(boatVisual.transform, "Bow", PrimitiveType.Cube, new Vector3(0f, -.22f, 1.48f * scale),
                    new Vector3(.45f * scale, .38f, .75f * scale), new Color(.36f, .19f, .08f));
                AddBoatPart(boatVisual.transform, "Cabin", PrimitiveType.Cube, new Vector3(0f, .23f, -.72f * scale),
                    new Vector3(.82f * scale, .48f, .7f * scale), new Color(.44f, .27f, .12f));
                AddBoatPart(boatVisual.transform, "Mast", PrimitiveType.Cylinder, new Vector3(0f, .72f, .1f),
                    new Vector3(.07f, .85f, .07f), new Color(.31f, .18f, .09f));
                AddBoatPart(boatVisual.transform, "Sail", PrimitiveType.Cube, new Vector3(0f, .92f, .1f),
                    new Vector3(.08f, .85f, .72f * scale), new Color(.86f, .81f, .65f));
            }
        }

        static void AddBoatPart(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }
    }
}