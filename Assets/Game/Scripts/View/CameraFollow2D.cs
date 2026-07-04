using System.Collections.Generic;
using UnityEngine;

namespace CGJ2026.View
{
    public sealed class CameraFollow2D : MonoBehaviour
    {
        [Header("Follow")]
        [Tooltip("主目标(玩家)。始终参与取景。")]
        [SerializeField] private Transform target;
        [Tooltip("次目标(巨石)。为空时退化为只跟随主目标。")]
        [SerializeField] private Transform secondaryTarget;
        [SerializeField] private Vector2 offset = new Vector2(0f, 1.5f);
        [SerializeField] private float followSpeed = 8f;
        [SerializeField] private float minX = -7f;
        [SerializeField] private float maxX = 41f;
        [SerializeField] private float minY = 1f;
        [SerializeField] private float maxY = 6f;

        [Header("Framing - 同时框住两个目标")]
        [Tooltip("正交相机引用。为空时在 Awake 里自动获取本物体上的 Camera。")]
        [SerializeField] private Camera framingCamera;
        [Tooltip("两目标水平跨度之外额外保留的世界边距(单位:世界单位)。")]
        [SerializeField] private float framingPaddingX = 5f;
        [Tooltip("两目标垂直跨度之外额外保留的世界边距(单位:世界单位)。")]
        [SerializeField] private float framingPaddingY = 4f;
        [SerializeField] private float minOrthographicSize = 6f;
        [SerializeField] private float maxOrthographicSize = 13f;
        [Tooltip("正交尺寸缩放的平滑速度。")]
        [SerializeField] private float zoomSpeed = 6f;

        [Header("Impact Shake - Shape")]
        [Tooltip("Maximum translation in world units before stacked shakes are clamped.")]
        [SerializeField] private float shakeMagnitude = 0.6f;
        [SerializeField] private float shakeRotationDegrees = 1.1f;
        [SerializeField] private float shakeFrequency = 22f;
        [SerializeField] private float shakeDuration = 0.38f;
        [SerializeField] private AnimationCurve magnitudeOverLifetime = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.07f, 1f),
            new Keyframe(0.32f, 0.72f),
            new Keyframe(1f, 0f));
        [SerializeField] private AnimationCurve frequencyOverLifetime = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(1f, 0.45f));
        [Tooltip("Scales the base duration by impact intensity: light hits should finish sooner.")]
        [SerializeField] private AnimationCurve durationByIntensity = new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(1f, 1f));

        [Header("Impact Shake - Feel")]
        [Range(0f, 1f)]
        [SerializeField] private float directionalInfluence = 0.78f;
        [Tooltip("Immediate displacement along the impact direction, as a fraction of max magnitude.")]
        [Range(0f, 1f)]
        [SerializeField] private float directionalKick = 0.22f;
        [Tooltip("Guarantees a readable first-frame shove before the noise envelope reaches full strength.")]
        [Range(0f, 1f)]
        [SerializeField] private float fastStartEnvelope = 0.42f;
        [Tooltip("How much of the pulse lifetime the one-way impact kick lasts.")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float kickLifetime = 0.14f;
        [SerializeField] private float intensityExponent = 1.35f;
        [SerializeField] private float maxStackedAmplitude = 1.2f;
        [SerializeField] private int maxConcurrentShakes = 8;
        [SerializeField] private bool useUnscaledTime = true;

        private readonly List<ShakePulse> activeShakes = new List<ShakePulse>(8);
        private Vector3 smoothedPosition;
        private Quaternion unshakenRotation;
        private float smoothedOrthographicSize;

        private struct ShakePulse
        {
            public float Intensity;
            public float Duration;
            public float Elapsed;
            public float NoiseTime;
            public float SeedX;
            public float SeedY;
            public float SeedRotation;
            public Vector2 Direction;
        }

        public void SetTarget(Transform followTarget)
        {
            target = followTarget;
        }

        /// <summary>
        /// 同时设置主、次目标,相机会持续把两者都框在画面内。
        /// </summary>
        public void SetTargets(Transform primaryTarget, Transform framedSecondaryTarget)
        {
            target = primaryTarget;
            secondaryTarget = framedSecondaryTarget;
        }

        public void SetSecondaryTarget(Transform framedSecondaryTarget)
        {
            secondaryTarget = framedSecondaryTarget;
        }

        // Kept for existing callers. New impact emitters should provide a direction as well.
        public void AddTrauma(float amount)
        {
            AddImpactShake(amount, Vector2.zero);
        }

        /// <summary>
        /// Adds one independently decaying shake pulse. Intensity is normalized to [0, 1].
        /// Multiple pulses can overlap, then their final displacement is safely clamped.
        /// </summary>
        public void AddImpactShake(float intensity, Vector2 impactDirection)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity <= 0.001f)
            {
                return;
            }

            float shapedIntensity = Mathf.Pow(intensity, Mathf.Max(0.01f, intensityExponent));
            float durationScale = Mathf.Max(0.05f, EvaluateCurve(durationByIntensity, intensity, 1f));

            if (activeShakes.Count >= Mathf.Max(1, maxConcurrentShakes))
            {
                RemoveWeakestShake();
            }

            activeShakes.Add(new ShakePulse
            {
                Intensity = shapedIntensity,
                Duration = Mathf.Max(0.01f, shakeDuration * durationScale),
                Elapsed = 0f,
                NoiseTime = 0f,
                SeedX = Random.Range(0f, 1000f),
                SeedY = Random.Range(0f, 1000f),
                SeedRotation = Random.Range(0f, 1000f),
                Direction = impactDirection.sqrMagnitude > 0.0001f
                    ? impactDirection.normalized
                    : Vector2.zero
            });
        }

        private void Awake()
        {
            smoothedPosition = transform.position;
            unshakenRotation = transform.rotation;

            if (framingCamera == null)
            {
                framingCamera = GetComponent<Camera>();
            }

            smoothedOrthographicSize = framingCamera != null && framingCamera.orthographic
                ? framingCamera.orthographicSize
                : minOrthographicSize;
        }

        private void LateUpdate()
        {
            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            if (TryGetFramingBounds(out Vector2 framingCenter, out Vector2 framingExtents))
            {
                Vector3 desiredPosition = new Vector3(
                    Mathf.Clamp(framingCenter.x + offset.x, minX, maxX),
                    Mathf.Clamp(framingCenter.y + offset.y, minY, maxY),
                    smoothedPosition.z);

                smoothedPosition = Vector3.Lerp(
                    smoothedPosition,
                    desiredPosition,
                    1f - Mathf.Exp(-followSpeed * deltaTime));

                UpdateOrthographicSize(framingExtents, deltaTime);
            }

            ComputeShake(deltaTime, out Vector2 shakeOffset, out float shakeRotation);
            transform.SetPositionAndRotation(
                smoothedPosition + (Vector3)shakeOffset,
                unshakenRotation * Quaternion.Euler(0f, 0f, shakeRotation));
        }

        /// <summary>
        /// 计算能同时包含所有有效目标的取景中心与半跨度(未含边距)。
        /// </summary>
        private bool TryGetFramingBounds(out Vector2 center, out Vector2 extents)
        {
            center = Vector2.zero;
            extents = Vector2.zero;

            bool hasPrimary = target != null;
            bool hasSecondary = secondaryTarget != null;
            if (!hasPrimary && !hasSecondary)
            {
                return false;
            }

            Vector2 first = hasPrimary ? (Vector2)target.position : (Vector2)secondaryTarget.position;
            Vector2 min = first;
            Vector2 max = first;

            if (hasPrimary && hasSecondary)
            {
                Vector2 other = secondaryTarget.position;
                min = Vector2.Min(min, other);
                max = Vector2.Max(max, other);
            }

            center = (min + max) * 0.5f;
            extents = (max - min) * 0.5f;
            return true;
        }

        private void UpdateOrthographicSize(Vector2 framingExtents, float deltaTime)
        {
            if (framingCamera == null || !framingCamera.orthographic)
            {
                return;
            }

            float aspect = framingCamera.aspect > 0.0001f ? framingCamera.aspect : 16f / 9f;
            float requiredHalfHeight = framingExtents.y + framingPaddingY;
            float requiredHalfWidth = framingExtents.x + framingPaddingX;

            // 正交尺寸即半高;水平方向需换算成等效半高。
            float desiredSize = Mathf.Max(requiredHalfHeight, requiredHalfWidth / aspect);
            desiredSize = Mathf.Clamp(desiredSize, minOrthographicSize, maxOrthographicSize);

            smoothedOrthographicSize = Mathf.Lerp(
                smoothedOrthographicSize,
                desiredSize,
                1f - Mathf.Exp(-zoomSpeed * deltaTime));

            framingCamera.orthographicSize = smoothedOrthographicSize;
        }

        private void OnDisable()
        {
            activeShakes.Clear();
            transform.SetPositionAndRotation(smoothedPosition, unshakenRotation);
        }

        private void ComputeShake(float deltaTime, out Vector2 totalOffset, out float totalRotation)
        {
            totalOffset = Vector2.zero;
            totalRotation = 0f;

            for (int i = activeShakes.Count - 1; i >= 0; i--)
            {
                ShakePulse pulse = activeShakes[i];
                pulse.Elapsed += deltaTime;

                float normalizedTime = Mathf.Clamp01(pulse.Elapsed / pulse.Duration);
                if (normalizedTime >= 1f)
                {
                    activeShakes.RemoveAt(i);
                    continue;
                }

                float envelope = Mathf.Max(0f, EvaluateCurve(magnitudeOverLifetime, normalizedTime, 1f));
                float fastStart = fastStartEnvelope * (1f - Mathf.Clamp01(normalizedTime / 0.08f));
                envelope = Mathf.Max(envelope, fastStart);
                float frequencyScale = Mathf.Max(0f, EvaluateCurve(frequencyOverLifetime, normalizedTime, 1f));
                pulse.NoiseTime += deltaTime * Mathf.Max(0f, shakeFrequency) * frequencyScale;

                float noiseX = SampleNoise(pulse.SeedX, pulse.NoiseTime);
                float noiseY = SampleNoise(pulse.SeedY, pulse.NoiseTime);
                Vector2 isotropicNoise = Vector2.ClampMagnitude(new Vector2(noiseX, noiseY), 1f);
                Vector2 shakeNoise = isotropicNoise;

                if (pulse.Direction.sqrMagnitude > 0f)
                {
                    float axialNoise = Mathf.Clamp(noiseX * 0.75f + noiseY * 0.25f, -1f, 1f);
                    Vector2 directionalNoise = pulse.Direction * axialNoise;
                    shakeNoise = Vector2.Lerp(isotropicNoise, directionalNoise, directionalInfluence);

                    // A short one-way kick makes heavy landings read before the oscillation begins.
                    float kickEnvelope = 1f - Mathf.Clamp01(normalizedTime / kickLifetime);
                    totalOffset += pulse.Direction
                        * (shakeMagnitude * directionalKick * pulse.Intensity * kickEnvelope);
                }

                float amplitude = shakeMagnitude * pulse.Intensity * envelope;
                totalOffset += shakeNoise * amplitude;
                totalRotation += SampleNoise(pulse.SeedRotation, pulse.NoiseTime)
                    * shakeRotationDegrees
                    * pulse.Intensity
                    * envelope;

                activeShakes[i] = pulse;
            }

            float stackLimit = shakeMagnitude * Mathf.Max(1f, maxStackedAmplitude);
            totalOffset = Vector2.ClampMagnitude(totalOffset, stackLimit);
            totalRotation = Mathf.Clamp(
                totalRotation,
                -shakeRotationDegrees * Mathf.Max(1f, maxStackedAmplitude),
                shakeRotationDegrees * Mathf.Max(1f, maxStackedAmplitude));
        }

        private void RemoveWeakestShake()
        {
            int weakestIndex = 0;
            float weakestStrength = float.PositiveInfinity;

            for (int i = 0; i < activeShakes.Count; i++)
            {
                ShakePulse pulse = activeShakes[i];
                float remaining = 1f - Mathf.Clamp01(pulse.Elapsed / pulse.Duration);
                float strength = pulse.Intensity * remaining;
                if (strength < weakestStrength)
                {
                    weakestStrength = strength;
                    weakestIndex = i;
                }
            }

            activeShakes.RemoveAt(weakestIndex);
        }

        private static float SampleNoise(float seed, float time)
        {
            return Mathf.PerlinNoise(seed, time) * 2f - 1f;
        }

        private static float EvaluateCurve(AnimationCurve curve, float time, float fallback)
        {
            return curve != null && curve.length > 0 ? curve.Evaluate(time) : fallback;
        }

        private void OnValidate()
        {
            followSpeed = Mathf.Max(0f, followSpeed);
            framingPaddingX = Mathf.Max(0f, framingPaddingX);
            framingPaddingY = Mathf.Max(0f, framingPaddingY);
            minOrthographicSize = Mathf.Max(0.1f, minOrthographicSize);
            maxOrthographicSize = Mathf.Max(minOrthographicSize, maxOrthographicSize);
            zoomSpeed = Mathf.Max(0f, zoomSpeed);
            shakeMagnitude = Mathf.Max(0f, shakeMagnitude);
            shakeRotationDegrees = Mathf.Max(0f, shakeRotationDegrees);
            shakeFrequency = Mathf.Max(0f, shakeFrequency);
            shakeDuration = Mathf.Max(0.01f, shakeDuration);
            fastStartEnvelope = Mathf.Clamp01(fastStartEnvelope);
            kickLifetime = Mathf.Clamp(kickLifetime, 0.01f, 0.5f);
            intensityExponent = Mathf.Max(0.01f, intensityExponent);
            maxStackedAmplitude = Mathf.Max(1f, maxStackedAmplitude);
            maxConcurrentShakes = Mathf.Max(1, maxConcurrentShakes);
        }
    }
}
