using UnityEngine;
using UnityEngine.AI;
using Mirror;
using System;
using TOP.Player;
using TOP.Core;

[RequireComponent(typeof(EnemyStats))]
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : NetworkBehaviour
{
    [Header("AI Settings")]
    [SerializeField] private float aggroRange = 10f;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float stopChaseRange = 20f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool showAIDebugLogs = true;

    public enum AIState { Idle, Chase, Attack, Dead, Return }
    private AIState currentState = AIState.Idle;
    private float lastAttackTime;
    private bool aiEnabled = true;

    private EnemyStats stats;
    private Animator anim;
    private NavMeshAgent agent;
    private Transform target;
    private Vector3 spawnPosition;

    void Awake()
    {
        stats = GetComponent<EnemyStats>();
        agent = GetComponent<NavMeshAgent>();
        agent.enabled = false;

        anim = GetComponent<Animator>();
        if (anim == null)
            anim = GetComponentInChildren<Animator>();

        spawnPosition = transform.position;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        // ✅ CORRIGIDO: Configura NavMeshAgent corretamente
        if (agent != null)
        {
            agent.speed = stats != null ? stats.MoveSpeed : 3.5f;
            agent.stoppingDistance = attackRange * 0.8f;
            agent.acceleration = 8f;
            agent.angularSpeed = rotationSpeed * 36f;
            agent.autoBraking = true;
            if (!NavMesh.SamplePosition(transform.position, out var spawnHit, 5f, NavMesh.AllAreas))
            {
                Debug.LogError($"[EnemyAI] {gameObject.name}: sem NavMesh para iniciar navegacao no servidor.");
                return;
            }
            transform.position = spawnHit.position;
            spawnPosition = spawnHit.position;

            // ✅ CORRIGIDO: Garante que o agent está ativo
            if (!agent.isActiveAndEnabled)
            {
                agent.enabled = true;
            }

            // ✅ CORRIGIDO: Verifica se está em um NavMesh válido
            if (!agent.isOnNavMesh)
            {
                Debug.LogWarning("[EnemyAI] Não está em um NavMesh válido! Tentando warp...");
                NavMeshHit hit;
                if (NavMesh.SamplePosition(transform.position, out hit, 5f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    Debug.Log("[EnemyAI] ✅ Warp para NavMesh bem-sucedido!");
                }
                else
                {
                    Debug.LogError("[EnemyAI] ❌ Não conseguiu encontrar NavMesh próximo!");
                }
            }
        }

        if (showAIDebugLogs)
            Debug.Log("[EnemyAI] " + gameObject.name + " iniciado. isOnNavMesh=" + (agent?.isOnNavMesh ?? false));
    }

    void Update()
    {
        if (!isServer) return;
        if (!aiEnabled) return;
        if (stats.IsDead)
        {
            if (currentState != AIState.Dead)
                ChangeState(AIState.Dead);
            return;
        }

        // ✅ CORRIGIDO: Verifica se o agent está em NavMesh antes de atualizar
        if (agent != null && (!agent.isActiveAndEnabled || !agent.isOnNavMesh))
        {
            // Tenta recolocar no NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas))
            {
                if (!agent.enabled)
                {
                    transform.position = hit.position;
                    agent.enabled = true;
                }
                agent.Warp(hit.position);
            }
            return;  // Sai do Update até conseguir estar no NavMesh
        }

        switch (currentState)
        {
            case AIState.Idle: UpdateIdle(); break;
            case AIState.Chase: UpdateChase(); break;
            case AIState.Attack: UpdateAttack(); break;
            case AIState.Return: UpdateReturn(); break;
        }

        UpdateAnimation();
    }

    #region State Machine

    private void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        if (showAIDebugLogs)
            Debug.Log("[EnemyAI] " + gameObject.name + ": " + currentState + " -> " + newState);

        currentState = newState;

        switch (newState)
        {
            case AIState.Idle:
                SafeResetPath();
                target = null;
                break;
            case AIState.Chase:
                break;
            case AIState.Attack:
                SafeResetPath();
                break;
            case AIState.Return:
                target = null;
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                    agent.SetDestination(spawnPosition);
                break;
            case AIState.Dead:
                SafeResetPath();
                if (agent != null) agent.enabled = false;
                break;
        }
    }

    #endregion

    #region Idle State

    private void UpdateIdle()
    {
        // ✅ CORRIGIDO: Usa LayerMask.GetMask com verificação
        int playerLayer = LayerMask.GetMask("Player");
        if (playerLayer == 0)
        {
            Debug.LogWarning("[EnemyAI] Layer 'Player' não encontrada! Verifique Project Settings > Tags and Layers.");
            return;
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, aggroRange, playerLayer);

        Transform closestPlayer = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            var netIdComp = hit.GetComponent<NetworkIdentity>();
            if (netIdComp == null) continue;

            // ✅ CORRIGIDO: Busca PlayerStats com fallback
            PlayerStats pStats = hit.GetComponent<PlayerStats>();
            if (pStats == null)
                pStats = hit.GetComponentInParent<PlayerStats>();
            if (pStats == null)
                pStats = hit.GetComponentInChildren<PlayerStats>();

            if (pStats == null || pStats.IsDead) continue;

            float dist = Vector3.Distance(transform.position, hit.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                closestPlayer = hit.transform;
            }
        }

        if (closestPlayer != null)
        {
            target = closestPlayer;
            ChangeState(AIState.Chase);
        }
    }

    #endregion

    #region Chase State

    private void UpdateChase()
    {
        if (target == null)
        {
            ChangeState(AIState.Idle);
            return;
        }

        // ✅ CORRIGIDO: Busca PlayerStats com fallback
        PlayerStats pStats = target.GetComponent<PlayerStats>();
        if (pStats == null)
            pStats = target.GetComponentInParent<PlayerStats>();
        if (pStats == null || pStats.IsDead)
        {
            ChangeState(AIState.Idle);
            return;
        }

        float distToTarget = Vector3.Distance(transform.position, target.position);
        float distToSpawn = Vector3.Distance(transform.position, spawnPosition);

        if (distToSpawn > stopChaseRange)
        {
            ChangeState(AIState.Return);
            return;
        }

        if (distToTarget <= attackRange)
        {
            ChangeState(AIState.Attack);
            return;
        }

        // ✅ CORRIGIDO: Só seta destino se estiver em NavMesh
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.SetDestination(target.position);
        }
    }

    #endregion

    #region Attack State

    private void UpdateAttack()
    {
        if (target == null)
        {
            ChangeState(AIState.Idle);
            return;
        }

        PlayerStats pStats = target.GetComponent<PlayerStats>();
        if (pStats == null)
            pStats = target.GetComponentInParent<PlayerStats>();
        if (pStats == null || pStats.IsDead)
        {
            ChangeState(AIState.Idle);
            return;
        }

        float distToTarget = Vector3.Distance(transform.position, target.position);

        if (distToTarget > attackRange * 1.5f)  // ✅ Histerese maior para evitar flicker
        {
            ChangeState(AIState.Chase);
            return;
        }

        LookAt(target.position);

        float cooldownTime = attackCooldown / Mathf.Max(stats.AttackSpeed, 0.1f);
        if (Time.time >= lastAttackTime + cooldownTime)
        {
            lastAttackTime = Time.time;
            PerformAttack();
        }
    }

    [Server]
    private void PerformAttack()
    {
        if (target == null) return;

        if (anim != null)
            anim.SetTrigger("Attack");

        // ✅ Aplica dano no player
        PlayerStats pStats = target.GetComponent<PlayerStats>();
        if (pStats == null)
            pStats = target.GetComponentInParent<PlayerStats>();

        if (pStats != null && !pStats.IsDead)
        {
            int damage = CalculateDamage();
            pStats.TakeDamage(damage, netId, DamageType.Physical);
            Debug.Log($"[EnemyAI] ⚔️ {gameObject.name} causou {damage} de dano em {target.name}");
        }
    }

    [Server]
    private int CalculateDamage()
    {
        float variance = UnityEngine.Random.Range(0.9f, 1.1f);
        return Mathf.Max(1, Mathf.RoundToInt(stats.Attack * variance));
    }

    #endregion

    #region Return State

    private void UpdateReturn()
    {
        float distToSpawn = Vector3.Distance(transform.position, spawnPosition);

        if (distToSpawn < 1f)
        {
            ChangeState(AIState.Idle);
            return;
        }

        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && !agent.hasPath)
        {
            agent.SetDestination(spawnPosition);
        }
    }

    #endregion

    #region Animation & Movement

    private void UpdateAnimation()
    {
        if (anim == null) return;

        bool isMoving = agent != null && agent.isActiveAndEnabled && 
                        agent.isOnNavMesh && agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.1f;

        anim.SetBool("IsMoving", isMoving);
        anim.SetBool("IsAttacking", currentState == AIState.Attack);
    }

    private void LookAt(Vector3 targetPos)
    {
        Vector3 dir = targetPos - transform.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, rotationSpeed * Time.deltaTime);
        }
    }

    private void SafeResetPath()
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    #endregion

    #region Public API

    public void SetEnabled(bool enabled)
    {
        aiEnabled = enabled;
        if (!enabled) SafeResetPath();
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        if (target != null && currentState != AIState.Chase && currentState != AIState.Attack)
            ChangeState(AIState.Chase);
    }

    public Transform CurrentTarget => target;
    public AIState State => currentState;

    #endregion

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, aggroRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}