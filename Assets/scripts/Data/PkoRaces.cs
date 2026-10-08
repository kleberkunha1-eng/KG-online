using UnityEngine;

namespace TOP.Data
{
    public static class PkoRaces
    {
        public const int OriginalCount = 4;
        public const int NewCharacterTest = 4;
        public const int Count = 5;

        public static int BaseRace(int race) => race == NewCharacterTest ? 0 : Mathf.Clamp(race, 0, OriginalCount - 1);
        public static string Name(int race) => race switch
        {
            0 => "Lance", 1 => "Carsise", 2 => "Phyllis", 3 => "Ami",
            NewCharacterTest => "NewCharacterTest",
            _ => "Race " + race
        };
    }
}
