using System;
using System.Collections.Generic;
using UnityEngine;

namespace TOP.UI
{
    public static class GameWindowControls
    {
        sealed class WindowContext
        {
            public GUI.WindowFunction Body;
            public Action Close;
            public float Width;
            public readonly GUI.WindowFunction Callback;
            public WindowContext() { Callback = Draw; }
            void Draw(int id)
            {
                if (GUI.Button(new Rect(Width - 26, 2, 22, 18), "X")) Close();
                Body(id);
            }
        }

        static readonly Dictionary<int, WindowContext> contexts = new Dictionary<int, WindowContext>();
        static int blockedFrame = -2;
        static GUISkin panelSkin;
        static Texture2D background, surface, selected;
        public static bool BlocksMouse => blockedFrame >= Time.frameCount - 1 && blockedFrame <= Time.frameCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            contexts.Clear();
            blockedFrame = -2;
            if (panelSkin != null) UnityEngine.Object.Destroy(panelSkin);
            if (background != null) UnityEngine.Object.Destroy(background);
            if (surface != null) UnityEngine.Object.Destroy(surface);
            if (selected != null) UnityEngine.Object.Destroy(selected);
            panelSkin = null;
        }

        public static Rect Window(int id, Rect rect, GUI.WindowFunction draw, string title, Action close)
        {
            var inverse = GUI.matrix.inverse;
            var screen = inverse.MultiplyPoint3x4(new Vector3(Screen.width, Screen.height, 0));
            rect.width = Mathf.Min(rect.width, screen.x - 16);
            rect.height = Mathf.Min(rect.height, screen.y - 16);
            rect.x = Mathf.Clamp(rect.x, 8, Mathf.Max(8, screen.x - rect.width - 8));
            rect.y = Mathf.Clamp(rect.y, 8, Mathf.Max(8, screen.y - rect.height - 8));
            var mouse = new Vector3(Input.mousePosition.x, Screen.height - Input.mousePosition.y, 0);
            Vector3 localMouse = GUI.matrix.inverse.MultiplyPoint3x4(mouse);
            if (rect.Contains(new Vector2(localMouse.x, localMouse.y))) blockedFrame = Time.frameCount;
            if (!contexts.TryGetValue(id, out var context))
            {
                context = new WindowContext();
                contexts.Add(id, context);
            }
            context.Body = draw;
            context.Close = close;
            context.Width = rect.width;
            var previousSkin = GUI.skin;
            var previousColor = GUI.color;
            var previousBackground = GUI.backgroundColor;
            var previousContent = GUI.contentColor;
            EnsureSkin();
            GUI.skin = panelSkin;
            GUI.color = GUI.backgroundColor = GUI.contentColor = Color.white;
            try { return GUI.Window(id, rect, context.Callback, title); }
            finally
            {
                GUI.skin = previousSkin;
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackground;
                GUI.contentColor = previousContent;
            }
        }

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        static void EnsureSkin()
        {
            if (panelSkin != null) return;
            panelSkin = UnityEngine.Object.Instantiate(GUI.skin);
            panelSkin.hideFlags = HideFlags.HideAndDontSave;
            background = Solid(new Color(.075f, .095f, .13f, 1));
            surface = Solid(new Color(.15f, .19f, .25f, 1));
            selected = Solid(new Color(.18f, .38f, .55f, 1));
            foreach (var style in new[] { panelSkin.window, panelSkin.box, panelSkin.button,
                panelSkin.textField, panelSkin.textArea })
            {
                style.normal.background = style == panelSkin.window ? background : surface;
                style.hover.background = selected;
                style.active.background = selected;
                style.focused.background = surface;
                style.onNormal.background = style.onHover.background = style.onActive.background = selected;
                style.normal.textColor = style.hover.textColor = style.active.textColor = Color.white;
                style.focused.textColor = style.onNormal.textColor = style.onHover.textColor = style.onActive.textColor = Color.white;
                style.border = new RectOffset(0, 0, 0, 0);
                style.fontSize = 13;
            }
            panelSkin.window.onNormal.background = background;
            panelSkin.window.padding = new RectOffset(14, 14, 32, 12);
            panelSkin.window.alignment = TextAnchor.UpperCenter;
            panelSkin.button.padding = new RectOffset(8, 8, 5, 5);
            panelSkin.label.normal.textColor = new Color(.9f, .93f, .97f);
            panelSkin.label.fontSize = 13;
            panelSkin.label.wordWrap = true;
        }
    }
}
