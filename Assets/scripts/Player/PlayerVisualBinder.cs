using TOP.Character;
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

        void Awake()
        {
            pc = GetComponent<PlayerController>();
            equipment = GetComponent<PlayerEquipment>();
        }

        void Update()
        {
            if (pc == null || string.IsNullOrEmpty(pc.CharacterName)) return;
            var items = equipment != null ? equipment.GetEquippedItemIds() : new System.Collections.Generic.List<int>();
            string key = pc.Job + "|" + pc.HairStyle + "|" + pc.FaceStyle + "|" + string.Join(",", items);
            if (key == appliedKey) return;
            appliedKey = key;

            if (visual == null)
            {
                var root = transform.Find("CharacterVisual") ?? transform;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                visual = PkoCharacterVisual.Create(transform, pc.Job, pc.FaceStyle, pc.HairStyle, items);
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
                visual.transform.localPosition = Vector3.zero;
            }
            else visual.Apply(pc.Job, pc.FaceStyle, pc.HairStyle, items);
        }
    }
}