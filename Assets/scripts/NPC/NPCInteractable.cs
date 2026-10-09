using UnityEngine;
using TOP.Core;
using TOP.Player;
using Mirror;
using System;

namespace TOP.NPC
{
    public partial class NPCInteractable : NetworkBehaviour, IInteractable
    {
        [Header("NPC Info")]
        [SerializeField] private string npcId;
        [SerializeField] private string npcName = "NPC";
        [SerializeField] private NPCType npcType = NPCType.Merchant;
        [SerializeField] private string mapName = "garner";
        [SerializeField] private Sprite npcPortrait;

        [Header("Interaction")]
        [SerializeField] private float interactionRange = 3f;
        public float InteractionRange => interactionRange;
        [SerializeField] private string[] dialogueLines;
        [SerializeField] private string[] shopItems;

        [Header("Quests")]
        [SerializeField] private string[] availableQuests;
        [SerializeField] private string[] completesQuests;

        [Header("Teleporter (apenas NPCType.Teleporter)")]
        [SerializeField] private string teleportDestinationMap = "";
        [SerializeField] private Vector3 teleportDestinationPosition;

        public void Interact(uint interactorId) 
        {
            if (NetworkServer.spawned.TryGetValue(interactorId, out NetworkIdentity identity))
            {
                PlayerMovement player = identity.GetComponent<PlayerMovement>();
                if (player != null) OnInteract(player);
            }
        }
        public void ShowInteractionUI() { if (interactIndicator != null) interactIndicator.SetActive(true); }
        public void HideInteractionUI() { if (interactIndicator != null) interactIndicator.SetActive(false); }

        [Header("Visual")]
        [SerializeField] private GameObject interactIndicator;
        [SerializeField] private Animator npcAnimator;

        [Header("Animation")]
        [SerializeField] private string idleAnimation = "Idle";
        [SerializeField] private string talkAnimation = "Talk";
        [SerializeField] private string greetAnimation = "Greet";

        public string NpcId => npcId;
        public string NpcName => npcName;
        public NPCType NpcType => npcType;

        public event Action<PlayerMovement> OnPlayerInteract;

        void Start()
        {
            if (npcAnimator != null)
                npcAnimator.SetTrigger(idleAnimation);
        }

        [Server]
        public void OnInteract(PlayerMovement player)
        {
            if (!CanInteract(player)) return;

            OnPlayerInteract?.Invoke(player);

            FacePlayer(player.transform.position);
            RpcPlayGreeting(player.transform.position);

            OpenNPCInterface(player);
            if (npcType != NPCType.Hairdresser && (npcType != NPCType.Blacksmith || ShopItemIds().Length > 0)
                && npcType != NPCType.Teleporter)
                TargetOpenDialogue(player.connectionToClient, dialogueLines ?? Array.Empty<string>(), ShopItemIds(),
                    QuestIds(availableQuests), QuestIds(completesQuests));
        }

        [ClientRpc]
        void RpcPlayGreeting(Vector3 playerPosition)
        {
            FacePlayer(playerPosition);
            if (npcAnimator != null)
            {
                npcAnimator.SetTrigger(greetAnimation);
                npcAnimator.SetTrigger(talkAnimation);
            }

        }

        void FacePlayer(Vector3 position)
        {
            var direction = new Vector3(position.x, transform.position.y, position.z) - transform.position;
            if (direction.sqrMagnitude > .001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        public string GetInteractionName()
        {
            return npcType switch
            {
                NPCType.Merchant => $"Comprar/Vender - {npcName}",
                NPCType.QuestGiver => $"Quest - {npcName}",
                NPCType.Blacksmith => $"Forjar - {npcName}",
                NPCType.Healer => $"Curar - {npcName}",
                NPCType.Banker => $"Banco - {npcName}",
                NPCType.GuildMaster => $"Guilda - {npcName}",
                NPCType.SkillMaster => $"Skills - {npcName}",
                NPCType.StableMaster => $"Estábulo - {npcName}",
                NPCType.Teleporter => $"Teleporte - {npcName}",
                NPCType.Hairdresser => $"Salão de Beleza - {npcName}",
                NPCType.Other => $"Falar - {npcName}",
                _ => $"Interagir - {npcName}"
            };
        }

        public float GetInteractionRange() => interactionRange;

        public bool CanInteract(PlayerMovement player)
        {
            if (player == null || !isActiveAndEnabled) return false;
            var controller = player.GetComponent<PlayerController>();
            var stats = player.GetComponent<PlayerStats>();
            var combat = player.GetComponent<PlayerCombat>();
            if (controller == null || !controller.IsInitialized || stats == null || stats.IsDead
                || controller.MapName != mapName
                || (player.GetComponent<PlayerTrade>()?.InTrade ?? false) || (combat != null && combat.DuelOpponentNetId != 0)) return false;
            float distance = Vector3.Distance(transform.position, player.transform.position);
            return distance <= interactionRange;
        }

        private void OpenNPCInterface(PlayerMovement player)
        {
            var controller = player.GetComponent<PlayerController>();
            if (npcType == NPCType.Hairdresser)
            {
                controller?.RpcOpenHairSalon();
                return;
            }
            if (npcType == NPCType.Blacksmith && ShopItemIds().Length == 0)
            {
                controller?.RpcOpenForge();
                return;
            }
            if (npcType == NPCType.Teleporter)
            {
                if (controller != null && !string.IsNullOrEmpty(teleportDestinationMap))
                {
                    controller.Teleport(teleportDestinationMap, teleportDestinationPosition);
                    controller.RpcShowMessage("Voce foi transportado para " + teleportDestinationMap + ".", PlayerMessageType.Info);
                }
                else controller?.RpcShowMessage("Destino original deste teleporte ainda nao configurado.", PlayerMessageType.Warning);
                return;
            }

            HandleQuestInteraction(player);

        }

        // Conecta este NPC ao sistema de quests: marca progresso de objetivos "falar com",
        // entrega automaticamente quests completaveis aqui, e oferece as quests disponiveis
        // listadas em availableQuests (ids numericos de QuestTable).
        [Server]
        private void HandleQuestInteraction(PlayerMovement player)
        {
            var pq = player.GetComponent<TOP.Player.PlayerQuests>();
            if (pq == null || string.IsNullOrEmpty(npcId)) return;

            pq.ServerNotifyTalk(npcId);

        }

        private void HealPlayer(PlayerMovement player)
        {
            PlayerStats stats = player.GetComponent<PlayerStats>();
            if (stats != null)
            {
                stats.Heal(stats.MaxHealth);
                stats.RestoreMana(stats.MaxMana);
            }
        }
    }
}