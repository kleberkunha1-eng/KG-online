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

        // ---- HTTP ----
        static async Task<string> SendAsync(string method, string path, string body, CancellationToken ct)
        {
            using (var req = new UnityWebRequest(ApiConfig.GameUrl + path, method))
            {
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Authorization", "Bearer " + TOP.Network.LoginNetworkClient.AuthToken);
                req.timeout = 20;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    if (ct.IsCancellationRequested) { req.Abort(); throw new OperationCanceledException(); }
                    await Task.Yield();
                }
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
        public async Task<bool> IsAdminAsync(long accountId, CancellationToken ct = default)
        {
            try { return JsonUtility.FromJson<Ok>(await SendAsync("GET", "/me", null, ct)).admin; }
            catch (Exception e) { Debug.LogError("[API] IsAdmin: " + e.Message); return false; }
        }

        public async Task<List<CharacterPreviewData>> GetCharacterListAsync(long accountId, CancellationToken ct = default)
        {
            var result = new List<CharacterPreviewData>();
            try
            {
                var dto = JsonUtility.FromJson<ListDto>(await SendAsync("GET", "/characters", null, ct));
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
            byte hairStyle, byte hairColor, StartCity city = null, byte faceStyle = 0, CancellationToken ct = default)
        {
            city ??= StartCities.Get(0);
            try
            {
                var body = JsonUtility.ToJson(new CreateDto
                {
                    name = name, slot = slot, job = job, gender = gender, hairStyle = hairStyle, hairColor = hairColor, faceStyle = faceStyle,
                    city = city == null ? null : new CityDto { map = city.map, x = city.x, y = city.y, z = city.z, rotY = city.rotY },
                });
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/characters", body, ct));
                return (r.success, r.charId, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] CreateCharacter: " + e.Message); return (false, 0, "DB_ERROR"); }
        }

        public async Task<CharacterData> LoadCharacterAsync(long charId, long accountId, CancellationToken ct = default)
        {
            try
            {
                var dto = JsonUtility.FromJson<LoadDto>(await SendAsync("GET", "/characters/" + charId, null, ct));
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

        public async Task<bool> SaveCharacterAsync(CharacterData data, CancellationToken ct = default)
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
                var r = JsonUtility.FromJson<Ok>(await SendAsync("PUT", "/characters/" + data.Id, JsonUtility.ToJson(save), ct));
                return r.success;
            }
            catch (Exception e) { Debug.LogError("[API] SaveCharacter: " + e.Message); return false; }
        }

        public async Task<(bool success, string error)> DeleteCharacterAsync(long charId, long accountId, string password, CancellationToken ct = default)
        {
            try
            {
                var r = JsonUtility.FromJson<Ok>(await SendAsync("POST", "/characters/" + charId + "/delete", JsonUtility.ToJson(new DeleteDto { password = password }), ct));
                return (r.success, r.error);
            }
            catch (Exception e) { Debug.LogError("[API] DeleteCharacter: " + e.Message); return (false, "DB_ERROR"); }
        }

        public async Task LogAuditAsync(long? accountId, long? charId, string action, object detail, string ip, CancellationToken ct = default)
        {
            try
            {
                string json = "{}";
                if (detail != null) { try { json = JsonUtility.ToJson(detail); } catch { json = detail.ToString(); } }
                await SendAsync("POST", "/audit", JsonUtility.ToJson(new AuditDto { action = action, charId = charId ?? 0, detail = json }), ct);
            }
            catch (Exception e) { Debug.LogWarning("[API] Audit: " + e.Message); }
        }
    }
}