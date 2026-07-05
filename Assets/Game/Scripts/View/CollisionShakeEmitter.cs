using System;
using UnityEngine;

namespace CGJ2026.View
{
    // Attach to a Rigidbody2D that should shake the camera when it hits the environment.
    // Normal impact speed, mass and the solver's normal impulse all contribute, while
    // glancing/rolling contacts remain quiet.
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class CollisionShakeEmitter : MonoBehaviour
    {
        // Fired the instant a qualifying impact drives the camera shake. Payload: normalized
        // intensity in [0, 1] and the impact travel direction. Sound/FX can subscribe here so
        // audio-visual feedback stays perfectly in sync with the screen shake.
        public event Action<float, Vector2> Impacted;

        [SerializeField] private CameraFollow2D cameraFollow;
        [SerializeField] private LayerMask impactMask = ~0;

        [Header("Impact Strength")]
        [SerializeField] private float minImpactSpeed = 3f;
        [SerializeField] private float maxImpactSpeed = 16f;
        [SerializeField] private float referenceMass = 8f;
        [Tooltip("0 ignores mass, 0.5 uses square-root scaling, 1 uses linear scaling.")]
        [Range(0f, 1f)]
        [SerializeField] private float massInfluence = 0.5f;
        [Tooltip("Solver impulse only contributes when there is at least this fraction of the minimum closing speed.")]
        [Range(0f, 1f)]
        [SerializeField] private float impulseRequiresClosingSpeed = 0.75f;
        [Tooltip("Reduces shake on mostly tangential scrape contacts even if the solver reports an impulse.")]
        [Range(0f, 1f)]
        [SerializeField] private float glancingContactDamping = 0.2f;
        [SerializeField] private AnimationCurve impactResponse = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.3f, 0.04f),
            new Keyframe(0.68f, 0.42f),
            new Keyframe(1f, 1f));
        [Range(0f, 1f)]
        [SerializeField] private float maxTrauma = 1f;

        [Header("Retrigger Control")]
        [Tooltip("Prevents bounces and contact seams from producing a machine-gun shake.")]
        [SerializeField] private float impactCooldown = 0.12f;
        [Tooltip("A new hit this many times stronger may break through the cooldown.")]
        [SerializeField] private float cooldownBreakMultiplier = 1.35f;

        private Rigidbody2D body;
        private float lastImpactTime = float.NegativeInfinity;
        private float lastImpactMetric;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            cameraFollow = FindFirstObjectByType<CameraFollow2D>();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();

            if (cameraFollow == null)
            {
                cameraFollow = FindFirstObjectByType<CameraFollow2D>();
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            Collider2D impactCollider = GetImpactCollider(collision);
            if (cameraFollow == null || impactCollider == null || !IsImpactLayer(impactCollider.gameObject.layer))
            {
                return;
            }

            MeasureImpact(
                collision,
                out float normalSpeed,
                out float relativeSpeed,
                out float normalImpulse,
                out Vector2 direction);

            float safeReferenceMass = Mathf.Max(0.01f, referenceMass);
            float massRatio = Mathf.Max(0.01f, body.mass) / safeReferenceMass;
            float massWeightedSpeed = normalSpeed * Mathf.Pow(massRatio, massInfluence);
            float impulseClosingGate = minImpactSpeed * impulseRequiresClosingSpeed;
            float impulseEquivalentSpeed = normalSpeed >= impulseClosingGate
                ? normalImpulse / safeReferenceMass
                : 0f;
            float impactMetric = Mathf.Max(massWeightedSpeed, impulseEquivalentSpeed);

            if (impactMetric < minImpactSpeed || IsSuppressedByCooldown(impactMetric))
            {
                return;
            }

            float normalizedImpact = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactMetric);
            float response = impactResponse != null && impactResponse.length > 0
                ? impactResponse.Evaluate(normalizedImpact)
                : normalizedImpact;
            float intensity = Mathf.Clamp01(response) * maxTrauma * GetGlancingScale(normalSpeed, relativeSpeed);

            if (intensity <= 0.001f)
            {
                return;
            }

            lastImpactTime = Time.unscaledTime;
            lastImpactMetric = impactMetric;
            cameraFollow.AddImpactShake(intensity, direction);
            Impacted?.Invoke(intensity, direction);
        }

        private bool IsImpactLayer(int layer)
        {
            return ((1 << layer) & impactMask.value) != 0;
        }

        private Collider2D GetImpactCollider(Collision2D collision)
        {
            if (collision.collider != null && collision.collider.attachedRigidbody != body)
            {
                return collision.collider;
            }

            if (collision.otherCollider != null && collision.otherCollider.attachedRigidbody != body)
            {
                return collision.otherCollider;
            }

            return collision.collider != null ? collision.collider : collision.otherCollider;
        }

        private bool IsSuppressedByCooldown(float impactMetric)
        {
            bool inCooldown = Time.unscaledTime - lastImpactTime < impactCooldown;
            bool substantiallyStronger = impactMetric >= lastImpactMetric * cooldownBreakMultiplier;
            return inCooldown && !substantiallyStronger;
        }

        private static void MeasureImpact(
            Collision2D collision,
            out float normalSpeed,
            out float relativeSpeed,
            out float normalImpulse,
            out Vector2 impactDirection)
        {
            normalSpeed = 0f;
            relativeSpeed = 0f;
            normalImpulse = 0f;
            impactDirection = Vector2.zero;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                Vector2 contactRelativeVelocity = contact.relativeVelocity;
                float contactRelativeSpeed = contactRelativeVelocity.magnitude;
                float contactSpeed = Mathf.Abs(Vector2.Dot(contactRelativeVelocity, contact.normal));

                if (contactSpeed >= normalSpeed)
                {
                    normalSpeed = contactSpeed;
                    relativeSpeed = contactRelativeSpeed;
                    // Contact normals point away from the struck surface; the impact travelled into it.
                    impactDirection = -contact.normal;
                }

                normalImpulse = Mathf.Max(normalImpulse, Mathf.Abs(contact.normalImpulse));
            }

            if (collision.contactCount == 0)
            {
                normalSpeed = collision.relativeVelocity.magnitude;
                relativeSpeed = normalSpeed;
                impactDirection = collision.relativeVelocity.sqrMagnitude > 0.0001f
                    ? collision.relativeVelocity.normalized
                    : Vector2.zero;
            }
        }

        private float GetGlancingScale(float normalSpeed, float relativeSpeed)
        {
            if (relativeSpeed <= 0.001f || normalSpeed >= relativeSpeed)
            {
                return 1f;
            }

            float normalShare = Mathf.Clamp01(normalSpeed / relativeSpeed);
            return Mathf.Lerp(glancingContactDamping, 1f, normalShare);
        }

        private void OnValidate()
        {
            minImpactSpeed = Mathf.Max(0f, minImpactSpeed);
            maxImpactSpeed = Mathf.Max(minImpactSpeed + 0.01f, maxImpactSpeed);
            referenceMass = Mathf.Max(0.01f, referenceMass);
            impulseRequiresClosingSpeed = Mathf.Clamp01(impulseRequiresClosingSpeed);
            glancingContactDamping = Mathf.Clamp01(glancingContactDamping);
            maxTrauma = Mathf.Clamp01(maxTrauma);
            impactCooldown = Mathf.Max(0f, impactCooldown);
            cooldownBreakMultiplier = Mathf.Max(1f, cooldownBreakMultiplier);
        }
    }
}
