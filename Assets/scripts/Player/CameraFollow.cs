using UnityEngine;
using Mirror;
using TOP.Systems;

namespace TOP.Player
{
    public class CameraFollow : NetworkBehaviour
    {
        [Header("Seguimento")]
        public Vector3 offset = new Vector3(0f, 12f, -8f);
        public Vector3 lookAtOffset = new Vector3(0f, 2.2f, 0f);

        [Header("Configuracoes de Zoom")]
        public float zoomSpeed = 5f;
        public float minZoom = 5f;
        public float maxZoom = 20f;

        [Header("Rotacao 3D")]
        public bool allowRotation = true;
        public float rotationSpeed = 3f;
        public float minVerticalAngle = -35f;
        public float maxVerticalAngle = 75f;

        [Header("Limite do Chao")]
        public LayerMask groundLayers = ~0;
        public float groundClearance = 0.5f;
        public float groundCheckHeight = 50f;

        public static bool FreeCam; // camera livre (WeaponTuner) assume o controle
        private Transform camTransform;
        private float currentZoom;
        private float currentYaw = 45f;
        private float currentPitch = 35f;

        public override void OnStartLocalPlayer()
        {
            // ✅ CORRIGIDO: Apenas isLocalPlayer (isOwned pode não existir)
            if (!isLocalPlayer) return;

            base.OnStartLocalPlayer();
            currentZoom = Mathf.Clamp(offset.magnitude, minZoom, maxZoom);
            currentPitch = Mathf.Clamp(22f, minVerticalAngle, maxVerticalAngle);
        }

        void LateUpdate()
        {
            // ✅ CORRIGIDO: Apenas isLocalPlayer
            if (!isLocalPlayer) return;

            if (camTransform == null)
            {
                if (Camera.main == null) return;

                camTransform = Camera.main.transform;
                camTransform.SetParent(null);

                CameraController controller = camTransform.GetComponent<CameraController>();
                if (controller != null)
                    controller.enabled = false;
            }

            if (FreeCam) return;

            bool blocked = TOP.Admin.AdminPanel.BlocksMouse;
            float scrollInput = blocked ? 0 : Input.GetAxis("Mouse ScrollWheel");
            if (scrollInput != 0f)
            {
                currentZoom -= scrollInput * zoomSpeed;
                currentZoom = Mathf.Clamp(currentZoom, minZoom, maxZoom);
            }

            if (!blocked && allowRotation && Input.GetMouseButton(1))
            {
                currentYaw += Input.GetAxis("Mouse X") * rotationSpeed;
                currentPitch -= Input.GetAxis("Mouse Y") * rotationSpeed;
                currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);
            }

            Vector3 pivotPosition = transform.position + lookAtOffset;
            Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            Vector3 orbitOffset = rotation * new Vector3(0f, 0f, -currentZoom);
            Vector3 desiredPosition = pivotPosition + orbitOffset;

            desiredPosition = ProtectGround(pivotPosition, desiredPosition, groundLayers,
                groundClearance, groundCheckHeight);
            camTransform.position = desiredPosition;
            camTransform.LookAt(pivotPosition);
        }

        public static Vector3 ProtectGround(Vector3 pivotPosition, Vector3 desiredPosition,
            LayerMask layers, float clearance, float checkHeight)
        {
            Vector3 travel = desiredPosition - pivotPosition;
            float radius = Mathf.Max(.35f, clearance);
            if (travel.sqrMagnitude > .001f && Physics.SphereCast(pivotPosition, radius,
                travel.normalized, out var obstruction, travel.magnitude, layers, QueryTriggerInteraction.Ignore))
                desiredPosition = pivotPosition + travel.normalized * Mathf.Max(0, obstruction.distance - .05f);
            Vector3 rayOrigin = desiredPosition + Vector3.up * checkHeight;
            float rayDistance = checkHeight * 2f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHit, rayDistance, layers, QueryTriggerInteraction.Ignore))
            {
                float minAllowedY = groundHit.point.y + radius;
                if (desiredPosition.y < minAllowedY)
                    desiredPosition.y = minAllowedY;
            }

            return desiredPosition;
        }
    }
}