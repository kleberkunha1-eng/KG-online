using UnityEngine;
using Mirror;
using System;
using TOP.Core;

namespace TOP.Player
{
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimation : NetworkBehaviour
    {
        [Header("Locomotion")]
        [SerializeField] private string moveSpeedParam = "MoveSpeed";
        [SerializeField] private string isMovingParam = "IsMoving";
        [SerializeField] private string isRunningParam = "IsRunning";

        [Header("Combat")]
        [SerializeField] private string attackTrigger = "Attack";
        [SerializeField] private string attackTypeParam = "AttackType";
        [SerializeField] private string isAttackingParam = "IsAttacking";
        [SerializeField] private string hitTrigger = "Hit";
        [SerializeField] private string blockTrigger = "Block";
        [SerializeField] private string dodgeTrigger = "Dodge";

        [Header("Skills")]
        [SerializeField] private string skillTrigger = "Skill";
        [SerializeField] private string skillIdParam = "SkillId";
        [SerializeField] private string isCastingParam = "IsCasting";
        [SerializeField] private string castProgressParam = "CastProgress";

        [Header("Death")]
        [SerializeField] private string dieTrigger = "Die";
        [SerializeField] private string reviveTrigger = "Revive";
        [SerializeField] private string isDeadParam = "IsDead";

        [Header("Idle")]
        [SerializeField] private string idleVarParam = "IdleVariation";
        [SerializeField] private int idleVariations = 3;

        [Header("Emotes")]
        [SerializeField] private string emoteTrigger = "Emote";
        [SerializeField] private string emoteIdParam = "EmoteId";

        [Header("Settings")]
        [SerializeField] private float locomotionBlendSpeed = 8f;
        [SerializeField] private float castBlendSpeed = 5f;

        private Animator anim;
        private PlayerMovement movement;
        private PlayerCombat combat;
        private PlayerStats stats;
        private PlayerSkills skills;

        static readonly int[] EmotePoses = { 2, 3, 18, 19, 20, 21, 22, 23 };
        private TOP.Character.PkoPoseDriver pose;
        private TOP.Character.PkoPoseDriver Pose { get { if (pose == null) pose = GetComponentInChildren<TOP.Character.PkoPoseDriver>(); return pose; } }

        private float currentMoveSpeed;
        private float targetMoveSpeed;
        private float currentCastProgress;
        private float targetCastProgress;
        private int currentIdleVar;

        void Awake()
        {
            var visual=transform.Find("CharacterVisual");
            anim = visual != null ? visual.GetComponentInChildren<Animator>() : GetComponent<Animator>();
            movement = GetComponent<PlayerMovement>();
            combat = GetComponent<PlayerCombat>();
            stats = GetComponent<PlayerStats>();
            skills = GetComponent<PlayerSkills>();

            SubscribeToEvents();
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        private void SubscribeToEvents()
        {
            if (movement != null)
            {
                movement.OnMovementStarted += OnMovementStarted;
                movement.OnMovementStopped += OnMovementStopped;
            }

            if (combat != null)
            {
                combat.OnAttackStarted += OnAttackStarted;
                combat.OnAttackFinished += OnAttackFinished;
                combat.OnTargetChanged += OnTargetChanged;
            }

            if (stats != null)
            {
                stats.OnDeath += OnDeath;
                stats.OnRevive += OnRevive;
                stats.OnHealthUpdated += OnHealthUpdated;
            }

            if (skills != null)
            {
                skills.OnSkillCastStarted += OnSkillCastStarted;
                skills.OnSkillCastFinished += OnSkillCastFinished;
                skills.OnSkillExecuted += OnSkillExecuted;
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (movement != null)
            {
                movement.OnMovementStarted -= OnMovementStarted;
                movement.OnMovementStopped -= OnMovementStopped;
            }

            if (combat != null)
            {
                combat.OnAttackStarted -= OnAttackStarted;
                combat.OnAttackFinished -= OnAttackFinished;
                combat.OnTargetChanged -= OnTargetChanged;
            }

            if (stats != null)
            {
                stats.OnDeath -= OnDeath;
                stats.OnRevive -= OnRevive;
                stats.OnHealthUpdated -= OnHealthUpdated;
            }

            if (skills != null)
            {
                skills.OnSkillCastStarted -= OnSkillCastStarted;
                skills.OnSkillCastFinished -= OnSkillCastFinished;
                skills.OnSkillExecuted -= OnSkillExecuted;
            }
        }

        void Update()
        {
            UpdateLocomotion();
            UpdateCasting();
        }

        private void UpdateLocomotion()
        {
            targetMoveSpeed = movement != null && movement.IsMoving ? (movement.IsRunning ? .5f : .25f) : 0f;
            anim.SetBool(isMovingParam, movement != null && movement.IsMoving);
            anim.SetBool(isRunningParam, movement != null && movement.IsRunning);
            currentMoveSpeed = Mathf.MoveTowards(currentMoveSpeed, targetMoveSpeed,
                locomotionBlendSpeed * Time.deltaTime);

            anim.SetFloat(moveSpeedParam, currentMoveSpeed);
        }

        private void UpdateCasting()
        {
            currentCastProgress = Mathf.MoveTowards(currentCastProgress, targetCastProgress,
                castBlendSpeed * Time.deltaTime);
            anim.SetFloat(castProgressParam, currentCastProgress);
        }

        private void OnMovementStarted()
        {
            anim.SetBool(isMovingParam, true);
        }

        private void OnMovementStopped()
        {
            anim.SetBool(isMovingParam, false);
            anim.SetBool(isRunningParam, false);

            if (idleVariations > 1)
            {
                int newVar = UnityEngine.Random.Range(0, idleVariations);
                while (newVar == currentIdleVar && idleVariations > 1)
                    newVar = UnityEngine.Random.Range(0, idleVariations);
                currentIdleVar = newVar;
                anim.SetFloat(idleVarParam, currentIdleVar);
            }
        }

        private void OnAttackStarted()
        {
            anim.SetBool(isAttackingParam, true);
        }

        private void OnAttackFinished()
        {
            anim.SetBool(isAttackingParam, false);
        }

        private void OnTargetChanged(Transform target)
        {
        }

        [ClientRpc] public void RpcDeath() { OnDeath(); }
        [ClientRpc] public void RpcRespawn() { OnRevive(); }

        private void OnDeath()
        {
            // ✅ PlayDeath() para PlayerController
            Pose?.Die();
            anim.SetTrigger(dieTrigger);
            anim.SetBool(isDeadParam, true);
            anim.SetBool(isMovingParam, false);
            anim.SetBool(isAttackingParam, false);
            anim.SetBool(isCastingParam, false);
        }

        private void OnRevive()
        {
            // ✅ PlayRespawn() para PlayerController
            Pose?.Revive();
            anim.SetTrigger(reviveTrigger);
            anim.SetBool(isDeadParam, false);
        }

        private void OnHealthUpdated()
        {
        }

        private void OnSkillCastStarted(SkillData skill, float castTime)
        {
            anim.SetBool(isCastingParam, true);
            targetCastProgress = 1f;
        }

        private void OnSkillCastFinished()
        {
            anim.SetBool(isCastingParam, false);
            targetCastProgress = 0f;
            currentCastProgress = 0f;
        }

        private void OnSkillExecuted(SkillData skill)
        {
            Pose?.Skill(GetSkillAnimationId(skill));
            anim.SetInteger(skillIdParam, GetSkillAnimationId(skill));
            anim.SetTrigger(skillTrigger);
        }

        // ✅ resolvido para PlayerCombat.cs
        [ClientRpc]
        public void RpcTriggerAttack(int index)
        {
            PlayAttack(index);
        }

        // ✅ resolvido para PlayerSkills.cs
        [TargetRpc]
        public void RpcTriggerSkill(int skillId)
        {
            Pose?.Skill(skillId);
            anim.SetInteger(skillIdParam, skillId);
            anim.SetTrigger(skillTrigger);
        }

        // ✅ resolvido para PlayerMovement.cs
        [TargetRpc]
        public void RpcSetMoving(bool isMoving)
        {
            anim.SetBool(isMovingParam, isMoving);
        }

        // ✅ resolvido para PlayerController.cs
        public void PlayDeath()
        {
            Pose?.Die();
            anim.SetTrigger(dieTrigger);
        }

        public void PlayRespawn()
        {
            Pose?.Revive();
            anim.SetTrigger(reviveTrigger);
        }

        public void PlayAttack(int attackType = 0)
        {
            Pose?.Attack();
            anim.SetInteger(attackTypeParam, attackType);
            anim.SetTrigger(attackTrigger);
        }

        public void PlayHit()
        {
            anim.SetTrigger(hitTrigger);
        }

        public void PlayBlock()
        {
            anim.SetTrigger(blockTrigger);
        }

        public void PlayDodge()
        {
            anim.SetTrigger(dodgeTrigger);
        }

        public void PlayEmote(int emoteId)
        {
            Pose?.Emote(EmotePoses[Mathf.Abs(emoteId) % EmotePoses.Length]);
            anim.SetInteger(emoteIdParam, emoteId);
            anim.SetTrigger(emoteTrigger);
        }

        public void SetAnimatorController(RuntimeAnimatorController controller)
        {
            AnimatorStateInfo currentState = anim.GetCurrentAnimatorStateInfo(0);
            float normalizedTime = currentState.normalizedTime;

            anim.runtimeAnimatorController = controller;
            anim.Play(currentState.fullPathHash, 0, normalizedTime);
        }

        public void SetRunning(bool running)
        {
            anim.SetBool(isRunningParam, running);
        }

        public void SetCastProgress(float progress)
        {
            targetCastProgress = progress;
        }

        public void SetTrigger(string triggerName)
        {
            anim.SetTrigger(triggerName);
        }

        public void SetBool(string parameterName, bool value)
        {
            anim.SetBool(parameterName, value);
        }

        private int GetSkillAnimationId(SkillData skill)
        {
            return skill != null ? skill.animationVariant : 0;
        }
    }
}
