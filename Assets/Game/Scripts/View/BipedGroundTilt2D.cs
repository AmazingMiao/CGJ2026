using UnityEngine;

namespace CGJ2026.View
{
    // Cosmetic-only torso lean derived from the two IK contacts. Runs after LegIK2D so it remains
    // compatible with idle, locomotion, and push animations that animate the upper body.
    [DefaultExecutionOrder(100)]
    public sealed class BipedGroundTilt2D : MonoBehaviour
    {
        [SerializeField] private Transform torso;
        [SerializeField] private LegIK2D legA;
        [SerializeField] private LegIK2D legB;
        [SerializeField] private float maxAngle = 40f;
        [SerializeField] private float alignDegreesPerSecond = 360f;

        private void Awake()
        {
            if (torso == null)
            {
                torso = transform;
            }
        }

        private void LateUpdate()
        {
            if (torso == null || legA == null || legB == null)
            {
                return;
            }

            float targetAngle = 0f;
            if (legA.IsFootGrounded && legB.IsFootGrounded)
            {
                Vector2 stanceLine = legB.FootWorldPosition - legA.FootWorldPosition;
                if (stanceLine.sqrMagnitude > 0.0001f)
                {
                    // Abs(x) keeps the sign meaningful (uphill/downhill) regardless of which leg
                    // ends up physically left/right after the character's visual facing flip.
                    targetAngle = Mathf.Clamp(Mathf.Atan2(stanceLine.y, Mathf.Abs(stanceLine.x)) * Mathf.Rad2Deg, -maxAngle, maxAngle);
                }
            }
            else if (legA.IsFootGrounded || legB.IsFootGrounded)
            {
                // Keep the torso calm while the other leg is in flight instead of snapping upright.
                Vector2 normal = legA.IsFootGrounded ? legA.GroundNormal : legB.GroundNormal;
                targetAngle = Mathf.Clamp(Mathf.Atan2(-normal.x, normal.y) * Mathf.Rad2Deg, -maxAngle, maxAngle);
            }

            float currentAngle = torso.localEulerAngles.z;
            if (currentAngle > 180f)
            {
                currentAngle -= 360f;
            }

            float smoothed = Mathf.MoveTowardsAngle(currentAngle, targetAngle, alignDegreesPerSecond * Time.deltaTime);
            torso.localRotation = Quaternion.Euler(0f, 0f, smoothed);
        }
    }
}
