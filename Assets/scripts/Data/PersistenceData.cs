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
        public long SaveRevision;
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
        public List<BoatData> Boats = new List<BoatData>();
        public int BoatOwnershipVersion;
        public int BoatServicesVersion;
        public List<InventoryItemData> BankItems = new List<InventoryItemData>();
        public int BankStorageVersion;
        public int GameplayStateVersion;
        public GameplayState Gameplay = new GameplayState();
        public string GuildCreateName;
        public int QuestStateVersion;
        public OriginalQuestState OriginalQuests = new OriginalQuestState();

        public Vector3 Position => new Vector3(PosX, PosY, PosZ);

        internal CharacterData CopySnapshot()
        {
            var snapshot = (CharacterData)MemberwiseClone();
            snapshot.Gameplay = Gameplay == null ? new GameplayState() : JsonUtility.FromJson<GameplayState>(JsonUtility.ToJson(Gameplay));
            snapshot.OriginalQuests = OriginalQuests == null ? new OriginalQuestState() : OriginalQuests.Copy();
            snapshot.Inventory = Inventory == null ? null : Inventory.ConvertAll(item => item == null ? null : new InventoryItemData
            {
                Id = item.Id, UniqueItemId = item.UniqueItemId, SlotIndex = item.SlotIndex, ItemId = item.ItemId,
                Quantity = item.Quantity, Durability = item.Durability, FusionItemId = item.FusionItemId, MedalHonor = item.MedalHonor, MedalWins = item.MedalWins, MedalEntries = item.MedalEntries, MedalKills = item.MedalKills, MedalDeaths = item.MedalDeaths,
                IsEquipped = item.IsEquipped, IsLocked = item.IsLocked, OwnerCharacterId = item.OwnerCharacterId,
                RefineLevel = item.RefineLevel, GemSlot1 = item.GemSlot1, GemSlot2 = item.GemSlot2, GemSlot3 = item.GemSlot3
            });
            snapshot.Skills = Skills == null ? null : Skills.ConvertAll(skill => skill == null ? null : new CharacterSkillData
            {
                Id = skill.Id, SkillId = skill.SkillId, Level = skill.Level, Exp = skill.Exp
            });
            snapshot.Boats = Boats == null ? null : Boats.ConvertAll(boat => boat.CopySnapshot());
            snapshot.BankItems = BankItems == null ? null : BankItems.ConvertAll(item => item == null ? null : new InventoryItemData
            {
                Id = item.Id, UniqueItemId = item.UniqueItemId, SlotIndex = item.SlotIndex, ItemId = item.ItemId,
                Quantity = item.Quantity, Durability = item.Durability, FusionItemId = item.FusionItemId, MedalHonor = item.MedalHonor, MedalWins = item.MedalWins, MedalEntries = item.MedalEntries, MedalKills = item.MedalKills, MedalDeaths = item.MedalDeaths,
                IsEquipped = item.IsEquipped, IsLocked = item.IsLocked, OwnerCharacterId = item.OwnerCharacterId,
                RefineLevel = item.RefineLevel, GemSlot1 = item.GemSlot1, GemSlot2 = item.GemSlot2, GemSlot3 = item.GemSlot3
            });
            return snapshot;
        }

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
        public int FusionItemId;
        public int MedalHonor, MedalWins, MedalEntries, MedalKills, MedalDeaths;
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
