using UnityEngine;
using Mirror;
using TOP.Core;
using TOP.Player;
using TOP.Data;

namespace TOP.World
{
    // Objeto interativo de mundo (objevent.txt do cliente original): pontos de salto/teleporte,
    // ancoradouro de navio e eventos de investigacao. Reaproveita a mesma interface IInteractable
    // usada pelos NPCs, entao funciona com o sistema de interacao/raio ja existente sem duplicar
    // infraestrutura. As posicoes originais de cada instancia estao compiladas nos arquivos de
    // mapa binarios do cliente (nao legiveis como texto); este componente deve ser posicionado
    // manualmente nas cenas pelo game designer, usando os tipos/parametros reais de objevent.txt
    // como padrao.
    public class WorldEventObject : NetworkBehaviour, IInteractable
    {
        [Header("Tipo (objevent.txt)")]
        [SerializeField] private int eventId = 1; // 1=Jump Point, 2=Ship Dock, 3=Investigate, 4=Jump Point (Sea)
        [SerializeField] private string objectName = "Ponto de Salto";

        [Header("Interacao")]
        [SerializeField] private float interactionRange = 2f;
        public float InteractionRange => interactionRange;

        [Header("Destino (Jump Point / Jump Point Sea)")]
        [SerializeField] private string destinationMapLabel = "";
        [SerializeField] private Vector3 destinationPosition;

        [Header("Investigate (opcional)")]
        [SerializeField] private string questTalkId = ""; // casado com QuestObjective.Target (TalkTo)

        [Header("Visual")]
        [SerializeField] private GameObject interactIndicator;

        ObjEventDef Def => ObjEventTable.ById.TryGetValue(eventId, out var d) ? d : ObjEventTable.ById[1];

        public void Interact(uint interactorId)
        {
            if (!NetworkServer.spawned.TryGetValue(interactorId, out NetworkIdentity identity)) return;
            var movement = identity.GetComponent<PlayerMovement>();
            if (movement == null) return;
            if (Vector3.Distance(transform.position, movement.transform.position) > interactionRange) return;

            var controller = identity.GetComponent<PlayerController>();
            switch (Def.Kind)
            {
                case ObjEventKind.JumpPoint:
                case ObjEventKind.JumpPointSea:
                    controller?.Teleport(string.IsNullOrEmpty(destinationMapLabel) ? controller.MapName : destinationMapLabel, destinationPosition);
                    controller?.RpcShowMessage("Voce foi transportado.", PlayerMessageType.Info);
                    break;

                case ObjEventKind.ShipDock:
                    controller?.RpcShowMessage("Navio ancorado em " + objectName + ".", PlayerMessageType.Info);
                    break;

                case ObjEventKind.Investigate:
                    if (!string.IsNullOrEmpty(questTalkId))
                        identity.GetComponent<TOP.Player.PlayerQuests>()?.ServerNotifyTalk(questTalkId);
                    controller?.RpcShowMessage(objectName, PlayerMessageType.Info);
                    break;
            }
        }

        public void ShowInteractionUI() { if (interactIndicator != null) interactIndicator.SetActive(true); }
        public void HideInteractionUI() { if (interactIndicator != null) interactIndicator.SetActive(false); }
    }
}
