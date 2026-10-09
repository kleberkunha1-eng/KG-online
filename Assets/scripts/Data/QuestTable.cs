using System.Collections.Generic;

namespace TOP.Data
{
    public enum QuestObjectiveType { TalkTo, Kill, Collect }

    public class QuestCollectionItem
    {
        public int ItemId;
        public int Quantity;
    }

    public class QuestObjective
    {
        public QuestObjectiveType Type;
        // TalkTo: NpcId (NPCInteractable.NpcId) do alvo.
        // Kill: substring (case-insensitive) do nome do monstro (EnemyStats).
        // Collect: substring (case-insensitive) do nome do item (ItemDatabase.itemName).
        public string Target;
        public int ItemId; // Original item ID; zero preserves legacy name-based definitions.
        public int Required = 1;
        public QuestCollectionItem[] CollectionItems;
    }

    public class QuestDef
    {
        public int Id;
        public string Name;
        public string Description;
        public int RequiredLevel;
        public int MaximumLevel; // Zero means no upper acceptance limit.
        public int PrerequisiteId; // 0 = nenhum
        public QuestObjective Objective;
        public int RewardExp;
        public int RewardGold;
        public string RewardItemName = ""; // resolvido por nome contra ItemDatabase (vazio = sem item)
        public int RewardItemQty;
        public int RewardItemId;
        // NPC que entrega e/ou recebe a quest (NPCInteractable.NpcId). Vazio = qualquer NPC marcado
        // com esta quest em availableQuests/completesQuests.
        public string GiverNpcId = "";
        public string TurnInNpcId = "";

        public bool CanAcceptAtLevel(int level) => level >= RequiredLevel && (MaximumLevel == 0 || level <= MaximumLevel);
    }

    // Tabela de quests seedada com os IDs e nomes REAIS do cliente original
    // (scripts/lua/mission/missioninfo.lua). Os dados detalhados de objetivo/recompensa de cada
    // missao estao compilados em mission.bin (binario, nao legivel como texto), entao os objetivos
    // abaixo sao inferidos de forma funcional a partir do nome de cada missao (ex.: "Battle
    // Assessment" = matar monstros, "Leaves Collection" = coletar item) para fornecer um sistema de
    // quests jogavel de ponta a ponta com os IDs/nomes autenticos.
    public static class QuestTable
    {
        public static readonly Dictionary<int, QuestDef> All = new Dictionary<int, QuestDef>();

        static QuestTable()
        {
            void Add(QuestDef q) => All[q.Id] = q;

            // ---- Cidade 1 ----
            Add(new QuestDef { Id = 1, Name = "Welcome", Description = "Fale com o guia da cidade para comecar sua jornada.", Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "town1_guide" }, RewardExp = 20, RewardGold = 50 });
            Add(new QuestDef { Id = 701, Name = "Blacksmith's Greetings", Description = "Visite o ferreiro da cidade.", PrerequisiteId = 1, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "blacksmith" }, RewardExp = 40, RewardGold = 100 });
            Add(new QuestDef { Id = 702, Name = "Tailor's Greetings", Description = "Visite o alfaiate da cidade.", PrerequisiteId = 1, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "tailor" }, RewardExp = 40, RewardGold = 100 });
            Add(new QuestDef { Id = 703, Name = "Physician's Greetings", Description = "Visite o medico da cidade.", PrerequisiteId = 1, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "physician" }, RewardExp = 40, RewardGold = 100 });
            Add(new QuestDef { Id = 704, Name = "Battle Assessment", Description = "Derrote monstros para provar seu valor em combate.", RequiredLevel = 2, Objective = new QuestObjective { Type = QuestObjectiveType.Kill, Target = "rat", Required = 3 }, RewardExp = 300, RewardGold = 150 });
            Add(new QuestDef { Id = 705, Name = "Courage Certificate", Description = "Retorne ao capitao da guarda apos provar seu valor.", PrerequisiteId = 704, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "guard_captain" }, RewardExp = 250, RewardGold = 200 });

            // ---- Cidade 2 ----
            Add(new QuestDef { Id = 2, Name = "Welcome", Description = "Fale com o guia da cidade para comecar sua jornada.", Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "town2_guide" }, RewardExp = 20, RewardGold = 50 });
            Add(new QuestDef { Id = 707, Name = "Blacksmith's Greetings", Description = "Visite o ferreiro da cidade.", PrerequisiteId = 2, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "blacksmith" }, RewardExp = 40, RewardGold = 100 });
            Add(new QuestDef { Id = 708, Name = "Tailor's Greetings", Description = "Visite o alfaiate da cidade.", PrerequisiteId = 2, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "tailor" }, RewardExp = 40, RewardGold = 100 });
            Add(new QuestDef { Id = 709, Name = "Nurse's Greetings", Description = "Visite a enfermeira da cidade.", PrerequisiteId = 2, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "nurse" }, RewardExp = 40, RewardGold = 100 });
            Add(new QuestDef { Id = 710, Name = "Battle Assessment", Description = "Derrote monstros para provar seu valor em combate.", RequiredLevel = 2, Objective = new QuestObjective { Type = QuestObjectiveType.Kill, Target = "wolf", Required = 3 }, RewardExp = 300, RewardGold = 150 });
            Add(new QuestDef { Id = 711, Name = "Righteous Document", Description = "Retorne ao capitao da guarda apos provar seu valor.", PrerequisiteId = 710, Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "guard_captain" }, RewardExp = 250, RewardGold = 200 });

            // ---- Cidade 3 ----
            Add(new QuestDef { Id = 3, Name = "Welcome", Description = "Fale com o guia da cidade para comecar sua jornada.", Objective = new QuestObjective { Type = QuestObjectiveType.TalkTo, Target = "town3_guide" }, RewardExp = 20, RewardGold = 50 });
            Add(new QuestDef { Id = 718, Name = "Emergency", Description = "Uma ameaca ronda os arredores da cidade. Elimine-a.", RequiredLevel = 3, Objective = new QuestObjective { Type = QuestObjectiveType.Kill, Target = "boar", Required = 5 }, RewardExp = 400, RewardGold = 200 });
            Add(new QuestDef { Id = 719, Name = "Cactus Invasion", Description = "Monstros cactos estao invadindo a regiao.", RequiredLevel = 3, Objective = new QuestObjective { Type = QuestObjectiveType.Kill, Target = "cactus", Required = 5 }, RewardExp = 400, RewardGold = 200 });
            Add(new QuestDef { Id = 720, Name = "Playful Squidy", Description = "Lulas travessas atrapalham os pescadores locais.", RequiredLevel = 3, Objective = new QuestObjective { Type = QuestObjectiveType.Kill, Target = "squid", Required = 5 }, RewardExp = 400, RewardGold = 200 });
            Add(new QuestDef { Id = 721, Name = "Leaves Collection", Description = "Colete folhas para o herborista.", RequiredLevel = 2, Objective = new QuestObjective { Type = QuestObjectiveType.Collect, Target = "leaf", Required = 10 }, RewardExp = 250, RewardGold = 120 });
            Add(new QuestDef { Id = 733, Name = "Herbs Gathering", Description = "Colete ervas medicinais para o medico.", RequiredLevel = 2, Objective = new QuestObjective { Type = QuestObjectiveType.Collect, Target = "herb", Required = 10 }, RewardExp = 250, RewardGold = 120 });
        }
    }
}
