using System;

namespace TOP.Data
{
    [Serializable]
    public class FriendData
    {
        public long Id;
        public string Name;
        public int Level;
        public int Job;
        public DateTime? LastOnline;
    }

    [Serializable]
    public class MailData
    {
        public long Id;
        public string SenderName;
        public string Subject;
        public string Body;
        public long Gold;
        public int ItemId;
        public int ItemQuantity;
        public int ItemRefine;
        public bool IsRead;
        public bool IsClaimed;
        public DateTime CreatedAt;

        public bool HasAttachment => Gold > 0 || ItemId >= 0;
    }

    [Serializable]
    public class GuildMemberData
    {
        public long CharacterId;
        public string Name;
        public string Rank;
    }

    [Serializable]
    public class GuildData
    {
        public bool InGuild;
        public int GuildId;
        public string Name;
        public string Notice;
        public int Level;
        public long LeaderCharacterId;
        public string MyRank;
        public System.Collections.Generic.List<GuildMemberData> Members = new System.Collections.Generic.List<GuildMemberData>();

        public bool IsLeaderOrOfficer => MyRank == "Lider" || MyRank == "Oficial";
    }
}
