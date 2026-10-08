using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TOP.Data;
using TOP.Network;
using TOP.Player;
using TOP.UI.Pko;

namespace TOP.Admin
{
    // Painel de administracao (F10): gera no jogo uma copia de um item existente (refino, sockets e gemas)
    // no primeiro slot vazio do inventario. So aparece para contas is_admin; o servidor revalida em CmdAdminGenerate.
    public class AdminPanel : MonoBehaviour
    {
        const float RowH = 26f, WinW = 880f, WinH = 620f;

        static readonly (int type, string name)[] Categories =
        {
            (0, "Todos"), (20, "Chapeu"), (22, "Armadura"), (23, "Luvas"), (24, "Botas"), (11, "Escudo"),
            (1, "Espada"), (2, "Espada 2M"), (3, "Arco"), (4, "Arma de fogo"), (7, "Adaga"), (9, "Cajado"),
            (25, "Colar"), (26, "Anel"), (44, "Asas"), (59, "Pet"), (49, "Gemas"), (50, "Refino"),
            (82, "Cinto / Calcas")
        };
        static readonly string[] CategoryNames = System.Array.ConvertAll(Categories, c => c.name);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("AdminPanel"); DontDestroyOnLoad(go); go.AddComponent<AdminPanel>();
        }

        public static bool IsAdmin => LoginNetworkClient.IsAdmin;
        // Usado pelo click-to-move: clique sobre o painel nao deve mover o personagem.
        public static bool BlocksMouse => panelBlocks || WeaponTuner.Blocks || WingTuner.BlocksMouse
            || TOP.UI.GameWindowControls.BlocksMouse;
        static bool panelBlocks;

        bool open;
        Rect win = new Rect(20, 20, WinW, WinH);
        Vector2 listScroll, gemScroll, statScroll;
        int category, classFilter = -1, selectedId, refine, sockets, pickSocket = -1, qty = 1;
        readonly int[] gems = new int[3];
        string search = "", status = "";
        PlayerInventory pendingInventory;
        int pendingRequestId;
        float pendingSince;
        List<PkoItem> all, shown;
        int[] classIds;
        string[] classNames;
        bool dirty = true;

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10) && IsAdmin && !Input.GetKey(KeyCode.LeftControl)
                && !Input.GetKey(KeyCode.RightControl) && !Input.GetKey(KeyCode.LeftShift)
                && !Input.GetKey(KeyCode.RightShift)) open = !open;
            if (!IsAdmin) open = false;
            panelBlocks = false;
            if (!open) return;
            float s = Scale;
            var m = Input.mousePosition;
            panelBlocks = win.Contains(new Vector2(m.x / s, (Screen.height - m.y) / s));
        }

        void Build()
        {
            all = PkoTables.Items.Values.Where(i => Categories.Any(c => c.type == i.Type && c.type != 0) && !string.IsNullOrEmpty(i.Name)).OrderBy(i => i.Level).ThenBy(i => i.Id).ToList();
            classIds = all.SelectMany(i => i.Classes).Distinct().OrderBy(x => x).ToArray();
            classNames = new string[classIds.Length + 1];
            classNames[0] = "Todas";
            for (int i = 0; i < classIds.Length; i++) classNames[i + 1] = PkoClasses.Name(classIds[i]);
            Filter();
        }

        void Filter()
        {
            dirty = false;
            string q = search.Trim().ToLowerInvariant();
            shown = all.Where(i =>
                (category == 0 || i.Type == Categories[category].type) &&
                (classFilter < 0 || i.Classes.Length == 0 || i.Classes.Contains(classFilter)) &&
                (q.Length == 0 || i.Name.ToLowerInvariant().Contains(q) || i.Id.ToString() == q)).ToList();
        }

        void OnGUI()
        {
            if (!open || !IsAdmin) return;
            if (all == null) Build();
            if (dirty) Filter();
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            win = TOP.UI.GameWindowControls.Window(7710, win, Draw, "Painel Admin - Gerar item (F10)", () => open = false);
            win.x = Mathf.Clamp(win.x, 0, Screen.width / s - 100); win.y = Mathf.Clamp(win.y, 0, Screen.height / s - 40);
            GUI.matrix = old;
        }

        PkoItem Selected => selectedId > 0 && PkoTables.Items.TryGetValue(selectedId, out var it) ? it : null;

        void Select(PkoItem it)
        {
            selectedId = it.Id; refine = 0; sockets = 0; pickSocket = -1; qty = 1;
            for (int i = 0; i < 3; i++) gems[i] = 0;
        }

        void Draw(int id)
        {
            GUILayout.BeginHorizontal();
            DrawList();
            DrawDetail();
            GUILayout.EndHorizontal();
            GUILayout.Label(status);
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        void DrawList()
        {
            GUILayout.BeginVertical(GUILayout.Width(380));
            GUILayout.Label("Tipo");
            int newCat = GUILayout.SelectionGrid(category, CategoryNames, 4);
            if (newCat != category) { category = newCat; dirty = true; }

            GUILayout.Label("Classe");
            int ci = GUILayout.SelectionGrid(classFilter < 0 ? 0 : System.Array.IndexOf(classIds, classFilter) + 1, classNames, 4);
            int nf = ci == 0 ? -1 : classIds[ci - 1];
            if (nf != classFilter) { classFilter = nf; dirty = true; }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Busca", GUILayout.Width(50));
            string ns = GUILayout.TextField(search);
            if (ns != search) { search = ns; dirty = true; }
            GUILayout.EndHorizontal();
            showChar = GUILayout.Toggle(showChar, showChar ? " v Personagem (classe / level / gold)" : " > Personagem (classe / level / gold)", "Button");
            if (showChar) DrawMyClass();
            GUILayout.Label(shown.Count + " itens");

            // Lista virtualizada: so desenha as linhas visiveis.
            var area = GUILayoutUtility.GetRect(380, 10000, 200, 10000, GUILayout.ExpandHeight(true));
            GUI.Box(area, GUIContent.none);
            var content = new Rect(0, 0, area.width - 20, shown.Count * RowH);
            listScroll = GUI.BeginScrollView(area, listScroll, content);
            int first = Mathf.Max(0, (int)(listScroll.y / RowH)), last = Mathf.Min(shown.Count, first + (int)(area.height / RowH) + 2);
            for (int i = first; i < last; i++)
            {
                var it = shown[i];
                var r = new Rect(0, i * RowH, content.width, RowH);
                if (it.Id == selectedId) GUI.Box(r, GUIContent.none);
                var icon = PkoUi.Instance != null ? PkoUi.Instance.Icon(it.Icon) : null;
                if (icon != null) GUI.DrawTexture(new Rect(r.x + 2, r.y + 1, 24, 24), icon);
                if (GUI.Button(new Rect(r.x + 28, r.y, r.width - 28, RowH), "Lv" + it.Level + "  " + it.Name + "  #" + it.Id, LeftButton)) Select(it);
            }
            GUI.EndScrollView();
            GUILayout.EndVertical();
        }

        // Classe do personagem logado (so para testes de equipamento).
        void DrawMyClass()
        {
            var cls = Mirror.NetworkClient.localPlayer != null ? Mirror.NetworkClient.localPlayer.GetComponent<PlayerClass>() : null;
            if (cls == null) return;
            var classes = (TOP.Core.CharacterClass[])System.Enum.GetValues(typeof(TOP.Core.CharacterClass));
            classes = classes.Where(c => c != TOP.Core.CharacterClass.None).ToArray();
            GUILayout.Label("Classe do personagem: " + cls.CurrentClass);
            int idx = System.Array.IndexOf(classes, cls.CurrentClass);
            int pick = GUILayout.SelectionGrid(idx, classes.Select(c => c.ToString()).ToArray(), 5);
            var inv = cls.GetComponent<PlayerInventory>(); var pc = cls.GetComponent<PlayerController>();
            if (pick != idx && pick >= 0 && inv != null) inv.CmdAdminSetClass((int)classes[pick]);
            if (inv == null || pc == null) return;

            GUILayout.Label("Personagem: Lv " + pc.Level + "  |  Gold " + pc.Gold + "  |  Pontos " + pc.StatPoints + " / Skill " + pc.SkillPoints);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Level", GUILayout.Width(40));
            levelText = GUILayout.TextField(levelText, 3, GUILayout.Width(45));
            if (GUILayout.Button("Aplicar") && int.TryParse(levelText, out int lv)) inv.CmdAdminSetLevel(lv);
            if (GUILayout.Button("-10")) inv.CmdAdminSetLevel(pc.Level - 10);
            if (GUILayout.Button("-1")) inv.CmdAdminSetLevel(pc.Level - 1);
            if (GUILayout.Button("+1")) inv.CmdAdminSetLevel(pc.Level + 1);
            if (GUILayout.Button("+10")) inv.CmdAdminSetLevel(pc.Level + 10);
            if (GUILayout.Button("Max")) inv.CmdAdminSetLevel(100);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Gold", GUILayout.Width(40));
            goldText = GUILayout.TextField(goldText, 12, GUILayout.Width(90));
            if (GUILayout.Button("Definir") && long.TryParse(goldText, out long gold)) inv.CmdAdminSetGold(gold);
            if (GUILayout.Button("+100k")) inv.CmdAdminSetGold((long)pc.Gold + 100000);
            if (GUILayout.Button("+1M")) inv.CmdAdminSetGold((long)pc.Gold + 1000000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+10 pontos de status")) inv.CmdAdminAddPoints(10, 0);
            if (GUILayout.Button("+10 pontos de skill")) inv.CmdAdminAddPoints(0, 10);
            GUILayout.EndHorizontal();
        }

        bool showChar; string levelText = "50", goldText = "1000000";

        static GUIStyle _left;
        static GUIStyle LeftButton { get { if (_left == null) _left = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft }; return _left; } }

        void DrawDetail()
        {
            GUILayout.BeginVertical();
            var it = Selected;
            if (it == null) { GUILayout.Label("Selecione um item na lista."); GUILayout.EndVertical(); return; }

            GUILayout.BeginHorizontal();
            var icon = PkoUi.Instance != null ? PkoUi.Instance.Icon(it.Icon) : null;
            if (icon != null) GUILayout.Label(icon, GUILayout.Width(40), GUILayout.Height(40));
            GUILayout.BeginVertical();
            GUILayout.Label("<b>" + it.Name + (refine > 0 ? " +" + refine : "") + "</b>  (#" + it.Id + ")", Rich);
            string typeName = PkoTables.ItemTypes.TryGetValue(it.Type, out var tn) ? tn : "Tipo " + it.Type;
            string cls = (it.Races.Length == 0 ? "Todas as racas" : string.Join("/", it.Races.Select(PkoClasses.RaceName))) + " | " + (it.Classes.Length == 0 ? "Todas as classes" : PkoClasses.NameList(it.Classes));
            GUILayout.Label(typeName + "  |  Nivel " + it.Level + "  |  " + cls);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            statScroll = GUILayout.BeginScrollView(statScroll, GUILayout.Height(120));
            GUILayout.Label(StatText(it));
            GUILayout.EndScrollView();

            if (PkoGems.CanSocket(it)) DrawUpgrade(it);
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Quantidade", GUILayout.Width(80));
                if (int.TryParse(GUILayout.TextField(qty.ToString(), 4, GUILayout.Width(60)), out var q)) qty = Mathf.Clamp(q, 1, Mathf.Max(1, it.Stack) * 40);
                GUILayout.Label("(pilha max " + it.Stack + ")");
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
            }

            if (GUILayout.Button("Gerar no inventario", GUILayout.Height(34))) Generate(it);
            GUILayout.EndVertical();
        }

        static GUIStyle _rich;
        static GUIStyle Rich { get { if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true }; return _rich; } }

        void DrawUpgrade(PkoItem it)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Refino +" + refine, GUILayout.Width(70));
            refine = Mathf.RoundToInt(GUILayout.HorizontalSlider(refine, 0, PkoGems.MaxRefine));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Sockets " + sockets + "/" + it.MaxSockets, GUILayout.Width(110));
            GUI.enabled = sockets < it.MaxSockets;
            if (GUILayout.Button("+ Socket", GUILayout.Width(90))) sockets++;
            GUI.enabled = sockets > 0;
            if (GUILayout.Button("- Socket", GUILayout.Width(90))) { sockets--; gems[sockets] = 0; if (pickSocket >= sockets) pickSocket = -1; }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            for (int i = 0; i < sockets; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Socket " + (i + 1), GUILayout.Width(70));
                string label = gems[i] > 0 ? PkoTables.Stones[gems[i]].Name + " (" + PkoGems.GemEffect(gems[i]) + ")" : "(vazio) escolher gema...";
                if (GUILayout.Button(label, LeftButton)) pickSocket = pickSocket == i ? -1 : i;
                if (gems[i] > 0 && GUILayout.Button("x", GUILayout.Width(24))) gems[i] = 0;
                GUILayout.EndHorizontal();
            }

            if (pickSocket >= 0 && pickSocket < sockets)
            {
                GUILayout.Label("Gemas compativeis com este item (socket " + (pickSocket + 1) + ")");
                var list = PkoGems.GemsFor(it);
                gemScroll = GUILayout.BeginScrollView(gemScroll, GUILayout.Height(150));
                foreach (var g in list)
                    if (GUILayout.Button(g.Name + "  -  " + PkoGems.GemEffect(g.ItemId), LeftButton)) { gems[pickSocket] = g.ItemId; pickSocket = -1; }
                if (list.Count == 0) GUILayout.Label("Nenhuma gema para este tipo de item.");
                GUILayout.EndScrollView();
            }
            else GUILayout.FlexibleSpace();

            var total = PkoGems.RefineEffect(it, refine);
            for (int i = 0; i < sockets; i++) if (gems[i] > 0) total += PkoGems.GemEffect(gems[i]);
            GUILayout.Label("Bonus adicional (refino + gemas): " + (total.IsZero ? "nenhum" : total.ToString()));
            GUILayout.Label("Valores de gema/refino sao aproximados (os originais ficam no servidor do jogo).");
        }

        static string StatText(PkoItem it)
        {
            var l = new List<string>();
            if (it.MaxAtk > 0) l.Add("Ataque " + it.MinAtk + " - " + it.MaxAtk);
            if (it.Def > 0) l.Add("Defesa " + it.Def);
            if (it.Str > 0) l.Add("Forca +" + it.Str);
            if (it.Agi > 0) l.Add("Agilidade +" + it.Agi);
            if (it.Acc > 0) l.Add("Precisao +" + it.Acc);
            if (it.Con > 0) l.Add("Constituicao +" + it.Con);
            if (it.Spr > 0) l.Add("Espirito +" + it.Spr);
            if (it.Hp > 0) l.Add("HP +" + it.Hp);
            if (it.Sp > 0) l.Add("SP +" + it.Sp);
            if (it.Flee > 0) l.Add("Esquiva +" + it.Flee);
            if (it.Hit > 0) l.Add("Acerto +" + it.Hit);
            if (it.Crit > 0) l.Add("Critico +" + it.Crit);
            if (it.MoveSpeed > 0) l.Add("Velocidade +" + it.MoveSpeed);
            if (it.Durability > 0) l.Add("Durabilidade " + it.Durability);
            if (!string.IsNullOrEmpty(it.Description)) l.Add(it.Description);
            return l.Count == 0 ? "Sem atributos." : string.Join("\n", l);
        }

        void Generate(PkoItem it)
        {
            if (pendingInventory != null) { status = "Aguarde a geracao anterior."; return; }
            PlayerInventory inv = null;
            foreach (var i in FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None)) if (i.isLocalPlayer) inv = i;
            if (inv == null) { status = "Entre no mundo com um personagem para receber itens."; return; }
            if (inv.FindEmptySlot() < 0) { status = "Inventario cheio."; return; }
            if (!Mirror.NetworkClient.isConnected || !inv.isOwned)
            {
                status = "Sem conexao ou controle do personagem. Entre novamente no mundo.";
                return;
            }
            pendingInventory = inv;
            pendingRequestId = inv.NewAdminGenerationRequestId();
            pendingSince = Time.realtimeSinceStartup;
            inv.AdminGenerationCompleted += OnGenerationCompleted;
            status = "Aguardando confirmacao do servidor...";
            if (PkoGems.CanSocket(it)) inv.CmdRequestAdminGenerate(pendingRequestId, it.Id, refine, sockets, gems[0], gems[1], gems[2]);
            else inv.CmdRequestAdminGive(pendingRequestId, it.Id, qty);
        }

        void OnGenerationCompleted(int requestId, bool success, string message)
        {
            if (requestId != pendingRequestId)
            {
                Debug.LogWarning("[Admin] Ignorando resposta de pedido antigo: " + requestId);
                return;
            }
            status = message;
            ClearPending();
        }

        void LateUpdate()
        {
            if (pendingInventory == null) return;
            if (!Mirror.NetworkClient.isConnected || Time.realtimeSinceStartup - pendingSince > 25f)
            {
                status = "Geracao nao confirmada. Verifique conexao, permissao admin e espaco na mochila.";
                Debug.LogWarning("[Admin] " + status);
                ClearPending();
            }
        }

        void ClearPending()
        {
            if (pendingInventory != null) pendingInventory.AdminGenerationCompleted -= OnGenerationCompleted;
            pendingInventory = null;
        }

        void OnDestroy() { ClearPending(); }
    }
}
