using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using TOP.Core;

namespace TOP.Data
{
    // Classes originais (ids usados em skillinfo/iteminfo). Nomes só onde as skills confirmam.
    public static class PkoClasses
    {
        public static string Name(int id) => id switch
        {
            1 => "Swordsman", 2 => "Hunter", 4 => "Explorer", 8 => "Champion", 10 => "Crusader",
            12 => "Sharpshooter", 13 => "Cleric", 14 => "Seal Master", 16 => "Voyager",
            5 => "Herbalist", 9 => "Crusader", 11 => "Sharpshooter",
            _ => "Class " + id
        };

        public static string RaceName(int id) => id switch { 1 => "Lance", 2 => "Carsise", 3 => "Phyllis", 4 => "Ami", _ => "Race " + id };

        // Mapeamento inferido dos ids de classe do iteminfo para cada classe do jogo.
        public static int[] IdsFor(CharacterClass c) => c switch
        {
            CharacterClass.Swordsman => new[] { 1 }, CharacterClass.Hunter => new[] { 2 }, CharacterClass.Explorer => new[] { 4 },
            CharacterClass.Herbalist => new[] { 5 }, CharacterClass.Champion => new[] { 8 }, CharacterClass.Crusader => new[] { 9, 10 },
            CharacterClass.Sharpshooter => new[] { 11, 12 }, CharacterClass.Cleric => new[] { 13 }, CharacterClass.Voyager => new[] { 16 },
            _ => System.Array.Empty<int>()
        };

        public static bool Allows(int[] itemClasses, CharacterClass c)
        {
            if (itemClasses == null || itemClasses.Length == 0) return true;
            bool hasRequirement = false;
            foreach (var cls in itemClasses) if (cls > 0) { hasRequirement = true; break; }
            if (!hasRequirement) return true;
            foreach (var id in IdsFor(c)) if (System.Array.IndexOf(itemClasses, id) >= 0) return true;
            return false;
        }

        // race: 0-based (Lance=0); o iteminfo usa 1-based.
        public static bool RaceOk(PkoItem it, int race)
        {
            if (it.Races.Length == 0) return true;
            bool hasRequirement = false;
            foreach (var r in it.Races) if (r > 0) { hasRequirement = true; break; }
            return !hasRequirement || System.Array.IndexOf(it.Races, race + 1) >= 0;
        }
        public static string NameList(int[] ids)
        {
            var seen = new List<string>();
            foreach (var i in ids) { var n = Name(i); if (!seen.Contains(n)) seen.Add(n); }
            return string.Join(", ", seen);
        }
    }

    public class PkoSkill
    {
        public int Id; public string Name, Description, Icon;
        public bool IsLife; public int Type, Phase, LearnLevel, Points, Range, TargetMode, AttackShape, Angle, Radius, CooldownMs;
        public Dictionary<int, int> ClassMaxLevel = new Dictionary<int, int>();
        public string SpFormula, DamageFormula;
        public int SpCost;
    }

    public class PkoSkillEffect { public int Id; public string Name, Description, Icon; public bool CanMove = true, CanAttack = true, CanUseSkills = true; }

    public class PkoStone { public int StoneId, ItemId, Kind; public string Name; public int[] ItemTypes = Array.Empty<int>(); }

    // npclist.txt: NPCs fixos do mundo (vendedores, teleportes, etc.) com localizacao e coordenadas originais.
    public class PkoNpc { public int Id; public string Name, Location, Continent; public double X, Y; }

    // monsterlist.txt: monstros nomeados do mundo com nivel e coordenada de spawn original.
    public class PkoWorldMonster { public int Id; public string Name, Continent; public int Level; public double X, Y; }

    // hairs.txt: opcoes de penteado/cor vendidas pelo Hairstylist. ModelStyle e o numero (01..NN) usado
    // por PkoCharacterVisual.Name(race, style, 1) para montar o nome do modelo de cabelo.
    public class PkoHair
    {
        public int Id; public string Name, Color; public ulong Cost; public int ModelStyle;
        public bool[] UsableRace = new bool[4];
    }

    // mountinfo.txt: montarias equipaveis no slot Mount (itens 9500+). Da cliente original so temos os
    // dados (nome, item, velocidade relativa); os modelos 3D ainda nao foram convertidos para o Unity
    // (nenhum .lgo de montaria foi importado ate agora), entao o bonus funciona sem troca de visual.
    public class PkoMount { public int Id, ItemId, BoneId; public string Name; }

    // shipinfo.txt: navios do sistema naval original (combate/navegacao em mar aberto). O cliente Unity
    // nao tem fisica/navegacao de mar ainda; carregamos os dados (nome, nivel minimo, escritura do navio)
    // para o item da escritura ja poder ser gerado/identificado corretamente no inventario e no Admin Panel.
    public class PkoShip { public string Id; public string Name; public int VesselDeedItemId, LevelRestriction; }

    // sceneffectinfo.txt: catalogo de efeitos de particula (.par) do motor original. O Unity ainda nao
    // tem um conversor .par -> ParticleSystem; guardamos so o catalogo (nome do arquivo original) para
    // uso futuro quando esse conversor existir.
    public class PkoSceneEffect { public int Id; public string File, Name; }

    // sceneobjinfo.txt: catalogo de objetos de cenario (predios/decoracoes .lmo) usados pelos mapas.
    // A posicao real desses objetos ja vem do arquivo binario do mapa (ver GarnerTerrainImporter); esta
    // tabela serve apenas para identificar o nome/arquivo de modelo de cada ID de objeto.
    public class PkoSceneObject { public int Id; public string ModelFile, Name; }

    public class PkoItem
    {
        public int Id, Type, Price, Level, Stack = 1, Durability, VisualEffectId, VisualEffectDummy = -1;
        public string Name, Icon, Description, Model;
        // Per-race model ids (Lance, Carsise, Phyllis, Ami); "0" when the item has no model for that race.
        public string[] RaceModels = new string[4] { "0", "0", "0", "0" };
        public int[] Classes = Array.Empty<int>();
        public int[] Races = Array.Empty<int>();
        public int Resist, HpRec, SpRec, PctDef, PctHp, PctCrit, MaxSockets;
        public int[] EquipSlots = Array.Empty<int>();
        public int Str, Agi, Acc, Con, Spr, MinAtk, MaxAtk, Def, Hp, Sp, Flee, Hit, Crit, MoveSpeed;
        public bool Tradeable = true;
    }

    // Leitor das tabelas originais do cliente (Resources/PKO/*.txt, latin1, colunas separadas por TAB).
    public static class PkoTables
    {
        public const int MeshyMageWingsItemId = 990001;
        public const string MeshyMageWingsModel = "MageWings";
        public const string MeshyMageWingsIcon = "magewings";

        static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        static Dictionary<int, PkoSkill> _skills;
        static Dictionary<int, PkoSkillEffect> _effects;
        static Dictionary<int, PkoItem> _items;
        static Dictionary<int, string> _itemTypes, _prefixes;
        static ulong[] _exp, _lifeExp;

        public static IReadOnlyDictionary<int, PkoSkill> Skills { get { if (_skills == null) LoadSkills(); return _skills; } }
        public static IReadOnlyDictionary<int, PkoSkillEffect> SkillEffects { get { if (_effects == null) LoadEffects(); return _effects; } }
        public static IReadOnlyDictionary<int, PkoItem> Items { get { if (_items == null) LoadItems(); return _items; } }
        public static IReadOnlyDictionary<int, string> ItemTypes { get { if (_itemTypes == null) LoadItemTypes(); return _itemTypes; } }
        public static IReadOnlyDictionary<int, string> ItemPrefixes { get { if (_prefixes == null) LoadItemTypes(); return _prefixes; } }

        public const int MaxLevel = 100;

        static Dictionary<int, PkoStone> _stones;
        // Gemas de engaste (stoneinfo.txt) indexadas pelo id do item da gema.
        public static IReadOnlyDictionary<int, PkoStone> Stones { get { if (_stones == null) LoadStones(); return _stones; } }

        static List<PkoNpc> _npcs;
        static List<PkoWorldMonster> _worldMonsters;
        static List<PkoHair> _hairs;
        static Dictionary<int, PkoMount> _mountsByItem;
        static List<PkoShip> _ships;
        static List<PkoSceneEffect> _sceneEffects;
        static List<PkoSceneObject> _sceneObjects;
        public static IReadOnlyList<PkoNpc> Npcs { get { if (_npcs == null) LoadNpcs(); return _npcs; } }
        public static IReadOnlyList<PkoWorldMonster> WorldMonsters { get { if (_worldMonsters == null) LoadWorldMonsters(); return _worldMonsters; } }
        public static IReadOnlyList<PkoHair> Hairs { get { if (_hairs == null) LoadHairs(); return _hairs; } }
        public static IReadOnlyDictionary<int, PkoMount> MountsByItemId { get { if (_mountsByItem == null) LoadMounts(); return _mountsByItem; } }
        public static IReadOnlyList<PkoShip> Ships { get { if (_ships == null) LoadShips(); return _ships; } }
        public static IReadOnlyList<PkoSceneEffect> SceneEffects { get { if (_sceneEffects == null) LoadSceneEffects(); return _sceneEffects; } }
        public static IReadOnlyList<PkoSceneObject> SceneObjects { get { if (_sceneObjects == null) LoadSceneObjects(); return _sceneObjects; } }

        static bool TryXY(string raw, out double x, out double y)
        {
            x = y = 0;
            var p = raw.Split(',');
            return p.Length == 2 && double.TryParse(p[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                                  && double.TryParse(p[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }

        // npclist.txt: ID | NPC name | Map (local) | Coordinates "x,y" | Continent
        static void LoadNpcs()
        {
            _npcs = new List<PkoNpc>();
            foreach (var c in Rows("npclist"))
            {
                if (c.Length < 5 || !int.TryParse(c[0].Trim(), out var id)) continue;
                if (!TryXY(c[3], out var x, out var y)) continue;
                _npcs.Add(new PkoNpc { Id = id, Name = S(c, 1), Location = S(c, 2), X = x, Y = y, Continent = S(c, 4) });
            }
        }

        // monsterlist.txt: ID | Name | Level | Coordinates "x,y" | Map (continent)
        static void LoadWorldMonsters()
        {
            _worldMonsters = new List<PkoWorldMonster>();
            foreach (var c in Rows("monsterlist"))
            {
                if (c.Length < 5 || !int.TryParse(c[0].Trim(), out var id) || id <= 0) continue;
                if (!TryXY(c[3], out var x, out var y)) continue;
                _worldMonsters.Add(new PkoWorldMonster { Id = id, Name = S(c, 1), Level = I(c, 2, 1), X = x, Y = y, Continent = S(c, 4) });
            }
        }

        // hairs.txt: Serial|Name|Color|ReqItem0..3|ReqGold|RelatedModel|MoldFailed|Usable1..4|ItemListID|ModelDenominate
        // ModelDenominate tem o formato raceeeeestilopart (ex: 0000090001 = raca 0000, estilo 09, parte 0001=cabelo).
        static void LoadHairs()
        {
            _hairs = new List<PkoHair>();
            foreach (var c in Rows("hairs"))
            {
                if (c.Length < 16 || !int.TryParse(c[0].Trim(), out var id)) continue;
                string denom = S(c, 15);
                if (denom.Length < 8 || !int.TryParse(denom.Substring(4, 2), out var style)) continue;
                var h = new PkoHair { Id = id, Name = S(c, 1), Color = S(c, 2), Cost = (ulong)Math.Max(0, I(c, 7)), ModelStyle = style };
                h.UsableRace[0] = I(c, 10) != 0; h.UsableRace[1] = I(c, 11) != 0; h.UsableRace[2] = I(c, 12) != 0; h.UsableRace[3] = I(c, 13) != 0;
                _hairs.Add(h);
            }
        }

        // mountinfo.txt: ID | Mount Name | Iteminfo ID | boneID | height-rect | offsetX | offsetY | poseID
        static void LoadMounts()
        {
            _mountsByItem = new Dictionary<int, PkoMount>();
            foreach (var c in Rows("mountinfo"))
            {
                if (c.Length < 4 || !int.TryParse(c[0].Trim(), out var id)) continue;
                int itemId = I(c, 2);
                if (itemId <= 0) continue;
                _mountsByItem[itemId] = new PkoMount { Id = id, Name = S(c, 1), ItemId = itemId, BoneId = I(c, 3) };
            }
        }

        // shipinfo.txt: ID | Name | Vessel Deed itemId | ... | Level Restriction (coluna 10)
        static void LoadShips()
        {
            _ships = new List<PkoShip>();
            foreach (var c in Rows("shipinfo"))
            {
                if (c.Length < 11 || string.IsNullOrEmpty(S(c, 0))) continue;
                _ships.Add(new PkoShip { Id = S(c, 0), Name = S(c, 1), VesselDeedItemId = I(c, 2), LevelRestriction = I(c, 10) });
            }
        }

        // sceneffectinfo.txt: ID | arquivo .par | nome (coluna com mojibake no original) | ...
        static void LoadSceneEffects()
        {
            _sceneEffects = new List<PkoSceneEffect>();
            foreach (var c in Rows("sceneffectinfo"))
            {
                if (c.Length < 2 || !int.TryParse(c[0].Trim(), out var id)) continue;
                _sceneEffects.Add(new PkoSceneEffect { Id = id, File = S(c, 1), Name = S(c, 2) });
            }
        }

        // sceneobjinfo.txt: ID | arquivo .lmo | nome | ... (resto sao parametros visuais: sombra, brilho, etc.)
        static void LoadSceneObjects()
        {
            _sceneObjects = new List<PkoSceneObject>();
            foreach (var c in Rows("sceneobjinfo"))
            {
                if (c.Length < 2 || !int.TryParse(c[0].Trim(), out var id)) continue;
                _sceneObjects.Add(new PkoSceneObject { Id = id, ModelFile = S(c, 1), Name = S(c, 2) });
            }
        }

        static void LoadStones()
        {
            _stones = new Dictionary<int, PkoStone>();
            foreach (var c in Rows("stoneinfo"))
            {
                var st = new PkoStone { StoneId = I(c, 0), Name = S(c, 1), ItemId = I(c, 2), Kind = I(c, 4) };
                var types = new List<int>();
                foreach (var t in List(c, 3)) if (t > 0) types.Add(t == 14 ? 20 : t);
                st.ItemTypes = types.ToArray();
                if (st.ItemId > 0) _stones[st.ItemId] = st;
            }
        }

        // Experiência acumulada necessária para ATINGIR o nível indicado (character_lvup.txt).
        public static ulong TotalExpForLevel(int level)
        {
            if (_exp == null) _exp = LoadLevelTable("character_lvup");
            return _exp[Mathf.Clamp(level, 1, _exp.Length - 1)];
        }

        // Experiência necessária para sair do nível atual (diferença entre linhas consecutivas).
        public static ulong ExpToNextLevel(int level)
        {
            if (_exp == null) _exp = LoadLevelTable("character_lvup");
            if (level >= _exp.Length - 1) return ulong.MaxValue;
            level = Mathf.Max(1, level);
            return _exp[level + 1] - _exp[level];
        }

        public static ulong LifeExpToNextLevel(int level)
        {
            if (_lifeExp == null) _lifeExp = LoadLevelTable("lifelvup");
            if (level >= _lifeExp.Length - 1) return ulong.MaxValue;
            level = Mathf.Max(1, level);
            return _lifeExp[level + 1] - _lifeExp[level];
        }

        static IEnumerable<string[]> Rows(string resource)
        {
            var asset = Resources.Load<TextAsset>("PKO/" + resource);
            if (asset == null) { Debug.LogWarning("[PkoTables] Tabela ausente: " + resource); yield break; }
            string text = Latin1.GetString(asset.bytes);
            foreach (var line in text.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.Length == 0 || l.StartsWith("//")) continue;
                yield return l.Split('\t');
            }
        }

        static int I(string[] c, int i, int def = 0) => i < c.Length && int.TryParse(c[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        static string S(string[] c, int i) => i < c.Length ? c[i].Trim() : "";
        static int[] List(string[] c, int i, char sep = ',')
        {
            var res = new List<int>();
            foreach (var p in S(c, i).Split(sep)) if (int.TryParse(p.Trim(), out var v) && v >= 0) res.Add(v);
            return res.ToArray();
        }
        // "min,max" -> primeiro valor
        static int Pair(string[] c, int i, int idx = 0)
        {
            var p = S(c, i).Split(',');
            return idx < p.Length && int.TryParse(p[idx], out var v) ? v : 0;
        }

        static ulong[] LoadLevelTable(string name)
        {
            var t = new ulong[MaxLevel + 2];
            foreach (var c in Rows(name))
            {
                int lv = I(c, 1);
                if (lv >= 1 && lv < t.Length && ulong.TryParse(S(c, 2), out var v)) t[lv] = v;
            }
            return t;
        }

        static void LoadItemTypes()
        {
            _itemTypes = new Dictionary<int, string>(); _prefixes = new Dictionary<int, string>();
            foreach (var c in Rows("itemtype")) _itemTypes[I(c, 0)] = S(c, 1);
            foreach (var c in Rows("itempre")) _prefixes[I(c, 0)] = S(c, 1);
        }

        static void LoadSkills()
        {
            _skills = new Dictionary<int, PkoSkill>();
            foreach (var c in Rows("skillinfo"))
            {
                var s = new PkoSkill
                {
                    Id = I(c, 0), Name = S(c, 1), IsLife = I(c, 2) != 1 && I(c, 2) != 0,
                    Phase = I(c, 8), Type = I(c, 9), LearnLevel = Mathf.Max(1, I(c, 11, 1)), Points = Mathf.Max(1, I(c, 13, 1)),
                    Range = I(c, 16), TargetMode = I(c, 17), AttackShape = I(c, 18), Angle = I(c, 19), Radius = I(c, 20),
                    SpFormula = S(c, 33), DamageFormula = S(c, 37), CooldownMs = I(c, 44), Description = S(c, 70), Icon = S(c, 67) is string ic && ic.Length > 2 ? ic : null
                };
                foreach (var pair in S(c, 3).Split(';'))
                {
                    var p = pair.Split(',');
                    if (p.Length == 2 && int.TryParse(p[0], out var cls) && int.TryParse(p[1], out var max) && cls > 0)
                        s.ClassMaxLevel[cls] = max;
                }
                s.SpCost = FormulaCost(s.SpFormula, "sp");
                _skills[s.Id] = s;
            }
        }

        // "sp=sp(0)-10" => 10
        static int FormulaCost(string f, string stat)
        {
            int i = f.LastIndexOf('-');
            return i > 0 && int.TryParse(f.Substring(i + 1).Trim(), out var v) ? v : 0;
        }

        static void LoadEffects()
        {
            _effects = new Dictionary<int, PkoSkillEffect>();
            foreach (var c in Rows("skilleff"))
                _effects[I(c, 0)] = new PkoSkillEffect
                {
                    Id = I(c, 0), Name = S(c, 1), CanMove = I(c, 8, 1) != 0, CanUseSkills = I(c, 9, 1) != 0, CanAttack = I(c, 10, 1) != 0,
                    Description = S(c, 31)
                };
        }

        static void LoadItems()
        {
            _items = new Dictionary<int, PkoItem>();
            foreach (var c in Rows("iteminfo"))
            {
                var it = new PkoItem
                {
                    Id = I(c, 0), Name = S(c, 1), Icon = S(c, 2), Model = S(c, 3), RaceModels = new[] { S(c, 4), S(c, 5), S(c, 6), S(c, 7) }, Type = I(c, 10), Tradeable = I(c, 16, 1) != 0,
                    Stack = Mathf.Max(1, I(c, 20, 1)), Price = I(c, 22), Level = I(c, 24), Classes = List(c, 25), Races = List(c, 23), EquipSlots = List(c, 28),
                    Str = Pair(c, 52, 0), Agi = Pair(c, 53, 0), Acc = Pair(c, 54, 0), Con = Pair(c, 55, 0), Spr = Pair(c, 56, 0),
                    MinAtk = Pair(c, 60), MaxAtk = Pair(c, 61), Def = Pair(c, 62), Hp = Pair(c, 63), Sp = Pair(c, 64),
                    Flee = Pair(c, 65), Hit = Pair(c, 66), Crit = Pair(c, 67), MoveSpeed = Pair(c, 71),
                    Resist = Pair(c, 73, 0), HpRec = Pair(c, 69, 0), SpRec = Pair(c, 70, 0), PctDef = I(c, 41), PctHp = I(c, 42), PctCrit = I(c, 46), MaxSockets = Mathf.Clamp(I(c, 77, 0), 0, 3),
                    Durability = Pair(c, 76, 1) / 50, Description = S(c, 93), VisualEffectId = Pair(c, 90, 0),
                    VisualEffectDummy = Pair(c, 90, 1)
                };
                _items[it.Id] = it;
            }

            RegisterCustomWings();
        }

        static void RegisterCustomWings()
        {
            if (_items.ContainsKey(MeshyMageWingsItemId))
            {
                Debug.LogError($"[PkoTables] Custom wing item id collision: {MeshyMageWingsItemId}.");
                return;
            }

            _items.Add(MeshyMageWingsItemId, new PkoItem
            {
                Id = MeshyMageWingsItemId,
                Name = "Mage Wings",
                Icon = MeshyMageWingsIcon,
                Model = MeshyMageWingsModel,
                Type = 44,
                Stack = 1,
                Level = 1,
                Durability = 100,
                EquipSlots = new[] { 14 },
                Description = "A pair of enchanted mage wings."
            });
        }

        // ---------- Conversão para os ScriptableObjects usados pelo jogo ----------

        static CharacterClass MapClass(PkoSkill s)
        {
            foreach (var k in s.ClassMaxLevel.Keys)
                switch (k)
                {
                    case 1: return CharacterClass.Swordsman;
                    case 2: return CharacterClass.Hunter;
                    case 4: return CharacterClass.Explorer;
                    case 5: return CharacterClass.Herbalist;
                    case 8: return CharacterClass.Champion;
                    case 10: return CharacterClass.Crusader;
                    case 12: return CharacterClass.Sharpshooter;
                    case 13: return CharacterClass.Cleric;
                    case 16: return CharacterClass.Voyager;
                }
            return CharacterClass.None;
        }

        public static SkillData ToSkillData(PkoSkill s)
        {
            var d = ScriptableObject.CreateInstance<SkillData>();
            d.hideFlags = HideFlags.DontSave;
            d.skillId = s.Id; d.name = "pko_skill_" + s.Id; d.skillName = s.Name; d.description = s.Description;
            d.skillType = s.Phase == 0 ? SkillType.Passive : SkillType.Active;
            d.targetType = s.TargetMode switch
            {
                1 => SkillTargetType.Self,
                4 => s.AttackShape == 2 ? SkillTargetType.Cone : SkillTargetType.SingleEnemy,
                _ => s.Radius > 0 ? SkillTargetType.AreaEnemy : SkillTargetType.SingleEnemy
            };
            d.requiredClass = MapClass(s);
            d.requiredLevel = s.LearnLevel; d.requiredSkillPoints = s.Points;
            d.spCost = s.SpCost; d.range = Mathf.Max(1f, s.Range / 100f); d.areaRadius = s.Radius / 100f; d.coneAngle = s.Angle;
            d.cooldown = s.CooldownMs / 1000f;
            int max = 10; foreach (var v in s.ClassMaxLevel.Values) max = v;
            d.maxLevel = max;
            return d;
        }

        // Slots de aparencia do iteminfo original (coluna 28): so mudam o visual.
        public static bool IsApparel(PkoItem it) { var s = SlotOf(it); return it.EquipSlots.Length > 0 && s >= EquipmentSlot.ApparelHelmet && s <= EquipmentSlot.ApparelGlow; }

        public static EquipmentSlot SlotOf(PkoItem it)
        {
            int slot = it.EquipSlots.Length > 0 ? it.EquipSlots[0] : -1;
            switch (slot)
            {
                case 19: return EquipmentSlot.ApparelHelmet;
                case 21: return EquipmentSlot.ApparelBody;
                case 22: return it.Type == 24 ? EquipmentSlot.ApparelBoots : EquipmentSlot.ApparelGloves;
                case 23: return it.Type == 23 ? EquipmentSlot.ApparelGloves : EquipmentSlot.ApparelBoots;
                case 24: return EquipmentSlot.ApparelPet;
                case 25: return EquipmentSlot.ApparelGlow;
                case 26: return EquipmentSlot.ApparelDagger;
                case 27: return EquipmentSlot.ApparelGun;
                case 28: return EquipmentSlot.ApparelSword;
                case 29: return EquipmentSlot.ApparelGreatSword;
                case 30: return EquipmentSlot.ApparelStaff;
                case 31: return it.Type == 2 ? EquipmentSlot.ApparelGreatSword : EquipmentSlot.ApparelBow;
                case 33: return EquipmentSlot.ApparelShield;
            }
            return slot switch
            {
                0 => EquipmentSlot.Helmet, 1 => EquipmentSlot.Costume, 2 => EquipmentSlot.Armor, 3 => EquipmentSlot.Gloves,
                4 => EquipmentSlot.Boots, 5 => EquipmentSlot.Necklace, 6 => it.Type == 11 ? EquipmentSlot.Shield : EquipmentSlot.Weapon,
                7 => EquipmentSlot.Ring1, 8 => EquipmentSlot.Ring1, 9 => EquipmentSlot.Weapon, 10 => EquipmentSlot.Earring, 11 => EquipmentSlot.Earring,
                12 => EquipmentSlot.Belt, 13 => EquipmentSlot.Tattoo, 14 => EquipmentSlot.Wing, 15 => EquipmentSlot.Cape,
                16 => EquipmentSlot.Pet, 18 => EquipmentSlot.Mount, _ => EquipmentSlot.Tattoo
            };
        }
        public static ItemData ToItemData(PkoItem it)
        {
            ItemData d;
            int slot = it.EquipSlots.Length > 0 ? it.EquipSlots[0] : -1;
            bool equip = slot >= 0 && it.Type != 12 && it.Type != 13;
            if (equip)
            {
                var e = ScriptableObject.CreateInstance<EquipmentData>();
                e.slot = SlotOf(it);
                e.requiredLevel = Mathf.Max(1, it.Level);
                e.bonusAttack = it.MaxAtk; e.bonusDefense = it.Def; e.bonusSTR = it.Str; e.bonusAGI = it.Agi;
                e.bonusCON = it.Con; e.bonusSPR = it.Spr; e.bonusINT = it.Spr;
                e.bonusHP = it.Hp; e.bonusSP = it.Sp; e.bonusSpeed = it.MoveSpeed;
                e.maxDurability = Mathf.Max(1, it.Durability); e.durability = e.maxDurability;
                d = e;
                d.itemType = ItemType.Equipment;
            }
            else if (it.Type == 57 || it.Type == 58 || it.Type == 71 || it.Type == 66)
            {
                var c = ScriptableObject.CreateInstance<ConsumableData>();
                c.requiredLevel = it.Level; d = c; d.itemType = ItemType.Consumable;
            }
            else
            {
                d = ScriptableObject.CreateInstance<ItemData>();
                d.itemType = it.Type switch { 42 => ItemType.Quest, 49 => ItemType.Gem, 90 => ItemType.Mount, 41 => ItemType.Material, _ => ItemType.Other };
            }
            d.hideFlags = HideFlags.DontSave;
            d.name = "pko_item_" + it.Id;
            d.itemId = it.Id; d.itemName = it.Name; d.description = it.Description;
            d.maxStack = it.Stack; d.sellPrice = it.Price / 2; d.buyPrice = it.Price;
            return d;
        }
    }
}
