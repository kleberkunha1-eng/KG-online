using UnityEngine;

// Drives converted PKO skinned models (legacy Animation clips named action_N) from observed movement.
[RequireComponent(typeof(Animation))]
public class LegacyAnimDriver : MonoBehaviour
{
    const string Idle = "action_1", Run = "action_5", Walk = "action_2";
    [SerializeField] float moveThreshold = 0.4f;
    Animation anim;
    Vector3 lastPosition;
    string current;

    void Awake()
    {
        anim = GetComponent<Animation>();
        lastPosition = transform.position;
        Play(Idle);
    }

    void Update()
    {
        float speed = (transform.position - lastPosition).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPosition = transform.position;
        Play(speed > moveThreshold ? (anim.GetClip(Run) != null ? Run : Walk) : Idle);
    }

    void Play(string clip)
    {
        if (clip == current || anim.GetClip(clip) == null) return;
        current = clip;
        anim.CrossFade(clip, 0.15f);
    }
}
