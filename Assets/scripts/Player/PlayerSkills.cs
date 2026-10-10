using UnityEngine;
using Mirror;
using System.Collections.Generic;
using TOP.Core;
using System.Linq;
using System;

namespace TOP.Player
{
    public class PlayerSkills : NetworkBehaviour
    {
        public event Action<SkillData, float> OnSkillCastStarted;
        public event Action OnSkillCastFinished;
        public event Action<SkillData> OnSkillExecuted;

        [SyncVar(hook = nameof(OnSkillsDataChanged))] 
        private string _skillsData = "";

        private readonly Dictionary<int, int> _skillLevels = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _skillCooldowns = new Dictionary<int, float>();
        private PlayerStats _stats;
        private PlayerAnimation _animation;
        private PlayerCombat _combat;
        private SkillData[] _allSkills;

        void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _animation = GetComponent<PlayerAnimation>();
            _combat = GetComponent<PlayerCombat>();
        }

        void Start()
        {
            LoadAllSkills();
        }

        void Update()
        {
            // Atualiza cooldowns
            List<int> keys = new List<int>(_skillCooldowns.Keys);
            foreach (int key in keys)
            {
                if (_skillCooldowns[key] > 0)
                    _skillCooldowns[key] -= Time.deltaTime;
            }
        }

        // ✅ CORREÇÃO: Método público chamado pelo PlayerHotbar (client-side)
        // Envia Command para o servidor executar
        public void TryUseSkill(int skillId)
        {
            CmdUseSkill(skillId);
        }

        /// <summary>
        /// ✅ ÚNICO Command para usar skill. Recebe apenas skillId.
        /// O servidor resolve posição/target internamente.
        /// </summary>
        [Command]
        public void CmdUseSkill(int skillId)
        {
            // Obtém posição/target atuais do player
            Vector3 targetPos = transform.position + transform.forward * 5f;
            uint targetNetId = 0;

            // Se tiver target no combat, usa ele
            var combat = GetComponent<PlayerCombat>();
            if (combat != null)
            {
                // Acessa o target atual via reflection ou propriedade pública
                // Por simplicidade, usamos posição à frente
            }

            if(combat != null && combat.CurrentTargetNetId != 0 && NetworkServer.spawned.TryGetValue(combat.CurrentTargetNetId, out var selected)) { targetNetId=selected.netId; targetPos=selected.transform.position; }
            UseSkill(skillId, targetPos, targetNetId);
        }

        // ✅ Overload server-only para uso interno (com target específico)
        [Server]
        public void UseSkill(int skillId, Vector3 targetPosition, uint targetNetId = 0)
        {
            if (_stats == null || _stats.IsDead) return;
            var inventory = GetComponent<PlayerInventory>();
            if (inventory != null && inventory.HasQuestTransaction) return;
            var life = GetComponent<PlayerLifeServices>();
            if (life != null && life.StallActive) return;
            var arenaCaster = GetComponent<PlayerController>();
            if (arenaCaster != null && arenaCaster.ArenaInstanceId > 0 && arenaCaster.ArenaResult != 0) return;
            if (targetNetId != 0 && NetworkServer.spawned.TryGetValue(targetNetId, out var arenaIdentity))
            {
                var arenaTarget = arenaIdentity.GetComponent<PlayerController>();
                if (arenaTarget != null && arenaCaster != null && arenaTarget.ArenaInstanceId != arenaCaster.ArenaInstanceId) return;
            }
            if (!_skillLevels.ContainsKey(skillId)) 
            {
                Debug.LogWarning($"[PlayerSkills] Skill {skillId} não aprendida!");
                return;
            }

            SkillData skillData = GetSkillData(skillId);
            if (skillData == null) 
            {
                Debug.LogWarning($"[PlayerSkills] SkillData {skillId} não encontrada!");
                return;
            }

            if (skillData.skillType == SkillType.Passive) return;

            // Verifica cooldown
            if (_skillCooldowns.ContainsKey(skillId) && _skillCooldowns[skillId] > 0)
            {
                Debug.Log($"[PlayerSkills] Skill {skillData.skillName} em cooldown!");
                return;
            }

            int originalSpCost = TOP.Data.OriginalSkillParameters.Cost(skillId, GetSkillLevel(skillId), skillData.spCost);
            float originalCooldown = TOP.Data.OriginalSkillParameters.Cooldown(skillId, GetSkillLevel(skillId), skillData.cooldown);

            // Verifica custo
            if (_stats.CurrentMp < skillData.mpCost || _stats.CurrentSp < originalSpCost)
            {
                Debug.Log($"[PlayerSkills] MP insuficiente: {_stats.CurrentMp}/{skillData.mpCost}");
                return;
            }

            if (_stats.CurrentSp < originalSpCost) return;
            if (skillData.targetType == SkillTargetType.AreaEnemy
                && (!float.IsFinite(targetPosition.x) || !float.IsFinite(targetPosition.y) || !float.IsFinite(targetPosition.z)
                    || Vector3.Distance(transform.position, targetPosition) > skillData.range))
            {
                GetComponent<PlayerController>()?.RpcShowMessage("Area da habilidade fora do alcance.", PlayerMessageType.Warning);
                return;
            }
            if (skillData.targetType == SkillTargetType.SingleEnemy || skillData.targetType == SkillTargetType.SingleAlly)
            {
                if (targetNetId == 0 || !NetworkServer.spawned.TryGetValue(targetNetId, out var enemyTarget)
                    || Vector3.Distance(transform.position, enemyTarget.transform.position) > skillData.range) return;
                var enemy = enemyTarget.GetComponent<EnemyStats>();
                var opponent = enemyTarget.GetComponent<PlayerCombat>();
                var friendly = enemyTarget.GetComponent<PlayerStats>();
                bool allowed = skillData.targetType == SkillTargetType.SingleAlly ? friendly != null && !friendly.IsDead : (enemy != null && !enemy.IsDead) || (opponent != null && GetComponent<PlayerCombat>().CanPlayerAttack(opponent));
                if (!allowed)
                {
                    GetComponent<PlayerController>()?.RpcShowMessage("Alvo protegido: verifique mapa, area, party ou aceite um duelo.", PlayerMessageType.Warning);
                    return;
                }
            }

            // Consome recursos
            _stats.ConsumeMp(skillData.mpCost);
            _stats.ConsumeSp(originalSpCost);

            // Aplica cooldown
            _skillCooldowns[skillId] = originalCooldown;

            // Executa skill
            OnSkillCastStarted?.Invoke(skillData, skillData.castTime);
            ExecuteSkill(skillData, targetPosition, targetNetId);
            OnSkillExecuted?.Invoke(skillData);

            // Animação
            _animation?.RpcTriggerSkill(skillId);

            Debug.Log($"[PlayerSkills] ✅ {skillData.skillName} executada!");
        }

        // ✅ Carrega skills automaticamente (igual ItemDatabase)
        void LoadAllSkills()
        {
            _allSkills = Resources.LoadAll<SkillData>("Skills");
            var known = new HashSet<int>(_allSkills.Select(s => s.skillId));
            var merged = new List<SkillData>(_allSkills);
            foreach (var pko in TOP.Data.PkoTables.Skills.Values)
                if (!known.Contains(pko.Id)) merged.Add(TOP.Data.PkoTables.ToSkillData(pko));
            _allSkills = merged.ToArray();
            Debug.Log($"[PlayerSkills] Carregadas {_allSkills.Length} skills de Resources/Skills");
        }

        SkillData GetSkillData(int skillId)
        {
            if (_allSkills == null) LoadAllSkills();

            return _allSkills.FirstOrDefault(s => s.skillId == skillId);
        }

        [Server]
        void ExecuteSkill(SkillData data, Vector3 targetPosition, uint targetNetId)
        {
            // Efeitos visuais
            if (data.castEffect != null)
            {
                var effect = Instantiate(data.castEffect, transform.position, Quaternion.identity);
                Destroy(effect.gameObject, 3f);
            }

            switch (data.targetType)
            {
                case SkillTargetType.Self:
                    ApplySkillEffect(data, netId);
                    break;

                case SkillTargetType.SingleAlly:
                case SkillTargetType.SingleEnemy:
                    if (targetNetId != 0)
                        ApplySkillEffect(data, targetNetId);
                    break;

                case SkillTargetType.Cone:
                case SkillTargetType.AreaEnemy:
                    var targets = new HashSet<uint>();
                    foreach (var hit in Physics.OverlapSphere(data.targetType == SkillTargetType.Cone ? transform.position : targetPosition, data.targetType == SkillTargetType.Cone ? data.range : data.areaRadius))
                    {
                        var enemy = hit.GetComponentInParent<EnemyStats>();
                        var player = hit.GetComponentInParent<PlayerCombat>();
                        uint targetId = enemy != null && !enemy.IsDead ? enemy.netId
                            : player != null && GetComponent<PlayerCombat>().CanPlayerAttack(player) ? player.netId : 0;
                        if (data.targetType == SkillTargetType.Cone && Vector3.Angle(transform.forward, hit.transform.position - transform.position) > data.coneAngle * 0.5f) continue;
                        if (targetId != 0 && targets.Add(targetId)) ApplySkillEffect(data, targetId);
                        if (targets.Count >= data.maxTargets) break;
                    }
                    break;
            }

            // Projétil
            if (data.isProjectile && data.projectilePrefab != null)
            {
                GameObject projectile = Instantiate(data.projectilePrefab, transform.position + Vector3.up, Quaternion.identity);
                NetworkServer.Spawn(projectile);
            }
        }

        [Server]
        void ApplySkillEffect(SkillData data, uint targetNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetObj))
                return;

            PlayerStats targetStats = targetObj.GetComponent<PlayerStats>();
            if (targetStats == null) { var enemy = targetObj.GetComponent<EnemyStats>(); if(enemy != null && !enemy.IsDead) enemy.TakeDamage(CalculateSkillDamage(data),netId,DamageType.Physical); return; }

            if (TOP.Data.PkoTables.Skills.TryGetValue(data.skillId, out var original) && TOP.Data.PkoTables.TryTargetFormula(original.DamageFormula, out string stat, out int perLevel) && perLevel > 0)
            {
                var targetController = targetObj.GetComponent<PlayerController>(); var casterController = GetComponent<PlayerController>();
                if (targetController == null || casterController == null || targetController.MapName != casterController.MapName || targetStats.IsDead || Vector3.Distance(transform.position, targetObj.transform.position) > data.range) return;
                int amount = perLevel * GetSkillLevel(data.skillId);
                if (stat == "hp") targetStats.Heal(amount); else if (stat == "sp") targetStats.RestoreSp(amount);
                return;
            }
            int damage = CalculateSkillDamage(data);

            if (data.healAmount > 0)
            {
                targetStats.Heal(Mathf.RoundToInt(data.healAmount * data.healMultiplier));
            }
            else
            {
                var combat = GetComponent<PlayerCombat>();
                var opponent = targetObj.GetComponent<PlayerCombat>();
                float range = data.targetType == SkillTargetType.AreaEnemy ? data.range + data.areaRadius : data.range;
                if (combat != null && opponent != null && Vector3.Distance(transform.position, targetObj.transform.position) <= range)
                    combat.ApplyPlayerDamage(opponent, damage);
            }
        }

        [Server]
        int CalculateSkillDamage(SkillData data)
        {
            int level = _skillLevels.ContainsKey(data.skillId) ? _skillLevels[data.skillId] : 1;
            if (TOP.Data.PkoTables.Skills.TryGetValue(data.skillId, out var original) && TOP.Data.PkoTables.TryTargetFormula(original.DamageFormula, out string stat, out int perLevel) && stat == "hp" && perLevel < 0) return Math.Max(1, -perLevel * level);
            float levelMultiplier = Mathf.Pow(data.damagePerLevel, level - 1);

            int finalDamage = Mathf.RoundToInt(data.baseDamage * data.damageMultiplier * levelMultiplier * data.elementMultiplier);

            if (data.element == SkillElement.None)
                finalDamage += _stats.PhysicalAttack;
            else
                finalDamage += _stats.MagicAttack;

            return Mathf.Max(1, finalDamage);
        }

        [Server]
        public void LearnSkill(int skillId, int level = 1)
        {
            _skillLevels[skillId] = level;
            SerializeSkills();
            Debug.Log($"[PlayerSkills] Skill {skillId} aprendida nível {level}");
        }

        [Server]
        void SerializeSkills()
        {
            List<string> list = new List<string>();
            foreach (KeyValuePair<int, int> kvp in _skillLevels)
            {
                list.Add($"{kvp.Key}:{kvp.Value}");
            }
            _skillsData = string.Join(";", list);
        }

        // Getters públicos
        public bool HasSkill(int skillId) => _skillLevels.ContainsKey(skillId);
        public int GetSkillLevel(int skillId) => _skillLevels.ContainsKey(skillId) ? _skillLevels[skillId] : 0;
        public float GetCooldown(int skillId) => _skillCooldowns.ContainsKey(skillId) ? _skillCooldowns[skillId] : 0f;

        void OnSkillsDataChanged(string oldValue, string newValue)
        {
            if (isServer) return;
            _skillLevels.Clear();
            foreach (var part in (newValue ?? "").Split(';'))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int id) && int.TryParse(kv[1], out int lv)) _skillLevels[id] = lv;
            }
        }

        public IReadOnlyDictionary<int, int> SkillLevels => _skillLevels;

        public static int ClassMaximum(TOP.Data.PkoSkill skill, int job)
        {
            int parent = job == 8 || job == 9 || job == 10 ? 1 : job == 11 || job == 12 ? 2 : job == 13 || job == 14 ? 5 : job == 16 ? 4 : job;
            int max = skill.ClassMaxLevel.TryGetValue(-1, out var global) ? global : 0;
            foreach (var entry in skill.ClassMaxLevel)
                if (entry.Key == job || entry.Key == parent || (job == 9 && entry.Key == 10) || (job == 10 && entry.Key == 9) || (job == 11 && entry.Key == 12) || (job == 12 && entry.Key == 11)) max = Math.Max(max, entry.Value);
            return max;
        }
        public List<TOP.Data.CharacterSkillData> GetPersistedSkills() => _skillLevels.Select(kv => new TOP.Data.CharacterSkillData { SkillId = kv.Key, Level = (byte)kv.Value }).ToList();
        [Server]
        public void LoadPersistedSkills(List<TOP.Data.CharacterSkillData> skills)
        {
            _skillLevels.Clear();
            foreach (var skill in skills ?? new List<TOP.Data.CharacterSkillData>()) _skillLevels[skill.SkillId] = skill.Level;
            SerializeSkills();
        }
        [Command]
        public async void CmdLearnSkill(int skillId)
        {
            var pc = GetComponent<PlayerController>();
            if (pc == null || !TOP.Data.PkoTables.Skills.TryGetValue(skillId, out var skill) || skill.IsLife) return;
            if (skillId != 241 && !skill.ClassMaxLevel.Keys.Any(job => job > 0 && job <= 18)) return;
            await PlayerDurableAction.Run(pc, data =>
            {
                int current = GetSkillLevel(skillId), maximum = ClassMaximum(skill, data.Job), cost = Math.Max(1, skill.Points);
                if (current >= maximum || data.Level < skill.LearnLevel || data.SkillPoints < cost) throw new InvalidOperationException("Classe, nivel ou pontos insuficientes para aprender esta habilidade.");
                foreach (var entry in (skill.Prerequisites ?? "").Split(';'))
                {
                    var fields = entry.Split(',');
                    if (fields.Length == 2 && int.TryParse(fields[0], out int required) && required > 0 && int.TryParse(fields[1], out int level) && GetSkillLevel(required) < level) throw new InvalidOperationException("Habilidade prerequisito insuficiente.");
                }
                data.SkillPoints -= cost;
                var learned = data.Skills.Find(i => i.SkillId == skillId);
                if (learned == null) data.Skills.Add(new TOP.Data.CharacterSkillData { SkillId = skillId, Level = (byte)(current + 1) }); else learned.Level = (byte)(current + 1);
            }, data => LoadPersistedSkills(data.Skills));
        }
    }
}