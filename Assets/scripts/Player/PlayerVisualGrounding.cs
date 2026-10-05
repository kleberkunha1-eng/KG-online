using UnityEngine;

namespace TOP.Player
{
    // Imported humanoid clips use a pelvis origin; keep their feet on the movement root.
    [DefaultExecutionOrder(100)]
    public class PlayerVisualGrounding : MonoBehaviour
    {
        Animator animator;
        Transform leftFoot, rightFoot;
        PlayerStats stats;
        void Start()
        {
            animator=GetComponent<Animator>(); stats=GetComponentInParent<PlayerStats>();
            if(animator!=null && animator.isHuman) { leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot); rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot); }
        }
        void LateUpdate()
        {
            if(leftFoot==null || rightFoot==null || transform.parent==null || (stats!=null && stats.IsDead)) return;
            float ankleHeight=Mathf.Min(leftFoot.position.y,rightFoot.position.y);
            transform.position+=Vector3.up*(transform.parent.position.y+.18f-ankleHeight);
        }
    }
}
