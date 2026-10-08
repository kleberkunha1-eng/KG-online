using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TOP.Data;
using TOP.Player;

namespace TOP.UI
{
    // Loja do Hairstylist (hairs.txt): lista os penteados/cores disponiveis para a raca do
    // jogador local e aplica a escolha via PlayerController.CmdApplyHairstyle.
    public class HairSalonUI : MonoBehaviour
    {
        const float RowH = 26f, WinW = 420f, WinH = 480f;
        static HairSalonUI instance;
        bool open;
        Rect win = new Rect(260, 80, WinW, WinH);
        Vector2 scroll;
        PlayerController player;
        List<PkoHair> options;

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void OpenLocal(PlayerController pc)
        {
            if (instance == null)
            {
                var go = new GameObject("HairSalonUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<HairSalonUI>();
            }
            instance.Show(pc);
        }

        void Show(PlayerController pc)
        {
            player = pc;
            int raceIdx = PkoRaces.BaseRace(pc.Job);
            options = PkoTables.Hairs.Where(h => h.UsableRace[raceIdx]).OrderBy(h => h.Name).ThenBy(h => h.Color).ToList();
            open = true;
        }

        void Update()
        {
            if (open && Input.GetKeyDown(KeyCode.Escape)) open = false;
        }

        void OnGUI()
        {
            if (!open || player == null) return;
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            win = GameWindowControls.Window(7720, win, Draw, "Salão de Beleza", () => open = false);
            win.x = Mathf.Clamp(win.x, 0, Screen.width / s - 100); win.y = Mathf.Clamp(win.y, 0, Screen.height / s - 40);
            GUI.matrix = old;
        }

        void Draw(int id)
        {
            GUI.Label(new Rect(10, 20, WinW - 20, 20), $"Ouro: {player.Gold}");
            scroll = GUI.BeginScrollView(new Rect(10, 44, WinW - 20, WinH - 90), scroll, new Rect(0, 0, WinW - 40, options.Count * RowH));
            for (int i = 0; i < options.Count; i++)
            {
                var h = options[i];
                GUI.Label(new Rect(0, i * RowH, 220, RowH), $"{h.Name} ({h.Color})");
                GUI.Label(new Rect(220, i * RowH, 90, RowH), $"{h.Cost} ouro");
                if (GUI.Button(new Rect(310, i * RowH + 1, 60, RowH - 2), "Usar"))
                {
                    player.CmdApplyHairstyle(h.Id);
                }
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(WinW - 90, WinH - 36, 70, 26), "Fechar")) open = false;
            GUI.DragWindow(new Rect(0, 0, WinW, 20));
        }
    }
}
