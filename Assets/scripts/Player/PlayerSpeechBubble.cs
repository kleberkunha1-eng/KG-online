using TOP.Character;
using TOP.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace TOP.Player
{
    public class PlayerSpeechBubble : MonoBehaviour
    {
        Text label;
        GameObject bubble;
        float expires;

        public static void Show(GameObject player, string text)
        {
            var component = player.GetComponent<PlayerSpeechBubble>();
            if (component == null) component = player.AddComponent<PlayerSpeechBubble>();
            component.Display(text);
        }

        public void Display(string text)
        {
            if (bubble == null)
            {
                bubble = new GameObject("SpeechBubble", typeof(RectTransform), typeof(Canvas), typeof(Image), typeof(WorldLabelBillboard));
                bubble.transform.SetParent(transform, false);
                var rect = (RectTransform)bubble.transform;
                rect.sizeDelta = new Vector2(260f, 64f);
                var scale = transform.lossyScale;
                rect.localScale = new Vector3(.008f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                    .008f / Mathf.Max(.0001f, Mathf.Abs(scale.y)), .008f / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
                bubble.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
                var background = bubble.GetComponent<Image>();
                background.color = new Color(.04f, .06f, .1f, .85f);
                background.raycastTarget = false;
                var content = new GameObject("Text", typeof(RectTransform), typeof(Text));
                content.transform.SetParent(bubble.transform, false);
                label = content.GetComponent<Text>();
                label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                label.fontSize = 18;
                label.alignment = TextAnchor.MiddleCenter;
                label.supportRichText = false;
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10; label.resizeTextMaxSize = 18;
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(8f, 4f);
                label.rectTransform.offsetMax = new Vector2(-8f, -4f);
            }
            label.text = text;
            expires = Time.unscaledTime + Mathf.Clamp(text.Length * .08f, 4f, 10f);
            bubble.SetActive(true);
            PlaceAboveHead();
        }

        void PlaceAboveHead()
        {
            var visual = GetComponentInChildren<PkoCharacterVisual>();
            if (visual != null && visual.TryGetBodyBounds(out var bounds))
                bubble.transform.position = new Vector3(bounds.center.x, bounds.max.y + .5f, bounds.center.z);
            else bubble.transform.position = transform.position + Vector3.up * 2.2f;
        }

        void LateUpdate()
        {
            if (bubble == null || !bubble.activeSelf) return;
            if (Time.unscaledTime >= expires) { bubble.SetActive(false); return; }
            PlaceAboveHead();
        }
    }
}
