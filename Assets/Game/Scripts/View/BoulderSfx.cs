using System.Collections.Generic;
using UnityEngine;

namespace CGJ2026.View
{
    // 巨石音效:
    // 1) 在地面上滚动时循环播放"石头滚动"音效(固定音量,仅做起停淡入淡出防爆音);
    // 2) 当 CollisionShakeEmitter 触发镜头震动(即撞击/落地那一刻)时,按撞击强度播放"石头落地"音效。
    // 是否接地通过与 groundMask 图层的碰撞接触判断,不依赖具体的重力控制器。
    // 挂在巨石根物体上(需能找到 Rigidbody2D / CollisionShakeEmitter)。
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class BoulderSfx : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField, InspectorName("刚体")] private Rigidbody2D body;
        [SerializeField, InspectorName("撞击震屏发射器")] private CollisionShakeEmitter impactEmitter;
        [Tooltip("视为\"地面\"的图层;只有与这些图层接触时才播放滚动声。")]
        [SerializeField, InspectorName("地面图层")] private LayerMask groundMask = ~0;

        [Header("滚动音效")]
        [SerializeField, InspectorName("滚动音频")] private AudioClip rollClip;
        [Tooltip("为空时从 Resources 按此路径加载(不含扩展名)。")]
        [SerializeField, InspectorName("滚动资源路径")] private string rollClipResourcePath = "SFX/石头滚动";
        [Tooltip("低于该速度视为静止,不播放滚动声。")]
        [SerializeField, InspectorName("最小滚动速度")] private float minRollSpeed = 1.2f;
        [Range(0f, 1f)]
        [SerializeField, InspectorName("滚动音量")] private float rollVolume = 0.85f;
        [Tooltip("起停音量淡入淡出速度(每秒变化量),仅用于防爆音,不随速度变化。")]
        [SerializeField, InspectorName("淡变速度")] private float rollFadeSpeed = 6f;
        [Tooltip("离地时立即静音滚动声。")]
        [SerializeField, InspectorName("离地静音")] private bool muteWhenAirborne = true;

        [Header("落地/撞击音效")]
        [SerializeField, InspectorName("落地音频")] private AudioClip landClip;
        [Tooltip("为空时从 Resources 按此路径加载(不含扩展名)。")]
        [SerializeField, InspectorName("落地资源路径")] private string landClipResourcePath = "SFX/石头落地";
        [Tooltip("最弱撞击时的落地音量。")]
        [Range(0f, 1f)]
        [SerializeField, InspectorName("最小落地音量")] private float minLandVolume = 0.45f;
        [Tooltip("最强撞击时的落地音量。")]
        [Range(0f, 1f)]
        [SerializeField, InspectorName("最大落地音量")] private float maxLandVolume = 1f;

        private readonly HashSet<Collider2D> groundContacts = new HashSet<Collider2D>();
        private AudioSource rollSource;
        private AudioSource landSource;
        private float currentRollVolume;

        private bool IsGrounded => groundContacts.Count > 0;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            impactEmitter = ResolveImpactEmitter();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (impactEmitter == null)
            {
                impactEmitter = ResolveImpactEmitter();
            }

            if (impactEmitter == null)
            {
                Debug.LogWarning(
                    "[BoulderSfx] 未找到 CollisionShakeEmitter,落地/撞击音效不会播放。" +
                    "请把巨石上的 Collision Shake Emitter 拖到\"撞击震屏发射器\"字段。", this);
            }

            if (rollClip == null && !string.IsNullOrEmpty(rollClipResourcePath))
            {
                rollClip = Resources.Load<AudioClip>(rollClipResourcePath);
            }

            if (landClip == null && !string.IsNullOrEmpty(landClipResourcePath))
            {
                landClip = Resources.Load<AudioClip>(landClipResourcePath);
            }

            rollSource = CreateSource(loop: true);
            rollSource.clip = rollClip;
            rollSource.volume = 0f;

            landSource = CreateSource(loop: false);

            if (rollClip != null)
            {
                rollSource.Play();
            }
        }

        private void OnEnable()
        {
            if (impactEmitter != null)
            {
                impactEmitter.Impacted += HandleImpact;
            }
        }

        private void OnDisable()
        {
            if (impactEmitter != null)
            {
                impactEmitter.Impacted -= HandleImpact;
            }

            groundContacts.Clear();

            if (rollSource != null)
            {
                rollSource.volume = 0f;
                currentRollVolume = 0f;
            }
        }

        private void Update()
        {
            if (rollSource == null || rollClip == null)
            {
                return;
            }

            float speed = body != null ? body.linearVelocity.magnitude : 0f;
            bool grounded = !muteWhenAirborne || IsGrounded;
            bool rolling = grounded && speed >= minRollSpeed;

            // 音量固定,只在开始/停止滚动时做淡入淡出防爆音,不随速度变化。
            float targetVolume = rolling ? rollVolume : 0f;
            currentRollVolume = Mathf.MoveTowards(
                currentRollVolume,
                targetVolume,
                rollFadeSpeed * Time.deltaTime);
            rollSource.volume = currentRollVolume;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (IsGroundLayer(collision.collider))
            {
                groundContacts.Add(collision.collider);
            }
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            groundContacts.Remove(collision.collider);
        }

        private void HandleImpact(float intensity, Vector2 direction)
        {
            if (landSource == null || landClip == null)
            {
                return;
            }

            float volume = Mathf.Lerp(minLandVolume, maxLandVolume, Mathf.Clamp01(intensity));
            landSource.PlayOneShot(landClip, volume);
        }

        private CollisionShakeEmitter ResolveImpactEmitter()
        {
            CollisionShakeEmitter found = GetComponent<CollisionShakeEmitter>();
            if (found == null)
            {
                found = GetComponentInParent<CollisionShakeEmitter>();
            }

            if (found == null)
            {
                found = GetComponentInChildren<CollisionShakeEmitter>();
            }

            return found;
        }

        private bool IsGroundLayer(Collider2D other)
        {
            return other != null && ((1 << other.gameObject.layer) & groundMask.value) != 0;
        }

        private AudioSource CreateSource(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            // 2D 混音,音量不随相机距离衰减,适合本作的正交 2D 场景。
            source.spatialBlend = 0f;
            return source;
        }

        private void OnValidate()
        {
            minRollSpeed = Mathf.Max(0f, minRollSpeed);
            rollVolume = Mathf.Clamp01(rollVolume);
            rollFadeSpeed = Mathf.Max(0.01f, rollFadeSpeed);
            maxLandVolume = Mathf.Clamp01(maxLandVolume);
            minLandVolume = Mathf.Clamp(minLandVolume, 0f, maxLandVolume);
        }
    }
}
