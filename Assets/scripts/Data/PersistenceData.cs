using System;
using System.Collections.Generic;
using UnityEngine;

namespace TOP.Data
{
    [Serializable]
    public class CharacterData
    {
        public long Id;
        public long AccountId;
        public string Name;
        public byte Job;
        public byte Gender;
        public int Level;
        public ulong Exp;

        public int CurrentHp;
        public int CurrentMp;
        public int CurrentSp;
        public int MaxHp;
        public int MaxMp;
        public int MaxSp;

        public int BaseStr;
        public int BaseAgi;
        public int BaseCon;
        public int BaseSpr;
        public int BaseSta;

        public ulong Gold;
        public int StatPoints;
        public int SkillPoints;
        public int PkPoints;
        public int Reputation;

        public byte HairStyle;
        public byte HairColor;
        public byte FaceStyle;

        public string MapName = "garner";
        public float PosX;
        public float PosY;
        public float PosZ;
        public float RotationY;

        public List<InventoryItemData> Inventory = new List<InventoryItemData>();
        public List<CharacterSkillData> Skills = new List<CharacterSkillData>();

        public Vector3 Position => new Vector3(PosX, PosY, PosZ);

        public void SetPosition(Vector3 position)
        {
            PosX = position.x;
            PosY = position.y;
            PosZ = position.z;
        }
    }

    [Serializable]
    public class InventoryItemData
    {
        public long Id;
        public string UniqueItemId;
        public ushort SlotIndex;
        public int ItemId;
        public int Quantity;
        public ushort Durability;
        public bool IsEquipped;
        public bool IsLocked;
        public long? OwnerCharacterId;
        public int RefineLevel;
        public int? GemSlot1;
        public int? GemSlot2;
        public int? GemSlot3;

        public void EnsureUniqueId()
        {
            if (string.IsNullOrWhiteSpace(UniqueItemId))
                UniqueItemId = Guid.NewGuid().ToString();
        }
    }

    [Serializable]
    public class CharacterSkillData
    {
        public long Id;
        public int SkillId;
        public byte Level;
        public ulong Exp;
    }
}
