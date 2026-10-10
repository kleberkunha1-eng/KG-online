using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace TOP.Data
{
    [Serializable]
    public sealed class BoatCargoItemData
    {
        public int ItemId;
        public int Quantity;

        public BoatCargoItemData CopySnapshot() => (BoatCargoItemData)MemberwiseClone();
    }

    [Serializable]
    public sealed class BoatData
    {
        public string Id;
        public string Name;
        public int TypeId;
        public int BerthId;
        public int Level = 1;
        public int HullId, EngineId, BowId, CannonId, ComponentId;
        public int Health, Fuel;
        public bool IsSunk;
        public List<BoatCargoItemData> Cargo = new List<BoatCargoItemData>();

        public BoatData CopySnapshot()
        {
            var copy = (BoatData)MemberwiseClone();
            copy.Cargo = Cargo == null ? new List<BoatCargoItemData>() : Cargo.ConvertAll(item => item.CopySnapshot());
            return copy;
        }
    }

    public sealed class BoatDefinition
    {
        public int Id, MinimumLevel, HullId, Capacity;
        public string Name;
        public int[] Classes, Engines, Bows, Cannons, Components;
    }

    public sealed class BoatPart
    {
        public int Id, ModelId, Price, Health, Fuel, FuelConsumption, Defense, MinimumAttack, MaximumAttack, Speed;
        public int[] Motors;
    }

    public sealed class BoatBuildQuote
    {
        public int Price, Health, Fuel, FuelConsumption, Defense, MinimumAttack, MaximumAttack, Speed, Capacity;
    }

    public static class BoatCatalog
    {
        public const int MaximumBoats = 3;
        public const int ResourcePackQuantity = 10;
        static readonly Dictionary<int, int> resourcePiles = new Dictionary<int, int>
        {
            { 4543, 4547 }, { 4544, 4548 }, { 4545, 4549 }, { 4546, 4550 }
        };

        public static bool TryGetPackedItem(int resourceItemId, out int pileItemId) => resourcePiles.TryGetValue(resourceItemId, out pileItemId);

        public static bool IsCargoPile(int itemId) => itemId >= 4547 && itemId <= 4550;

        public static int CargoQuantity(BoatData boat) => boat == null || boat.Cargo == null
            ? 0 : boat.Cargo.Where(item => item != null).Sum(item => Mathf.Max(0, item.Quantity));
        static Dictionary<int, BoatDefinition> definitions;
        static Dictionary<int, BoatPart> parts;
        public static IReadOnlyDictionary<int, BoatDefinition> Definitions { get { Load(); return definitions; } }
        public static bool IsArgentOffering(int typeId) => typeId == 1 || typeId == 2 || typeId == 3 || typeId == 6;
        public static bool ValidName(string name) => name != null && name.Length >= 2 && name.Length <= 16
            && name.All(c => c >= 32 && c <= 126 && c != '<' && c != '>');

        static int Number(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        static int[] Numbers(string value) => value.Split(',').Select(Number).ToArray();
        static IEnumerable<string[]> Rows(string resource)
        {
            var asset = Resources.Load<TextAsset>("PKO/" + resource);
            if (asset == null) throw new InvalidOperationException("Original boat table missing: " + resource);
            foreach (string line in asset.text.Split('\n'))
                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("//"))
                    yield return line.TrimEnd('\r').Split('\t');
        }

        static void Load()
        {
            if (definitions != null) return;
            var loadedParts = new Dictionary<int, BoatPart>();
            foreach (var row in Rows("shipiteminfo"))
            {
                var part = new BoatPart { Id = Number(row[0]), ModelId = Number(row[2]), Price = Number(row[7]), Health = Number(row[8]),
                    Defense = Number(row[10]), MinimumAttack = Number(row[12]), MaximumAttack = Number(row[13]),
                    Fuel = Number(row[18]), FuelConsumption = Number(row[19]), Speed = Number(row[21]),
                    Motors = new[] { Number(row[3]), Number(row[4]), Number(row[5]), Number(row[6]) } };
                loadedParts.Add(part.Id, part);
            }
            var loadedDefinitions = new Dictionary<int, BoatDefinition>();
            foreach (var row in Rows("shipinfo"))
            {
                var definition = new BoatDefinition { Id = Number(row[0]), Name = row[1], HullId = Number(row[5]),
                    Engines = Numbers(row[6]), Bows = Numbers(row[7]), Cannons = Numbers(row[8]), Components = Numbers(row[9]),
                    MinimumLevel = Number(row[10]), Classes = Numbers(row[11]), Capacity = Number(row[21]) };
                loadedDefinitions.Add(definition.Id, definition);
            }
            parts = loadedParts;
            definitions = loadedDefinitions;
        }

        public static bool CanBuild(int typeId, int level, int job)
        {
            Load();
            return definitions.TryGetValue(typeId, out var definition) && level >= definition.MinimumLevel
                && (definition.Classes[0] == -1 || definition.Classes.Contains(job));
        }

        public static BoatBuildQuote Quote(BoatData boat) =>
            Quote(boat.TypeId, boat.EngineId, boat.BowId, boat.CannonId, boat.ComponentId);

        public static int HullModelId(BoatData boat)
        {
            Load();
            if (!definitions.TryGetValue(boat.TypeId, out var definition) || !parts.TryGetValue(definition.HullId, out var hull))
                throw new InvalidOperationException("Original boat hull model is missing.");
            return hull.ModelId;
        }

        public static int MaximumHealth(BoatData boat)
        {
            int health = Quote(boat).Health;
            if (boat.Level < 1 || boat.Level > 100)
                throw new InvalidOperationException("Original boat level must be between 1 and 100.");
            return boat.Level < 60 ? checked((int)((long)health * (boat.Level + 100) / 120))
                : checked((int)((long)health * 160 / 120 + (boat.Level - 60) * 20));
        }

        public static int MaintenancePrice(BoatData boat, int characterLevel, bool refuel)
        {
            if (characterLevel < 1 || boat.IsSunk)
                throw new InvalidOperationException("A sunken boat must be salvaged before maintenance.");
            int missing = refuel ? Quote(boat).Fuel - boat.Fuel : MaximumHealth(boat) - boat.Health;
            if (missing <= 0 || characterLevel <= 10) return 0;
            return checked((refuel ? missing : missing / 20) + characterLevel * 20);
        }

        public static BoatBuildQuote Quote(int typeId, int engineId, int bowId, int cannonId, int componentId)
        {
            Load();
            if (!definitions.TryGetValue(typeId, out var definition) || !definition.Engines.Contains(engineId)
                || !definition.Bows.Contains(bowId) || !definition.Cannons.Contains(cannonId)
                || !definition.Components.Contains(componentId))
                throw new InvalidOperationException("Parts do not belong to the selected original ship.");
            var selected = new List<BoatPart> { parts[definition.HullId], parts[engineId], parts[bowId], parts[cannonId] };
            if (componentId > 0) selected.Add(parts[componentId]);
            foreach (int motor in parts[engineId].Motors)
                if (motor > 0) selected.Add(parts[motor]);
            return new BoatBuildQuote { Price = checked(selected.Sum(p => p.Price)), Health = selected.Sum(p => p.Health),
                Fuel = selected.Sum(p => p.Fuel), FuelConsumption = selected.Sum(p => p.FuelConsumption), Defense = selected.Sum(p => p.Defense),
                MinimumAttack = selected.Sum(p => p.MinimumAttack), MaximumAttack = selected.Sum(p => p.MaximumAttack),
                Speed = selected.Sum(p => p.Speed), Capacity = definition.Capacity };
        }
    }
}
