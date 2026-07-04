using UnityEngine;

namespace CGJ2026.View
{
    // Reserved hook for future boulder animation/FX art: stays a no-op until an Animator with a
    // real AnimatorController is assigned. Expected parameter: "Speed" (float).
    public sealed class BoulderAnimatorBridge : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody2D body;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponentInParent<Rigidbody2D>();
            }
        }

        private void Update()
        {
            if (animator == null || animator.runtimeAnimatorController == null || body == null)
            {
                return;
            }

            animator.SetFloat(SpeedParam, body.linearVelocity.magnitude);
        }
    }
}
