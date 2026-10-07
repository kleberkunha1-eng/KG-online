using UnityEngine;

namespace TOP.Core
{

    [CreateAssetMenu(fileName = "NewEquipment", menuName = "Tales of Pirates/Equipment Data")]
    public class EquipmentData : ItemData
    {
        [Header("Slot de Equipamento")]
        public EquipmentSlot slot = EquipmentSlot.Weapon;

        [Header("Requisitos")]
        public int requiredLevel = 1;
        public int requiredSTR;
        public int requiredAGI;
        public int requiredINT;

        [Header("Bônus de Atributos")]
        public int bonusAttack;
        public int bonusDefense;
        public int bonusMagicAttack;
        public int bonusMagicDefense;
        public int bonusSTR;
        public int bonusAGI;
        public int bonusCON;
        public int bonusSPR;
        public int bonusINT;
        public int bonusHP;
        public int bonusMP;
        public int bonusSP;
        public int bonusSpeed;

        [Header("Durabilidade")]
        public int maxDurability = 100;
        public int durability;

        [Header("Refinamento")]
        public int maxRefineLevel = 9;

        [Header("Visual no Personagem")]
        [Tooltip("Prefab 3D que aparece no personagem quando equipado (ex: modelo da espada)")]
        public GameObject equipPrefab;
        [Tooltip("Offset de posição local no holder")]
        public Vector3 equipPositionOffset = Vector3.zero;
        [Tooltip("Offset de rotação local no holder")]
        public Vector3 equipRotationOffset = Vector3.zero;
        [Tooltip("Escala local no holder")]
        public Vector3 equipScale = Vector3.one;

        void OnEnable()
        {
            if (durability == 0 && maxDurability > 0)
                durability = maxDurability;
            itemType = GetItemTypeFromSlot(slot);
        }

        static ItemType GetItemTypeFromSlot(EquipmentSlot s)
        {
            return s switch
            {
                EquipmentSlot.Weapon => ItemType.Equipment,
                EquipmentSlot.Armor => ItemType.Equipment,
                EquipmentSlot.Helmet => ItemType.Equipment,
                EquipmentSlot.Shield => ItemType.Equipment,
                EquipmentSlot.Gloves => ItemType.Equipment,
                EquipmentSlot.Boots => ItemType.Equipment,
                EquipmentSlot.Cape => ItemType.Equipment,
                EquipmentSlot.Belt => ItemType.Equipment,
                _ => ItemType.Equipment
            };
        }
    }
}
