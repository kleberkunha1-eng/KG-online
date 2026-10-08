using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using TOP.Data;

namespace TOP.Services
{
    /// <summary>
    /// Persistencia do jogo via API HTTP (/api/game). O cliente nunca acessa o banco de dados.
    /// A conta e sempre a do token JWT do login; o accountId recebido nos metodos e ignorado pelo servidor.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class DatabaseService : MonoBehaviour
    {
        public static DatabaseService Instance { get; private set; }
        public bool IsConnected => TOP.Network.LoginNetworkClient.IsLoggedIn;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ---- DTOs (JsonUtility) ----
        [Serializable] class Ok { public bool success; public string error; public long charId; public bool admin; }

        [Serializable] class PreviewDto
        {
            public long Id; public byte SlotIndex; public string Name; public byte Gender, Job; public int Level; public string MapName;
            public float PosX, PosY, PosZ, RotationY; public byte HairStyle, HairColor, FaceStyle; public string Equipped; public long LastOnline;
        }
        [Serializable] class ListDto { public bool success; public List<PreviewDto> Characters = new List<PreviewDto>(); }

        [Serializable] class ItemDto
        {
            public long Id; public string UniqueItemId; public ushort SlotIndex; public int ItemId, Quantity; public ushort Durability;
            public bool IsEquipped, IsLocked; public long OwnerCharacterId = -1; public int RefineLevel; public int Gem1 = -1, Gem2 = -1, Gem3 = -1;
        }
        [Serializable] class SkillDto { public long Id; public int SkillId; public byte Level; public ulong Exp; }
        [Serializable] class LoadDto
        {
            public bool success; public string error; public CharacterData Character;
            public List<ItemDto> Inventory = new List<ItemDto>(); public List<SkillDto> Skills = new List<SkillDto>();
        }
        [Serializable] class SaveDto { public CharacterData Character; public List<ItemDto> Inventory; public List<SkillDto> Skills; }
        [Serializable] class CityDto { public string map; public float x, y, z, rotY; }
        [Serializable] class CreateDto { public string name; public int slot, job, gender, hairStyle, hairColor, faceStyle; public CityDto city; }
        [Serializable] class DeleteDto { public string password; }
        [Serializable] class AuditDto { public string action; public long charId; public string detail; }

        [Serializable] class FriendDto { public long Id; public string Name; public int Level; public int Job; public long LastOnline; }
        [Serializable] class FriendListDto { public bool success; public string error; public List<FriendDto> Friends = new List<FriendDto>(); }
        [Serializable] class AddFriendDto { public string name; }
        [Serializable] class RemoveFriendDto { public long friendId; }

        [Serializable] class MailDto
        {
            public long Id; public string SenderName; public string Subject; public string Body; public long Gold;
            public int ItemId; public int ItemQuantity; public int ItemRefine; public bool IsRead; public bool IsClaimed; public long CreatedAt;
        }
        [Serializable] class MailListDto { public bool success; public string error; public List<MailDto> Mails = new List<MailDto>(); }
        [Serializable] class SendMailDto { public string targetName; public string subject; public string body; public long gold; public int itemId = -1; public int itemQuantity; public int itemRefine; }
        [Serializable] class ClaimMailDto { public bool success; public string error; public long Gold; public int ItemId; public int ItemQuantity; public int ItemRefine; }

        [Serializable] class GuildMemberDto { public long CharacterId; public string Name; public string Rank; }
        [Serializable] class GuildDto
        {
            public bool success; public string error; public bool InGuild; public int GuildId; public string Name; public string Notice;
            public int Level; public long LeaderCharacterId; public string MyRank; public List<GuildMemberDto> Members = new List<GuildMemberDto>();
        }
        [Serializable] class CreateGuildDto { public string name; }
        [Serializable] class GuildCreateResultDto { public bool success; public string error; public int GuildId; }
        [Serializable] class GuildTargetDto { public string targetName; }
        [Serializable] class GuildNoticeDto { public string notice; }

        [Serializable] class QuestActiveDto { public int questId; public int progress; }
        [Serializable] class QuestListDto { public bool success; public string error; public List<QuestActiveDto> active = new List<QuestActiveDto>(); public List<int> completed = new List<int>(); }
        [Serializable] class QuestIdDto { public int questId; }
        [Serializable] class QuestProgressDto { public int questId; public int progress; }

        // ---- HTTP ----
        // O token JWT e sempre o da conta/jogador especifico da requisicao - nunca um estado estatico global.
        // No cliente standalone (login local), quem chama passa TOP.Network.LoginNetworkClient.AuthToken.
        // No servidor dedicado, quem chama passa o token armazenado por conexao (PlayerConnection.SessionToken),
        // pois o processo do servidor nunca faz login localmente e nao possui um AuthToken estatico valido.
        static async Task<string> SendAsync(string method, string path, string body, string token, CancellationToken ct)
        {
            using (var req = new UnityWebRequest(ApiConfig.GameUrl + path, method))
            {
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", "Bearer " + (token ?? ""));
                req.timeout = 20;
                double traceStarted = TOP.Diagnostics.GameTrace.Now;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (ct.IsCancellationRequested) { req.Abort(); throw new OperationCanceledException(); }
                    await Task.Yield();
                }
                TOP.Diagnostics.GameTrace.Http(req, traceStarted);
                string text = req.downloadHandler.text;
                if (req.result == UnityWebRequest.Result.ConnectionError)
                    throw new Exception("Sem conexao com a API: " + req.error);
                if (string.IsNullOrEmpty(text)) throw new Exception("Resposta vazia da API (" + req.responseCode + ")");
                return text;
            }
        }

        static int[] ParseIds(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return new int[0];
            var list = new List<int>();
            foreach (var s in csv.Split(',')) if (int.TryParse(s, out int v)) list.Add(v);
            return list.ToArray();
        }

        // ---- API publica (mesmas assinaturas do antigo acesso direto ao MySQL) ----
        // IMPORTANTE: 'token' deve ser o JWT especifico do jogador desta chamada (PlayerConnection.SessionToken
        // no servidor dedicado, ou LoginNetworkClient.AuthToken no cliente standalone). Nunca reutilizar um
        // token estatico/global, pois o servidor atende multiplos jogadores simultaneamente.
        public async Task<bool> IsAdminAsync(long accountId, string token, CancellationToken ct = default)
        {
            try { return JsonUtility.FromJson<Ok>(await SendAsync("GET", "/me", null, token, ct)).admin; }
            catch (Exception e) { Debug.LogError("[API] IsAdmin: " + e.Message); return false; }
        }

        public async Task<List<CharacterPreviewData>> GetCharacterListAsync(long accountId, string token, CancellationToken ct = default)
        {
            var result = new List<CharacterPreviewData>();
            try
            {
                var dto = JsonUtility.FromJson<ListDto>(await SendAsync("GET", "/characters", null, token, ct));
                if (dto == null || !dto.success) return result;
                foreach (var p in dto.Characters)
                    result.Add(new CharacterPreviewData
                    {
                        Id = p.Id, SlotIndex = p.SlotIndex, Name = p.Name, Gender = p.Gender, Job = p.Job, Level = p.Level, MapName = p.MapName,
                        PosX = p.PosX, PosY = p.PosY, PosZ = p.PosZ, RotationY = p.RotationY,
                        HairStyle = p.HairStyle, HairColor = p.HairColor, FaceStyle = p.FaceStyle,
                        Equipped = ParseIds(p.Equipped),
                        LastOnline = p.LastOnline > 0 ? DateTimeOffset.FromUnixTimeSeconds(p.LastOnline).UtcDateTime : (DateTime?)null,
                    });
            }
            catch (Exception e) { Debug.LogError("[API] GetCharacterList: " + e.Message); }
            return result;
        }

        public async Task<(bool success, long charId, string error)> CreateCharacterAsync(
            long accountId, byte slot, string name, byte gender, byte job,
            byte hairStyle, byte hairColor, string token, StartCity city = null, byte faceStyle = 0, CancellationToken ct = default)
        {
            city ??= StartCities.Get(0);
            try
            {
                var body = JsonUtility.ToJson(new CreateDto
                {
                    name = name, slot = slot, job = job, gender = gender, hairStyle = hairStyle, hairColor = hairColor, faceStyle = faceStyle,
                    city = city == null ? null : new CityDto { map = city.map, x = city.x, y = city.y, z = city.z, rotY = city.rotY },
                });
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/characters", body, token, ct));
                return (r.success, r.charId, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] CreateCharacter: " + e.Message); return (false, 0, "DB_ERROR"); }
        }

        public async Task<CharacterData> LoadCharacterAsync(long charId, long accountId, string token, CancellationToken ct = default)
        {
            try
            {
                var dto = JsonUtility.FromJson<LoadDto>(await SendAsync("GET", "/characters/" + charId, null, token, ct));
                if (dto == null || !dto.success || dto.Character == null) return null;
                var c = dto.Character;
                c.Inventory = new List<InventoryItemData>();
                foreach (var i in dto.Inventory)
                    c.Inventory.Add(new InventoryItemData
                    {
                        Id = i.Id, UniqueItemId = i.UniqueItemId, SlotIndex = i.SlotIndex, ItemId = i.ItemId, Quantity = i.Quantity,
                        Durability = i.Durability, IsEquipped = i.IsEquipped, IsLocked = i.IsLocked,
                        OwnerCharacterId = i.OwnerCharacterId >= 0 ? i.OwnerCharacterId : (long?)null, RefineLevel = i.RefineLevel,
                        GemSlot1 = i.Gem1 >= 0 ? i.Gem1 : (int?)null, GemSlot2 = i.Gem2 >= 0 ? i.Gem2 : (int?)null, GemSlot3 = i.Gem3 >= 0 ? i.Gem3 : (int?)null,
                    });
                c.Skills = new List<CharacterSkillData>();
                foreach (var s in dto.Skills) c.Skills.Add(new CharacterSkillData { Id = s.Id, SkillId = s.SkillId, Level = s.Level, Exp = s.Exp });
                return c;
            }
            catch (Exception e) { Debug.LogError("[API] LoadCharacter: " + e.Message); return null; }
        }

        public async Task<bool> SaveCharacterAsync(CharacterData data, string token, CancellationToken ct = default)
        {
            if (data == null) return false;
            try
            {
                var save = new SaveDto { Character = data, Inventory = new List<ItemDto>(), Skills = new List<SkillDto>() };
                if (data.Inventory != null)
                    foreach (var i in data.Inventory)
                    {
                        i.EnsureUniqueId();
                        save.Inventory.Add(new ItemDto
                        {
                            Id = i.Id, UniqueItemId = i.UniqueItemId, SlotIndex = i.SlotIndex, ItemId = i.ItemId, Quantity = i.Quantity,
                            Durability = i.Durability, IsEquipped = i.IsEquipped, IsLocked = i.IsLocked,
                            OwnerCharacterId = i.OwnerCharacterId ?? -1, RefineLevel = i.RefineLevel,
                            Gem1 = i.GemSlot1 ?? -1, Gem2 = i.GemSlot2 ?? -1, Gem3 = i.GemSlot3 ?? -1,
                        });
                    }
                if (data.Skills != null)
                    foreach (var s in data.Skills) save.Skills.Add(new SkillDto { Id = s.Id, SkillId = s.SkillId, Level = s.Level, Exp = s.Exp });
                var r = JsonUtility.FromJson<Ok>(await SendAsync("PUT", "/characters/" + data.Id, JsonUtility.ToJson(save), token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] SaveCharacter: " + e.Message); return false; }
        }

        public async Task<(bool success, string error)> DeleteCharacterAsync(long charId, long accountId, string password, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/characters/" + charId + "/delete", JsonUtility.ToJson(new DeleteDto { password = password }), token, ct));
                return (r.success, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] DeleteCharacter: " + e.Message); return (false, "DB_ERROR"); }
        }

        public async Task LogAuditAsync(long? accountId, long? charId, string action, object detail, string ip, string token, CancellationToken ct = default)
        {
            try
            {
                string json = "{}";
                if (detail != null) { try { json = JsonUtility.ToJson(detail); } catch { json = detail.ToString(); } }
                await SendAsync("POST", "/audit", JsonUtility.ToJson(new AuditDto { action = action, charId = charId ?? 0, detail = json }), token, ct);
            }
            catch (Exception e) { Debug.LogWarning("[API] Audit: " + e.Message); }
        }

        // ---- Amigos ----
        public async Task<List<FriendData>> GetFriendsAsync(long charId, string token, CancellationToken ct = default)
        {
            var result = new List<FriendData>();
            try
            {
                var dto = JsonUtility.FromJson<FriendListDto>(await SendAsync("GET", "/friends/" + charId, null, token, ct));
                if (dto == null || !dto.success) return result;
                foreach (var f in dto.Friends)
                    result.Add(new FriendData { Id = f.Id, Name = f.Name, Level = f.Level, Job = f.Job, LastOnline = f.LastOnline > 0 ? DateTimeOffset.FromUnixTimeSeconds(f.LastOnline).UtcDateTime : (DateTime?)null });
            }
            catch (Exception e) { Debug.LogError("[API] GetFriends: " + e.Message); }
            return result;
        }

        public async Task<(bool success, string error)> AddFriendAsync(long charId, string targetName, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/friends/" + charId + "/add", JsonUtility.ToJson(new AddFriendDto { name = targetName }), token, ct));
                return (r.success, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] AddFriend: " + e.Message); return (false, "DB_ERROR"); }
        }

        public async Task<bool> RemoveFriendAsync(long charId, long friendId, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/friends/" + charId + "/remove", JsonUtility.ToJson(new RemoveFriendDto { friendId = friendId }), token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] RemoveFriend: " + e.Message); return false; }
        }

        // ---- Correio (Mail) ----
        public async Task<List<MailData>> GetMailAsync(long charId, string token, CancellationToken ct = default)
        {
            var result = new List<MailData>();
            try
            {
                var dto = JsonUtility.FromJson<MailListDto>(await SendAsync("GET", "/mail/" + charId, null, token, ct));
                if (dto == null || !dto.success) return result;
                foreach (var m in dto.Mails)
                    result.Add(new MailData
                    {
                        Id = m.Id, SenderName = m.SenderName, Subject = m.Subject, Body = m.Body, Gold = m.Gold,
                        ItemId = m.ItemId, ItemQuantity = m.ItemQuantity, ItemRefine = m.ItemRefine,
                        IsRead = m.IsRead, IsClaimed = m.IsClaimed, CreatedAt = DateTimeOffset.FromUnixTimeSeconds(m.CreatedAt).UtcDateTime,
                    });
            }
            catch (Exception e) { Debug.LogError("[API] GetMail: " + e.Message); }
            return result;
        }

        public async Task<(bool success, string error)> SendMailAsync(long charId, string targetName, string subject, string body, long gold, int itemId, int itemQuantity, int itemRefine, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/mail/" + charId + "/send", JsonUtility.ToJson(new SendMailDto
                {
                    targetName = targetName, subject = subject, body = body, gold = gold, itemId = itemId, itemQuantity = itemQuantity, itemRefine = itemRefine,
                }), token, ct));
                return (r.success, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] SendMail: " + e.Message); return (false, "DB_ERROR"); }
        }

        public async Task<(bool success, long gold, int itemId, int itemQuantity, int itemRefine)> ClaimMailAsync(long charId, long mailId, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<ClaimMailDto>(await SendAsync("POST", "/mail/" + charId + "/" + mailId + "/claim", "{}", token, ct));
                return (r.success, r.Gold, r.ItemId, r.ItemQuantity, r.ItemRefine);
            }
            catch (Exception e) { Debug.LogError("[API] ClaimMail: " + e.Message); return (false, 0, -1, 0, 0); }
        }

        public async Task MarkMailReadAsync(long charId, long mailId, string token, CancellationToken ct = default)
        {
            try { await SendAsync("POST", "/mail/" + charId + "/" + mailId + "/read", "{}", token, ct); }
            catch (Exception e) { Debug.LogWarning("[API] MarkMailRead: " + e.Message); }
        }

        public async Task<bool> DeleteMailAsync(long charId, long mailId, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/mail/" + charId + "/" + mailId + "/delete", "{}", token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] DeleteMail: " + e.Message); return false; }
        }

        // ---- Guilda ----
        public async Task<GuildData> GetGuildAsync(long charId, string token, CancellationToken ct = default)
        {
            try
            {
                var dto = JsonUtility.FromJson<GuildDto>(await SendAsync("GET", "/guild/" + charId, null, token, ct));
                if (dto == null || !dto.success) return null;
                var g = new GuildData { InGuild = dto.InGuild, GuildId = dto.GuildId, Name = dto.Name, Notice = dto.Notice, Level = dto.Level, LeaderCharacterId = dto.LeaderCharacterId, MyRank = dto.MyRank };
                foreach (var m in dto.Members) g.Members.Add(new GuildMemberData { CharacterId = m.CharacterId, Name = m.Name, Rank = m.Rank });
                return g;
            }
            catch (Exception e) { Debug.LogError("[API] GetGuild: " + e.Message); return null; }
        }

        public async Task<(bool success, string error, int guildId)> CreateGuildAsync(long charId, string name, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<GuildCreateResultDto>(await SendAsync("POST", "/guild/" + charId + "/create", JsonUtility.ToJson(new CreateGuildDto { name = name }), token, ct));
                return (r.success, r.error, r.GuildId);
            }
            catch (Exception e) { Debug.LogError("[API] CreateGuild: " + e.Message); return (false, "DB_ERROR", 0); }
        }

        public async Task<(bool success, string error)> GuildInviteAsync(long charId, string targetName, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/guild/" + charId + "/invite", JsonUtility.ToJson(new GuildTargetDto { targetName = targetName }), token, ct));
                return (r.success, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] GuildInvite: " + e.Message); return (false, "DB_ERROR"); }
        }

        public async Task<bool> GuildLeaveAsync(long charId, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/guild/" + charId + "/leave", "{}", token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] GuildLeave: " + e.Message); return false; }
        }

        public async Task<(bool success, string error)> GuildKickAsync(long charId, string targetName, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/guild/" + charId + "/kick", JsonUtility.ToJson(new GuildTargetDto { targetName = targetName }), token, ct));
                return (r.success, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] GuildKick: " + e.Message); return (false, "DB_ERROR"); }
        }

        public async Task<bool> GuildSetNoticeAsync(long charId, string notice, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/guild/" + charId + "/notice", JsonUtility.ToJson(new GuildNoticeDto { notice = notice }), token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] GuildSetNotice: " + e.Message); return false; }
        }

        // ---- Quests ----
        public async Task<(List<(int questId, int progress)> active, List<int> completed)> GetQuestsAsync(long charId, string token, CancellationToken ct = default)
        {
            try
            {
                var dto = JsonUtility.FromJson<QuestListDto>(await SendAsync("GET", "/quests/" + charId, null, token, ct));
                var active = new List<(int, int)>();
                if (dto != null && dto.success) foreach (var a in dto.active) active.Add((a.questId, a.progress));
                return (active, dto != null && dto.success ? dto.completed : new List<int>());
            }
            catch (Exception e) { Debug.LogError("[API] GetQuests: " + e.Message); return (new List<(int, int)>(), new List<int>()); }
        }

        public async Task<bool> AcceptQuestAsync(long charId, int questId, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/quests/" + charId + "/accept", JsonUtility.ToJson(new QuestIdDto { questId = questId }), token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] AcceptQuest: " + e.Message); return false; }
        }

        public async Task<bool> AbandonQuestAsync(long charId, int questId, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/quests/" + charId + "/abandon", JsonUtility.ToJson(new QuestIdDto { questId = questId }), token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] AbandonQuest: " + e.Message); return false; }
        }

        public async Task<bool> SaveQuestProgressAsync(long charId, int questId, int progress, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/quests/" + charId + "/progress", JsonUtility.ToJson(new QuestProgressDto { questId = questId, progress = progress }), token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] SaveQuestProgress: " + e.Message); return false; }
        }

        public async Task<bool> CompleteQuestAsync(long charId, int questId, string token, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/quests/" + charId + "/complete", JsonUtility.ToJson(new QuestIdDto { questId = questId }), token, ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] CompleteQuest: " + e.Message); return false; }
        }
    }
}