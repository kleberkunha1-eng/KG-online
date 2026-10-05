using UnityEngine;
using TOP.Player;
using Mirror;

namespace TOP.Systems
{
    public class CameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0, 12, -8);
        [SerializeField] private Vector3 lookAtOffset = new Vector3(0, 1.5f, 0);

        [Header("Movement")]
        [SerializeField] private float followSpeed = 5f;
        [SerializeField] private float rotationSpeed = 3f;
        [SerializeField] private float zoomSpeed = 5f;

        [Header("Zoom")]
        [SerializeField] private float minZoom = 5f;
        [SerializeField] private float maxZoom = 20f;
        [SerializeField] private float defaultZoom = 12f;

        [Header("Rotation")]
        [SerializeField] private bool allowRotation = true;
        [SerializeField] private float minAngle = 10f;
        [SerializeField] private float maxAngle = 80f;

        [Header("Collision")]
        [SerializeField] private bool avoidCollision = true;
        [SerializeField] private LayerMask collisionLayers = ~0;
        [SerializeField] private float collisionRadius = 0.3f;

        private float currentZoom;
        private float currentRotationX;
        private float currentRotationY;
        private Vector3 currentVelocity;
        private float _findPlayerTimer = 0f;
        private const float FIND_PLAYER_INTERVAL = 0.5f;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        void Start()
        {
            currentZoom = defaultZoom;
            currentRotationY = 45f;
            currentRotationX = Mathf.Lerp(minAngle, maxAngle, 0.35f);

            // Verifica se CameraFollow existe — se sim, desabilita este script
            if (GetComponent<CameraFollow>() != null)
            {
                Debug.Log("[CameraController] CameraFollow detectado — desabilitando CameraController (usando CameraFollow).");
                enabled = false;
                return;
            }

            if (target == null)
                FindLocalPlayer();
        }

        void LateUpdate()
        {
            if (target == null)
            {
                _findPlayerTimer += Time.deltaTime;
                if (_findPlayerTimer >= FIND_PLAYER_INTERVAL)
                {
                    _findPlayerTimer = 0f;
                    FindLocalPlayer();
                }
                return;
            }

            HandleInput();
            UpdatePosition();
        }

        private void FindLocalPlayer()
        {
            // Busca por PlayerController PRIMEIRO (mais confiavel)
            var players = UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
            foreach (var player in players)
            {
                if (player.isLocalPlayer)
                {
                    target = player.transform;
                    Debug.Log($"[CameraController] Player encontrado via PlayerController: {player.CharacterName}");
                    return;
                }
            }

            // Fallback: busca por PlayerMovement
            var movements = UnityEngine.Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include);
            foreach (var movement in movements)
            {
                if (movement.isLocalPlayer)
                {
                    target = movement.transform;
                    Debug.Log("[CameraController] Player encontrado via PlayerMovement.");
                    return;
                }
            }

            // Ultimo fallback: qualquer NetworkBehaviour com isLocalPlayer
            var netBehaviours = UnityEngine.Object.FindObjectsByType<NetworkBehaviour>(FindObjectsInactive.Include);
            foreach (var nb in netBehaviours)
            {
                if (nb.isLocalPlayer)
                {
                    target = nb.transform;
                    Debug.Log($"[CameraController] Player encontrado via NetworkBehaviour: {nb.GetType().Name}");
                    return;
                }
            }
        }

        private void HandleInput()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                currentZoom -= scroll * zoomSpeed;
                currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
            }

            bool isOrbiting = allowRotation && Input.GetMouseButton(1);

            if (isOrbiting)
            {
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");
                currentRotationY += mouseX * rotationSpeed;
                currentRotationX -= mouseY * rotationSpeed;
                currentRotationX = Mathf.Clamp(currentRotationX, minAngle, maxAngle);
            }
        }

        private void UpdatePosition()
        {
            Vector3 pivotPosition = target.position + lookAtOffset;
            Quaternion rotation = Quaternion.Euler(currentRotationX, currentRotationY, 0);
            Vector3 desiredPosition = pivotPosition + rotation * new Vector3(0, 0, -currentZoom);
            desiredPosition += offset;

            if (avoidCollision)
            {
                Vector3 direction = desiredPosition - pivotPosition;
                float distance = direction.magnitude;

                RaycastHit[] hits = Physics.SphereCastAll(
                    pivotPosition,
                    collisionRadius,
                    direction.normalized,
                    distance,
                    collisionLayers,
                    QueryTriggerInteraction.Ignore);

                if (hits.Length > 0)
                {
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                    foreach (RaycastHit hit in hits)
                    {
                        if (target != null && hit.transform.IsChildOf(target))
                            continue;

                        desiredPosition = hit.point + hit.normal * collisionRadius;
                        break;
                    }
                }
            }

            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition,
                ref currentVelocity, 1f / followSpeed);

            transform.LookAt(pivotPosition);
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        public void ResetCamera()
        {
            currentZoom = defaultZoom;
            currentRotationX = Mathf.Lerp(minAngle, maxAngle, 0.35f);
            currentRotationY = 45f;
        }

        /// <summary>
        /// Aplica um preset de camera usado pela janela de configuracoes (frmGame -> Camera Top1/Top2).
        /// Top1 (0) = visao padrao em terceira pessoa. Top2 (1) = visao mais alta/top-down.
        /// </summary>
        public void SetPreset(int mode)
        {
            if (mode == 1)
            {
                currentZoom = Mathf.Clamp(defaultZoom * 1.3f, minZoom, maxZoom);
                currentRotationX = Mathf.Lerp(minAngle, maxAngle, 0.75f);
            }
            else
            {
                currentZoom = defaultZoom;
                currentRotationX = Mathf.Lerp(minAngle, maxAngle, 0.35f);
            }
        }

        public void Shake(float duration, float magnitude)
        {
            StartCoroutine(ShakeCoroutine(duration, magnitude));
        }

        private System.Collections.IEnumerator ShakeCoroutine(float duration, float magnitude)
        {
            Vector3 originalPosition = transform.localPosition;
            float elapsed = 0;

            while (elapsed < duration)
            {
                float x = Random.Range(-1f, 1f) * magnitude;
                float y = Random.Range(-1f, 1f) * magnitude;

                transform.localPosition = originalPosition + new Vector3(x, y, 0);

                elapsed += Time.deltaTime;
                yield return null;
            }

            transform.localPosition = originalPosition;
        }
    }
}