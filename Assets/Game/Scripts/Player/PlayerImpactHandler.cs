using CGJ2026.Boulder;
using CGJ2026.Gameplay;
using UnityEngine;
using UnityEngine.Serialization;

namespace CGJ2026.Player
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class PlayerImpactHandler : MonoBehaviour
    {
        public enum ImpactOutcome
        {
            Knockback,
            Crushed
        }

        [Header("References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bodyCollider;
        [SerializeField] private RespawnService respawnService;

        [Header("Knockback")]
        [SerializeField] private float knockbackImpulse = 12f;
        [SerializeField, Range(0f, 1f)] private float upwardBias = 0.45f;
        [SerializeField] private float impactCooldown = 0.15f;

        [Header("Crush Detection")]
        [SerializeField] private LayerMask terrainMask = 1 << 8;
        [FormerlySerializedAs("wallProbeDistance")]
        [SerializeField, Min(0.02f)] private float terrainProbeDistance = 0.16f;
        [FormerlySerializedAs("wallProbeHeightRatio")]
        [SerializeField, Range(0.2f, 1f)] private float terrainProbeWidthRatio = 0.7f;
        [SerializeField, Range(0.5f, 1f)] private float minimumHorizontalCrushNormal = 0.7f;

        private readonly Collider2D[] terrainOverlaps = new Collider2D[4];
        private float nextImpactTime;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (bodyCollider == null)
            {
                bodyCollider = GetComponent<Collider2D>();
            }

            if (respawnService == null)
            {
                respawnService = FindFirstObjectByType<RespawnService>();
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HandleBoulderCollision(collision, allowKnockback: true);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            // A harmless first contact can become a crush after the boulder pushes the player into terrain.
            HandleBoulderCollision(collision, allowKnockback: false);
        }

        private void HandleBoulderCollision(Collision2D collision, bool allowKnockback)
        {
            if (respawnService != null && respawnService.IsPlayerDead)
            {
                return;
            }

            Rigidbody2D otherBody = collision.rigidbody;
            BoulderGravityController boulder = otherBody != null
                ? otherBody.GetComponent<BoulderGravityController>()
                : collision.collider.GetComponentInParent<BoulderGravityController>();
            if (boulder == null)
            {
                return;
            }

            Vector2 playerPosition = body != null ? body.worldCenterOfMass : transform.position;
            Vector2 boulderPosition = otherBody != null ? otherBody.worldCenterOfMass : collision.transform.position;
            Vector2 awayDirection = GetAwayDirection(boulderPosition - playerPosition, collision);

            bool hasHorizontalCrushDirection = TryGetHorizontalCrushDirection(
                awayDirection,
                minimumHorizontalCrushNormal,
                out Vector2 crushDirection);
            bool terrainBehindPlayer = hasHorizontalCrushDirection && HasTerrainBehind(crushDirection);
            if (DecideImpact(terrainBehindPlayer) == ImpactOutcome.Crushed)
            {
                respawnService?.KillPlayer();
                return;
            }

            if (!allowKnockback || Time.time < nextImpactTime)
            {
                return;
            }

            nextImpactTime = Time.time + impactCooldown;
            Vector2 launchDirection = (awayDirection + Vector2.up * upwardBias).normalized;
            body.linearVelocity = Vector2.zero;
            body.AddForce(launchDirection * knockbackImpulse, ForceMode2D.Impulse);
        }

        public static ImpactOutcome DecideImpact(bool terrainBehindPlayer)
        {
            return terrainBehindPlayer ? ImpactOutcome.Crushed : ImpactOutcome.Knockback;
        }

        private static Vector2 GetAwayDirection(Vector2 boulderOffset, Collision2D collision)
        {
            Vector2 contactNormal = Vector2.zero;
            for (int i = 0; i < collision.contactCount; i++)
            {
                contactNormal += collision.GetContact(i).normal;
            }

            return ResolveAwayDirection(boulderOffset, contactNormal);
        }

        private static Vector2 ResolveAwayDirection(Vector2 boulderOffset, Vector2 contactNormal)
        {
            Vector2 centerAway = boulderOffset.sqrMagnitude > 0.0001f
                ? -boulderOffset.normalized
                : Vector2.up;
            if (contactNormal.sqrMagnitude < 0.0001f)
            {
                return centerAway;
            }

            contactNormal.Normalize();
            return Vector2.Dot(contactNormal, centerAway) >= 0f ? contactNormal : -contactNormal;
        }

        private static bool TryGetHorizontalCrushDirection(
            Vector2 awayDirection,
            float minimumHorizontalNormal,
            out Vector2 horizontalDirection)
        {
            if (Mathf.Abs(awayDirection.normalized.x) < Mathf.Clamp01(minimumHorizontalNormal))
            {
                horizontalDirection = Vector2.zero;
                return false;
            }

            horizontalDirection = awayDirection.x >= 0f ? Vector2.right : Vector2.left;
            return true;
        }

        private bool HasTerrainBehind(Vector2 awayDirection)
        {
            if (bodyCollider == null || terrainMask.value == 0 || awayDirection.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = terrainMask,
                useTriggers = false
            };
            GetTerrainProbe(bodyCollider, awayDirection, out Vector2 center, out Vector2 size, out float angle);
            int hitCount = Physics2D.OverlapBox(
                center,
                size,
                angle,
                filter,
                terrainOverlaps);
            return hitCount > 0;
        }

        private void GetTerrainProbe(
            Collider2D colliderToProbe,
            Vector2 direction,
            out Vector2 center,
            out Vector2 size,
            out float angle)
        {
            direction.Normalize();
            Bounds bounds = colliderToProbe.bounds;
            float distance = Mathf.Max(0.02f, terrainProbeDistance);
            float width = Mathf.Max(0.1f, Mathf.Min(bounds.size.x, bounds.size.y) * terrainProbeWidthRatio);
            Vector2 boundsCenter = bounds.center;
            Vector2 surface = colliderToProbe.ClosestPoint(
                boundsCenter + direction * Mathf.Max(bounds.size.x, bounds.size.y) * 2f);
            center = surface + direction * distance * 0.5f;
            size = new Vector2(distance, width);
            angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        }

        private void OnDrawGizmosSelected()
        {
            Collider2D colliderToDraw = bodyCollider != null ? bodyCollider : GetComponent<Collider2D>();
            if (colliderToDraw == null)
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.75f);
            DrawProbeGizmo(colliderToDraw, Vector2.right);
            DrawProbeGizmo(colliderToDraw, Vector2.left);
        }

        private void DrawProbeGizmo(Collider2D colliderToDraw, Vector2 direction)
        {
            GetTerrainProbe(colliderToDraw, direction, out Vector2 center, out Vector2 size, out float angle);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = previousMatrix;
        }
    }
}
