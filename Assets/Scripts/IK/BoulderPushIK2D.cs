#nullable enable

using UnityEngine;

namespace CGJ2026.IK
{
    /// 未吸附巨石时手臂的行为模式。
    public enum ArmIdleMode
    {
        /// 回到静止点(Rest Target),或停在原地。
        HoldRest,

        /// 程序化步态摆动:随水平位移前后交替摆,受控、稳定。
        Procedural,

        /// 纯物理钟摆:受重力与走路惯性带动,完全不受控。
        Physics
    }

    /// 推石手臂:肩离巨石表面足够近时,把手臂 IK 的 Target 吸附到巨石圆周上;巨石滚动时
    /// 接触点被带着沿圆周移动(带回弹),模拟推着滚石的手感。松开时手回到静止姿势。
    ///
    /// 每条手臂挂一个,引用各自的肩(origin)与手 Target。放在 Update 刷新,先于手臂
    /// TwoBoneIK2D 的 LateUpdate 求解。
    public class BoulderPushIK2D : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("肩关节(判定距离用),一般是手臂 IK 的 Origin。")]
        [SerializeField] Transform shoulder = null!;

        [Tooltip("被驱动的手臂 IK Target。")]
        [SerializeField] Transform target = null!;

        [Tooltip("巨石(需带 CircleCollider2D 以自动取半径)。")]
        [SerializeField] Transform boulder = null!;

        [Tooltip("松开吸附时手回到的位置(可空;为空则停在原地)。")]
        [SerializeField] Transform restTarget = null!;

        [Tooltip("该臂的 TwoBoneIK2D;Physics 模式需要它来渲染双摆姿态。为空则物理退化为单摆。")]
        [SerializeField] TwoBoneIK2D arm = null!;

        [Header("吸附")]
        [Tooltip("自动从巨石的 CircleCollider2D 读取半径(乘缩放)。")]
        [SerializeField] bool autoRadius = true;

        [Tooltip("手动指定巨石半径(autoRadius 关闭时用)。")]
        [SerializeField] float boulderRadius = 1.5f;

        [Tooltip("接触点相对表面的额外偏移(+ 向外,容纳手的厚度)。")]
        [SerializeField] float contactRadiusOffset;

        [Tooltip("肩到巨石表面小于此距离即吸附。")]
        [SerializeField] float grabDistance = 0.6f;

        [Tooltip("松开迟滞:需比吸附距离再远这么多才松手,避免临界抖动。")]
        [SerializeField] float releaseHysteresis = 0.25f;

        [Tooltip("吸附/松开时目标位置的平滑速度(0 = 瞬间)。")]
        [SerializeField] float snapSpeed = 18f;

        [Header("滚动推动")]
        [Tooltip("手随巨石滚动被带动的比例;方向不对就取负值。")]
        [SerializeField] float rollFollow = 1f;

        [Tooltip("手沿圆周被带离近侧的最大角度(度)。")]
        [SerializeField] float maxRollAngle = 30f;

        [Tooltip("巨石不滚动时,手回到近侧的角速度(度/秒)。")]
        [SerializeField] float rollReturnSpeed = 120f;

        [Header("自由摆动(未吸附时)")]
        [Tooltip("未吸附时手臂的行为:HoldRest 回到静止点;Procedural 程序化步态摆动;Physics 纯物理钟摆。")]
        [SerializeField] ArmIdleMode idleMode = ArmIdleMode.Physics;

        [Tooltip("手到肩的悬垂长度(摆动半径 / 摆锤长度)。")]
        [SerializeField] float armLength = 0.8f;

        [Tooltip("静止时手相对肩的角度(度):-90 = 正下方。")]
        [SerializeField] float restAngle = -90f;

        [Tooltip("前后摆动的最大角度(度)。")]
        [SerializeField] float maxSwingAngle = 25f;

        [Tooltip("肩每水平走过这么远,手臂完成一个摆动周期。步频 ≈ 移动速度 / 本值。")]
        [SerializeField] float strideLength = 1.5f;

        [Tooltip("与另一侧反相:左右臂各留一只不勾、一只勾,即成交替前后摆。")]
        [SerializeField] bool oppositePhase;

        [Tooltip("起摆/收摆过渡速度:移动时幅度涨到满,停下收回垂手。")]
        [SerializeField] float swingSettleSpeed = 8f;

        [Tooltip("低于此水平速度(单位/秒)视为站立,不推进摆动。")]
        [SerializeField] float idleSpeedThreshold = 0.05f;

        [Header("物理摆动(Physics 模式)")]
        [Tooltip("重力强度:把手臂拉回静止方向的力度。")]
        [SerializeField] float pendulumGravity = 25f;

        [Tooltip("角速度阻尼:越大摆动衰减越快。")]
        [SerializeField] float pendulumDamping = 2.5f;

        [Tooltip("肩部加速度对摆动的驱动强度:走路启停/变向带动手臂的幅度。")]
        [SerializeField] float pendulumDriveScale = 1f;

        [Tooltip("摆动角速度上限(度/秒),防止极端加速度导致数值爆炸。")]
        [SerializeField] float pendulumMaxAngularSpeed = 1440f;

        [Tooltip("双摆模式下,上臂与下臂夹角相对伸直状态的最大弯曲角(度)。")]
        [SerializeField] float maxElbowBend = 90f;

        [Tooltip("约束迭代次数,越多越硬(骨长/夹角越不易被拉长)。")]
        [SerializeField] int solverIterations = 6;

        float radius = 1.5f;
        bool grabbing;
        float rollOffset;
        float lastBoulderAngle;
        bool hasLastAngle;

        float swingPhase;
        float lastShoulderX;
        bool hasLastShoulderX;
        float swingBlend;

        float pendulumAngle;
        float pendulumAngularVelocity;
        Vector2 lastShoulderPosition;
        Vector2 lastShoulderVelocity;
        bool hasPendulumState;

        Vector2 elbowPosition;
        Vector2 elbowPrevious;
        Vector2 handPosition;
        Vector2 handPrevious;
        bool hasChainState;

        /// 当前手是否吸附在巨石上。
        public bool IsGrabbing => grabbing;

        void OnEnable() => Initialize();

        public void Initialize()
        {
            radius = ResolveRadius();
            grabbing = false;
            rollOffset = 0f;
            hasLastAngle = false;
            swingPhase = 0f;
            swingBlend = 0f;
            hasLastShoulderX = false;
            hasPendulumState = false;
            hasChainState = false;
        }

        void Update()
        {
            if (shoulder == null || target == null || boulder == null)
            {
                return;
            }

            Vector2 center = boulder.position;
            Vector2 toShoulder = (Vector2)shoulder.position - center;
            float surfaceDistance = toShoulder.magnitude - radius;

            if (!grabbing && surfaceDistance <= grabDistance)
            {
                grabbing = true;
            }
            else if (grabbing && surfaceDistance > grabDistance + releaseHysteresis)
            {
                grabbing = false;
            }

            float deltaAngle = ReadBoulderDeltaAngle();

            if (grabbing)
            {
                ReleasePhysicsControl();
                float baseAngle = Mathf.Atan2(toShoulder.y, toShoulder.x) * Mathf.Rad2Deg;

                // 随滚动被带动并钳制,不滚动时回到近侧;稳态下落后角与滚速成正比。
                rollOffset = Mathf.Clamp(rollOffset + deltaAngle * rollFollow, -maxRollAngle, maxRollAngle);
                rollOffset = Mathf.MoveTowards(rollOffset, 0f, rollReturnSpeed * Time.deltaTime);

                float contactAngle = (baseAngle + rollOffset) * Mathf.Deg2Rad;
                float reach = radius + contactRadiusOffset;
                Vector2 contact = center + new Vector2(Mathf.Cos(contactAngle), Mathf.Sin(contactAngle)) * reach;
                MoveTargetTo(contact);
                return;
            }

            rollOffset = 0f;
            switch (idleMode)
            {
                case ArmIdleMode.Procedural:
                    ReleasePhysicsControl();
                    float swingAngle = (restAngle + AdvanceArmSwing()) * Mathf.Deg2Rad;
                    MoveTargetTo((Vector2)shoulder.position + new Vector2(Mathf.Cos(swingAngle), Mathf.Sin(swingAngle)) * armLength);
                    break;

                case ArmIdleMode.Physics:
                    if (arm != null)
                    {
                        arm.ExternalPose = true;
                        StepArmPhysics();
                    }
                    else
                    {
                        // 未接 TwoBoneIK2D:退化为单摆,直接驱动手 Target。
                        Vector3 handPhysics = StepPendulum();
                        handPhysics.z = target.position.z;
                        target.position = handPhysics;
                    }
                    break;

                default:
                    ReleasePhysicsControl();
                    if (restTarget != null)
                    {
                        MoveTargetTo(restTarget.position);
                    }
                    break;
            }
        }

        /// 交还姿态控制权给 TwoBoneIK2D,并清理物理状态,以便下次重新起摆时续接。
        void ReleasePhysicsControl()
        {
            hasPendulumState = false;
            hasChainState = false;
            if (arm != null)
            {
                arm.ExternalPose = false;
            }
        }

        /// 物理双摆一步:Verlet 质点链(肩→肘→手),约束骨长与肘部夹角,渲染到 TwoBoneIK2D。
        void StepArmPhysics()
        {
            Vector2 shoulderPosition = shoulder.position;
            float upperLength = Mathf.Max(arm.UpperLength, 1e-3f);
            float lowerLength = Mathf.Max(arm.LowerLength, 1e-3f);
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            if (!hasChainState)
            {
                float restRad = restAngle * Mathf.Deg2Rad;
                Vector2 restDir = new Vector2(Mathf.Cos(restRad), Mathf.Sin(restRad));
                elbowPosition = shoulderPosition + restDir * upperLength;
                handPosition = elbowPosition + restDir * lowerLength;
                elbowPrevious = elbowPosition;
                handPrevious = handPosition;
                hasChainState = true;
            }

            Vector2 gravity = new Vector2(0f, -pendulumGravity);
            float damp = Mathf.Clamp01(1f - pendulumDamping * dt);
            float step = dt * dt;

            // Verlet 积分(速度由位置历史隐含,支点随肩移动自然注入惯性)。
            Vector2 elbowCurrent = elbowPosition;
            Vector2 handCurrent = handPosition;
            elbowPosition = elbowCurrent + (elbowCurrent - elbowPrevious) * damp + gravity * step;
            handPosition = handCurrent + (handCurrent - handPrevious) * damp + gravity * step;
            elbowPrevious = elbowCurrent;
            handPrevious = handCurrent;

            int iterations = Mathf.Max(1, solverIterations);
            for (int i = 0; i < iterations; i++)
            {
                // 肘固定到肩(骨长 upperLength)。
                Vector2 d1 = elbowPosition - shoulderPosition;
                float dist1 = d1.magnitude;
                if (dist1 > 1e-6f)
                {
                    elbowPosition = shoulderPosition + d1 * (upperLength / dist1);
                }

                // 手固定到肘(骨长 lowerLength)。
                Vector2 d2 = handPosition - elbowPosition;
                float dist2 = d2.magnitude;
                if (dist2 > 1e-6f)
                {
                    handPosition = elbowPosition + d2 * (lowerLength / dist2);
                }

                ApplyElbowLimit(shoulderPosition, lowerLength);
            }

            arm.RenderPose(elbowPosition, handPosition);
        }

        /// 限制上臂与下臂夹角:相对伸直(共线)状态的弯曲角钳制在 ±maxElbowBend。
        void ApplyElbowLimit(Vector2 shoulderPosition, float lowerLength)
        {
            Vector2 upper = elbowPosition - shoulderPosition;
            Vector2 lower = handPosition - elbowPosition;
            if (upper.sqrMagnitude < 1e-8f || lower.sqrMagnitude < 1e-8f)
            {
                return;
            }

            float upperAngle = Mathf.Atan2(upper.y, upper.x) * Mathf.Rad2Deg;
            float lowerAngle = Mathf.Atan2(lower.y, lower.x) * Mathf.Rad2Deg;
            float relative = Mathf.DeltaAngle(upperAngle, lowerAngle);
            float clamped = Mathf.Clamp(relative, -maxElbowBend, maxElbowBend);
            if (Mathf.Abs(clamped - relative) > 1e-4f)
            {
                float newLowerRad = (upperAngle + clamped) * Mathf.Deg2Rad;
                handPosition = elbowPosition + new Vector2(Mathf.Cos(newLowerRad), Mathf.Sin(newLowerRad)) * lowerLength;
            }
        }

        /// 纯物理钟摆一步:受重力拉回静止方向、受肩部加速度(走路惯性)带动,返回手的世界坐标。
        Vector2 StepPendulum()
        {
            Vector2 shoulderPosition = shoulder.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            if (!hasPendulumState)
            {
                // 从当前手的方向续接,避免切换时突跳;无有效方向则从静止角起摆。
                Vector2 fromShoulder = (Vector2)target.position - shoulderPosition;
                pendulumAngle = fromShoulder.sqrMagnitude > 1e-6f
                    ? Mathf.Atan2(fromShoulder.y, fromShoulder.x) * Mathf.Rad2Deg
                    : restAngle;
                pendulumAngularVelocity = 0f;
                lastShoulderPosition = shoulderPosition;
                lastShoulderVelocity = Vector2.zero;
                hasPendulumState = true;
            }

            Vector2 shoulderVelocity = (shoulderPosition - lastShoulderPosition) / dt;
            Vector2 shoulderAcceleration = (shoulderVelocity - lastShoulderVelocity) / dt;
            lastShoulderPosition = shoulderPosition;
            lastShoulderVelocity = shoulderVelocity;

            // 支点移动的等效惯性力 = -肩加速度;叠加重力,投影到摆动切向得到角加速度。
            float angleRad = pendulumAngle * Mathf.Deg2Rad;
            Vector2 tangent = new Vector2(-Mathf.Sin(angleRad), Mathf.Cos(angleRad));
            Vector2 forcePerMass = new Vector2(0f, -pendulumGravity) - shoulderAcceleration * pendulumDriveScale;

            float length = Mathf.Max(armLength, 1e-3f);
            float angularAccel = Vector2.Dot(forcePerMass, tangent) / length * Mathf.Rad2Deg;
            angularAccel -= pendulumDamping * pendulumAngularVelocity;

            pendulumAngularVelocity = Mathf.Clamp(
                pendulumAngularVelocity + angularAccel * dt,
                -pendulumMaxAngularSpeed,
                pendulumMaxAngularSpeed);
            pendulumAngle += pendulumAngularVelocity * dt;

            float finalRad = pendulumAngle * Mathf.Deg2Rad;
            return (Vector2)shoulder.position + new Vector2(Mathf.Cos(finalRad), Mathf.Sin(finalRad)) * armLength;
        }

        /// 根据肩的水平位移推进手臂摆动相位,返回本帧相对静止角的摆动偏移(度)。
        float AdvanceArmSwing()
        {
            float shoulderX = shoulder.position.x;
            if (!hasLastShoulderX)
            {
                lastShoulderX = shoulderX;
                hasLastShoulderX = true;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float deltaX = shoulderX - lastShoulderX;
            lastShoulderX = shoulderX;

            bool moving = Mathf.Abs(deltaX) / dt > idleSpeedThreshold;
            if (moving && strideLength > 1e-4f)
            {
                float phaseStep = deltaX / strideLength * (Mathf.PI * 2f);
                swingPhase += Mathf.Clamp(phaseStep, -Mathf.PI, Mathf.PI);
            }

            swingBlend = Mathf.MoveTowards(swingBlend, moving ? 1f : 0f, swingSettleSpeed * dt);

            float phase = swingPhase + (oppositePhase ? Mathf.PI : 0f);
            return maxSwingAngle * Mathf.Sin(phase) * swingBlend;
        }

        float ReadBoulderDeltaAngle()
        {
            float angle = boulder.eulerAngles.z;
            if (!hasLastAngle)
            {
                lastBoulderAngle = angle;
                hasLastAngle = true;
            }

            float delta = Mathf.DeltaAngle(lastBoulderAngle, angle);
            lastBoulderAngle = angle;
            return delta;
        }

        void MoveTargetTo(Vector2 position)
        {
            Vector3 next = snapSpeed > 0f
                ? Vector2.Lerp(target.position, position, 1f - Mathf.Exp(-snapSpeed * Time.deltaTime))
                : position;
            next.z = target.position.z;
            target.position = next;
        }

        float ResolveRadius()
        {
            if (autoRadius && boulder != null && boulder.TryGetComponent(out CircleCollider2D circle))
            {
                float scale = Mathf.Max(Mathf.Abs(boulder.lossyScale.x), Mathf.Abs(boulder.lossyScale.y));
                return circle.radius * scale;
            }

            return boulderRadius;
        }

        void OnDrawGizmosSelected()
        {
            if (boulder == null)
            {
                return;
            }

            float r = Application.isPlaying ? radius : ResolveRadius();
            Gizmos.color = grabbing ? Color.green : Color.gray;
            Gizmos.DrawWireSphere(boulder.position, r);
            if (target != null)
            {
                Gizmos.DrawWireSphere(target.position, 0.06f);
            }
        }
    }
}
