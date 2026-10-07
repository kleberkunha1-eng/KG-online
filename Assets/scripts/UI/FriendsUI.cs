using UnityEngine;
using TOP.Player;

namespace TOP.UI
{
    // Painel de lista de amigos (tecla N). Mostra nome/nivel/classe/status online-offline e permite
    // adicionar por nome ou remover. Segue o mesmo padrao IMGUI de PartyUI/HairSalonUI.
    public class FriendsUI : MonoBehaviour
    {
        static FriendsUI instance;
        PlayerFriends local;

        bool open;
        string addNameBuffer = "";
        Vector2 scroll;
        Rect win = new Rect(250, 120, 260, 320);

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void Bind(PlayerFriends pf)
        {
            if (instance == null)
            {
                var go = new GameObject("FriendsUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<FriendsUI>();
            }
            if (instance.local == pf) return;
            instance.open = false;
            instance.addNameBuffer = "";
            instance.local = pf;
            pf.CmdRefreshFriends();
        }

        void Update()
        {
            if (local == null) { open = false; return; }
        }

        public static bool ToggleLocal()
        {
            if (instance == null || instance.local == null) return false;
            instance.open = !instance.open;
            if (instance.open) instance.local.CmdRefreshFriends();
            return true;
        }

        void OnGUI()
        {
            if (!open || local == null) return;
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            win = GameWindowControls.Window(7740, win, Draw, "Amigos (Alt+N / Alt+F)", () => open = false);
            GUI.matrix = old;
        }

        void Draw(int id)
        {
            GUI.Label(new Rect(10, 20, 160, 20), "Adicionar amigo:");
            addNameBuffer = GUI.TextField(new Rect(10, 42, 160, 22), addNameBuffer);
            if (GUI.Button(new Rect(175, 42, 65, 22), "Add") && !string.IsNullOrWhiteSpace(addNameBuffer))
            {
                local.CmdAddFriend(addNameBuffer.Trim());
                addNameBuffer = "";
            }

            scroll = GUI.BeginScrollView(new Rect(10, 72, win.width - 20, win.height - 90), scroll, new Rect(0, 0, win.width - 40, Mathf.Max(200, local.Friends.Count * 24)));
            float y = 0;
            foreach (var f in local.Friends)
            {
                bool online = f.LastOnline.HasValue && (System.DateTime.UtcNow - f.LastOnline.Value).TotalMinutes < 1;
                string status = online ? "<color=#4CFF4C>Online</color>" : "Offline";
                GUI.Label(new Rect(0, y, win.width - 90, 20), $"{f.Name} (Lv.{f.Level}) - {status}");
                if (GUI.Button(new Rect(win.width - 85, y - 1, 45, 20), "Rem"))
                    local.CmdRemoveFriend(f.Id);
                y += 24;
            }
            GUI.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, win.width, 20));
        }
    }
}
