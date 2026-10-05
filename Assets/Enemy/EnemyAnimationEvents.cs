using UnityEngine;
using Mirror;
using TOP.Core;

/// <summary>
/// Deve ficar no mesmo GameObject do <see cref="Animator"/> que toca Attack1 (ex.: filho Mob1).
/// Stats / NetworkIdentity ficam na raiz Enemy_* — use sempre GetComponentInParent.
/// </summary>
public class EnemyAnimationEvents : MonoBehaviour
{
    private EnemyStats stats;
    private EnemyAI enemyAI;
    private Animator anim;
    private NetworkIdentity networkIdentity;

    void Awake()
    {
        stats = GetComponentInParent<EnemyStats>();
        enemyAI = GetComponentInParent<EnemyAI>();
        anim = GetComponent<Animator>();
        networkIdentity = GetComponentInParent<NetworkIdentity>();
    }

    // Chamado pelo Animation Event no frame do golpe
    public void AnimEvent_AttackHit()
    {
        // EnemyAI applies one authoritative hit per cooldown; animation events must not apply it twice.
    }

    // Chamado no final da animação de ataque
    public void AnimEvent_AttackEnd()
    {
        anim?.SetBool("IsAttacking", false);
    }
}
