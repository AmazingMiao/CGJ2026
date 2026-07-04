using CGJ2026.Gameplay;
using UnityEngine;

namespace CGJ2026.View
{
    // Cosmetic-only: leans the visual sprite to match the ground normal so a legless placeholder
    // (or a rigged character before real per-leg IK exists) doesn't look like it floats on slopes.
    // Never touches the physics body's rotation (that stays upright/frozen for movement to work).
    public sealed class SlopeAligner : MonoBehaviour
    {
        [SerializeField] private GroundRaySensor2D groundSensor;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private float maxAngle = 50f;
        [SerializeField] private float alignDegreesPerSecond = 360f;
        [Tooltip("Per-leg IK already handles slopes; do not also rotate its entire hierarchy.")]
        [SerializeField] private bool yieldToLegIK = true;

        private bool hasLegIK;

        private void Awake()
        {
            if (visualRoot == null)
            {
                visualRoot = transform;
            }

            hasLegIK = GetComponentInChildren<LegIK2D>(true) != null;
        }

        private void LateUpdate()
        {
            if (groundSensor == null || visualRoot == null)
            {
                return;
            }

            if (yieldToLegIK && hasLegIK)
            {
                return;
            }

            float targetAngle = 0f;
            if (groundSensor.IsGrounded)
            {
                Vector2 normal = groundSensor.LastHit.normal;
                targetAngle = Mathf.Clamp(Mathf.Atan2(-normal.x, normal.y) * Mathf.Rad2Deg, -maxAngle, maxAngle);
            }

            float currentAngle = visualRoot.localEulerAngles.z;
            if (currentAngle > 180f)
            {
                currentAngle -= 360f;
            }

            float smoothedAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, alignDegreesPerSecond * Time.deltaTime);
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, smoothedAngle);
        }
    }
}
