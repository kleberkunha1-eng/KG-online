using System.Linq;
using UnityEngine;
using TOP.Data;
using TOP.Player;
using TOP.Inventory;

namespace TOP.UI
{
    // Janela de forja do ferreiro (forgeitem.txt): lista os itens forjaveis do inventario do
    // jogador local e permite tentar subir o RefineLevel, mostrando custo/materiais/chance antes
    // da tentativa.
    public class ForgeUI : MonoBehaviour
    {
        const float WinW = 460f, WinH = 420f;
        static ForgeUI instance;
        bool open;
        Rect win = new Rect(260, 80, WinW, WinH);
        Vector2 scroll;
        PlayerForge forge;
        int selectedSlot = -1;
        int mode;
        string firstSlot = "0", secondSlot = "1", thirdSlot = "2";

        float Scale => Mathf.Max(1f, Screen.height / 900f);

        public static void OpenLocal(PlayerForge pf)
        {
            if (pf == null) return;
            if (instance == null)
            {
                var go = new GameObject("ForgeUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<ForgeUI>();
            }
            instance.forge = pf;
            instance.open = true;
            instance.selectedSlot = -1;
        }

        void Update()
        {
            if (open && Input.GetKeyDown(KeyCode.Escape)) open = false;
        }

        void OnGUI()
        {
            if (!open || forge == null || forge.Inventory == null) return;
            float s = Scale;
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            win = GameWindowControls.Window(7760, win, Draw, "Forja", () => open = false);
            win.x = Mathf.Clamp(win.x, 0, Screen.width / s - 100); win.y = Mathf.Clamp(win.y, 0, Screen.height / s - 40);
            GUI.matrix = old;
        }

        void DrawOriginalOperation()
        {
            GUI.Label(new Rect(10, 60, WinW - 20, 45), mode == 1 ? "Fusion: apparel cheio + equipamento do mesmo tipo.\nFusion Scroll (453), Catalyst (454) se refinado/gemas; nivel x 1000 ouro." : "Combinacao: duas gemas identicas + Combining Scroll.\nCusto: 5000 ouro; falha consome ambas as gemas.");
            GUI.Label(new Rect(10, 120, 200, 22), mode == 1 ? "Slot apparel (0-based)" : "Slot primeira gema (0-based)");
            firstSlot = GUI.TextField(new Rect(220, 120, 90, 22), firstSlot);
            GUI.Label(new Rect(10, 150, 200, 22), mode == 1 ? "Slot equipamento" : "Slot segunda gema");
            secondSlot = GUI.TextField(new Rect(220, 150, 90, 22), secondSlot);
            if (mode == 2) { GUI.Label(new Rect(10, 180, 200, 22), "Slot Combining Scroll"); thirdSlot = GUI.TextField(new Rect(220, 180, 90, 22), thirdSlot); }
            if (GUI.Button(new Rect(10, 220, 200, 28), "Confirmar operacao") && int.TryParse(firstSlot, out int a) && int.TryParse(secondSlot, out int b))
            { if (mode == 1) forge.CmdFuseItem(a, b); else if (int.TryParse(thirdSlot, out int c)) forge.CmdCombineGems(a, b, c); }
            GUI.DragWindow(new Rect(0, 0, WinW, 20));
        }
        void Draw(int id)
        {
            if (GUI.Button(new Rect(190, 20, 80, 22), "Forja")) mode = 0;
            if (GUI.Button(new Rect(275, 20, 80, 22), "Fusion")) mode = 1;
            if (GUI.Button(new Rect(360, 20, 85, 22), "Gemas")) mode = 2;
            if (mode != 0) { DrawOriginalOperation(); return; }
            var inv = forge.Inventory;
            GUI.Label(new Rect(10, 20, WinW - 20, 20), $"Ouro: {forge.Controller.Gold}");

            scroll = GUI.BeginScrollView(new Rect(10, 44, WinW - 20, 150), scroll, new Rect(0, 0, WinW - 40, inv.totalSlots * 20));
            for (int i = 0; i < inv.totalSlots; i++)
            {
                var it = inv.GetSlot(i);
                if (it == null || it.IsEquipped) continue;
                if (!PkoTables.Items.TryGetValue(it.ItemId, out var pko) || pko.EquipSlots == null || pko.EquipSlots.Length == 0) continue;
                string label = $"[{i}] {pko.Name}" + (it.RefineLevel > 0 ? " +" + it.RefineLevel : "");
                if (GUI.Toggle(new Rect(0, i * 20, WinW - 60, 20), selectedSlot == i, label, "Button"))
                    selectedSlot = i;
            }
            GUI.EndScrollView();

            float y = 204;
            if (selectedSlot >= 0 && inv.GetSlot(selectedSlot) != null)
            {
                var item = inv.GetSlot(selectedSlot);
                PkoTables.Items.TryGetValue(item.ItemId, out var pko);
                int targetLevel = item.RefineLevel + 1;
                if (ForgeTable.ByLevel.TryGetValue(targetLevel, out var def))
                {
                    GUI.Label(new Rect(10, y, WinW - 20, 20), $"Forjar {pko?.Name} +{item.RefineLevel} -> +{targetLevel}  (Chance: {def.SuccessRate}%)"); y += 22;
                    GUI.Label(new Rect(10, y, WinW - 20, 20), $"Custo: {def.RequiredGold} ouro"); y += 20;
                    foreach (var (matId, qty) in def.Requirements)
                    {
                        if (matId <= 0 || qty <= 0) continue;
                        PkoTables.Items.TryGetValue(matId, out var mat);
                        int have = inv.GetItemCount(matId);
                        GUI.Label(new Rect(10, y, WinW - 20, 20), $"{mat?.Name ?? ("Item#" + matId)}: {have}/{qty}");
                        y += 18;
                    }
                    y += 6;
                    if (GUI.Button(new Rect(10, y, 120, 28), "Forjar"))
                        forge.CmdForgeItem(selectedSlot);
                }
                else
                {
                    GUI.Label(new Rect(10, y, WinW - 20, 20), "Este item ja atingiu o nivel maximo de forja.");
                }
            }
            else
            {
                GUI.Label(new Rect(10, y, WinW - 20, 20), "Selecione um item forjavel acima.");
            }

            if (GUI.Button(new Rect(WinW - 90, WinH - 36, 70, 26), "Fechar")) open = false;
            GUI.DragWindow(new Rect(0, 0, WinW, 20));
        }
    }
}
