using UnityEngine;
using Mirror;
using TOP.Data;
using TOP.Core;
using TOP.Inventory;
using System.Collections.Generic;

namespace TOP.Player
{
    public enum PlayerMessageType
    {
        Info, Warning, Error, QuestUpdate,
        LevelUp, ItemAcquired, Death, Revive,
        Success
    }

    public class PlayerController : NetworkBehaviour
    {
        [Header("Dados do Personagem")]
        [SyncVar] public long CharacterId;
        [SyncVar] public long AccountId;
        [SyncVar] public string CharacterName;
        [SyncVar] public byte Job;
        [SyncVar] public byte Gender;
        [SyncVar] public int Level;
        [SyncVar] public ulong Exp;

        [Header("Stats")]
        [SyncVar] public int CurrentHp;
        [SyncVar] public int CurrentMp;
        [SyncVar] public int CurrentSp;
        [SyncVar] public int MaxHp;
        [SyncVar] public int MaxMp;
        [SyncVar] public int MaxSp;

        [Header("Atributos Base")]
        [SyncVar] public int BaseStr;
        [SyncVar] public int BaseAgi;
        [SyncVar] public int BaseCon;
        [SyncVar] public int BaseSpr;
        [SyncVar] public int BaseSta;

        [Header("Economia")]
        [SyncVar] public ulong Gold;

        [Header("Pontos")]
        [SyncVar] public int StatPoints;
        [SyncVar] public int SkillPoints;

        [Header("Aparência")]
        [SyncVar] public byte HairStyle;
        [SyncVar] public byte HairColor;
        [SyncVar] public byte FaceStyle;

        [Header("Sistema")]
        [SyncVar] public string MapName = "garner";
        [SyncVar] public int PkPoints;
        [SyncVar] public int Reputation;

        private CharacterData _characterData;
        private PlayerInventory _playerInventory;
        private PlayerEquipment _playerEquipment;
        private PlayerStats _playerStats;

        public bool IsInitialized => _characterData != null;

        public override void OnStartServer() { base.OnStartServer(); DontDestroyOnLoad(gameObject); }
        public override void OnStartClient() { base.OnStartClient(); DontDestroyOnLoad(gameObject); }

        void Awake()
        {
            if (GetComponent<PlayerVisualBinder>() == null) gameObject.AddComponent<PlayerVisualBinder>();
            _playerInventory = GetComponent<PlayerInventory>();
            _playerEquipment = GetComponent<PlayerEquipment>();
            _playerStats = GetComponent<PlayerStats>();
        }

        [Server]
        public void InitializeFromCharacterData(CharacterData data)
        {
            if (data == null)
            {
                Debug.LogError("[PlayerController] CharacterData é null!");
                return;
            }

            _characterData = data;

            CharacterId = data.Id;
            AccountId = data.AccountId;
            CharacterName = data.Name;
            Job = data.Job;
            Gender = data.Gender;
            Level = data.Level;
            Exp = data.Exp;

            CurrentHp = data.CurrentHp;
            CurrentMp = data.CurrentMp;
            CurrentSp = data.CurrentSp;
            MaxHp = data.MaxHp;
            MaxMp = data.MaxMp;
            MaxSp = data.MaxSp;

            BaseStr = data.BaseStr;
            BaseAgi = data.BaseAgi;
            BaseCon = data.BaseCon;
            BaseSpr = data.BaseSpr;
            BaseSta = data.BaseSta;

            Gold = data.Gold;
            StatPoints = data.StatPoints;
            SkillPoints = data.SkillPoints;

            HairStyle = data.HairStyle;
            HairColor = data.HairColor;
            FaceStyle = data.FaceStyle;

            MapName = data.MapName;
            PkPoints = data.PkPoints;
            Reputation = data.Reputation;

            if (_playerInventory != null)
                _playerInventory.InitializeFromData(data.Inventory);

            if (_playerEquipment != null)
                _playerEquipment.LoadEquippedFromInventory(data.Inventory);

            if (_playerStats != null)
                _playerStats.InitializeFromData(data);

            Debug.Log($"[PlayerController] {CharacterName} (Lv.{Level}) inicializado do banco.");
        }

        [Server]
        public CharacterData GetCharacterData()
        {
            if (_characterData == null) return null;

            _playerStats?.SyncController();
            _characterData.CurrentHp = CurrentHp;
            _characterData.CurrentMp = CurrentMp;
            _characterData.CurrentSp = CurrentSp;
            _characterData.Level = Level;
            _characterData.Exp = Exp;
            _characterData.Gold = Gold;
            _characterData.StatPoints = StatPoints;
            _characterData.SkillPoints = SkillPoints;
            _characterData.PkPoints = PkPoints;
            _characterData.Reputation = Reputation;
            _characterData.MapName = MapName;

            _characterData.PosX = transform.position.x;
            _characterData.PosY = transform.position.y;
            _characterData.PosZ = transform.position.z;
            _characterData.RotationY = transform.rotation.eulerAngles.y;

            if (_playerInventory != null)
                _characterData.Inventory = _playerInventory.GetInventoryData();

            if (_playerStats != null)
                _characterData.Skills = _playerStats.GetSkillsData();

            return _characterData;
        }

        [Server]
        public void SetHp(int value)
        {
            CurrentHp = Mathf.Clamp(value, 0, MaxHp);
            RpcSyncStat("hp", CurrentHp);
        }

        [Server]
        public void SetMp(int value)
        {
            CurrentMp = Mathf.Clamp(value, 0, MaxMp);
            RpcSyncStat("mp", CurrentMp);
        }

        [Server]
        public void SetSp(int value)
        {
            CurrentSp = Mathf.Clamp(value, 0, MaxSp);
            RpcSyncStat("sp", CurrentSp);
        }

        [ClientRpc] void RpcLevelUp() { TOP.Systems.EffectManager.Instance?.PlayLevelUpEffect(transform.position); }

        public ulong ExperienceToNextLevel => TOP.Data.PkoTables.ExpToNextLevel(Level);

        [Server]
        public void AddExp(ulong amount)
        {
            Exp += amount;
            while (Level < 100 && Exp >= ExperienceToNextLevel)
            {
                Exp -= ExperienceToNextLevel;
                Level++; StatPoints += 5; SkillPoints++;
                _playerStats?.SetCurrentHpMpSp(_playerStats.MaxHp, _playerStats.MaxMp, _playerStats.MaxSp);
                RpcShowMessage($"Nível {Level}!", PlayerMessageType.LevelUp);
                RpcLevelUp();
            }
        }

        // Admin: sets level/points directly (points follow the level-up rule: 5 stat + 1 skill per level gained).
        [Server]
        public void AdminSetLevel(int level, bool refill = true)
        {
            level = Mathf.Clamp(level, 1, 100);
            int gained = level - Level;
            Level = level; Exp = 0;
            if (gained > 0) { StatPoints += 5 * gained; SkillPoints += gained; }
            if (refill) _playerStats?.SetCurrentHpMpSp(_playerStats.MaxHp, _playerStats.MaxMp, _playerStats.MaxSp);
            RpcShowMessage("Nível " + Level, PlayerMessageType.LevelUp);
        }

        [Server]
        public void AdminSetGold(ulong gold) { Gold = gold; RpcSyncStat("gold", (int)Gold); }

        [Server]
        public void AdminAddPoints(int stat, int skill) { StatPoints = Mathf.Max(0, StatPoints + stat); SkillPoints = Mathf.Max(0, SkillPoints + skill); }

        [Server]
        public void AddGold(ulong amount)
        {
            Gold += amount;
            RpcSyncStat("gold", (int)Gold);
        }

        [Server]
        public bool SpendGold(ulong amount)
        {
            if (Gold < amount) return false;
            Gold -= amount;
            RpcSyncStat("gold", (int)Gold);
            return true;
        }

        // =================================================================================
        // SALAO DE BELEZA (hairs.txt)
        // =================================================================================
        [Command]
        public void CmdApplyHairstyle(int hairId)
        {
            PkoHair chosen = null;
            foreach (var h in PkoTables.Hairs) if (h.Id == hairId) { chosen = h; break; }
            if (chosen == null) { RpcShowMessage("Penteado inválido.", PlayerMessageType.Error); return; }
            int raceIdx = Mathf.Clamp(Job, 0, chosen.UsableRace.Length - 1);
            if (!chosen.UsableRace[raceIdx]) { RpcShowMessage("Penteado não disponível para sua raça.", PlayerMessageType.Error); return; }
            if (!SpendGold(chosen.Cost)) { RpcShowMessage("Ouro insuficiente.", PlayerMessageType.Error); return; }
            HairStyle = (byte)chosen.ModelStyle;
            RpcShowMessage($"Novo penteado: {chosen.Name} ({chosen.Color})", PlayerMessageType.Success);
        }

        [ClientRpc]
        public void RpcOpenHairSalon()
        {
            if (!isLocalPlayer) return;
            TOP.UI.HairSalonUI.OpenLocal(this);
        }

        [ClientRpc]
        public void RpcOpenForge()
        {
            if (!isLocalPlayer) return;
            TOP.UI.ForgeUI.OpenLocal(GetComponent<PlayerForge>());
        }

        // =================================================================================
        // MORTE E RESPAWN
        // =================================================================================
        [Server]
        public void Die()
        {
            if (CurrentHp > 0) CurrentHp = 0;

            RpcShowMessage($"{CharacterName} morreu!", PlayerMessageType.Death);

            var respawn = GetComponent<PlayerRespawn>();
            if (respawn != null)
            {
                respawn.ScheduleRespawn();
            }

            GetComponent<PlayerAnimation>()?.RpcDeath();
            OnDeath?.Invoke();

            Debug.Log($"[PlayerController] {CharacterName} morreu.");
        }

        public event System.Action OnDeath;
        public event System.Action OnRevive;

        [Server]
        public void RespawnPlayer()
        {
            _playerStats?.SetCurrentHpMpSp(MaxHp, MaxMp, MaxSp);
            GetComponent<PlayerAnimation>()?.RpcRespawn();
            CurrentHp = MaxHp;
            CurrentMp = MaxMp;
            CurrentSp = MaxSp;

            transform.position = GetSpawnPosition();

            RpcShowMessage($"{CharacterName} renasceu!", PlayerMessageType.Revive);

            OnRevive?.Invoke();

            Debug.Log($"[PlayerController] {CharacterName} renasceu.");
        }

        // =================================================================================
        // COMBAT COMMANDS (Mirror)
        // =================================================================================
        /// <summary>
        /// Define o alvo do combate.
        /// ✅ Recebe NetworkIdentity do client, extrai netId e passa para PlayerCombat.
        /// </summary>
        [Command]
        public void CmdSpendStatPoint(int stat)
        {
            if (StatPoints <= 0) return;
            switch (stat)
            {
                case 0: BaseStr++; break;
                case 1: BaseAgi++; break;
                case 2: BaseCon++; break;
                case 3: BaseSpr++; break;
                case 4: BaseSta++; break;
                default: return;
            }
            StatPoints--;
            if (_playerStats != null)
            {
                _playerStats.BaseStrength = BaseStr; _playerStats.BaseAgility = BaseAgi; _playerStats.BaseConstitution = BaseCon;
                _playerStats.BaseSpirit = BaseSpr; _playerStats.BaseStamina = BaseSta;
            }
        }

        [Command]
        public void CmdSetTarget(NetworkIdentity targetIdentity)
        {
            if (targetIdentity == null) return;
            var combat = GetComponent<PlayerCombat>();
            combat?.SetTarget(targetIdentity.netId);
        }

   [Command]
public void CmdAttackTarget(NetworkIdentity targetIdentity, int skillId)
{
    if (targetIdentity == null) return;
    
    var combat = GetComponent<PlayerCombat>();
    if (combat != null)
    {
        combat.AttackTarget(targetIdentity.netId, skillId);
    }
}

        /// <summary>
        /// Move o jogador para um destino.
        /// </summary>
        [Command]
        public void CmdMoveTo(Vector3 destination)
        {
            var movement = GetComponent<PlayerMovement>();
            if (movement != null)
                movement.SetDestination(destination);
        }

        public PlayerCombat Combat => GetComponent<PlayerCombat>();

        // =================================================================================
        // RPCs
        // =================================================================================
        [ClientRpc]
        public void RpcShowMessage(string text, PlayerMessageType type)
        {
            if (isLocalPlayer)
            {
                TOP.UI.UIManager.Instance?.ShowMessage(text, type);
            }
        }

        [ClientRpc]
        private void RpcSyncStat(string statName, int value)
        {
            Debug.Log($"[PlayerController] Stat sync: {statName} = {value}");
        }

        // =================================================================================
        // HELPERS
        // =================================================================================
        public Vector3 GetSpawnPosition()
        {
            if (TOP.Core.SpawnManager.Instance != null) return TOP.Core.SpawnManager.Instance.GetSafeSpawnPoint();
            return new Vector3(_characterData?.PosX ?? 0f,
                               _characterData?.PosY ?? 1.5f,
                               _characterData?.PosZ ?? 0f);
        }

        public void Teleport(string mapName, Vector3 position)
        {
            MapName = mapName;
            transform.position = position;
        }
    }
}