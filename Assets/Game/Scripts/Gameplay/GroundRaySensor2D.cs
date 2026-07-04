using UnityEngine;

namespace CGJ2026.Gameplay
{
    public sealed class GroundRaySensor2D : MonoBehaviour
    {
        [Header("Ray")]
        [SerializeField] private Vector2 localOrigin = Vector2.down;
        [SerializeField] private Vector2 localDirection = Vector2.down;
        [SerializeField] private float length = 0.28f;
        [SerializeField] private LayerMask groundMask;

        [Header("Editor")]
        [SerializeField] private bool drawAlways = true;
        [SerializeField] private Color groundedColor = new Color(0.1f, 1f, 0.25f, 1f);
        [SerializeField] private Color emptyColor = new Color(1f, 0.25f, 0.15f, 1f);

        private RaycastHit2D lastHit;

        public bool IsGrounded { get; private set; }
        public RaycastHit2D LastHit => lastHit;

        public void Configure(Vector2 origin, Vector2 direction, float rayLength, LayerMask mask)
        {
            localOrigin = origin;
            localDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            length = Mathf.Max(0.01f, rayLength);
            groundMask = mask;
        }

        public bool Refresh()
        {
            Vector2 origin = GetWorldOrigin();
            Vector2 direction = GetWorldDirection();
            lastHit = Physics2D.Raycast(origin, direction, length, groundMask);
            IsGrounded = lastHit.collider != null;
            return IsGrounded;
        }

        private void OnDrawGizmos()
        {
            if (!drawAlways)
            {
                return;
            }

            DrawRayGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            DrawRayGizmo();
        }

        private void DrawRayGizmo()
        {
            Vector2 origin = GetWorldOrigin();
            Vector2 direction = GetWorldDirection();
            RaycastHit2D hit = Physics2D.Raycast(origin, direction, length, groundMask);
            Vector2 end = hit.collider != null ? hit.point : origin + direction * length;

            Gizmos.color = hit.collider != null ? groundedColor : emptyColor;
            Gizmos.DrawLine(origin, end);
            Gizmos.DrawWireSphere(origin, 0.06f);
            Gizmos.DrawWireSphere(end, 0.08f);
        }

        private Vector2 GetWorldOrigin()
        {
            Vector2 scaledOffset = Vector2.Scale(localOrigin, transform.lossyScale);
            return (Vector2)transform.position + scaledOffset;
        }

        private Vector2 GetWorldDirection()
        {
            return localDirection.sqrMagnitude > 0.0001f ? localDirection.normalized : Vector2.down;
        }
    }
}
