using System.Collections.Generic;
using UnityEngine;

namespace CGJ2026.Gameplay
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(HingeJoint2D))]
    public sealed class SeesawLever2D : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField, InspectorName("板子刚体")] private Rigidbody2D body;
        [SerializeField, InspectorName("板子碰撞体")] private Collider2D boardCollider;
        [SerializeField, InspectorName("中心铰链")] private HingeJoint2D hinge;

        [Header("板子尺寸")]
        [SerializeField, InspectorName("板子长度")] private float boardLength = 7f;
        [SerializeField, InspectorName("板子厚度")] private float boardHeight = 0.35f;
        [SerializeField, InspectorName("占地高度")] private float footprintHeight = 2f;
        [SerializeField, InspectorName("最大旋转角度")] private float maxAngle = 22f;
        [SerializeField, InspectorName("支点无效范围")] private float centerDeadZone = 0.35f;

        [Header("铰链手感")]
        [SerializeField, InspectorName("板子质量")] private float boardMass = 12f;
        [SerializeField, InspectorName("线性阻尼")] private float linearDamping = 0.25f;
        [SerializeField, InspectorName("旋转阻尼")] private float angularDamping = 1.75f;
        [SerializeField, InspectorName("接触保留时间")] private float contactStaleTime = 0.08f;

        [Header("弹射规则")]
        [SerializeField, InspectorName("最低力臂倍率"), Range(0f, 1f)] private float minLeverArmScale = 0.45f;
        [SerializeField, InspectorName("满力距离比例"), Range(0.01f, 1f)] private float fullLeverArmAt = 0.6f;
        [SerializeField, InspectorName("最低下砸速度")] private float minImpactSpeed = 4f;
        [SerializeField, InspectorName("最低质量倍率")] private float minSourceMassRatio = 1.5f;
        [SerializeField, InspectorName("最低弹射能量")] private float minLaunchPower = 18f;
        [SerializeField, InspectorName("被弹质量阻力")] private float targetMassResistance = 8f;
        [SerializeField, InspectorName("能量转冲量倍率")] private float launchImpulsePerPower = 0.55f;
        [SerializeField, InspectorName("最小弹射冲量")] private float minLaunchImpulse = 9f;
        [SerializeField, InspectorName("最大弹射冲量")] private float maxLaunchImpulse = 28f;
        [SerializeField, InspectorName("向外弹射偏移"), Range(0f, 1f)] private float outwardLaunchBias = 0.22f;
        [SerializeField, InspectorName("弹射冷却时间")] private float launchCooldown = 0.18f;

        private readonly Dictionary<Rigidbody2D, ContactLoad> contacts = new Dictionary<Rigidbody2D, ContactLoad>();
        private float nextLaunchTime;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            boardCollider = GetComponent<Collider2D>();
            hinge = GetComponent<HingeJoint2D>();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (boardCollider == null)
            {
                boardCollider = GetComponent<Collider2D>();
            }

            if (hinge == null)
            {
                hinge = GetComponent<HingeJoint2D>();
            }

            ConfigureBody();
            ConfigureHinge();
        }

        private void OnEnable()
        {
            contacts.Clear();
            nextLaunchTime = 0f;
        }

        private void FixedUpdate()
        {
            PruneContacts();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            UpdateContact(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            UpdateContact(collision);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            Rigidbody2D otherBody = GetOtherBody(collision);
            if (otherBody != null)
            {
                contacts.Remove(otherBody);
            }
        }

        private void ConfigureBody()
        {
            if (body == null)
            {
                return;
            }

            body.bodyType = RigidbodyType2D.Dynamic;
            body.mass = Mathf.Max(0.01f, boardMass);
            body.gravityScale = 1f;
            body.linearDamping = Mathf.Max(0f, linearDamping);
            body.angularDamping = Mathf.Max(0f, angularDamping);
            body.useFullKinematicContacts = false;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        private void ConfigureHinge()
        {
            if (hinge == null)
            {
                return;
            }

            hinge.anchor = Vector2.zero;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.useLimits = true;
            hinge.useMotor = false;
            hinge.enableCollision = false;
            JointAngleLimits2D limits = hinge.limits;
            limits.min = -Mathf.Abs(maxAngle);
            limits.max = Mathf.Abs(maxAngle);
            hinge.limits = limits;
        }

        private void UpdateContact(Collision2D collision)
        {
            Rigidbody2D otherBody = GetOtherBody(collision);
            if (otherBody == null || otherBody == body || !otherBody.simulated)
            {
                return;
            }

            Vector2 contactPoint = GetAverageContactPoint(collision, otherBody.worldCenterOfMass);
            float leverX = GetLeverX(contactPoint);
            if (Mathf.Abs(leverX) < centerDeadZone)
            {
                contacts.Remove(otherBody);
                return;
            }

            int side = leverX < 0f ? -1 : 1;
            float rawArm = Mathf.Clamp01(Mathf.Abs(leverX) / Mathf.Max(0.01f, boardLength * 0.5f));
            float normalizedArm = Mathf.Clamp01(rawArm / Mathf.Max(0.01f, fullLeverArmAt));
            float arm = Mathf.Lerp(Mathf.Clamp01(minLeverArmScale), 1f, normalizedArm);
            Vector2 relativeVelocity = otherBody.linearVelocity - (body != null ? body.linearVelocity : Vector2.zero);
            float downwardSpeed = Mathf.Max(0f, -Vector2.Dot(relativeVelocity, (Vector2)transform.up));

            ContactLoad contact = new ContactLoad(otherBody, side, arm, downwardSpeed, Time.fixedTime);
            contacts[otherBody] = contact;

            if (downwardSpeed >= minImpactSpeed)
            {
                TryLaunchOppositeBodies(contact);
            }
        }

        private Rigidbody2D GetOtherBody(Collision2D collision)
        {
            Rigidbody2D otherBody = collision.rigidbody;
            if (otherBody == body)
            {
                otherBody = collision.otherRigidbody;
            }

            return otherBody == body ? null : otherBody;
        }

        private Vector2 GetAverageContactPoint(Collision2D collision, Vector2 fallback)
        {
            int count = collision.contactCount;
            if (count <= 0)
            {
                return fallback;
            }

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < count; i++)
            {
                sum += collision.GetContact(i).point;
            }

            return sum / count;
        }

        private float GetLeverX(Vector2 worldPoint)
        {
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            return localPoint.x * Mathf.Max(0.01f, Mathf.Abs(transform.lossyScale.x));
        }

        private void TryLaunchOppositeBodies(ContactLoad source)
        {
            if (Time.time < nextLaunchTime || source.Body == null)
            {
                return;
            }

            float sourcePower = source.Body.mass * source.DownwardSpeed * source.Arm;
            foreach (ContactLoad target in contacts.Values)
            {
                if (target.Body == null || target.Body == source.Body || target.Side == source.Side)
                {
                    continue;
                }

                if (!CanLaunch(source.Body.mass, sourcePower, target.Body.mass, minSourceMassRatio, minLaunchPower, targetMassResistance))
                {
                    continue;
                }

                float requiredPower = minLaunchPower + target.Body.mass * targetMassResistance;
                float impulse = Mathf.Clamp((sourcePower - requiredPower) * launchImpulsePerPower, minLaunchImpulse, maxLaunchImpulse);
                Vector2 launchDirection = ((Vector2)transform.up + Vector2.right * (target.Side * outwardLaunchBias)).normalized;
                Vector2 velocity = target.Body.linearVelocity;
                if (velocity.y < 0f)
                {
                    velocity.y = 0f;
                    target.Body.linearVelocity = velocity;
                }

                target.Body.AddForce(launchDirection * impulse, ForceMode2D.Impulse);
                nextLaunchTime = Time.time + launchCooldown;
                return;
            }
        }

        public static bool CanLaunch(
            float sourceMass,
            float sourcePower,
            float targetMass,
            float sourceMassRatio,
            float launchPower,
            float massResistance)
        {
            if (sourceMass < targetMass * Mathf.Max(0f, sourceMassRatio))
            {
                return false;
            }

            float requiredPower = Mathf.Max(0f, launchPower) + targetMass * Mathf.Max(0f, massResistance);
            return sourcePower >= requiredPower;
        }

        private void PruneContacts()
        {
            if (contacts.Count == 0)
            {
                return;
            }

            float now = Time.fixedTime;
            using (Dictionary<Rigidbody2D, ContactLoad>.KeyCollection.Enumerator iterator = contacts.Keys.GetEnumerator())
            {
                List<Rigidbody2D> staleBodies = null;
                while (iterator.MoveNext())
                {
                    Rigidbody2D contactBody = iterator.Current;
                    if (contactBody == null || now - contacts[contactBody].LastSeenTime > contactStaleTime)
                    {
                        if (staleBodies == null)
                        {
                            staleBodies = new List<Rigidbody2D>();
                        }

                        staleBodies.Add(contactBody);
                    }
                }

                if (staleBodies == null)
                {
                    return;
                }

                for (int i = 0; i < staleBodies.Count; i++)
                {
                    contacts.Remove(staleBodies[i]);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform root = transform.parent != null ? transform.parent : transform;
            Gizmos.matrix = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);

            Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.45f);
            Gizmos.DrawWireCube(new Vector3(0f, footprintHeight * 0.5f, 0f), new Vector3(boardLength, footprintHeight, 0f));

            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 0.85f, 0.15f, 0.85f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(boardLength, boardHeight, 0f));

            Gizmos.color = new Color(0.3f, 1f, 0.35f, 0.75f);
            float endCenter = boardLength * 0.37f;
            float endWidth = boardLength * 0.2f;
            Gizmos.DrawWireCube(new Vector3(-endCenter, 0f, 0f), new Vector3(endWidth, boardHeight * 1.6f, 0f));
            Gizmos.DrawWireCube(new Vector3(endCenter, 0f, 0f), new Vector3(endWidth, boardHeight * 1.6f, 0f));
        }

        private readonly struct ContactLoad
        {
            public ContactLoad(Rigidbody2D body, int side, float arm, float downwardSpeed, float lastSeenTime)
            {
                Body = body;
                Side = side;
                Arm = arm;
                DownwardSpeed = downwardSpeed;
                LastSeenTime = lastSeenTime;
            }

            public Rigidbody2D Body { get; }
            public int Side { get; }
            public float Arm { get; }
            public float DownwardSpeed { get; }
            public float LastSeenTime { get; }
        }
    }
}
