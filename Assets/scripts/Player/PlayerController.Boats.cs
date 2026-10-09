using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using TOP.Data;
using TOP.Network;
using TOP.Services;
using UnityEngine;

namespace TOP.Player
{
    public partial class PlayerController
    {
        [Serializable] sealed class BoatList { public List<BoatData> Boats = new List<BoatData>(); }
        [SyncVar(hook = nameof(OnBoatsChanged))] string boatsState = "{\"Boats\":[]}";
        [SyncVar] public bool BoatOperationPending;
        [SyncVar] public bool BoatOwnershipAvailable;
        [SyncVar] public bool BoatServicesAvailable;
        List<BoatData> ownedBoats = new List<BoatData>();
        public IReadOnlyList<BoatData> OwnedBoats => ownedBoats;

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

        [Command]
        public async void CmdMaintainBoat(string boatId, int action)
        {
            var movement = GetComponent<PlayerMovement>();
            var npc = movement != null ? movement.ActiveNpc : null;
            var inventory = GetComponent<PlayerInventory>();
            if (!IsInitialized || !BoatOwnershipAvailable || !BoatServicesAvailable || BoatOperationPending
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
