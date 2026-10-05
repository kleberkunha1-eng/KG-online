using UnityEngine;
using TOP.Core;
using TOP.Player;
using Mirror;
using System;

namespace TOP.NPC
{
    public class NPCInteractable : NetworkBehaviour, IInteractable
    {
        [Header("NPC Info")]
        [SerializeField] private string npcId;
        [SerializeField] private string npcName = "NPC";
        [SerializeField] private NPCType npcType = NPCType.Merchant;
        [SerializeField] private Sprite npcPortrait;

        [Header("Interaction")]
        [SerializeField] private float interactionRange = 3f;
        public float InteractionRange => interactionRange;
        [SerializeField] private string[] dialogueLines;
        [SerializeField] private string[] shopItems;

        [Header("Quests")]
        [SerializeField] private string[] availableQuests;
        [SerializeField] private string[] completesQuests;

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

        public void OnInteract(PlayerMovement player)
        {
            if (!CanInteract(player)) return;

            OnPlayerInteract?.Invoke(player);

            if (npcAnimator != null)
            {
                npcAnimator.SetTrigger(greetAnimation);
                npcAnimator.SetTrigger(talkAnimation);
            }

            transform.LookAt(new Vector3(player.transform.position.x, transform.position.y, player.transform.position.z));

            OpenNPCInterface(player);
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
                NPCType.Other => $"Falar - {npcName}",
                _ => $"Interagir - {npcName}"
            };
        }

        public float GetInteractionRange() => interactionRange;

        public bool CanInteract(PlayerMovement player)
        {
            if (player == null) return false;
            float distance = Vector3.Distance(transform.position, player.transform.position);
            return distance <= interactionRange;
        }

        private void OpenNPCInterface(PlayerMovement player)
        {
            // Implementação de UI de NPC
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