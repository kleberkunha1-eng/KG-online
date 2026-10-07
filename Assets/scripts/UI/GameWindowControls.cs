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
        public static bool BlocksMouse => blockedFrame >= Time.frameCount - 1 && blockedFrame <= Time.frameCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            contexts.Clear();
            blockedFrame = -2;
        }

        public static Rect Window(int id, Rect rect, GUI.WindowFunction draw, string title, Action close)
        {
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
            return GUI.Window(id, rect, context.Callback, title);
        }

    }
}
