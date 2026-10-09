using UnityEngine;
using Mirror;
using TOP.Core;
using TOP.Systems;
using System;

namespace TOP.Player
{
    public partial class PlayerCombat : NetworkBehaviour
    {
        public event Action OnAttackStarted;
        public event Action OnAttackFinished;
        public event Action<Transform> OnTargetChanged;

        [Header("Combat")]
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float attackCooldown = 1f;
        [SerializeField] private Transform attackPoint;

        [Header("Target Selection Visual")]
        [SerializeField] private bool showSelectionCircle = true;

        [SyncVar] private uint _currentTargetNetId;
        public uint CurrentTargetNetId => _currentTargetNetId;
        [SyncVar] private bool _isAttacking;

        private float _lastAttackTime;
        private bool _attackHitPending = false;
        private bool _attackAnimationPlaying = false;
        private PlayerStats _stats;
        private PlayerAnimation _animation;
        private PlayerEquipment _equipment;
        private PlayerController _controller;
        private PlayerMovement _movement;
        private EnemySelection _currentEnemySelection;
        private NetworkIdentity _currentTarget;

        void Awake()
        {
            _stats = GetComponent<PlayerStats>();
            _animation = GetComponent<PlayerAnimation>();
            _equipment = GetComponent<PlayerEquipment>();
            _controller = GetComponent<PlayerController>();
            _movement = GetComponent<PlayerMovement>();
        }

        void Update()
        {
            if (!isServer) return;
            UpdateDuel();
            if (_stats == null || _stats.IsDead) { StopAttack(); return; }

            // Atualiza referência do target a partir do netId
            if (_currentTargetNetId != 0 && (_currentTarget == null || _currentTarget.netId != _currentTargetNetId))
            {
                if (NetworkServer.spawned.TryGetValue(_currentTargetNetId, out NetworkIdentity identity))
                    _currentTarget = identity;
                else
                    _currentTarget = null;
            }

            if (_currentTarget != null && _isAttacking)
            {
                float distance = Vector3.Distance(transform.position, _currentTarget.transform.position);

                // Se o target morreu, para de atacar
                EnemyStats enemyStats = _currentTarget.GetComponent<EnemyStats>();
                var targetPlayer = _currentTarget.GetComponent<PlayerCombat>();
                if (targetPlayer != null && !CanDuelAttack(targetPlayer))
                {
                    StopAttack();
                    return;
                }
                if (enemyStats != null && enemyStats.IsDead)
                {
                    StopAttack();
                    return;
                }

                // Só ataca se estiver em range E cooldown passou
                if (distance > attackRange) { _movement?.SetDestination(_currentTarget.transform.position); return; }
                if (distance <= attackRange)
                {
                    _movement?.Stop();
                    if (Time.time >= _lastAttackTime + attackCooldown && !_attackAnimationPlaying)
                    {
                        StartAttackAnimation();
                    }

                    // Aplica dano quando o Animation Event dispara
                    if (_attackHitPending)
                    {
                        ApplyDamage();
                        _attackHitPending = false;
                    }
                }
            }
        }

        [Server]
        void StartAttackAnimation()
        {
            _lastAttackTime = Time.time;
            _attackHitPending = true;
            _attackAnimationPlaying = true;

            // Dispara animação nos clientes
            _animation?.RpcTriggerAttack(0);
            OnAttackStarted?.Invoke();

            Debug.Log($"[PlayerCombat] 🎬 Animação de ataque iniciada em {(_currentTarget != null ? _currentTarget.name : "NULL")}");

            Invoke(nameof(EndAttackAnimation), attackCooldown * 0.8f);
        }

        [Server]
        void EndAttackAnimation()
        {
            _attackAnimationPlaying = false;
            OnAttackFinished?.Invoke();
            Debug.Log("[PlayerCombat] 🏁 Animação de ataque finalizada");
        }

        [Server]
        void ApplyDamage()
        {
            if (_currentTarget == null) return;
            if (Vector3.Distance(transform.position, _currentTarget.transform.position) > attackRange) return;

            int damage = CalculateDamage();

            EnemyStats targetStats = _currentTarget.GetComponent<EnemyStats>();
            if (targetStats != null)
            {
                targetStats.TakeDamage(damage, netId, DamageType.Physical);
                Debug.Log($"[PlayerCombat] ⚔️ {damage} de dano em {_currentTarget.name} | HP={targetStats.Health}/{targetStats.MaxHealth}");
            }
            else
            {
                var targetPlayer = _currentTarget.GetComponent<PlayerCombat>();
                if (targetPlayer == null || !CanDuelAttack(targetPlayer)) { StopAttack(); return; }
                ApplyDuelDamage(targetPlayer, damage);
            }

            _stats?.ConsumeSp(5);
        }

        [Server]
        public void OnAnimationAttackHit()
        {
            if (!isServer) return;
            // Hits are scheduled once by the server, never duplicated by client animation events.
        }

        /// <summary>
        /// Define o alvo do combate pelo netId.
        /// </summary>
        [Server]
        public void SetTarget(uint targetNetId)
        {
            if (_currentEnemySelection != null)
            {
                _currentEnemySelection.SetSelected(false);
                _currentEnemySelection = null;
            }

            _currentTargetNetId = targetNetId;

            NetworkIdentity target = null;
            if (targetNetId != 0 && NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
                target = identity;

            _currentTarget = target;
            OnTargetChanged?.Invoke(target?.transform);

            if (target != null && showSelectionCircle)
            {
                _currentEnemySelection = target.GetComponent<EnemySelection>();
                if (_currentEnemySelection == null)
                    _currentEnemySelection = target.GetComponentInChildren<EnemySelection>();

                if (_currentEnemySelection != null)
                {
                    _currentEnemySelection.SetSelected(true);
                    Debug.Log($"[PlayerCombat] ✅ Círculo verde ativado em {target.name}");
                }
                else
                {
                    Debug.LogWarning($"[PlayerCombat] {target.name} não tem EnemySelection!");
                }
            }

            Debug.Log($"[PlayerCombat] Target setado: {(target != null ? target.name : "NULL")} (netId={targetNetId})");
        }

        /// <summary>
        /// Inicia auto-attack no alvo.
        /// </summary>
        [Server]
        public void AttackTarget(uint targetNetId, int skillId = 0)
        {
            if (targetNetId == 0) return;
            if (!NetworkServer.spawned.TryGetValue(targetNetId, out var identity)) return;
            var playerTarget = identity.GetComponent<PlayerCombat>();
            if (playerTarget != null && !CanDuelAttack(playerTarget))
            {
                _controller?.RpcShowMessage("O jogador precisa aceitar um duelo antes do ataque.", PlayerMessageType.Warning);
                return;
            }
            if (playerTarget == null && identity.GetComponent<EnemyStats>() == null) return;

            SetTarget(targetNetId);
            _isAttacking = true;

            Debug.Log($"[PlayerCombat] Auto-attack iniciado em netId={targetNetId} (skillId={skillId})");
        }

        [Server]
        int CalculateDamage()
        {
            int baseDamage = _stats.PhysicalAttack;

            if (_equipment != null)
                baseDamage += _equipment.GetTotalAttackBonus();

            float variation = UnityEngine.Random.Range(0.9f, 1.1f);
            int finalDamage = Mathf.RoundToInt(baseDamage * variation);

            if (UnityEngine.Random.value < _stats.CriticalRate)
                finalDamage = Mathf.RoundToInt(finalDamage * _stats.CriticalDamage);

            return Mathf.Max(1, finalDamage);
        }

        [Server]
        public void StopAttack()
        {
            if (_currentEnemySelection != null)
            {
                _currentEnemySelection.SetSelected(false);
                _currentEnemySelection = null;
            }

            _isAttacking = false;
            _currentTargetNetId = 0;
            _currentTarget = null;
            _attackHitPending = false;
            _attackAnimationPlaying = false;
            CancelInvoke(nameof(EndAttackAnimation));
            OnTargetChanged?.Invoke(null);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attackPoint ? attackPoint.position : transform.position, attackRange);
        }
    }
}