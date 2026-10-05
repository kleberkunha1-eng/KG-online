using UnityEngine;
using Mirror;
using TOP.Character;
using TOP.Player;

namespace TOP.Admin
{
    // F9: ajuste em tempo real da rotacao/posicao das armas por raca e mao + camera livre
    // (botao direito: olhar, WASD: mover, Q/E: descer/subir, Shift: rapido, scroll: avancar).
    public class WeaponTuner : MonoBehaviour
    {
        public static bool Blocks { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { var go = new GameObject("WeaponTuner"); DontDestroyOnLoad(go); go.AddComponent<WeaponTuner>(); }

        bool open, freeCam;
        Rect win = new Rect(20, 20, 420, 560);
        int hand;
        string status = "";
        float yaw, pitch;
        readonly string[] fields = new string[6];
        int fieldKey = -1;

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        PkoCharacterVisual Visual()
        {
            var lp = NetworkClient.localPlayer;
            return lp != null ? lp.GetComponentInChildren<PkoCharacterVisual>() : null;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9) && Visual() != null) { open = !open; if (!open) SetFree(false); }
            Blocks = false;
            if (!open) return;
            float s = Scale; var m = Input.mousePosition;
            Blocks = win.Contains(new Vector2(m.x / s, (Screen.height - m.y) / s));
        }

        void SetFree(bool on)
        {
            freeCam = on; CameraFollow.FreeCam = on;
            if (on && Camera.main != null) { var e = Camera.main.transform.eulerAngles; yaw = e.y; pitch = e.x > 180 ? e.x - 360 : e.x; }
        }

        void LateUpdate()
        {
            if (!open || !freeCam || Camera.main == null) return;
            var t = Camera.main.transform;
            if (Input.GetMouseButton(1) && !Blocks)
            {
                yaw += Input.GetAxis("Mouse X") * 3f; pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, -89f, 89f);
            }
            t.rotation = Quaternion.Euler(pitch, yaw, 0f);
            float sp = (Input.GetKey(KeyCode.LeftShift) ? 12f : 3f) * Time.unscaledDeltaTime;
            Vector3 d = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) d += t.forward;
            if (Input.GetKey(KeyCode.S)) d -= t.forward;
            if (Input.GetKey(KeyCode.D)) d += t.right;
            if (Input.GetKey(KeyCode.A)) d -= t.right;
            if (Input.GetKey(KeyCode.E)) d += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) d -= Vector3.up;
            d += t.forward * (Input.GetAxis("Mouse ScrollWheel") * 40f);
            t.position += d * sp;
        }

        void Focus(PkoCharacterVisual v)
        {
            if (Camera.main == null) return;
            var c = v.transform.position + Vector3.up * 1.0f;
            var t = Camera.main.transform;
            t.position = c + v.transform.forward * 2.2f + Vector3.up * 0.3f;
            t.LookAt(c);
            yaw = t.eulerAngles.y; pitch = t.eulerAngles.x > 180 ? t.eulerAngles.x - 360 : t.eulerAngles.x;
        }

        void OnGUI()
        {
            if (!open) return;
            var v = Visual(); if (v == null) return;
            float s = Scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            win = GUI.Window(94001, win, id => Draw(id, v), "Ajuste de armas (F9)");
        }

        void Draw(int id, PkoCharacterVisual v)
        {
            int k = PkoWeaponPose.Key(v.Race, hand);
            string[] races = { "Lance", "Carsise", "Phyllis", "Ami" };
            GUILayout.Label("Raca: " + races[Mathf.Clamp(v.Race, 0, 3)] + "  (valores sao por raca e por mao)");

            bool free = GUILayout.Toggle(freeCam, " Camera livre (botao direito + WASD/QE, Shift rapido)");
            if (free != freeCam) { SetFree(free); if (free) Focus(v); }
            if (GUILayout.Button("Focar no personagem")) { if (!freeCam) SetFree(true); Focus(v); }

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(hand == 0, "Mao direita", "Button")) hand = 0;
            if (GUILayout.Toggle(hand == 1, "Mao esquerda", "Button")) hand = 1;
            GUILayout.EndHorizontal();
            k = PkoWeaponPose.Key(v.Race, hand);

            var e = PkoWeaponPose.Euler[k]; var p = PkoWeaponPose.Pos[k];
            GUILayout.Label("Rotacao");
            e.x = Row("Rot X", k * 10 + 0, e.x, -180f, 180f);
            e.y = Row("Rot Y", k * 10 + 1, e.y, -180f, 180f);
            e.z = Row("Rot Z", k * 10 + 2, e.z, -180f, 180f);
            GUILayout.Label("Posicao");
            p.x = Row("Pos X", k * 10 + 3, p.x, -0.5f, 0.5f);
            p.y = Row("Pos Y", k * 10 + 4, p.y, -0.5f, 0.5f);
            p.z = Row("Pos Z", k * 10 + 5, p.z, -0.5f, 0.5f);
            PkoWeaponPose.Euler[k] = e; PkoWeaponPose.Pos[k] = p;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Girar 90 X")) PkoWeaponPose.Euler[k] = new Vector3(e.x + 90f, e.y, e.z);
            if (GUILayout.Button("Girar 90 Y")) PkoWeaponPose.Euler[k] = new Vector3(e.x, e.y + 90f, e.z);
            if (GUILayout.Button("Girar 90 Z")) PkoWeaponPose.Euler[k] = new Vector3(e.x, e.y, e.z + 90f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Direita -> esquerda")) { int r = PkoWeaponPose.Key(v.Race, 0), l = PkoWeaponPose.Key(v.Race, 1); PkoWeaponPose.Euler[l] = PkoWeaponPose.Euler[r]; PkoWeaponPose.Pos[l] = PkoWeaponPose.Pos[r]; }
            if (GUILayout.Button("Aplicar a todas as racas"))
                for (int r = 0; r < PkoWeaponPose.Races; r++) { int kk = PkoWeaponPose.Key(r, hand); PkoWeaponPose.Euler[kk] = PkoWeaponPose.Euler[k]; PkoWeaponPose.Pos[kk] = PkoWeaponPose.Pos[k]; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Espera")) v.Pose?.Loop(PkoPoses.Wait);
            if (GUILayout.Button("Ataque")) v.Pose?.Once(PkoPoses.Attack1);
            if (GUILayout.Button("Guarda")) v.Pose?.Loop(PkoPoses.Guard);
            if (GUILayout.Button("Correr")) v.Pose?.Loop(PkoPoses.Run);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Salvar")) status = PkoWeaponPose.Save();
            if (GUILayout.Button("Copiar valores")) { GUIUtility.systemCopyBuffer = PkoWeaponPose.ToText(); Debug.Log("[WeaponPose]\n" + PkoWeaponPose.ToText()); status = "Copiado e logado no Console"; }
            if (GUILayout.Button("Resetar")) { PkoWeaponPose.ResetAll(); fieldKey = -1; }
            GUILayout.EndHorizontal();
            GUILayout.Label(status);
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        float Row(string label, int key, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(50));
            float nv = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(210));
            string txt = (key == fieldKey && GUI.GetNameOfFocusedControl() == "f" + key) ? fields[key % 10] : value.ToString("F3");
            GUI.SetNextControlName("f" + key);
            string nt = GUILayout.TextField(txt, GUILayout.Width(70));
            if (GUI.GetNameOfFocusedControl() == "f" + key)
            {
                fieldKey = key; fields[key % 10] = nt;
                if (float.TryParse(nt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f)) nv = f;
            }
            else if (key == fieldKey) fieldKey = -1;
            GUILayout.EndHorizontal();
            return nv;
        }
    }
}