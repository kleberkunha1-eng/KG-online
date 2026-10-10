using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TOP.Data;
using TOP.Network;
using TOP.NPC;
using TOP.Services;
using UnityEngine;
using UnityEngine.AI;

namespace TOP.Player
{
    public partial class PlayerController
    {
        [Serializable] sealed class BoatList { public List<BoatData> Boats = new List<BoatData>(); }
        [SyncVar(hook = nameof(OnBoatsChanged))] string boatsState = "{\"Boats\":[]}";
        [SyncVar] public bool BoatOperationPending;
        [SyncVar] public bool BoatOwnershipAvailable;
        [SyncVar] public bool BoatServicesAvailable;
        [SyncVar] public bool BankStorageAvailable;
        [SyncVar] public bool BankOperationPending;
        [SyncVar] public string ActiveBoatId;
        float nextBoatFuelTick;
        List<BoatData> ownedBoats = new List<BoatData>();
        public IReadOnlyList<BoatData> OwnedBoats => ownedBoats;
        public bool IsAboardBoat => !string.IsNullOrEmpty(ActiveBoatId);
        public BoatData ActiveBoat => IsAboardBoat ? ownedBoats.SingleOrDefault(boat => boat.Id == ActiveBoatId) : null;
        public float BoatMovementSpeed
        {
            get
            {
                var boat = ActiveBoat;
                if (boat == null) return 0f;
                try { return Mathf.Max(2f, BoatCatalog.Quote(boat).Speed * .01f); }
                catch (InvalidOperationException) { return 0f; }
            }
        }

        public static Vector3 ArgentBerthPosition => ResolveHarborGround(WorldFromOriginalCoordinates(2231, 2827));
        public static Vector3 ArgentLaunchPosition => WorldFromOriginalCoordinates(2260, 2829) + Vector3.up * .6f;

        static Vector3 WorldFromOriginalCoordinates(float x, float z) =>
            new Vector3(x - WorldBlockGrid.OriginX, 0f, WorldBlockGrid.OriginZ - z);

        static Vector3 ResolveHarborGround(Vector3 position)
        {
            int layers = LayerMask.GetMask("Terrain", "Ground");
            if (Physics.Raycast(position + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 200f, layers, QueryTriggerInteraction.Ignore))
                position.y = hit.point.y;
            else position.y = .6f;
            return position;
        }

        public static bool IsWaterPosition(Vector3 position)
        {
            float mapX = position.x + WorldBlockGrid.OriginX;
            float mapZ = WorldBlockGrid.OriginZ - position.z;
            if (float.IsNaN(mapX) || float.IsNaN(mapZ) || float.IsInfinity(mapX) || float.IsInfinity(mapZ)
                || mapX < 0f || mapX >= 4096f || mapZ < 0f || mapZ >= 4096f
                || WorldBlockGrid.IsBlocked(position)) return false;
            return !NavMesh.SamplePosition(position, out NavMeshHit ground, .45f, NavMesh.AllAreas);
        }

        void OnBoatsChanged(string oldValue, string newValue)
        {
            if (!isServer) ownedBoats = JsonUtility.FromJson<BoatList>(newValue).Boats;
        }

        [Server]
        void SynchronizeBoats(List<BoatData> boats)
        {
            ownedBoats = boats == null ? new List<BoatData>() : boats.ConvertAll(boat => boat.CopySnapshot());
            boatsState = JsonUtility.ToJson(new BoatList { Boats = ownedBoats });
        }

        [ServerCallback]
        void Update()
        {
            if (!IsAboardBoat || BoatOperationPending || Time.time < nextBoatFuelTick) return;
            ServerApplyBoatFuelTick();
        }

        [Server]
        void ServerApplyBoatFuelTick()
        {
            nextBoatFuelTick = Time.time + 5f;
            var boat = ActiveBoat;
            if (boat == null || boat.IsSunk || DatabaseService.Instance == null) return;
            BoatBuildQuote quote;
            int maximumHealth;
            try { quote = BoatCatalog.Quote(boat); maximumHealth = BoatCatalog.MaximumHealth(boat); }
            catch (InvalidOperationException e) { Debug.LogError("[Boats] Invalid fuel attributes: " + e.Message); return; }
            int consumption = quote.FuelConsumption;
            if (consumption < 0 || boat.Health <= 0) return;
            var updated = boat.CopySnapshot();
            if (updated.Fuel > 0 && consumption > 0) updated.Fuel = Mathf.Max(0, updated.Fuel - consumption);
            bool sunk = false;
            if (updated.Fuel <= 0)
            {
                int loss = Mathf.Max(1, (int)(maximumHealth * .025f));
                updated.Health = Mathf.Max(0, updated.Health - loss);
                if (updated.Health == 0)
                {
                    updated.IsSunk = true;
                    updated.BerthId = 1;
                    sunk = true;
                }
            }
            if (updated.Fuel == boat.Fuel && updated.Health == boat.Health) return;
            PersistBoatStateAsync(updated, sunk ? string.Empty : ActiveBoatId,
                sunk ? ArgentBerthPosition : transform.position,
                sunk ? Quaternion.identity : transform.rotation,
                sunk ? "Seu barco afundou por falta de combustivel. Retornou ao ultimo ancoradouro para resgate." : null);
        }

        // Client npclist IDs are one above the original server NPCDefine freight IDs.
        static bool IsFreightNpc(string npcId) => npcId == "120" || npcId == "122" || npcId == "141";

        bool CanUseFreight(NPCInteractable npc, PlayerMovement movement)
        {
            var inventory = GetComponent<PlayerInventory>();
            var trade = GetComponent<PlayerTrade>();
            var combat = GetComponent<PlayerCombat>();
            var quests = GetComponent<PlayerQuests>();
            if (!IsInitialized || !BoatOwnershipAvailable || !BoatServicesAvailable || BoatOperationPending
                || npc == null || movement == null || !IsFreightNpc(npc.NpcId) || movement.ActiveNpc != npc
                || !npc.CanInteract(movement) || inventory == null || inventory.HasQuestTransaction
                || (trade != null && trade.InTrade) || (combat != null && combat.DuelOpponentNetId != 0)
                || (quests != null && quests.HasPendingCompletion)) return false;
            return true;
        }

        [Command]
        public async void CmdPackBoatCargo(string boatId, int resourceItemId, int resourceQuantity)
        {
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            if (!CanUseFreight(npc, movement))
            { RpcShowMessage("Fale com um agente de frete e aguarde a confirmacao da API naval.", PlayerMessageType.Warning); return; }
            if (!BoatCatalog.TryGetPackedItem(resourceItemId, out int pileItemId) || resourceQuantity <= 0
                || resourceQuantity % BoatCatalog.ResourcePackQuantity != 0)
            { RpcShowMessage("A embalagem exige multiplos de 10 recursos originais.", PlayerMessageType.Warning); return; }
            var boat = ownedBoats.SingleOrDefault(candidate => candidate.Id == boatId);
            if (boat == null || boat.IsSunk)
            { RpcShowMessage("Selecione um barco proprio disponivel para transportar carga.", PlayerMessageType.Warning); return; }
            int bundles = resourceQuantity / BoatCatalog.ResourcePackQuantity;
            int capacity;
            try { capacity = BoatCatalog.Quote(boat).Capacity; }
            catch (InvalidOperationException e)
            { Debug.LogError("[Cargo] Invalid original boat configuration: " + e.Message); return; }
            if (capacity <= 0 || BoatCatalog.CargoQuantity(boat) > capacity - bundles)
            { RpcShowMessage("A carga excede a capacidade original do barco.", PlayerMessageType.Warning); return; }
            var inventory = GetComponent<PlayerInventory>();
            var transaction = inventory.PrepareQuestTransaction(resourceItemId, resourceQuantity, 0, 0, out string error);
            if (transaction == null) { RpcShowMessage(error, PlayerMessageType.Warning); return; }
            var updated = boat.CopySnapshot();
            if (updated.Cargo == null) updated.Cargo = new List<BoatCargoItemData>();
            var cargo = updated.Cargo.SingleOrDefault(item => item.ItemId == pileItemId);
            if (cargo == null) updated.Cargo.Add(new BoatCargoItemData { ItemId = pileItemId, Quantity = bundles });
            else cargo.Quantity = checked(cargo.Quantity + bundles);
            await PersistBoatCargoAsync(updated, transaction, Gold, false,
                "Recursos embalados e salvos no porao de " + boat.Name + ".");
            transaction.Dispose();
        }

        [Command]
        public async void CmdDeliverBoatCargo(string boatId, int pileItemId)
        {
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            if (!CanUseFreight(npc, movement))
            { RpcShowMessage("Fale com um agente de frete e aguarde a confirmacao da API naval.", PlayerMessageType.Warning); return; }
            if (!BoatCatalog.IsCargoPile(pileItemId) || !PkoTables.Items.TryGetValue(pileItemId, out var item)
                || item.Price <= 0)
            { RpcShowMessage("Esta carga nao possui recompensa original configurada.", PlayerMessageType.Warning); return; }
            var boat = ownedBoats.SingleOrDefault(candidate => candidate.Id == boatId);
            var cargo = boat != null && boat.Cargo != null ? boat.Cargo.SingleOrDefault(entry => entry.ItemId == pileItemId) : null;
            if (boat == null || boat.IsSunk || cargo == null || cargo.Quantity <= 0)
            { RpcShowMessage("Carga indisponivel neste barco.", PlayerMessageType.Warning); return; }
            ulong reward = checked((ulong)item.Price * (ulong)cargo.Quantity);
            const ulong maximumExactInteger = 9007199254740991UL;
            if (reward > maximumExactInteger || Gold > maximumExactInteger - reward)
            { RpcShowMessage("Limite de ouro excedido.", PlayerMessageType.Warning); return; }
            ulong updatedGold = Gold + reward;
            var inventory = GetComponent<PlayerInventory>();
            var transaction = inventory.PrepareQuestTransaction(Array.Empty<QuestCollectionItem>(), 0, 0, out string error);
            if (transaction == null) { RpcShowMessage(error, PlayerMessageType.Warning); return; }
            var updated = boat.CopySnapshot();
            updated.Cargo.Remove(updated.Cargo.Single(entry => entry.ItemId == pileItemId));
            await PersistBoatCargoAsync(updated, transaction, updatedGold, true,
                "Carga entregue: " + item.Name + " x" + cargo.Quantity + ", recompensa " + reward + " ouro.");
            transaction.Dispose();
        }

        async System.Threading.Tasks.Task PersistBoatCargoAsync(BoatData updatedBoat,
            PlayerInventory.QuestInventoryTransaction inventoryTransaction, ulong updatedGold, bool changeGold, string successMessage)
        {
            if (BoatOperationPending || DatabaseService.Instance == null) return;
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null) return;
            BoatOperationPending = true;
            try
            {
                CharacterData live = null, prepared = null;
                var saved = await DatabaseService.Instance.PersistCharacterAsync(CharacterId, () =>
                {
                    if (this == null) throw new InvalidOperationException("Player disconnected before cargo save.");
                    live = GetCharacterData();
                    prepared = live.CopySnapshot();
                    int index = prepared.Boats.FindIndex(candidate => candidate.Id == updatedBoat.Id);
                    if (index < 0) throw new InvalidOperationException("Boat ownership changed before cargo save.");
                    prepared.Boats[index] = updatedBoat.CopySnapshot();
                    prepared.BoatServicesVersion = 1;
                    if (inventoryTransaction != null) prepared.Inventory = inventoryTransaction.PreparedItems;
                    if (changeGold) prepared.Gold = updatedGold;
                    return prepared;
                }, TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId), 0);
                if (!saved.success)
                {
                    if (this != null && connectionToClient != null)
                    {
                        RpcShowMessage("Carga nao confirmada; reconecte para recarregar o save autoritativo.", PlayerMessageType.Warning);
                        if (saved.error == "RELOAD_REQUIRED" || saved.error == "SAVE_CONFLICT" || saved.error == "OPERATION_MISMATCH")
                            connectionToClient.Disconnect();
                    }
                    return;
                }
                live.SaveRevision = prepared.SaveRevision;
                live.Boats = prepared.Boats;
                live.BoatServicesVersion = 1;
                live.Gold = prepared.Gold;
                if (this == null) return;
                inventoryTransaction.Commit();
                live.Inventory = inventory.GetInventoryData();
                SynchronizeBoats(prepared.Boats);
                if (changeGold) Gold = updatedGold;
                RpcShowMessage(successMessage, PlayerMessageType.Success);
            }
            catch (Exception e)
            {
                Debug.LogError("[Cargo] Durable operation failed: " + e.Message);
                if (this != null && connectionToClient != null) connectionToClient.Disconnect();
            }
            finally { if (this != null) BoatOperationPending = false; }
        }

        [Command]
        public void CmdLaunchBoat(string boatId)
        {
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            var quests = GetComponent<PlayerQuests>();
            var combat = GetComponent<PlayerCombat>();
            var trade = GetComponent<PlayerTrade>();
            var boat = ownedBoats.SingleOrDefault(candidate => candidate.Id == boatId);
            if (!IsInitialized || IsAboardBoat || BoatOperationPending || !BoatOwnershipAvailable || !BoatServicesAvailable
                || npc == null || npc.NpcId != "88" || MapName != "garner" || !npc.CanInteract(movement)
                || (quests != null && quests.HasPendingCompletion) || (combat != null && combat.DuelOpponentNetId != 0)
                || (trade != null && trade.InTrade) || boat == null || boat.BerthId != 1 || boat.IsSunk || boat.Health <= 0)
            {
                RpcShowMessage("Fale com Shirley no porto de Argent e selecione um barco disponivel no ancoradouro.", PlayerMessageType.Warning);
                return;
            }
            if (!IsWaterPosition(ArgentLaunchPosition))
            {
                RpcShowMessage("O ancoradouro de lancamento esta bloqueado; navegacao cancelada.", PlayerMessageType.Error);
                return;
            }
            var updated = boat.CopySnapshot();
            updated.BerthId = 0;
            PersistBoatStateAsync(updated, boat.Id, ArgentLaunchPosition, Quaternion.Euler(0f, 177f, 0f),
                "Barco lancado. Clique na agua para navegar; atraque no ancoradouro de Argent.");
        }

        [Command]
        public void CmdDockBoat()
        {
            var movement = GetComponent<PlayerMovement>();
            var trade = GetComponent<PlayerTrade>();
            var combat = GetComponent<PlayerCombat>();
            var quests = GetComponent<PlayerQuests>();
            var boat = ActiveBoat;
            if (!IsInitialized || boat == null || boat.IsSunk || BoatOperationPending || !BoatOwnershipAvailable || !BoatServicesAvailable
                || MapName != "garner" || movement == null || boat.BerthId != 0 || (trade != null && trade.InTrade)
                || (combat != null && combat.DuelOpponentNetId != 0) || (quests != null && quests.HasPendingCompletion)
                || Vector3.Distance(transform.position, ArgentBerthPosition) > 8f)
            {
                RpcShowMessage("Aproxime-se pelo mar do ancoradouro de Argent para atracar.", PlayerMessageType.Warning);
                return;
            }
            var updated = boat.CopySnapshot();
            updated.BerthId = 1;
            PersistBoatStateAsync(updated, string.Empty, ArgentBerthPosition, Quaternion.Euler(0f, 177f, 0f),
                "Barco atracado e salvo no ancoradouro de Argent.");
        }

        async void PersistBoatStateAsync(BoatData updatedBoat, string nextActiveBoatId, Vector3 position,
            Quaternion rotation, string successMessage)
        {
            if (BoatOperationPending || DatabaseService.Instance == null) return;
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null) return;
            using (var reservation = inventory.PrepareQuestTransaction(Array.Empty<QuestCollectionItem>(), 0, 0, out string error))
            {
                if (reservation == null)
                {
                    if (!string.IsNullOrEmpty(error)) RpcShowMessage(error, PlayerMessageType.Warning);
                    return;
                }
                BoatOperationPending = true;
                try
                {
                    CharacterData live = null, prepared = null;
                    var saved = await DatabaseService.Instance.PersistCharacterAsync(CharacterId, () =>
                    {
                        if (this == null) throw new InvalidOperationException("Player disconnected before naval save.");
                        live = GetCharacterData();
                        prepared = live.CopySnapshot();
                        int index = prepared.Boats.FindIndex(candidate => candidate.Id == updatedBoat.Id);
                        if (index < 0) throw new InvalidOperationException("Boat ownership changed before navigation save.");
                        prepared.Boats[index] = updatedBoat.CopySnapshot();
                        prepared.BoatServicesVersion = 1;
                        prepared.SetPosition(position);
                        prepared.MapName = MapName;
                        prepared.RotationY = rotation.eulerAngles.y;
                        return prepared;
                    }, TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId), 0);
                    if (!saved.success)
                    {
                        if (this != null && connectionToClient != null)
                        {
                            RpcShowMessage("Estado naval nao confirmado; reconecte para recarregar o save autoritativo.", PlayerMessageType.Warning);
                            if (saved.error == "RELOAD_REQUIRED" || saved.error == "SAVE_CONFLICT" || saved.error == "OPERATION_MISMATCH")
                                connectionToClient.Disconnect();
                        }
                        return;
                    }
                    live.SaveRevision = prepared.SaveRevision;
                    live.Boats = prepared.Boats;
                    live.BoatServicesVersion = 1;
                    live.MapName = prepared.MapName;
                    live.SetPosition(position);
                    live.RotationY = prepared.RotationY;
                    if (this == null) return;
                    SynchronizeBoats(prepared.Boats);
                    ActiveBoatId = nextActiveBoatId;
                    transform.SetPositionAndRotation(position, rotation);
                    var playerMovement = GetComponent<PlayerMovement>();
                    if (playerMovement != null) playerMovement.Stop();
                    nextBoatFuelTick = Time.time + 5f;
                    if (!string.IsNullOrEmpty(successMessage)) RpcShowMessage(successMessage, PlayerMessageType.Success);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Boats] Navigation save failed: " + e.Message);
                    if (this != null && connectionToClient != null) connectionToClient.Disconnect();
                }
                finally { if (this != null) BoatOperationPending = false; }
            }
        }

        void OnGUI()
        {
            if (!isLocalPlayer || !IsAboardBoat) return;
            var boat = ActiveBoat;
            if (boat == null) return;
            GUILayout.BeginArea(new Rect(12, 12, 300, 105), GUI.skin.box);
            GUILayout.Label(boat.Name + "  HP " + boat.Health + "  Combustivel " + boat.Fuel);
            GUILayout.Label("Velocidade " + BoatMovementSpeed.ToString("0.0") + " m/s ? clique na agua para navegar.");
            bool previous = GUI.enabled;
            GUI.enabled = previous && !BoatOperationPending;
            if (GUILayout.Button(BoatOperationPending ? "Salvando estado naval..." : "Atracar em Argent", GUILayout.Height(28)))
                CmdDockBoat();
            GUI.enabled = previous;
            GUILayout.EndArea();
        }

        [Command]
        public async void CmdMaintainBoat(string boatId, int action)
        {
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            var inventory = GetComponent<PlayerInventory>();
            if (!IsInitialized || IsAboardBoat || !BoatOwnershipAvailable || !BoatServicesAvailable || BoatOperationPending
                || npc == null || npc.NpcId != "88" || MapName != "garner" || !npc.CanInteract(movement)
                || inventory == null || DatabaseService.Instance == null || action < 0 || action > 2
                || (GetComponent<PlayerQuests>()?.HasPendingCompletion ?? false)
                || (GetComponent<PlayerCombat>()?.DuelOpponentNetId ?? 0) != 0)
            {
                RpcShowMessage("Fale com Shirley no porto de Argent; aguarde a API naval e as operacoes pendentes.", PlayerMessageType.Warning);
                return;
            }
            var boat = ownedBoats.SingleOrDefault(candidate => candidate.Id == boatId);
            if (boat == null || boat.BerthId != 1 || boat.IsSunk != (action == 2))
            {
                RpcShowMessage("Barco nao disponivel neste porto. Barcos afundados precisam de resgate.", PlayerMessageType.Warning);
                return;
            }
            int maximum, price;
            try
            {
                maximum = action == 0 ? BoatCatalog.MaximumHealth(boat) : BoatCatalog.Quote(boat).Fuel;
                if (action != 2 && (action == 0 ? boat.Health : boat.Fuel) >= maximum)
                { RpcShowMessage("O barco nao precisa desse servico.", PlayerMessageType.Info); return; }
                price = action == 2 ? 1000 : BoatCatalog.MaintenancePrice(boat, Level, action == 1);
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError("[Boats] Invalid original boat configuration: " + e.Message);
                RpcShowMessage("Configuracao do barco invalida; manutencao bloqueada.", PlayerMessageType.Error);
                return;
            }
            if (Gold < (ulong)price)
            { RpcShowMessage("Ouro insuficiente para o servico naval.", PlayerMessageType.Warning); return; }
            using (var reservation = inventory.PrepareQuestTransaction(Array.Empty<QuestCollectionItem>(), 0, 0, out string error))
            {
                if (reservation == null) { RpcShowMessage(error, PlayerMessageType.Warning); return; }
                BoatOperationPending = true;
                try
                {
                    CharacterData live = null, prepared = null;
                    var saved = await DatabaseService.Instance.PersistCharacterAsync(CharacterId, () =>
                    {
                        if (this == null) throw new InvalidOperationException("Player disconnected before naval service.");
                        if (!npc.CanInteract(movement)) throw new InvalidOperationException("Player left the original harbor service.");
                        live = GetCharacterData();
                        if (live.Gold < (ulong)price) throw new InvalidOperationException("Naval service funds changed before save.");
                        prepared = live.CopySnapshot();
                        prepared.Gold -= (ulong)price;
                        prepared.BoatServicesVersion = 1;
                        var target = prepared.Boats.Single(candidate => candidate.Id == boatId);
                        if (action == 0) target.Health = maximum;
                        else if (action == 1) target.Fuel = maximum;
                        else target.IsSunk = false;
                        return prepared;
                    }, TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId), 0);
                    if (!saved.success)
                    {
                        if (this != null && connectionToClient != null)
                        {
                            RpcShowMessage("Servico naval nao confirmado; ouro e barco nao foram alterados nesta sessao.", PlayerMessageType.Warning);
                            if (saved.error == "RELOAD_REQUIRED" || saved.error == "SAVE_CONFLICT" || saved.error == "OPERATION_MISMATCH")
                                connectionToClient.Disconnect();
                        }
                        return;
                    }
                    live.SaveRevision = prepared.SaveRevision;
                    live.Boats = prepared.Boats;
                    if (this == null)
                    { Debug.Log("[Boats] Harbor service persisted after disconnect; reload restores the ship."); return; }
                    Gold -= (ulong)price;
                    SynchronizeBoats(prepared.Boats);
                    RpcShowMessage((action == 0 ? "Reparo" : action == 1 ? "Abastecimento" : "Resgate")
                        + " confirmado e salvo: " + boat.Name + " (" + price + " ouro).", PlayerMessageType.Success);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Boats] Harbor service failed: " + e.Message);
                    if (this != null && connectionToClient != null)
                    {
                        RpcShowMessage("Falha no servico naval. Reconecte para recuperar o estado confirmado.", PlayerMessageType.Error);
                        connectionToClient.Disconnect();
                    }
                }
                finally { if (this != null) BoatOperationPending = false; }
            }
        }

        [Command]
        public async void CmdBuildBoat(int typeId, string boatName, int engineId, int bowId, int cannonId, int componentId)
        {
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            var inventory = GetComponent<PlayerInventory>();
            if (!BoatOwnershipAvailable)
            { RpcShowMessage("A API ainda nao confirmou suporte a propriedade naval. Construcao bloqueada para proteger seu ouro.", PlayerMessageType.Warning); return; }
            if (!IsInitialized || BoatOperationPending || npc == null || npc.NpcId != "87" || MapName != "garner"
                || !npc.CanInteract(movement) || inventory == null || (GetComponent<PlayerQuests>()?.HasPendingCompletion ?? false))
            { RpcShowMessage("Fale com Sinbad e aguarde as operacoes de inventario antes de construir.", PlayerMessageType.Warning); return; }
            boatName = boatName?.Trim();
            if (!BoatCatalog.ValidName(boatName) || !BoatCatalog.IsArgentOffering(typeId) || !BoatCatalog.CanBuild(typeId, Level, Job)
                || ownedBoats.Count >= BoatCatalog.MaximumBoats)
            { RpcShowMessage("Barco indisponivel: verifique nome (2-16 caracteres ASCII), nivel, classe e limite de tres barcos.", PlayerMessageType.Warning); return; }
            if (DatabaseService.Instance == null)
            { RpcShowMessage("Servico de persistencia naval indisponivel.", PlayerMessageType.Error); return; }
            BoatBuildQuote quote;
            try { quote = BoatCatalog.Quote(typeId, engineId, bowId, cannonId, componentId); }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning("[Boats] " + e.Message);
                RpcShowMessage("Selecao de pecas invalida para esse barco.", PlayerMessageType.Warning);
                return;
            }
            if (Gold < (ulong)quote.Price)
            { RpcShowMessage("Ouro insuficiente para construir o barco.", PlayerMessageType.Warning); return; }
            using (var reservation = inventory.PrepareQuestTransaction(Array.Empty<QuestCollectionItem>(), 0, 0, out string error))
            {
                if (reservation == null) { RpcShowMessage(error, PlayerMessageType.Warning); return; }
                BoatOperationPending = true;
                try
                {
                    var definition = BoatCatalog.Definitions[typeId];
                    var boat = new BoatData { Id = Guid.NewGuid().ToString(), Name = boatName, TypeId = typeId, BerthId = 1,
                        HullId = definition.HullId, EngineId = engineId, BowId = bowId, CannonId = cannonId,
                        ComponentId = componentId, Health = quote.Health, Fuel = quote.Fuel };
                    CharacterData live = null, prepared = null;
                    var saved = await DatabaseService.Instance.PersistCharacterAsync(CharacterId, () =>
                    {
                        if (this == null) throw new InvalidOperationException("Player disconnected before boat construction.");
                        live = GetCharacterData();
                        if (live.Gold < (ulong)quote.Price) throw new InvalidOperationException("Boat funds changed before save.");
                        prepared = live.CopySnapshot();
                        prepared.Gold -= (ulong)quote.Price;
                        prepared.Boats = new List<BoatData>(ownedBoats) { boat };
                        return prepared;
                    }, TOPNetworkManager.Instance.GetSessionToken(connectionToClient.connectionId), 0);
                    if (!saved.success)
                    {
                        if (this != null && connectionToClient != null)
                        {
                            RpcShowMessage("Construcao nao confirmada; nenhum barco ou ouro foi alterado nesta sessao.", PlayerMessageType.Warning);
                            if (saved.error == "RELOAD_REQUIRED" || saved.error == "SAVE_CONFLICT" || saved.error == "OPERATION_MISMATCH")
                                connectionToClient.Disconnect();
                        }
                        return;
                    }
                    live.SaveRevision = prepared.SaveRevision;
                    live.Boats = prepared.Boats;
                    if (this == null)
                    { Debug.Log("[Boats] Construction persisted after disconnect; reload restores the ship."); return; }
                    Gold -= (ulong)quote.Price;
                    SynchronizeBoats(prepared.Boats);
                    RpcShowMessage("Barco construido e salvo no porto de Argent: " + boat.Name + ".", PlayerMessageType.Success);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Boats] Construction failed: " + e.Message);
                    if (this != null && connectionToClient != null)
                    {
                        RpcShowMessage("Falha na construcao. Reconecte para recuperar o estado confirmado.", PlayerMessageType.Error);
                        connectionToClient.Disconnect();
                    }
                }
                finally { if (this != null) BoatOperationPending = false; }
            }
        }
    }
}
