using UnityEngine;

namespace TOP.Core
{

    public enum CharacterClass
    {
        None = 0,
        Swordsman = 1,
        Hunter = 2,
        Explorer = 3,
        Herbalist = 4,
        Champion = 5,
        Crusader = 6,
        Sharpshooter = 7,
        Voyager = 8,
        Cleric = 9
    }

    public enum ItemRarity
    {
        Normal = 0,
        Magic = 1,
        Rare = 2,
        Legendary = 3,
        Unique = 4
    }

    public enum ItemType
    {
        Equipment,
        Consumable,
        Material,
        Quest,
        Gem,
        Pet,
        Mount,
        SkillBook,
        Other
    }

    public enum EquipmentSlot
    {
        Helmet = 0,
        Armor = 1,
        Weapon = 2,
        Shield = 3,
        Gloves = 4,
        Boots = 5,
        Belt = 6,
        Earring = 7,
        Necklace = 8,
        Ring1 = 9,
        Ring2 = 10,
        Tattoo = 11,
        Costume = 12,
        Cape = 13,
        Pet = 14,
        Mount = 15,
        // Slots de aparencia (visual) e extras do cliente original
        Wing = 16,
        ApparelHelmet = 17,
        ApparelBody = 18,
        ApparelGloves = 19,
        ApparelBoots = 20,
        ApparelShield = 21,
        ApparelSword = 22,
        ApparelGreatSword = 23,
        ApparelGun = 24,
        ApparelDagger = 25,
        ApparelStaff = 26,
        ApparelBow = 27,
        ApparelPet = 28,
        ApparelGlow = 29
    }

    public enum WeaponType
    {
        None,
        Sword,
        GreatSword,
        DualSword,
        Bow,
        Gun,
        Dagger,
        Staff,
        Wand,
        Axe,
        Spear,
        Fist
    }

    public enum ArmorType
    {
        Light,
        Medium,
        Heavy,
        Robe
    }

    public enum ElementType
    {
        None = 0,
        Fire = 1,
        Water = 2,
        Earth = 3,
        Wind = 4,
        Thunder = 5,
        Ice = 6,
        Poison = 7,
        Holy = 8,
        Dark = 9
    }

    public enum SkillType
    {
        Active,
        Passive,
        Toggle,
        Buff,
        Debuff,
        Summon,
        Transform
    }

    public enum SkillTargetType
    {
        Self,
        SingleEnemy,
        SingleAlly,
        AreaEnemy,
        AreaAlly,
        AreaAll,
        Line,
        Cone,
        Chain
    }

    public enum SkillElement
    {
        None,
        Fire,
        Water,
        Earth,
        Wind,
        Thunder,
        Ice,
        Poison,
        Holy,
        Dark
    }

    public enum ConsumableType
    {
        HP_Potion,
        MP_Potion,
        SP_Potion,
        Food,
        Scroll,
        Buff,
        Resurrection,
        Teleport,
        Repair,
        Identify
    }

    public enum StatType
    {
        STR, AGI, CON, SPR, ACC, LUCK,
        HP, MP, SP,
        Attack, Defense, MagicAttack, MagicDefense,
        AttackSpeed, MoveSpeed,
        CriticalRate, CriticalDamage,
        HitRate, DodgeRate,
        HPRegen, MPRegen, SPRegen,
        EXP
    }

    public enum QuestType
    {
        Main,
        Side,
        Daily,
        Guild,
        Event,
        Repeatable
    }

    public enum QuestObjectiveType
    {
        KillMonster,
        CollectItem,
        TalkToNPC,
        ReachLocation,
        DefeatBoss,
        EscortNPC,
        DeliverItem,
        UseItem,
        CraftItem,
        TrainSkill
    }

    public enum DamageType
    {
        Physical,
        Magical,
        True,
        Fire,
        Water,
        Earth,
        Wind,
        Thunder,
        Ice,
        Poison,
        Holy,
        Dark
    }

    public enum NPCType
    {
        Merchant,
        QuestGiver,
        Blacksmith,
        Healer,
        Banker,
        GuildMaster,
        SkillMaster,
        StableMaster,
        Teleporter,
        Hairdresser,
        Other
    }

    public enum HotbarSlotType
    {
        Empty,
        Skill,
        Item,
        Emote
    }

    public enum GemType
    {
        Ruby,
        Sapphire,
        Emerald,
        Topaz,
        Diamond,
        Amethyst,
        Obsidian,
        Pearl
    }

    [CreateAssetMenu(fileName = "NewCharacterClass", menuName = "TOP/Character Class")]
    public class CharacterClassData : ScriptableObject
    {
        [Header("Informações Básicas")]
        public string className = "Swordsman";
        public CharacterClass classType = CharacterClass.Swordsman;
        public Sprite classIcon;
        public string description;

        [Header("Atributos Base (Nível 1)")]
        public int baseSTR = 10;
        public int baseAGI = 10;
        public int baseCON = 10;
        public int baseSPR = 10;
        public int baseACC = 10;
        public int baseLUCK = 5;

        [Header("Crescimento por Nível")]
        public float strGrowth = 2.0f;
        public float agiGrowth = 1.5f;
        public float conGrowth = 1.5f;
        public float sprGrowth = 1.0f;
        public float accGrowth = 1.0f;
        public float luckGrowth = 0.5f;

        [Header("Combate")]
        public float baseMoveSpeed = 5.0f;
        public float baseAttackSpeed = 1.0f;
        public int baseAttackRange = 2;

        [Header("Animação")]
        public RuntimeAnimatorController maleAnimator;
        public RuntimeAnimatorController femaleAnimator;

        [Header("Armas Permitidas")]
        public WeaponType[] allowedWeapons;

        [Header("Armaduras Permitidas")]
        public ArmorType[] allowedArmors;
    }
}
