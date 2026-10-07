using UnityEngine;
using System;
using Mirror;
using UnityEngine.EventSystems;
using TOP.Core;
using TOP.Data;

namespace TOP.Player
{
    public class PlayerMovement : NetworkBehaviour
    {
        public event Action OnMovementStarted;
        public event Action OnMovementStopped;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float runSpeed = 10f;
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float stoppingDistance = 0.5f;

        [Header("Target Selection")]
        [SerializeField] private LayerMask enemyLayers;
        [SerializeField] private float raycastDistance = 100f;
        [SerializeField] private bool showDebugRay = true;

        [Header("Ground Check")]
        [SerializeField] private float groundCheckDistance = 2f;

        private PlayerAnimation _animation;
        private Vector3 _targetPosition;
        [SyncVar] private bool _isMoving;
        private bool _wasMoving;
        [SyncVar] private bool running;
        public bool IsRunning => running;
        public bool InputEnabled { get; set; } = true;
        [Command] void CmdSetRunning(bool value) { running = value; }

        // Lazy load do PlayerController
        private PlayerController _playerController;
        private PlayerEquipment _equipment;
        private PlayerEquipment EquipmentRef
        {
            get
            {
                if (_equipment == null) _equipment = GetComponent<PlayerEquipment>() ?? GetComponentInParent<PlayerEquipment>();
                return _equipment;
            }
        }

        // mountinfo.txt: montaria equipada no slot Mount concede bonus de velocidade de deslocamento
        // (o modelo 3D da montaria ainda nao foi convertido para o Unity, ver PkoMount).
        float MountSpeedMultiplier()
        {
            var eq = EquipmentRef;
            if (eq == null) return 1f;
            var mountItem = eq.GetEquippedItem(EquipmentSlot.Mount);
            if (mountItem == null || mountItem.ItemId <= 0) return 1f;
            return PkoTables.MountsByItemId.ContainsKey(mountItem.ItemId) ? 1.5f : 1f;
        }
        private PlayerController PlayerControllerRef
        {
            get
            {
                if (_playerController == null)
                {
                    _playerController = GetComponent<PlayerController>();
                    if (_playerController == null)
                        _playerController = GetComponentInParent<PlayerController>();
                    if (_playerController == null)
                        _playerController = GetComponentInChildren<PlayerController>();
                }
                return _playerController;
            }
        }

        void Awake()
        {
            _animation = GetComponent<PlayerAnimation>();
        }

        void Update()
        {
            if (isLocalPlayer && InputEnabled)
            {
                HandleInput();
            }

            if (isServer)
            {
                UpdateMovementState();
            }
        }

        void HandleInput()
        {
            if (Camera.main == null || (PlayerControllerRef != null && PlayerControllerRef.CurrentHp <= 0)) return;
            bool requestedRun = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (requestedRun != running) CmdSetRunning(requestedRun);
            if (Input.GetMouseButtonDown(0))
            {
                if ((EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) || TOP.UI.Pko.PkoUi.PointerOverWindow() || TOP.Admin.AdminPanel.BlocksMouse)
                    return;

                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

                if (showDebugRay)
                    Debug.DrawRay(ray.origin, ray.direction * raycastDistance, Color.red, 1f);

                if (Physics.Raycast(ray, out RaycastHit hit, raycastDistance))
                {
                    // PRIORIDADE 1: Verifica se clicou em um enemy
                    EnemyStats enemyStats = hit.collider.GetComponent<EnemyStats>();
                    if (enemyStats == null)
                        enemyStats = hit.collider.GetComponentInParent<EnemyStats>();

                    NetworkIdentity enemyIdentity = hit.collider.GetComponent<NetworkIdentity>();
                    if (enemyIdentity == null)
                        enemyIdentity = hit.collider.GetComponentInParent<NetworkIdentity>();

                    if (enemyStats != null && enemyIdentity != null && !enemyStats.IsDead)
                    {
                        Debug.Log($"[PlayerMovement] Mob clicado: {hit.collider.name} (netId={enemyIdentity.netId})");

                        var controller = PlayerControllerRef;
                        if (controller != null)
                        {
                            controller.CmdSetTarget(enemyIdentity);
                            controller.CmdAttackTarget(enemyIdentity, 0);

                        }
                        else
                        {
                            Debug.LogError("[PlayerMovement] PlayerController e NULL!");
                        }
                        return;
                    }

                    // PRIORIDADE 2: Clicou em um NPC interativo (vendedor, ferreiro, etc.)
                    IInteractable interactable = hit.collider.GetComponent<IInteractable>();
                    if (interactable == null)
                        interactable = hit.collider.GetComponentInParent<IInteractable>();

                    NetworkIdentity npcIdentity = hit.collider.GetComponent<NetworkIdentity>();
                    if (npcIdentity == null)
                        npcIdentity = hit.collider.GetComponentInParent<NetworkIdentity>();

                    if (interactable != null && npcIdentity != null)
                    {
                        CmdInteractWithNpc(npcIdentity);
                        return;
                    }

                    // PRIORIDADE 3: Clicou no chao → move e para o ataque atual
                    Debug.Log($"[PlayerMovement] Movendo para: {hit.point}");

                    var ctrl = PlayerControllerRef;
                    if (ctrl != null && ctrl.Combat != null)
                    {
                        // Stopped by the movement command on the server.
                    }

                    CmdMoveTo(hit.point);
                }
            }
        }

        [Command]
        public void CmdMoveTo(Vector3 destination)
        {
            GetComponent<PlayerCombat>()?.StopAttack();
            SetDestination(destination);
        }

        [Command]
        public void CmdInteractWithNpc(NetworkIdentity npcIdentity)
        {
            if (npcIdentity == null) return;
            IInteractable interactable = npcIdentity.GetComponent<IInteractable>();
            if (interactable == null) return;
            if (Vector3.Distance(transform.position, npcIdentity.transform.position) > interactable.InteractionRange + 1f) return;
            interactable.Interact(netId);
        }

        [Server]
        public void SetDestination(Vector3 destination)
        {
            if (PlayerControllerRef != null && PlayerControllerRef.CurrentHp <= 0) return;
            if (float.IsNaN(destination.x) || float.IsNaN(destination.z) || float.IsInfinity(destination.x) || float.IsInfinity(destination.z)) return;
            _targetPosition = destination;
            _isMoving = true;
            Debug.Log($"[PlayerMovement] Destino definido: {destination}");
        }

        [Server]
        void UpdateMovementState()
        {
            if (PlayerControllerRef != null && PlayerControllerRef.CurrentHp <= 0) _isMoving = false;

            if (_isMoving)
            {
                Vector3 delta = _targetPosition - transform.position;
                delta.y = 0;
                Vector3 direction = delta.normalized;
                float distance = delta.magnitude;

                if (distance > stoppingDistance)
                {
                    // Ground check — mantém o player no chão
                    if (Physics.Raycast(transform.position + direction * 0.5f + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 12f, LayerMask.GetMask("Ground", "Terrain"), QueryTriggerInteraction.Ignore))
                    {
                        Vector3 movePos = transform.position + direction * Mathf.Min(distance, (running ? runSpeed : walkSpeed) * MountSpeedMultiplier() * Time.deltaTime);
                        Vector3 cur = transform.position;
                        if (WorldBlockGrid.IsBlocked(movePos))
                        {
                            // desliza ao longo do eixo livre; senão para
                            var ax = new Vector3(movePos.x, cur.y, cur.z);
                            var az = new Vector3(cur.x, cur.y, movePos.z);
                            if (!WorldBlockGrid.IsBlocked(ax)) movePos = ax;
                            else if (!WorldBlockGrid.IsBlocked(az)) movePos = az;
                            else { _isMoving = false; movePos = cur; }
                        }
                        movePos.y = hit.point.y; // Mantém no chao
                        transform.position = movePos;

                        // Rotação suave
                        if (direction != Vector3.zero)
                        {
                            Quaternion targetRotation = Quaternion.LookRotation(direction);
                            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                        }
                    }
                }
                else
                {
                    _isMoving = false;
                }
            }
            else if (Physics.Raycast(transform.position + Vector3.up * 5f, Vector3.down, out RaycastHit idleHit, 20f, LayerMask.GetMask("Ground", "Terrain"), QueryTriggerInteraction.Ignore) && Mathf.Abs(idleHit.point.y - transform.position.y) > 0.01f)
            {
                var snapped = transform.position; snapped.y = idleHit.point.y; transform.position = snapped;
            }

            // Eventos de movimento
            if (_wasMoving != _isMoving)
            {
                if (_isMoving)
                {
                    OnMovementStarted?.Invoke();
                    _animation?.RpcSetMoving(true);
                }
                else
                {
                    OnMovementStopped?.Invoke();
                    _animation?.RpcSetMoving(false);
                }
            }
            _wasMoving = _isMoving;
        }

        public bool IsMoving => _isMoving;
        public Vector3 TargetPosition => _targetPosition;

        [Server]
        public void Stop()
        {
            _isMoving = false;
            _targetPosition = transform.position;
        }
    }
}
