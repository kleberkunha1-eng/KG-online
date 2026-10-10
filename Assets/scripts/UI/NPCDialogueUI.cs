using System;
using Mirror;
using TOP.Core;
using TOP.Data;
using TOP.NPC;
using TOP.Player;
using UnityEngine;

namespace TOP.UI
{
    public sealed class NPCDialogueUI : MonoBehaviour
    {
        static NPCDialogueUI instance;
        NPCInteractable npc;
        string[] lines = Array.Empty<string>();
        int[] items = Array.Empty<int>(), offers = Array.Empty<int>(), turnIns = Array.Empty<int>();
        Rect window = new Rect(60, 100, 430, 430);
        Vector2 scroll;
        string quantity = "1";
        bool selling;
        int selectedBoat = 1, engineIndex, bowIndex, cannonIndex, componentIndex;
        string boatName = "Meu barco";
        public static bool BlocksMouse => instance != null && instance.npc != null;

        public static void Close()
        {
            if (instance != null) instance.npc = null;
        }

        public static void Open(NPCInteractable npc, string[] lines, int[] items, int[] offers, int[] turnIns)
        {
            if (instance == null)
            {
                instance = new GameObject("NPCDialogueUI").AddComponent<NPCDialogueUI>();
                DontDestroyOnLoad(instance.gameObject);
            }
            instance.npc = npc;
            instance.lines = lines;
            instance.items = items;
            instance.offers = offers;
            instance.turnIns = turnIns;
            instance.scroll = Vector2.zero;
            instance.quantity = "1";
            instance.selling = false;
        }

        void Update()
        {
            var player = NetworkClient.localPlayer;
            if (npc == null) return;
            if (player == null || Input.GetKeyDown(KeyCode.Escape)
                || Vector3.Distance(player.transform.position, npc.transform.position) > npc.InteractionRange
                || player.GetComponent<PlayerStats>().IsDead) npc = null;
        }

        void OnGUI()
        {
            if (npc == null || NetworkClient.localPlayer == null) return;
            window = GameWindowControls.Window(7962, window, Draw, npc.NpcName, () => npc = null);
        }

        bool ReadQuantity(out int amount)
        {
            if (int.TryParse(quantity, out amount) && amount > 0) return true;
            UIManager.Instance?.ShowMessage("Informe uma quantidade inteira maior que zero.", PlayerMessageType.Warning);
            return false;
        }

        void Draw(int id)
        {
            if (npc == null) return;
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var line in lines) GUILayout.Label(line);
            if (lines.Length == 0) GUILayout.Label(npc.GetInteractionName());
            if (npc.IsArenaAdministrator)
            {
                GUILayout.Label("Medal of Valor: nivel >25, 50000 ouro; 20 arenas isoladas. Ao registrar voce aceita o confronto.");
                var arenaBag = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
                var medal = arenaBag != null ? arenaBag.GetSlot(arenaBag.FindItemSlot(3849)) : null;
                if (medal != null) GUILayout.Label("Honra: " + medal.MedalHonor + " | Vitorias: " + medal.MedalWins + " | Participacoes: " + medal.MedalEntries + " | K/D: " + medal.MedalKills + "/" + medal.MedalDeaths);
                if (GUILayout.Button("Obter Medal of Valor")) npc.CmdObtainArenaMedal();
                if (GUILayout.Button("Inscrever desafio individual")) npc.CmdArenaRegister(false, false);
                if (GUILayout.Button("Inscrever grupo (lider; ate 5 membros)")) npc.CmdArenaRegister(true, false);
                if (GUILayout.Button("Cancelar inscricao")) npc.CmdArenaRegister(false, true);
            }
            if (npc.NpcId == "87" || npc.NpcId == "88") DrawBoats();
            if (npc.NpcId == "120" || npc.NpcId == "122" || npc.NpcId == "141") DrawFreight();
            if (offers.Length > 0 || turnIns.Length > 0)
            {
                GUILayout.Label("Missoes");
                foreach (int quest in offers)
                    if (QuestTable.All.TryGetValue(quest, out var def) && (!OriginalQuestCatalog.All.ContainsKey(quest)
                        || NetworkClient.localPlayer.GetComponent<PlayerQuests>().CanBeginOriginal(quest)) && GUILayout.Button("Consultar: " + def.Name))
                        npc.CmdQuestAction(quest, false);
                foreach (int quest in turnIns)
                    if (QuestTable.All.TryGetValue(quest, out var def) && (!OriginalQuestCatalog.All.ContainsKey(quest)
                        || NetworkClient.localPlayer.GetComponent<PlayerQuests>().CanResultOriginal(quest)) && GUILayout.Button("Entregar: " + def.Name))
                        npc.CmdQuestAction(quest, true);
            }

            void DrawFreight()
            {
                var controller = NetworkClient.localPlayer.GetComponent<PlayerController>();
                var inventory = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
                if (controller == null || inventory == null) return;
                GUILayout.Space(8);
                GUILayout.Label("Frete naval - recursos: 10 unidades por pacote. Capacidade por volume de carga do barco.");
                foreach (var boat in controller.OwnedBoats)
                {
                    int capacity;
                    try { capacity = BoatCatalog.Quote(boat).Capacity; }
                    catch (InvalidOperationException) { continue; }
                    int loaded = BoatCatalog.CargoQuantity(boat);
                    GUILayout.Label(boat.Name + " - porao " + loaded + "/" + capacity + (boat.IsSunk ? " - afundado" : ""));
                    foreach (int resourceId in new[] { 4543, 4544, 4545, 4546 })
                    {
                        if (!PkoTables.Items.TryGetValue(resourceId, out var resource)
                            || !BoatCatalog.TryGetPackedItem(resourceId, out int pileId)
                            || !PkoTables.Items.TryGetValue(pileId, out var pile)) continue;
                        bool available = GUI.enabled;
                        GUI.enabled = available && !controller.BoatOperationPending && !boat.IsSunk && loaded < capacity
                            && inventory.GetQuestMaterialCount(resourceId) >= BoatCatalog.ResourcePackQuantity;
                        if (GUILayout.Button("Embalar " + resource.Name + " x10 -> " + pile.Name + " x1"))
                            controller.CmdPackBoatCargo(boat.Id, resourceId, BoatCatalog.ResourcePackQuantity);
                        GUI.enabled = available;
                    }
                    if (boat.Cargo != null)
                        foreach (var cargo in boat.Cargo)
                        {
                            if (cargo == null || cargo.Quantity <= 0 || !PkoTables.Items.TryGetValue(cargo.ItemId, out var pile)) continue;
                            bool available = GUI.enabled;
                            GUI.enabled = available && !controller.BoatOperationPending && !boat.IsSunk;
                            if (GUILayout.Button("Entregar " + pile.Name + " x" + cargo.Quantity + " - "
                                + (ulong)pile.Price * (ulong)cargo.Quantity + " ouro"))
                                controller.CmdDeliverBoatCargo(boat.Id, cargo.ItemId);
                            GUI.enabled = available;
                        }
                }
                GUILayout.Label("Entrega remove os pacotes do porao e credita a recompensa apenas apos confirmacao do save.");
            }

            void DrawBoats()
            {
                var controller = NetworkClient.localPlayer.GetComponent<PlayerController>();
                GUILayout.Label("Frota (" + controller.OwnedBoats.Count + "/3)");
                foreach (var boat in controller.OwnedBoats)
                {
                    GUILayout.Label(boat.Name + " - " + (BoatCatalog.Definitions.TryGetValue(boat.TypeId, out var ship)
                        ? ship.Name : "Tipo nao configurado " + boat.TypeId) + " - porto " + boat.BerthId
                        + " - HP " + boat.Health + " - combustivel " + boat.Fuel + (boat.IsSunk ? " - afundado" : ""));
                    if (npc.NpcId == "88" && boat.BerthId == 1)
                    {
                        bool launchPreviousService = GUI.enabled;
                        GUI.enabled = launchPreviousService && controller.BoatOwnershipAvailable && controller.BoatServicesAvailable
                            && !controller.BoatOperationPending && !boat.IsSunk && boat.Health > 0;
                        if (GUILayout.Button("Lancamento do " + boat.Name + " ? Guppy e configuracao salva"))
                            controller.CmdLaunchBoat(boat.Id);
                        GUI.enabled = launchPreviousService;
                        bool previousService = GUI.enabled;
                        GUI.enabled = previousService && controller.BoatServicesAvailable && !controller.BoatOperationPending;
                        if (boat.IsSunk)
                        {
                            GUI.enabled &= controller.Gold >= 1000;
                            if (GUILayout.Button("Resgatar " + boat.Name + " - 1000 ouro")) controller.CmdMaintainBoat(boat.Id, 2);
                        }
                        else
                        {
                            int repair = BoatCatalog.MaintenancePrice(boat, controller.Level, false);
                            int refuel = BoatCatalog.MaintenancePrice(boat, controller.Level, true);
                            GUI.enabled = previousService && controller.BoatServicesAvailable && !controller.BoatOperationPending
                                && boat.Health < BoatCatalog.MaximumHealth(boat) && controller.Gold >= (ulong)repair;
                            if (GUILayout.Button("Reparar " + boat.Name + " - " + repair + " ouro")) controller.CmdMaintainBoat(boat.Id, 0);
                            GUI.enabled = previousService && controller.BoatServicesAvailable && !controller.BoatOperationPending
                                && boat.Fuel < BoatCatalog.Quote(boat).Fuel && controller.Gold >= (ulong)refuel;
                            if (GUILayout.Button("Abastecer " + boat.Name + " - " + refuel + " ouro")) controller.CmdMaintainBoat(boat.Id, 1);
                        }
                        GUI.enabled = previousService;
                    }
                }
                GUILayout.Label(npc.NpcId == "88" ? "Lance pelo cais de Shirley; atraque junto ao ancoradouro marcado no mar." : "Navegacao naval disponivel no porto de Argent.");
                if (npc.NpcId == "88" && !controller.BoatServicesAvailable)
                    GUILayout.Label("API naval ainda nao confirmou suporte a manutencao/resgate.");
                if (!controller.BoatOwnershipAvailable)
                    GUILayout.Label("API naval ainda nao atualizada: construcao bloqueada para proteger seu ouro.");
                if (npc.NpcId != "87") return;
                foreach (int type in new[] { 1, 2, 3, 6 })
                    if (GUILayout.Button(BoatCatalog.Definitions[type].Name + " (nivel " + BoatCatalog.Definitions[type].MinimumLevel + ")"))
                    { selectedBoat = type; engineIndex = bowIndex = cannonIndex = componentIndex = 0; }
                var definition = BoatCatalog.Definitions[selectedBoat];
                GUILayout.Label("Construir: " + definition.Name);
                boatName = GUILayout.TextField(boatName, 16);
                engineIndex = GUILayout.SelectionGrid(engineIndex, Array.ConvertAll(definition.Engines, id => "Motor " + id), 2);
                bowIndex = GUILayout.SelectionGrid(bowIndex, Array.ConvertAll(definition.Bows, id => "Proa " + id), 3);
                cannonIndex = GUILayout.SelectionGrid(cannonIndex, Array.ConvertAll(definition.Cannons, id => "Canhao " + id), 3);
                componentIndex = GUILayout.SelectionGrid(componentIndex, Array.ConvertAll(definition.Components, id => "Componente " + id), 2);
                var quote = BoatCatalog.Quote(selectedBoat, definition.Engines[engineIndex], definition.Bows[bowIndex],
                    definition.Cannons[cannonIndex], definition.Components[componentIndex]);
                GUILayout.Label("Preco original: " + quote.Price + " ouro; HP " + quote.Health + "; combustivel " + quote.Fuel
                    + "; carga " + quote.Capacity);
                bool previous = GUI.enabled;
                GUI.enabled = previous && controller.BoatOwnershipAvailable && !controller.BoatOperationPending && controller.OwnedBoats.Count < BoatCatalog.MaximumBoats
                    && BoatCatalog.CanBuild(selectedBoat, controller.Level, controller.Job) && BoatCatalog.ValidName(boatName.Trim())
                    && controller.Gold >= (ulong)quote.Price;
                if (GUILayout.Button(controller.BoatOperationPending ? "Salvando construcao..." : "Construir e salvar"))
                    controller.CmdBuildBoat(selectedBoat, boatName, definition.Engines[engineIndex], definition.Bows[bowIndex],
                        definition.Cannons[cannonIndex], definition.Components[componentIndex]);
                GUI.enabled = previous;
            }
            if (GUILayout.Button("Diario de missoes")) QuestLogUI.ToggleLocal();
            if (npc.NpcType == NPCType.Blacksmith && GUILayout.Button("Abrir forja")) npc.CmdOpenForge();
            if (npc.NpcType == NPCType.GuildMaster && GUILayout.Button("Abrir guilda")) GuildUI.ToggleLocal();
            if (npc.FullHealCost >= 0)
            {
                GUILayout.Label("Cura completa: " + npc.FullHealCost + " ouro. Gratuita abaixo do nivel 6 com missao 500 concluida.");
                if (GUILayout.Button("Restaurar vida, mana e stamina")) npc.CmdFullHeal();
            }
            foreach (var recipe in npc.Recipes)
            {
                if (recipe == null || !PkoTables.Items.TryGetValue(recipe.ResultItemId, out var result)
                    || !PkoTables.Items.TryGetValue(recipe.MaterialItemId, out var material)
                    || !PkoTables.Items.TryGetValue(recipe.BottleItemId, out var bottle)) continue;
                GUILayout.Label(material.Name + " x" + recipe.MaterialQuantity + " + " + bottle.Name
                    + " x1 + " + recipe.GoldCost + " ouro");
                if (GUILayout.Button("Preparar: " + result.Name)) npc.CmdCraftRecipe(recipe.ResultItemId);
            }
            if (items.Length > 0)
            {
                GUILayout.Label("Ouro: " + NetworkClient.localPlayer.GetComponent<PlayerController>().Gold);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Comprar")) { selling = false; scroll = Vector2.zero; }
                if (GUILayout.Button("Vender")) { selling = true; scroll = Vector2.zero; }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Quantidade", GUILayout.Width(100));
                quantity = GUILayout.TextField(quantity, GUILayout.Width(80));
                GUILayout.EndHorizontal();
                if (!selling)
                    foreach (int itemId in items)
                    {
                        if (!PkoTables.Items.TryGetValue(itemId, out var item)) continue;
                        if (GUILayout.Button(item.Name + " - " + item.Price + " ouro/un. (max " + item.Stack + ")")
                            && ReadQuantity(out int amount)) npc.CmdBuyItem(itemId, amount);
                    }
                else
                {
                    GUILayout.Label("Venda por metade do preco base. Refino/gemas e itens equipados sao protegidos.");
                    var inventory = NetworkClient.localPlayer.GetComponent<PlayerInventory>();
                    for (int i = 0; i < inventory.totalSlots; i++)
                    {
                        var slot = inventory.GetSlot(i);
                        if (slot == null || slot.IsEquipped || !PkoTables.Items.TryGetValue(slot.ItemId, out var item)) continue;
                        GUI.enabled = item.Tradeable && item.Price >= 2 && slot.RefineLevel == 0
                            && Array.TrueForAll(slot.Gems, gem => gem < 0);
                        if (GUILayout.Button((i + 1) + ": " + item.Name + " x" + slot.Quantity + " - " + item.Price / 2 + " ouro/un.")
                            && ReadQuantity(out int amount)) npc.CmdSellItem(i, amount);
                        GUI.enabled = true;
                    }
                }
            }
            else if (npc.NpcType == NPCType.Merchant) GUILayout.Label("Este NPC ainda nao tem catalogo de loja importado.");
            else if (npc.NpcType == NPCType.Banker)
            {
                GUILayout.Label("Armazenamento pessoal original: 32 espacos em uma pagina; ouro nao e depositado.");
                var controller = NetworkClient.localPlayer.GetComponent<PlayerController>();
                GUI.enabled = controller != null && controller.BankStorageAvailable;
                if (GUILayout.Button("Abrir banco")) npc.CmdOpenBank();
                GUI.enabled = true;
                if (controller != null && !controller.BankStorageAvailable) GUILayout.Label("Banco aguardando migracao compativel da API.");
            }
            else if ((npc.NpcType == NPCType.Healer && npc.FullHealCost < 0 && npc.Recipes.Count == 0)
                || npc.NpcType == NPCType.StableMaster
                || npc.NpcType == NPCType.SkillMaster)
                GUILayout.Label("Servico original ainda nao importado; nenhuma cobranca sera realizada.");
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, window.width - 30, 22));
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }
}
