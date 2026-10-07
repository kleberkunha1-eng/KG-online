using UnityEngine;
using TOP.Player;

namespace TOP.UI
{
    // Painel de guilda (tecla G). Permite fundar (custa ouro), ver membros/patentes, convidar,
    // kickar (apenas lider/oficial), definir aviso, e sair da guilda.
    public class GuildUI : MonoBehaviour
    {
        static GuildUI instance;
        PlayerGuild local;

        bool open;
        Rect win = new Rect(790, 120, 300, 340);
        Vector2 scroll;

        string createNameBuffer = "";
        string inviteNameBuffer = "";
        string noticeBuffer = "";
        bool noticeDirty;

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void Bind(PlayerGuild pg)
        {
            if (instance == null)
            {
                var go = new GameObject("GuildUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<GuildUI>();
            }
            if (instance.local == pg) return;
            if (instance.local != null) instance.local.OnGuildChanged -= instance.OnGuildChanged;
            instance.open = false;
            instance.noticeDirty = false;
            instance.noticeBuffer = instance.createNameBuffer = instance.inviteNameBuffer = "";
            instance.local = pg;
            pg.OnGuildChanged += instance.OnGuildChanged;
        }

        void OnGuildChanged()
        {
            if (local?.Guild != null && !noticeDirty) noticeBuffer = local.Guild.Notice ?? "";
        }

        void Update()
        {
            if (local == null) { open = false; return; }
        }

        public static bool ToggleLocal()
        {
            if (instance == null || instance.local == null) return false;
            instance.open = !instance.open;
            if (instance.open) instance.local.CmdRefreshGuild();
            return true;
        }

        void OnGUI()
        {
            if (!open || local == null) return;
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            win = GameWindowControls.Window(7742, win, Draw, "Guilda (Alt+G)", () => open = false);
            GUI.matrix = old;
        }

        void Draw(int id)
        {
            var g = local.Guild;
            if (g == null || !g.InGuild) DrawCreateForm();
            else DrawGuildPanel(g);
            GUI.DragWindow(new Rect(0, 0, win.width, 20));
        }

        void DrawCreateForm()
        {
            GUI.Label(new Rect(10, 30, win.width - 20, 40), "Voce nao pertence a nenhuma guilda.\nFundar custa 100.000 de ouro.");
            createNameBuffer = GUI.TextField(new Rect(10, 75, win.width - 20, 24), createNameBuffer);
            if (GUI.Button(new Rect(10, 105, win.width - 20, 26), "Fundar guilda") && !string.IsNullOrWhiteSpace(createNameBuffer))
            {
                local.CmdCreateGuild(createNameBuffer.Trim());
                createNameBuffer = "";
            }
        }

        void DrawGuildPanel(TOP.Data.GuildData g)
        {
            GUI.Label(new Rect(10, 24, win.width - 20, 20), $"<b>{g.Name}</b> (Nv.{g.Level}) - {g.MyRank}");

            GUI.Label(new Rect(10, 46, 60, 20), "Aviso:");
            string newNotice = GUI.TextField(new Rect(70, 46, win.width - 150, 20), noticeBuffer);
            if (newNotice != noticeBuffer) { noticeBuffer = newNotice; noticeDirty = true; }
            if (g.IsLeaderOrOfficer && GUI.Button(new Rect(win.width - 75, 46, 60, 20), "Salvar"))
            {
                local.CmdGuildSetNotice(noticeBuffer);
                noticeDirty = false;
            }

            scroll = GUI.BeginScrollView(new Rect(10, 72, win.width - 20, win.height - 150), scroll, new Rect(0, 0, win.width - 40, Mathf.Max(150, g.Members.Count * 22)));
            float y = 0;
            foreach (var m in g.Members)
            {
                GUI.Label(new Rect(0, y, win.width - 90, 20), $"{m.Name} - {m.Rank}");
                if (g.IsLeaderOrOfficer && m.CharacterId != g.LeaderCharacterId && GUI.Button(new Rect(win.width - 85, y - 1, 60, 20), "Kick"))
                    local.CmdGuildKick(m.Name);
                y += 22;
            }
            GUI.EndScrollView();

            float by = win.height - 72;
            inviteNameBuffer = GUI.TextField(new Rect(10, by, win.width - 100, 22), inviteNameBuffer);
            if (GUI.Button(new Rect(win.width - 85, by, 75, 22), "Convidar") && !string.IsNullOrWhiteSpace(inviteNameBuffer))
            {
                local.CmdGuildInvite(inviteNameBuffer.Trim());
                inviteNameBuffer = "";
            }

            if (GUI.Button(new Rect(10, win.height - 42, win.width - 20, 26), "Sair da guilda"))
                local.CmdGuildLeave();
        }
    }
}
