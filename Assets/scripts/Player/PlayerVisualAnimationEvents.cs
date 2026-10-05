using UnityEngine;

namespace TOP.Player
{
    // Animation events only update presentation; server combat owns damage timing.
    public class PlayerVisualAnimationEvents : MonoBehaviour
    {
        public void OnAttackStarted() { GetComponent<Animator>().SetBool("IsAttacking",true); }
        public void OnAttackFinished() { GetComponent<Animator>().SetBool("IsAttacking",false); }
    }
}
