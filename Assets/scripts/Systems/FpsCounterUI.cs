using UnityEngine;

namespace TOP.Systems
{
    /// <summary>
    /// Pequeno contador de FPS no canto da tela, ligado/desligado pela janela de
    /// configuracoes (frmGame -> FPS 30/60). O valor 30/60 escolhido define o cap de
    /// quadros por segundo (Application.targetFrameRate); o contador em si so mostra o FPS atual.
    /// </summary>
    public class FpsCounterUI : MonoBehaviour
    {
        static FpsCounterUI instance;
        float fps; float accum; int frames; GUIStyle style;

        public static void SetVisible(bool on)
        {
            if (on && instance == null)
            {
                var go = new GameObject("FpsCounterUI"); DontDestroyOnLoad(go);
                instance = go.AddComponent<FpsCounterUI>();
            }
            else if (!on && instance != null)
            {
                Destroy(instance.gameObject); instance = null;
            }
        }

        void Update()
        {
            accum += Time.unscaledDeltaTime; frames++;
            if (accum >= 0.5f) { fps = frames / accum; accum = 0; frames = 0; }
        }

        void OnGUI()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.yellow } };
            }
            GUI.Label(new Rect(8, 8, 120, 24), $"FPS: {fps:0}", style);
        }
    }
}
