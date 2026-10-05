using System;
using System.IO;
using UnityEngine;

namespace TOP.Character
{
    // Rotacao/posicao do modelo da arma dentro do dummy da mao, por raca (0-3) e mao (0 direita, 1 esquerda).
    // Editavel em tempo real pelo WeaponTuner (F9); "Salvar" grava o JSON que e carregado no proximo inicio.
    public static class PkoWeaponPose
    {
        public const int Races = 4;
        public static readonly Vector3[] Euler = new Vector3[Races * 2];
        public static readonly Vector3[] Pos = new Vector3[Races * 2];

        [Serializable] class Store { public float[] e = new float[Races * 6]; public float[] p = new float[Races * 6]; }

        static string UserPath => Path.Combine(Application.persistentDataPath, "WeaponPose.json");
        static string ProjectPath => Path.Combine(Application.dataPath, "Resources", "PkoChar", "WeaponPose.json");

        static PkoWeaponPose() { ResetAll(); Load(); }

        public static int Key(int race, int hand) => Mathf.Clamp(race, 0, Races - 1) * 2 + hand;

        public static void ResetAll()
        {
            for (int i = 0; i < Euler.Length; i++) { Euler[i] = new Vector3(-90f, 0f, 0f); Pos[i] = Vector3.zero; }
        }

        static void Load()
        {
            string json = null;
            try
            {
                if (File.Exists(UserPath)) json = File.ReadAllText(UserPath);
                else { var ta = Resources.Load<TextAsset>("PkoChar/WeaponPose"); if (ta != null) json = ta.text; }
                if (string.IsNullOrEmpty(json)) return;
                var s = JsonUtility.FromJson<Store>(json);
                for (int i = 0; i < Euler.Length; i++)
                {
                    Euler[i] = new Vector3(s.e[i * 3], s.e[i * 3 + 1], s.e[i * 3 + 2]);
                    Pos[i] = new Vector3(s.p[i * 3], s.p[i * 3 + 1], s.p[i * 3 + 2]);
                }
            }
            catch (Exception ex) { Debug.LogWarning("[PkoWeaponPose] " + ex.Message); }
        }

        public static string ToText()
        {
            var sb = new System.Text.StringBuilder();
            string[] races = { "Lance", "Carsise", "Phyllis", "Ami" };
            for (int r = 0; r < Races; r++)
                for (int h = 0; h < 2; h++)
                {
                    var e = Euler[r * 2 + h]; var p = Pos[r * 2 + h];
                    sb.AppendLine(races[r] + (h == 0 ? " direita" : " esquerda") + ": rot(" + e.x.ToString("F1") + ", " + e.y.ToString("F1") + ", " + e.z.ToString("F1") + ") pos(" + p.x.ToString("F3") + ", " + p.y.ToString("F3") + ", " + p.z.ToString("F3") + ")");
                }
            return sb.ToString();
        }

        public static string Save()
        {
            var s = new Store();
            for (int i = 0; i < Euler.Length; i++)
            {
                s.e[i * 3] = Euler[i].x; s.e[i * 3 + 1] = Euler[i].y; s.e[i * 3 + 2] = Euler[i].z;
                s.p[i * 3] = Pos[i].x; s.p[i * 3 + 1] = Pos[i].y; s.p[i * 3 + 2] = Pos[i].z;
            }
            string json = JsonUtility.ToJson(s, true);
            try
            {
                File.WriteAllText(UserPath, json);
#if UNITY_EDITOR
                Directory.CreateDirectory(Path.GetDirectoryName(ProjectPath));
                File.WriteAllText(ProjectPath, json);
                return "Salvo em " + ProjectPath;
#else
                return "Salvo em " + UserPath;
#endif
            }
            catch (Exception ex) { return "Erro ao salvar: " + ex.Message; }
        }
    }
}