using UnityEngine;
using UnityEngine.UI;

namespace TOP.UI.Pko
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class RawImageCover : MonoBehaviour
    {
        RawImage image;
        RectTransform rectTransform;
        Texture lastTexture;
        Vector2 lastSize = new Vector2(-1f, -1f);

        void Awake()
        {
            CacheComponents();
            Refresh();
        }

        void LateUpdate()
        {
            if (image == null) CacheComponents();
            if (image == null || rectTransform == null) return;

            var size = rectTransform.rect.size;
            if (image.texture != lastTexture || size != lastSize)
                Refresh();
        }

        void OnRectTransformDimensionsChange()
        {
            Refresh();
        }

        public void Refresh()
        {
            CacheComponents();
            if (image == null || rectTransform == null) return;

            var texture = image.texture;
            var size = rectTransform.rect.size;
            lastTexture = texture;
            lastSize = size;

            if (texture == null || texture.width <= 0 || texture.height <= 0 ||
                size.x <= 0f || size.y <= 0f)
            {
                image.uvRect = new Rect(0f, 0f, 1f, 1f);
                return;
            }

            float viewportAspect = size.x / size.y;
            float textureAspect = (float)texture.width / texture.height;

            if (viewportAspect > textureAspect)
            {
                float visibleHeight = textureAspect / viewportAspect;
                image.uvRect = new Rect(0f, (1f - visibleHeight) * 0.5f, 1f, visibleHeight);
            }
            else
            {
                float visibleWidth = viewportAspect / textureAspect;
                image.uvRect = new Rect((1f - visibleWidth) * 0.5f, 0f, visibleWidth, 1f);
            }
        }

        void CacheComponents()
        {
            if (image == null) image = GetComponent<RawImage>();
            if (rectTransform == null) rectTransform = transform as RectTransform;
        }
    }
}
