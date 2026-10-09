using System;
using System.Collections.Generic;
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
        List<BoatData> ownedBoats = new List<BoatData>();
        public IReadOnlyList<BoatData> OwnedBoats => ownedBoats;

        void OnBoatsChanged(string oldValue, string newValue)
        {
            if (!isServer) ownedBoats = JsonUtility.FromJson<BoatList>(newValue).Boats;
        }

        [Server]
        void SynchronizeBoats(List<BoatData> boats)
        {
            ownedBoats = boats ?? new List<BoatData>();
            boatsState = JsonUtility.ToJson(new BoatList { Boats = ownedBoats });
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
