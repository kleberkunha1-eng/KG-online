using System;
using System.Collections.Generic;
using TOP.Data;
using UnityEngine;
using Mirror;
using TOP.Core;

namespace TOP.Player
{
    public class PlayerStats : NetworkBehaviour, ICharacterStats
    {
        // ============================================================
        // EVENTOS
        // ============================================================
        public event System.Action OnHealthUpdated;
        public event System.Action OnManaUpdated;
        public event System.Action OnStaminaUpdated;
        public event System.Action OnDeath;
        public event System.Action OnRevive;
        public event System.Action OnStatsChanged;

        // ============================================================
        // SKILLS
        // ============================================================
        [Serializable]
        public class SkillInfo
        {
            public byte level;
            public ulong exp;
        }

        private readonly Dictionary<int, SkillInfo> learnedSkills = new Dictionary<int, SkillInfo>();

        // ============================================================
        // ICharacterStats IMPLEMENTATION
        // ============================================================
        public int Health => CurrentHp;
        public int Mana => CurrentMp;
        public int Stamina => CurrentSp;
        public int MaxHealth => MaxHp;
        public int MaxMana => MaxMp;
        public int MaxStamina => MaxSp;
        public int Accuracy => 0;
        public int Luck => 0;
        public int Attack => PhysicalAttack;
        public int Defense => PhysicalDefense;
        public int MagicAttack => CalculateMagicAttack();
        public int MagicDefense => CalculateMagicDefense();
        public int Level => GetComponent<PlayerController>()?.Level ?? 1;
        public bool IsDead => CurrentHp <= 0;

        // ============================================================
        // ESTATÍSTICAS DERIVADAS
        // ============================================================
        public float MoveSpeed => 5f + (Agility * 0.01f);
        public float AttackSpeed => 1f + (Agility * 0.005f);
        public float CriticalRate => 0.05f + (Agility * 0.002f);
        public float CriticalDamage => 1.5f;

        public float HpRegen => 1f + (Constitution * 0.1f);
        public float MpRegen => 1f + (Spirit * 0.1f);
        public float SpRegen => 2f + (Constitution * 0.05f);

        // ============================================================
        // BASE STATS
        // ============================================================
        [Header("Base Stats")]
        [SyncVar] public int BaseStrength;
        [SyncVar] public int BaseAgility;
        [SyncVar] public int BaseConstitution;
        [SyncVar] public int BaseSpirit;
        [SyncVar] public int BaseStamina;

        // ============================================================
        // CURRENT VALUES
        // ============================================================
        [Header("Current Values")]
        [SyncVar] public int CurrentHp;
        [SyncVar] public int CurrentMp;
        [SyncVar] public int CurrentSp;

        // ============================================================
        // BONUS STATS
        // ============================================================
        [SyncVar] private int _bonusStr;
        [SyncVar] private int _bonusAgi;
        [SyncVar] private int _bonusCon;
        [SyncVar] private int _bonusSpr;
        [SyncVar] private int _bonusSta;
        [SyncVar] private int _bonusHp;
        [SyncVar] private int _bonusMp;
        [SyncVar] private int _bonusSp;
        [SyncVar] private int _bonusAtk;
        [SyncVar] private int _bonusDef;

        // ============================================================
        // PROPRIEDADES CALCULADAS
        // ============================================================
        public int Strength => BaseStrength + _bonusStr;
        public int Agility => BaseAgility + _bonusAgi;
        public int Constitution => BaseConstitution + _bonusCon;
        public int Spirit => BaseSpirit + _bonusSpr;
        // ✅ CORREÇÃO: Stamina é uma propriedade calculada, não conflita com ICharacterStats.Stamina
        public int StaminaStat => BaseStamina + _bonusSta;

        public int MaxHp => CalculateMaxHp() + _bonusHp;
        public int MaxMp => CalculateMaxMp() + _bonusMp;
        public int MaxSp => CalculateMaxSp() + _bonusSp;

        public int PhysicalAttack => CalculatePhysicalAttack() + _bonusAtk;
        public int PhysicalDefense => CalculatePhysicalDefense() + _bonusDef;

        // ============================================================
        // REGENERAÇÃO
        // ============================================================
        private float _lastHpRegen;
        private float _lastMpRegen;
        private float _lastSpRegen;

        // ============================================================
        // MODIFICADORES
        // ============================================================
        private readonly List<StatModifier> _modifiers = new List<StatModifier>();

        // ============================================================
        // INICIALIZAÇÃO DO BANCO
        // ============================================================
        public void InitializeFromData(CharacterData data)
        {
            if (data == null) return;

            BaseStrength = data.BaseStr;
            BaseAgility = data.BaseAgi;
            BaseConstitution = data.BaseCon;
            BaseSpirit = data.BaseSpr;
            BaseStamina = data.BaseSta;

            ResetEquipmentBonuses();
            CurrentHp = data.CurrentHp;
            CurrentMp = data.CurrentMp;
            CurrentSp = data.CurrentSp;

            if (data.Skills != null)
            {
                foreach (var skill in data.Skills)
                {
                    LearnSkill(skill.SkillId, skill.Level);
                }
            }

            Debug.Log($"[PlayerStats] Inicializado — STR:{Strength} AGI:{Agility} CON:{Constitution}");
        }

        public List<CharacterSkillData> GetSkillsData()
        {
            var skills = new List<CharacterSkillData>();

            foreach (var kvp in learnedSkills)
            {
                skills.Add(new CharacterSkillData
                {
                    SkillId = kvp.Key,
                    Level = kvp.Value.level,
                    Exp = kvp.Value.exp
                });
            }

            return skills;
        }

        private void RecalculateStats() { SyncController(); }

        [Server]
        public void SyncController()
        {
            var controller = GetComponent<PlayerController>();
            if (controller == null) return;
            controller.MaxHp = MaxHp; controller.MaxMp = MaxMp; controller.MaxSp = MaxSp;
            controller.CurrentHp = CurrentHp; controller.CurrentMp = CurrentMp; controller.CurrentSp = CurrentSp;
        }

        private void LearnSkill(int skillId, byte level)
        {
            if (!learnedSkills.ContainsKey(skillId))
            {
                learnedSkills[skillId] = new SkillInfo { level = level, exp = 0 };
            }
            else
            {
                learnedSkills[skillId].level = level;
            }
        }

        // ============================================================
        // MODIFICADORES
        // ============================================================
        public void AddModifier(StatModifier modifier)
        {
            _modifiers.Add(modifier);
            OnStatsChanged?.Invoke();
        }

        public void RemoveModifier(StatModifier modifier)
        {
            if (_modifiers.Contains(modifier))
            {
                _modifiers.Remove(modifier);
                OnStatsChanged?.Invoke();
            }
        }

        public void ClearModifiers()
        {
            _modifiers.Clear();
            OnStatsChanged?.Invoke();
        }

        // ============================================================
        // DANO / CURA
        // ============================================================
        [Server]
        public void TakeDamage(int damage, uint attackerId, DamageType damageType = DamageType.Physical)
        {
            TakeDamage(damage);
        }

        [Server]
        public void TakeTrueDamage(int damage, uint attackerId)
        {
            TakeDamage(damage);
        }

        [Server]
        public void Heal(int amount)
        {
            if (IsDead) return;
            CurrentHp = Mathf.Min(CurrentHp + amount, MaxHp);
            OnHealthUpdated?.Invoke();
        }

        [Server]
        public void RestoreMana(int amount)
        {
            CurrentMp = Mathf.Min(CurrentMp + amount, MaxMp);
            OnManaUpdated?.Invoke();
        }

        [Server]
        public void RestoreStamina(int amount)
        {
            CurrentSp = Mathf.Min(CurrentSp + amount, MaxSp);
            OnStaminaUpdated?.Invoke();
        }

        [Server]
        public void RestoreMp(int amount)
        {
            RestoreMana(amount);
        }

        [Server]
        public void RestoreSp(int amount)
        {
            RestoreStamina(amount);
        }

        // ============================================================
        // XP / GOLD
        // ============================================================
        [Server]
        public void AddExperience(int amount)
        {
            var controller = GetComponent<PlayerController>();
            if (controller != null)
                controller.AddExp((ulong)Mathf.Max(0, amount));
        }

        [Server]
        public void AddGold(int amount)
        {
            var controller = GetComponent<PlayerController>();
            if (controller != null)
                controller.AddGold((ulong)Mathf.Max(0, amount));
        }

        // ============================================================
        // REGENERAÇÃO AUTOMÁTICA
        // ============================================================
        void Update()
        {
            if (!isServer) return;
            SyncController();
            if (IsDead) return;

            if (Time.time > _lastHpRegen + 1f)
            {
                _lastHpRegen = Time.time;
                Heal(Mathf.RoundToInt(HpRegen));
            }

            if (Time.time > _lastMpRegen + 1f)
            {
                _lastMpRegen = Time.time;
                RestoreMana(Mathf.RoundToInt(MpRegen));
            }

            if (Time.time > _lastSpRegen + 1f)
            {
                _lastSpRegen = Time.time;
                RestoreStamina(Mathf.RoundToInt(SpRegen));
            }
        }

        // ============================================================
        // INICIALIZAÇÃO
        // ============================================================
        [Server]
        public void Initialize(int str, int agi, int con, int spr, int maxHp, int maxMp, int maxSp)
        {
            BaseStrength = str;
            BaseAgility = agi;
            BaseConstitution = con;
            BaseSpirit = spr;

            CurrentHp = maxHp;
            CurrentMp = maxMp;
            CurrentSp = maxSp;
        }

        [Server]
        public void SetCurrentHpMpSp(int hp, int mp, int sp)
        {
            CurrentHp = Mathf.Clamp(hp, 0, MaxHp);
            CurrentMp = Mathf.Clamp(mp, 0, MaxMp);
            CurrentSp = Mathf.Clamp(sp, 0, MaxSp);
            SyncController();
            OnHealthUpdated?.Invoke();
            OnManaUpdated?.Invoke();
            OnStaminaUpdated?.Invoke();
        }

        // ============================================================
        // DANO SERVIDOR
        // ============================================================
        [Server]
        public void TakeDamage(int damage)
        {
            int finalDamage = Mathf.Max(1, damage - PhysicalDefense);
            if (IsDead || damage <= 0) return;
            CurrentHp = Mathf.Max(0, CurrentHp - finalDamage);
            SyncController();

            OnHealthUpdated?.Invoke();

            if (CurrentHp <= 0)
            {
                CurrentHp = 0;
                PlayerController controller = GetComponent<PlayerController>();
                if (controller != null)
                {
                    OnDeath?.Invoke();
                    controller.Die();
                }
            }
        }

        [Server]
        public void ConsumeMp(int amount)
        {
            CurrentMp = Mathf.Max(0, CurrentMp - amount);
        }

        [Server]
        public void ConsumeSp(int amount)
        {
            CurrentSp = Mathf.Max(0, CurrentSp - amount);
        }

        // ============================================================
        // BONUS
        // ============================================================
        [Server]
        public void ResetEquipmentBonuses()
        {
            _bonusStr = _bonusAgi = _bonusCon = _bonusSpr = _bonusSta = 0;
            _bonusHp = _bonusMp = _bonusSp = _bonusAtk = _bonusDef = 0;
        }

        [Server]
        public void FinishEquipmentStats()
        {
            SetCurrentHpMpSp(CurrentHp, CurrentMp, CurrentSp);
            OnStatsChanged?.Invoke();
        }

        public void AddBonusStrength(int value) => _bonusStr += value;
        public void AddBonusAgility(int value) => _bonusAgi += value;
        public void AddBonusConstitution(int value) => _bonusCon += value;
        public void AddBonusSpirit(int value) => _bonusSpr += value;
        public void AddBonusStamina(int value) => _bonusSta += value;
        public void AddBonusHp(int value) => _bonusHp += value;
        public void AddBonusMp(int value) => _bonusMp += value;
        public void AddBonusSp(int value) => _bonusSp += value;
        public void AddBonusAttack(int value) => _bonusAtk += value;
        public void AddBonusDefense(int value) => _bonusDef += value;

        // ============================================================
        // CÁLCULOS
        // ============================================================
        int CalculateMaxHp() => 100 + (Constitution * 10) + (BaseStrength * 2);
        int CalculateMaxMp() => 50 + (Spirit * 8) + (BaseConstitution * 2);
        int CalculateMaxSp() => 30 + (Constitution * 5) + (BaseAgility * 2);

        int CalculatePhysicalAttack() => 10 + (Strength * 2) + (Agility * 1);
        int CalculateMagicAttack() => 5 + (Spirit * 3);

        int CalculatePhysicalDefense() => 5 + (Constitution * 1) + (Strength * 1);
        int CalculateMagicDefense() => 3 + (Spirit * 2) + (Constitution * 1);

        // ============================================================
        // HELPERS
        // ============================================================
        public bool IsMale => true;
    }
}