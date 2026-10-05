using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TOP.Inventory;

namespace TOP.Data
{
    public struct ItemBonus
    {
        public int Str, Agi, Con, Spr, Acc, Atk, Def, Hp;

        public static ItemBonus operator +(ItemBonus a, ItemBonus b) => new ItemBonus
        {
            Str = a.Str + b.Str, Agi = a.Agi + b.Agi, Con = a.Con + b.Con, Spr = a.Spr + b.Spr,
            Acc = a.Acc + b.Acc, Atk = a.Atk + b.Atk, Def = a.Def + b.Def, Hp = a.Hp + b.Hp
        };

        public bool IsZero => Str == 0 && Agi == 0 && Con == 0 && Spr == 0 && Acc == 0 && Atk == 0 && Def == 0 && Hp == 0;

        public override string ToString()
        {
            var p = new List<string>();
            if (Str != 0) p.Add("STR +" + Str);
            if (Agi != 0) p.Add("AGI +" + Agi);
            if (Con != 0) p.Add("CON +" + Con);
            if (Spr != 0) p.Add("SPR +" + Spr);
            if (Acc != 0) p.Add("ACC +" + Acc);
            if (Atk != 0) p.Add("Attack +" + Atk);
            if (Def != 0) p.Add("Defense +" + Def);
            if (Hp != 0) p.Add("HP +" + Hp);
            return string.Join(", ", p);
        }
    }

    // Regras de gemas e refino. Os numeros reais ficam no servidor original (nao existem no cliente):
    // aqui o efeito e inferido do nome/descricao da gema, entao sao valores aproximados e ajustaveis.
    public static class PkoGems
    {
        public const int MaxSockets = 3, MaxRefine = 12, RefiningGemItemId = 885;
        static readonly Regex Points = new Regex(@"(?:by|adds?)\s+(\d+)", RegexOptions.IgnoreCase);

        public static bool IsGem(int itemId) => PkoTables.Stones.ContainsKey(itemId);

        public static bool IsWeapon(int type) => type == 1 || type == 2 || type == 3 || type == 4 || type == 7 || type == 9;
        public static bool IsArmor(int type) => type == 11 || type == 20 || type == 22 || type == 23 || type == 24;

        // Itens que aceitam socket/refino: armas e armaduras, como no original.
        public static bool CanSocket(PkoItem it) => it != null && (IsWeapon(it.Type) || IsArmor(it.Type));

        public static List<PkoStone> GemsFor(PkoItem it)
        {
            if (it == null) return new List<PkoStone>();
            return PkoTables.Stones.Values.Where(s => s.ItemTypes.Contains(it.Type)).OrderBy(s => s.Name).ToList();
        }

        public static bool Accepts(PkoItem it, int gemId) => it != null && PkoTables.Stones.TryGetValue(gemId, out var s) && s.ItemTypes.Contains(it.Type);

        public static ItemBonus GemEffect(int gemId)
        {
            var b = new ItemBonus();
            if (!PkoTables.Stones.TryGetValue(gemId, out var st)) return b;
            PkoTables.Items.TryGetValue(gemId, out var gi);
            string n = st.Name ?? "";
            int v = 0;
            if (gi != null) { var m = Points.Match(gi.Description ?? ""); if (m.Success) int.TryParse(m.Groups[1].Value, out v); }
            if (v == 0) v = n.StartsWith("Great") ? 5 : 3;

            if (n.Contains("Wind")) b.Agi = v;
            else if (n.Contains("Striking")) b.Acc = v;
            else if (n.Contains("Colossus")) b.Con = v;
            else if (n.Contains("Rage")) b.Str = v;
            else if (n.Contains("Soul")) b.Spr = v;
            else if (st.Kind == 1) b.Atk = v * 3;
            else if (st.Kind == 2) b.Def = v * 3;
            else b.Hp = v * 20;
            return b;
        }

        public static ItemBonus RefineEffect(PkoItem it, int level)
        {
            var b = new ItemBonus();
            if (it == null || level <= 0) return b;
            if (IsWeapon(it.Type)) b.Atk = level * System.Math.Max(1, (int)System.Math.Round(it.MaxAtk * 0.05f));
            else if (IsArmor(it.Type)) b.Def = level * System.Math.Max(1, (int)System.Math.Round(it.Def * 0.05f));
            return b;
        }

        // Bonus extra de uma instancia (refino + gemas); os stats base do item ja vem de EquipmentData.
        public static ItemBonus InstanceBonus(InventoryItem item)
        {
            if (item == null || item.IsEmpty) return default;
            PkoTables.Items.TryGetValue(item.ItemId, out var it);
            var total = RefineEffect(it, item.RefineLevel);
            for (int i = 0; i < MaxSockets; i++) if (item.Gems[i] > 0) total += GemEffect(item.Gems[i]);
            return total;
        }
    }
}
