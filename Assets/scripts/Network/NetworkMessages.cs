using Mirror;
using UnityEngine;
using TOP.Core;
using TOP.Data;  // ✅ Usa CharacterPreviewData de TOP.Data

namespace TOP.Network
{

    // ============================================================
// MIRROR-FRIENDLY PREVIEW (sem DateTime? nem Vector3)
// ============================================================
public struct NetworkCharacterPreview : NetworkMessage
{
    public long Id;
    public byte SlotIndex;
    public string Name;
    public byte Gender;
    public byte Job;
    public int Level;
    public string MapName;
    public float PosX;
    public float PosY;
    public float PosZ;
    public float RotationY;
    public byte HairStyle;
    public byte HairColor;
    public byte FaceStyle;
    public int[] EquippedItems;
    public long LastOnlineTicks; // 0 = null
}
    // ============================================================
    // AUTH
    // ============================================================
    public struct AuthRequestMessage : NetworkMessage
    {
        public string jwtToken;
        public long accountId;
    }

    public struct AuthResponseMessage : NetworkMessage
    {
        public bool accepted;
        public string errorMessage;
    }

    public struct LoginRequest : NetworkMessage
    {
        public string Username;
        public string Password;
    }

    public struct LoginResponse : NetworkMessage
    {
        public bool Success;
        public string SessionToken;
        public string ErrorCode;
        public string Message;
    }

    // ============================================================
    // CHARACTER
    // ============================================================
    public struct CharacterListRequest : NetworkMessage { }

   public struct CharacterListResponse : NetworkMessage
    {
        public bool Success;
        public string Error;
        public NetworkCharacterPreview[] Characters;  // ✅ NOVO - MIRROR-FRIENDLY
    }

    public struct CreateCharacterRequest : NetworkMessage
    {
        public byte SlotIndex;
        public string Name;
        public byte Gender;
        public byte Job;
        public byte HairStyle;
        public byte HairColor;
        public byte FaceStyle;
        public byte StartCity;
    }

    public struct CreateCharacterResponse : NetworkMessage
    {
        public bool Success;
        public long CharacterId;
        public string Error;
    }

    public struct SelectCharacterRequest : NetworkMessage
    {
        public long CharacterId;
    }

    public struct SelectCharacterResponse : NetworkMessage
    {
        public bool Success;
        public long CharacterId;
        public string MapName;
        public Vector3 Position;
        public float RotationY;
        public string Error;
    }

    public struct DeleteCharacterRequest : NetworkMessage
    {
        public long CharacterId;
        public string Password;
    }

    public struct DeleteCharacterResponse : NetworkMessage
    {
        public bool Success;
        public string Error;
    }

    // ============================================================
    // PING
    // ============================================================
    public struct ClientPing : NetworkMessage
    {
        public float ClientTime;
    }

    public struct ServerPong : NetworkMessage
    {
        public float ClientTime;
        public float ServerTime;
    }

    public struct ServerMessage : NetworkMessage
    {
        public ServerMessageType Type;
        public string Text;
    }

    public enum ServerMessageType
    {
        Info, Warning, Error, Kicked, Banned, Maintenance
    }

    // ============================================================
    // INVENTORY
    // ============================================================
    public struct MoveItemRequest : NetworkMessage
    {
        public ushort FromSlot;
        public ushort ToSlot;
    }

    public struct EquipItemRequest : NetworkMessage
    {
        public ushort InventorySlot;
        public EquipmentSlot TargetSlot;
    }

    public struct DropItemRequest : NetworkMessage
    {
        public ushort SlotIndex;
        public int Quantity;
        public Vector3 DropPosition;
    }

    public struct UseItemRequest : NetworkMessage
    {
        public ushort SlotIndex;
    }

    // ============================================================
    // CHAT
    // ============================================================
    public struct ChatMessage : NetworkMessage
    {
        public ChatChannel Channel;
        public string Text;
        public string TargetName;
    }

    public enum ChatChannel
    {
        World, Party, Guild, Whisper, System, Trade, Shout
    }

    // ============================================================
    // COMBAT
    // ============================================================
    public struct AttackRequest : NetworkMessage
    {
        public uint TargetNetId;
        public int SkillId;
    }

    public struct DamageResponse : NetworkMessage
    {
        public uint TargetNetId;
        public int Damage;
        public bool IsCritical;
        public bool IsMiss;
        public DamageType DamageType;
    }

    // ============================================================
    // MOVEMENT
    // ============================================================
    public struct MoveRequest : NetworkMessage
    {
        public Vector3 Position;
        public float RotationY;
        public bool IsRunning;
    }

    // ============================================================
    // LEVEL UP
    // ============================================================
    public struct LevelUpResponse : NetworkMessage
    {
        public int NewLevel;
        public int StatPointsGained;
        public int SkillPointsGained;
    }
}
