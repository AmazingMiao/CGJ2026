using UnityEngine;

namespace CGJ2026.View
{
    public sealed class VelocityFacing2D : MonoBehaviour
    {
        [SerializeField] private Rigidbody2D targetBody;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private float flipThreshold = 0.1f;

        private void Awake()
        {
            if (targetBody == null)
            {
                targetBody = GetComponentInParent<Rigidbody2D>();
            }

            if (visualRoot == null)
            {
                visualRoot = transform;
            }
        }

        private void LateUpdate()
        {
            if (targetBody == null || visualRoot == null)
            {
                return;
            }

            float velocityX = targetBody.linearVelocity.x;
            if (Mathf.Abs(velocityX) < flipThreshold)
            {
                return;
            }

            Vector3 scale = visualRoot.localScale;
            scale.x = Mathf.Abs(scale.x) * Mathf.Sign(velocityX);
            visualRoot.localScale = scale;
        }
    }
}
