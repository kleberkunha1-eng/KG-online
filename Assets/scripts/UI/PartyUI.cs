using UnityEngine;
using TOP.Player;

namespace TOP.UI
{
    // Painel de grupo (lista de membros + sair/expulsar) e popup de convite recebido.
    // Segue o mesmo padrao IMGUI leve usado por HairSalonUI/AdminPanel.
    public class PartyUI : MonoBehaviour
    {
        static PartyUI instance;
        PlayerParty localParty;

        bool invitePending;
        string inviterName;
        PlayerParty inviteTarget;
        float inviteExpireAt;

        bool panelOpen;
        Rect inviteWin = new Rect(0, 0, 320, 110);
        Rect panelWin = new Rect(20, 120, 220, 220);

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void Bind(PlayerParty pp)
        {
            if (instance == null)
            {
                var go = new GameObject("PartyUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<PartyUI>();
            }
            if (instance.localParty == pp) return;
            if (instance.localParty != null) instance.localParty.OnPartyChanged -= instance.OnPartyChanged;
            instance.invitePending = false;
            instance.inviteTarget = null;
            instance.inviterName = null;
            instance.localParty = pp;
            instance.panelOpen = pp.PartyId != 0;
            pp.OnPartyChanged += instance.OnPartyChanged;
        }

        public static void ShowInvite(PlayerParty target, string inviter)
        {
            if (instance == null) return;
            instance.invitePending = true;
            instance.inviterName = inviter;
            instance.inviteTarget = target;
            instance.inviteExpireAt = Time.time + 30f;
        }

        void OnPartyChanged()
        {
            panelOpen = localParty != null && localParty.PartyId != 0;
        }

        void Update()
        {
            if (localParty == null) panelOpen = false;
            if (inviteTarget == null) invitePending = false;
            if (invitePending && Time.time > inviteExpireAt) invitePending = false;
        }

        public static bool ToggleLocal()
        {
            if (instance == null || instance.localParty == null) return false;
            instance.panelOpen = !instance.panelOpen;
            return true;
        }

        void OnGUI()
        {
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));

            if (invitePending)
            {
                inviteWin.x = Screen.width / s / 2f - inviteWin.width / 2f;
                inviteWin.y = 80;
                inviteWin = GameWindowControls.Window(7730, inviteWin, DrawInvite, "Convite de grupo", () =>
                {
                    inviteTarget.CmdPartyDecline(inviterName);
                    invitePending = false;
                });
            }

            if (panelOpen && localParty != null)
                panelWin = GameWindowControls.Window(7731, panelWin, DrawPanel, "Grupo (Alt+P)", () => panelOpen = false);

            GUI.matrix = old;
        }

        void DrawInvite(int id)
        {
            GUI.Label(new Rect(10, 20, 300, 40), inviterName + " te convidou para um grupo.");
            if (GUI.Button(new Rect(40, 70, 100, 28), "Aceitar"))
            {
                inviteTarget.CmdPartyAccept(inviterName);
                invitePending = false;
            }
            if (GUI.Button(new Rect(180, 70, 100, 28), "Recusar"))
            {
                inviteTarget.CmdPartyDecline(inviterName);
                invitePending = false;
            }
        }

        void DrawPanel(int id)
        {
            float y = 24;
            foreach (var name in localParty.Members)
            {
                bool isLeaderEntry = name.StartsWith("*");
                string clean = isLeaderEntry ? name.Substring(1) : name;
                GUI.Label(new Rect(10, y, 140, 20), (isLeaderEntry ? "\u2605 " : "") + clean);
                if (localParty.IsLeader && clean != LocalName())
                {
                    if (GUI.Button(new Rect(155, y - 1, 50, 20), "Kick"))
                        localParty.CmdPartyKick(clean);
                }
                y += 22;
            }
            if (GUI.Button(new Rect(10, panelWin.height - 32, panelWin.width - 20, 26), "Sair do grupo"))
                localParty.CmdPartyLeave();
            GUI.DragWindow(new Rect(0, 0, panelWin.width, 20));
        }

        string LocalName()
        {
            var pc = localParty.GetComponent<TOP.Player.PlayerController>();
            return pc != null ? pc.CharacterName : "";
        }
    }
}
