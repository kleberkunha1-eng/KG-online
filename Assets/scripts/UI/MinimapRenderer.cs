using UnityEngine;
using UnityEngine.UI;
using Mirror;
namespace TOP.UI
{
    public class MinimapRenderer : MonoBehaviour
    {
        [SerializeField] Camera minimapCamera;
        [SerializeField] RawImage minimapDisplay;
        [SerializeField] RenderTexture minimapTexture;
        public RenderTexture Texture => minimapTexture;
        [SerializeField] float mapScale = 35f, mapHeight = 100f;
        [SerializeField] bool followPlayerRotation;
        [SerializeField] Image playerIndicator;
        bool ownsTexture, ownsCamera;
        void Start()
        {
            if (minimapTexture == null) { minimapTexture = new RenderTexture(256,256,24); minimapTexture.Create(); ownsTexture = true; }
            if (minimapCamera == null) { minimapCamera = new GameObject("MinimapCamera").AddComponent<Camera>(); ownsCamera = true; }
            minimapCamera.orthographic = true;
            minimapCamera.orthographicSize = mapScale;
            minimapCamera.nearClipPlane = .1f; minimapCamera.farClipPlane = 250f;
            minimapCamera.cullingMask = ~(1 << LayerMask.NameToLayer("UI"));
            minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            minimapCamera.backgroundColor = new Color(.08f,.22f,.3f);
            minimapCamera.targetTexture = minimapTexture;
            if (minimapDisplay != null) minimapDisplay.texture = minimapTexture;
        }
        void LateUpdate()
        {
            if (NetworkClient.localPlayer == null || minimapCamera == null) return;
            var target = NetworkClient.localPlayer.transform;
            minimapCamera.transform.SetPositionAndRotation(target.position + Vector3.up * mapHeight,
                Quaternion.Euler(90f, followPlayerRotation ? target.eulerAngles.y : 0f, 0f));
            if (playerIndicator != null) playerIndicator.rectTransform.localEulerAngles = new Vector3(0,0,followPlayerRotation ? 0 : -target.eulerAngles.y);
        }
        public void SetMinimapVisible(bool visible) { if(minimapDisplay != null) minimapDisplay.enabled = visible; if(minimapCamera != null) minimapCamera.enabled = visible; }
        public void SetMinimapZoom(float zoom) { if(minimapCamera != null) minimapCamera.orthographicSize = mapScale / Mathf.Clamp(zoom,.25f,8f); }
        public void SetMinimapHeight(float height) { mapHeight = Mathf.Max(10,height); }
        void OnDestroy() { if(ownsCamera && minimapCamera != null) Destroy(minimapCamera.gameObject); if(ownsTexture && minimapTexture != null) { minimapTexture.Release(); Destroy(minimapTexture); } }
    }
}
