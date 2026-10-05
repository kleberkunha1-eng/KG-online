using UnityEngine;
using UnityEngine.UI;

public class ScreenHitFlash : MonoBehaviour
{
    public static ScreenHitFlash Instance { get; private set; }

    [SerializeField] private Color flashColor = new Color(0.9f, 0.1f, 0.1f, 0.35f);
    [SerializeField] private float fadeSpeed = 3.5f;

    private Canvas canvas;
    private CanvasScaler scaler;
    private GraphicRaycaster raycaster;
    private Image overlayImage;
    private float targetAlpha;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // Scene UI follows the lifetime of its canvas.

        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        raycaster = gameObject.AddComponent<GraphicRaycaster>();
        raycaster.enabled = false;

        GameObject overlay = new GameObject("HitFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlay.transform.SetParent(transform, false);

        RectTransform rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        overlayImage = overlay.GetComponent<Image>();
        overlayImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
        overlayImage.raycastTarget = false;
    }

    private void Update()
    {
        if (overlayImage == null)
            return;

        Color color = overlayImage.color;
        color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
        overlayImage.color = color;

        if (Mathf.Approximately(color.a, targetAlpha) && targetAlpha > 0f)
            targetAlpha = 0f;
    }

    public static void EnsureExists()
    {
        if (Instance != null)
            return;

        GameObject root = new GameObject("ScreenHitFlash");
        root.AddComponent<ScreenHitFlash>();
    }

    public void Flash(float intensity = 1f)
    {
        if (overlayImage == null)
            return;

        targetAlpha = Mathf.Clamp01(flashColor.a * intensity);

        Color color = overlayImage.color;
        color.r = flashColor.r;
        color.g = flashColor.g;
        color.b = flashColor.b;
        overlayImage.color = color;
    }
}
