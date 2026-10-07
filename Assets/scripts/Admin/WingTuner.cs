using System;
using System.Globalization;
using Mirror;
using TOP.Character;
using UnityEngine;

namespace TOP.Admin
{
    public class WingTuner : MonoBehaviour
    {
        static WingTuner instance;
        public static bool BlocksMouse => instance != null && instance.open
            && instance.win.Contains(new Vector2(Input.mousePosition.x,
                Screen.height - Input.mousePosition.y));
        bool open;
        Rect win = new Rect(20, 80, 410, 490);
        readonly string[] fields = new string[7];
        string status = "";
        int currentRace = -1, currentItem = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("WingTuner");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WingTuner>();
        }

        PkoCharacterVisual Visual()
        {
            return NetworkClient.localPlayer != null
                ? NetworkClient.localPlayer.GetComponentInChildren<PkoCharacterVisual>() : null;
        }

        void Update()
        {
            var visual = Visual();
            if (visual == null) { open = false; return; }
            if (Input.GetKeyDown(KeyCode.F8)) open = !open;
        }

        void OnGUI()
        {
            if (!open) return;
            var visual = Visual();
            if (visual == null) return;
            win.width = Mathf.Min(410, Screen.width);
            win.x = Mathf.Clamp(win.x, 0, Mathf.Max(0, Screen.width - win.width));
            win.y = Mathf.Clamp(win.y, 0, Mathf.Max(0, Screen.height - win.height));
            win = TOP.UI.GameWindowControls.Window(94002, win, _ => Draw(visual), "Ajustar asas nas costas (F8)", () => open = false);
        }

        void Draw(PkoCharacterVisual visual)
        {
            if (visual.WingItemId == 0)
            {
                GUILayout.Label("Equipe uma asa para ajustar.");
                if (GUILayout.Button("Fechar")) open = false;
                GUI.DragWindow(new Rect(0, 0, 10000, 22));
                return;
            }
            var entry = PkoWingPose.Get(visual.Race, visual.WingItemId);
            if (currentRace != visual.Race || currentItem != visual.WingItemId)
            {
                currentRace = visual.Race; currentItem = visual.WingItemId;
                SyncFields(entry);
            }
            GUILayout.Label($"Raca: {visual.Race} | Asa: {visual.WingItemId}");
            GUILayout.Label("Posicao proporcional a altura do personagem.");
            GUILayout.Label("X: lateral | Y: altura | +Z: costas / -Z: frente");
            entry.position = new Vector3(Row(0, "Pos X", entry.position.x, .01f),
                Row(1, "Pos Y", entry.position.y, .01f), Row(2, "Pos Z", entry.position.z, .01f));
            GUILayout.Label("Rotacao em graus");
            entry.euler = new Vector3(Row(3, "Rot X", entry.euler.x, .01f),
                Row(4, "Rot Y", entry.euler.y, .01f), Row(5, "Rot Z", entry.euler.z, .01f));
            GUILayout.Label($"Tamanho: {entry.scale * 100f:F0}% (1 = tamanho base)");
            float size = Row(6, "Escala", entry.scale, .05f);
            entry.scale = Mathf.Clamp(size, PkoWingPose.MinScale, PkoWingPose.MaxScale);
            if (size != entry.scale)
            {
                fields[6] = entry.scale.ToString("0.###", CultureInfo.CurrentCulture);
                status = "Escala permitida: 0,1 a 150000 (150 mil).";
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Salvar"))
            {
                try { status = "Salvo: " + PkoWingPose.Save(); }
                catch (Exception e) { status = "Erro ao salvar: " + e.Message; Debug.LogException(e); }
            }
            if (GUILayout.Button("Copiar dados"))
            {
                GUIUtility.systemCopyBuffer = PkoWingPose.ToJson();
                status = "Dados copiados. Cole na conversa.";
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restaurar esta asa"))
            {
                entry.position = Vector3.zero; entry.euler = Vector3.zero; entry.scale = 1f; SyncFields(entry);
            }
            if (GUILayout.Button("Fechar")) open = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Ajustes locais por raca e asa; nao alteram o servidor.");
            GUILayout.Label(status);
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        void SyncFields(PkoWingPose.Entry entry)
        {
            for (int i = 0; i < 6; i++)
                fields[i] = (i < 3 ? entry.position[i] : entry.euler[i - 3])
                    .ToString("0.###", CultureInfo.CurrentCulture);
            fields[6] = entry.scale.ToString("0.###", CultureInfo.CurrentCulture);
        }

        float Row(int index, string label, float value, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(55));
            bool changed = false;
            if (GUILayout.Button("-", GUILayout.Width(35))) { value -= step; changed = true; }
            fields[index] = GUILayout.TextField(fields[index], GUILayout.Width(100));
            if (GUILayout.Button("+", GUILayout.Width(35))) { value += step; changed = true; }
            if (changed) fields[index] = value.ToString("0.###", CultureInfo.CurrentCulture);
            if (float.TryParse(fields[index].Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed) && float.IsFinite(parsed)) value = parsed;
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
