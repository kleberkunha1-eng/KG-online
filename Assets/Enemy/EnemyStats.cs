using UnityEngine;
using Mirror;
using UnityEngine.AI;
using System;
using System.Collections;
using System.Collections.Generic;
using TOP.Core;
using TOP.Player;

public class EnemyStats : NetworkBehaviour, ICharacterStats
{
    [Header("Base Stats")]
    [SerializeField] private int _health = 100;
    [SerializeField] private int _maxHealth = 100;
    [SerializeField] private int _mana = 50;
    [SerializeField] private int _maxMana = 50;
    [SerializeField] private int _stamina = 100;
    [SerializeField] private int _maxStamina = 100;
    [SerializeField] private int _strength = 10;
    [SerializeField] private int _agility = 5;
    [SerializeField] private int _constitution = 10;
    [SerializeField] private int _spirit = 5;
    [SerializeField] private int _accuracy = 10;
    [SerializeField] private int _luck = 5;
    [SerializeField] private int _attack = 10;
    [SerializeField] private int _defense = 5;
    [SerializeField] private int _magicAttack = 5;
    [SerializeField] private int _magicDefense = 5;
    [SerializeField] private float _attackSpeed = 1f;
    [SerializeField] private float _moveSpeed = 3.5f;
    [SerializeField] private int _level = 1;

    [Header("Config")]
    [SerializeField] private string _enemyName = "Enemy";
    [SerializeField] private int _originalMonsterId;
    [SerializeField] private int _experienceReward = 10;
    [SerializeField] private int _goldReward = 5;
    [SerializeField] private float _respawnTime = 10f;

    [Header("Death Animation")]
    [SerializeField] private string deathStateName = "Morrendo";
    [SerializeField] private float deathAnimationDuration = 1.02f;
    [SerializeField] private float deathPoseDuration = 1.5f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs = true;

    // SyncVars
    [SyncVar(hook = nameof(OnHealthSyncChanged))] private int _syncHealth;
    [SyncVar] private bool _isDead;

    // Estado local
    private bool _initialized;
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private List<StatModifier> _modifiers = new List<StatModifier>();

    // Referencias
    private EnemyAI _enemyAI;
    private Animator _animator;
    private Collider _collider;
    private NavMeshAgent _agent;

    // Eventos
    public event Action OnHealthUpdated;
    public event Action OnManaUpdated;
    public event Action OnStaminaUpdated;
    public event Action OnDeath;
    public event Action OnRevive;
    public event Action OnStatsChanged;

    #region Propriedades ICharacterStats

    public int Health => _syncHealth;
    public int MaxHealth => _maxHealth;
    public int Mana => _mana;
    public int MaxMana => _maxMana;
    public int Stamina => _stamina;
    public int MaxStamina => _maxStamina;
    public int Strength => _strength;
    public int Agility => _agility;
    public int Constitution => _constitution;
    public int Spirit => _spirit;
    public int Accuracy => _accuracy;
    public int Luck => _luck;
    public int Attack => _attack;
    public int Defense => _defense;
    public int MagicAttack => _magicAttack;
    public int MagicDefense => _magicDefense;
    public float AttackSpeed => _attackSpeed;
    public float MoveSpeed => _moveSpeed;
    public int Level => _level;
    public bool IsDead => _isDead;
    public string EnemyName => _enemyName;
    public int ExperienceReward => _experienceReward;
    public int GoldReward => _goldReward;

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        _enemyAI = GetComponent<EnemyAI>();
        _animator = GetComponentInChildren<Animator>();
        _collider = GetComponent<Collider>();
        _agent = GetComponent<NavMeshAgent>();
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;

        if (_animator == null && GetComponentInChildren<Animation>(true) == null)
            Debug.LogError($"[EnemyStats] Animator NÃO ENCONTRADO em {gameObject.name}!");
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        InitializeStats();
    }

    #endregion

    #region Initialization

    [Server]
    public void InitializeStats()
    {
        if (_initialized) return;

        _syncHealth = _maxHealth;
        _mana = _maxMana;
        _stamina = _maxStamina;
        _isDead = false;
        _initialized = true;

        if (showDebugLogs)
            Debug.Log("[EnemyStats] " + _enemyName + " (netId=" + netId + ") inicializado | HP=" + _syncHealth + "/" + _maxHealth);
    }

    #endregion

    #region Combat - ICharacterStats

    [Server]
    public void TakeDamage(int damage, uint attackerId, DamageType damageType)
    {
        if (_isDead) return;

        if (showDebugLogs)
        {
            Debug.Log("[EnemyStats] ===============================================");
            Debug.Log("[EnemyStats] TakeDamage CHAMADO em " + _enemyName + " (netId=" + netId + ")");
            Debug.Log("[EnemyStats]   -> dmg=" + damage + " | attackerId=" + attackerId + " | damageType=" + damageType);
            Debug.Log("[EnemyStats]   -> isDead=" + _isDead + " | HP antes=" + _syncHealth + "/" + _maxHealth);
        }

        int finalDamage = Mathf.Max(1, damage - _defense);
        _syncHealth = Mathf.Max(0, _syncHealth - finalDamage);

        if (showDebugLogs)
            Debug.Log("[EnemyStats] " + _enemyName + " HP depois=" + _syncHealth + " | danoFinal=" + finalDamage);

        RpcOnDamageTaken(finalDamage, attackerId);

        if (_syncHealth <= 0)
        {
            if (showDebugLogs)
                Debug.Log("[EnemyStats] " + _enemyName + " MORREU! killerId=" + attackerId);
            Die(attackerId);
        }
    }

    [Server]
    public void TakeTrueDamage(int damage, uint attackerId)
    {
        if (_isDead) return;

        _syncHealth = Mathf.Max(0, _syncHealth - damage);
        RpcOnDamageTaken(damage, attackerId);

        if (_syncHealth <= 0)
            Die(attackerId);
    }

    [Server]
    public void Heal(int amount)
    {
        if (_isDead) return;
        _syncHealth = Mathf.Min(_maxHealth, _syncHealth + amount);
        OnHealthUpdated?.Invoke();
    }

    [Server]
    public void RestoreMana(int amount)
    {
        _mana = Mathf.Min(_maxMana, _mana + amount);
        OnManaUpdated?.Invoke();
    }

    [Server]
    public void RestoreStamina(int amount)
    {
        _stamina = Mathf.Min(_maxStamina, _stamina + amount);
        OnStaminaUpdated?.Invoke();
    }

    [Server]
    public void PerformAttack(GameObject target)
    {
        if (_isDead) return;
        if (target == null) return;

        if (showDebugLogs)
        {
            Debug.Log("[EnemyStats] ===============================================");
            Debug.Log("[EnemyStats] PerformAttack() chamado");
            Debug.Log("[EnemyStats]   Atacante: " + _enemyName);
            Debug.Log("[EnemyStats]   Alvo: " + target.name);
            Debug.Log("[EnemyStats] ===============================================");
        }

        ICharacterStats targetStats = target.GetComponent<ICharacterStats>();
        if (targetStats == null)
        {
            Debug.LogWarning("[EnemyStats] Alvo " + target.name + " nao implementa ICharacterStats");
            return;
        }

        if (targetStats.IsDead)
        {
            Debug.Log("[EnemyStats] Alvo " + target.name + " ja esta morto");
            return;
        }

        int damage = CalculateDamage();
        targetStats.TakeDamage(damage, netId, DamageType.Physical);

        if (showDebugLogs)
            Debug.Log("[EnemyStats] " + _enemyName + " causando " + damage + " de dano em " + target.name);

        RpcOnAttackLanded(target.GetComponent<NetworkIdentity>()?.netId ?? 0, damage);
    }

    [Server]
    private int CalculateDamage()
    {
        float variance = UnityEngine.Random.Range(0.9f, 1.1f);
        return Mathf.Max(1, Mathf.RoundToInt(_attack * variance));
    }

    #endregion

    #region Modifiers - ICharacterStats

    [Server]
    public void AddModifier(StatModifier modifier)
    {
        _modifiers.Add(modifier);
        ApplyModifiers();
        OnStatsChanged?.Invoke();
    }

    [Server]
    public void RemoveModifier(StatModifier modifier)
    {
        _modifiers.Remove(modifier);
        ApplyModifiers();
        OnStatsChanged?.Invoke();
    }

    [Server]
    public void ClearModifiers()
    {
        _modifiers.Clear();
        ApplyModifiers();
        OnStatsChanged?.Invoke();
    }

    [Server]
    private void ApplyModifiers()
    {
        // Reset para valores base
    }

    #endregion

    #region Death & Respawn

    [Server]
    private void Die(uint killerId)
    {
        if (_isDead) return;
        _isDead = true;

        if (showDebugLogs)
            Debug.Log("[EnemyStats] Die() chamado em " + _enemyName + " | killerId=" + killerId);

        // Recompensas
        if (killerId != 0 && NetworkServer.spawned.TryGetValue(killerId, out var killer))
        {
            var killerStats = killer.GetComponent<PlayerStats>();
            if (killerStats != null)
            {
                killerStats.AddExperience(_experienceReward);
                killerStats.AddGold(_goldReward);
                if (showDebugLogs)
                    Debug.Log("[EnemyStats] Recompensa: " + _experienceReward + " XP, " + _goldReward + " Gold para " + killer.name);
            }
            var quests = killer.GetComponent<TOP.Player.PlayerQuests>();
            if (quests != null) quests.ServerNotifyKill(_enemyName, _originalMonsterId > 0 ? _originalMonsterId
                : TOP.Data.OriginalQuestCatalog.MonsterId(_enemyName, _level));
        }

        // Desativa IA e colisão imediatamente (servidor)
        if (_collider != null) _collider.enabled = false;
        if (_enemyAI != null) _enemyAI.SetEnabled(false);
        if (_agent != null && _agent.isActiveAndEnabled) _agent.enabled = false;

        // Envia RPC de morte para TODOS os clientes tocarem a animação
        RpcOnDeath();

        OnDeath?.Invoke();

        // Inicia sequência de morte (aguarda animação + pose)
        StartCoroutine(DeathSequence());
    }

    [Server]
    private IEnumerator DeathSequence()
    {
        float totalWait = deathAnimationDuration + deathPoseDuration;

        if (showDebugLogs)
            Debug.Log("[EnemyStats] DeathSequence: aguardando " + totalWait + "s");

        yield return new WaitForSeconds(totalWait);

        // Esconde o inimigo nos clientes (renderers)
        RpcSetVisible(false);

        // Aguarda respawn
        yield return new WaitForSeconds(_respawnTime);
        Respawn();
    }

    [Server]
    private void Respawn()
    {
        _syncHealth = _maxHealth;
        _mana = _maxMana;
        _stamina = _maxStamina;
        _isDead = false;

        transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;

        // Reativa componentes
        if (_collider != null) _collider.enabled = true;
        if (_enemyAI != null) _enemyAI.SetEnabled(true);
        if (_agent != null) _agent.enabled = true;

        // ✅ CORRIGIDO: Reativa visibilidade E a barra de HP nos clientes
        RpcSetVisible(true);

        // ✅ CORRIGIDO: Reseta Animator nos clientes
        RpcResetAnimator();

        if (showDebugLogs)
            Debug.Log("[EnemyStats] " + _enemyName + " respawnou em " + _spawnPosition);
    }

    #endregion

    #region RPCs

    [ClientRpc]
    private void RpcOnDamageTaken(int damage, uint attackerId)
    {
        TOP.Systems.EffectManager.Instance?.PlayHitEffect(transform.position + Vector3.up, Vector3.up);
        TOP.Systems.DamagePopupManager.Instance?.ShowDamage(transform.position + Vector3.up, damage, false);
        OnHealthUpdated?.Invoke();
    }

    [ClientRpc]
    private void RpcOnAttackLanded(uint targetNetId, int damage)
    {
        if (showDebugLogs)
            Debug.Log("[EnemyStats] RpcOnAttackLanded: dano " + damage + " no netId=" + targetNetId);
    }

    [ClientRpc]
    private void RpcSetVisible(bool visible)
    {
        if (showDebugLogs)
            Debug.Log("[EnemyStats] RpcSetVisible(" + visible + ") em " + gameObject.name);

        // ✅ CORRIGIDO: Reativa TODOS os renderers e componentes visuais
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var rend in renderers)
        {
            rend.enabled = visible;
        }

        // ✅ CORRIGIDO: Reativa a barra de HP especificamente
        EnemyHealthBar healthBar = GetComponentInChildren<EnemyHealthBar>(true);
        if (healthBar != null)
        {
            healthBar.gameObject.SetActive(visible);
            Debug.Log("[EnemyStats] ✅ EnemyHealthBar reativada!");
        }

        // ✅ CORRIGIDO: Reativa o círculo de seleção se existir
        EnemySelection selection = GetComponentInChildren<EnemySelection>(true);
        if (selection != null)
        {
            // Não reativa a seleção aqui — ela só ativa quando o player clica
            // Mas garante que o GameObject está ativo
            if (selection.gameObject != gameObject)
                selection.gameObject.SetActive(visible);
        }

        if (visible && !gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    [ClientRpc]
    private void RpcOnDeath()
    {
        if (showDebugLogs)
            Debug.Log($"[EnemyStats] RpcOnDeath em {gameObject.name}");

        Animator anim = _animator ?? GetComponentInChildren<Animator>();
        if (anim == null || anim.runtimeAnimatorController == null) return;

        // 1. Para movimento e ataque
        anim.SetBool("IsMoving", false);
        anim.SetBool("IsAttacking", false);

        // 2. Limpa APENAS triggers de combate
        anim.ResetTrigger("Attack");
        anim.ResetTrigger("Hit");

        // 3. Dispara morte
        anim.SetTrigger("Die");

        if (showDebugLogs)
            Debug.Log("[EnemyStats] Trigger 'Die' disparado");

        // 4. Esconde UI
        EnemyHealthBar healthBar = GetComponentInChildren<EnemyHealthBar>();
        if (healthBar != null) healthBar.gameObject.SetActive(false);
    }

    [ClientRpc]
    private void RpcResetAnimator()
    {
        Animator anim = _animator ?? GetComponentInChildren<Animator>();
        if (anim != null)
        {
            // Reseta bools
            anim.SetBool("IsDead", false);
            anim.SetBool("IsMoving", false);
            anim.SetBool("IsAttacking", false);

            // Reseta triggers
            anim.ResetTrigger("Die");
            anim.ResetTrigger("Attack");
            anim.ResetTrigger("Hit");

            // Volta para idle
            int idleHash = Animator.StringToHash("Parado1");
            if (anim.HasState(0, idleHash))
            {
                anim.Play("Parado1", 0, 0f);
            }
            else
            {
                anim.Play(0, 0, 0f);
            }

            if (showDebugLogs)
                Debug.Log("[EnemyStats] Animator resetado para respawn");
        }
    }

    private bool HasAnimatorParameter(Animator animator, string paramName, AnimatorControllerParameterType type)
    {
        if (animator == null || animator.parameters == null) return false;

        foreach (var param in animator.parameters)
        {
            if (param.name == paramName && param.type == type)
                return true;
        }
        return false;
    }

    #endregion

    #region Hooks

    private void OnHealthSyncChanged(int oldHealth, int newHealth)
    {
        OnHealthUpdated?.Invoke();
    }

    #endregion

    #region Gizmos

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }

    #endregion
}