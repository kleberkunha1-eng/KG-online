using System.Collections.Generic;

namespace TOP.Data
{
    public class ForgeLevelDef
    {
        public int Level;       // nivel alvo (RefineLevel apos sucesso)
        public int FailedLevel; // RefineLevel resultante em caso de falha
        public int SuccessRate; // 0-100
        // Materiais exigidos (itemId, quantidade); -1/0 entradas vazias sao ignoradas.
        public (int itemId, int qty)[] Requirements;
        public int RequiredGold;
    }

    // Tabela extraida diretamente do cliente original (scripts/table/forgeitem.txt).
    // Cada linha representa a tentativa de forjar um item do RefineLevel (Level-1) para Level.
    public static class ForgeTable
    {
        public static readonly Dictionary<int, ForgeLevelDef> ByLevel = new Dictionary<int, ForgeLevelDef>
        {
            [1] = new ForgeLevelDef { Level = 1, FailedLevel = 1, SuccessRate = 100, Requirements = new[] { (1779, 1) }, RequiredGold = 50000 },
            [2] = new ForgeLevelDef { Level = 2, FailedLevel = 2, SuccessRate = 100, Requirements = new[] { (1779, 1) }, RequiredGold = 50000 },
            [3] = new ForgeLevelDef { Level = 3, FailedLevel = 3, SuccessRate = 100, Requirements = new[] { (1779, 1) }, RequiredGold = 50000 },
            [4] = new ForgeLevelDef { Level = 4, FailedLevel = 4, SuccessRate = 85, Requirements = new[] { (1779, 2), (1774, 1) }, RequiredGold = 50000 },
            [5] = new ForgeLevelDef { Level = 5, FailedLevel = 5, SuccessRate = 75, Requirements = new[] { (1779, 2), (1774, 1) }, RequiredGold = 50000 },
            [6] = new ForgeLevelDef { Level = 6, FailedLevel = 6, SuccessRate = 65, Requirements = new[] { (1779, 2), (1774, 1) }, RequiredGold = 50000 },
            [7] = new ForgeLevelDef { Level = 7, FailedLevel = 7, SuccessRate = 50, Requirements = new[] { (1779, 2), (1774, 1) }, RequiredGold = 50000 },
            [8] = new ForgeLevelDef { Level = 8, FailedLevel = 7, SuccessRate = 45, Requirements = new[] { (1779, 3), (1774, 2), (1804, 1) }, RequiredGold = 50000 },
            [9] = new ForgeLevelDef { Level = 9, FailedLevel = 8, SuccessRate = 40, Requirements = new[] { (1779, 3), (1774, 2), (1804, 1) }, RequiredGold = 50000 },
            [10] = new ForgeLevelDef { Level = 10, FailedLevel = 9, SuccessRate = 35, Requirements = new[] { (1779, 3), (1774, 2), (1804, 1) }, RequiredGold = 50000 },
            [11] = new ForgeLevelDef { Level = 11, FailedLevel = 10, SuccessRate = 33, Requirements = new[] { (1779, 3), (1774, 2), (1804, 1) }, RequiredGold = 50000 },
            [12] = new ForgeLevelDef { Level = 12, FailedLevel = 0, SuccessRate = 30, Requirements = new[] { (1779, 4), (1774, 3), (1804, 2), (1775, 1) }, RequiredGold = 50000 },
        };
    }
}
