#nullable enable

using UnityEngine;

namespace CGJ2026.IK
{
    /// 腿部踩地驱动 + 走路步态:以髋为支点向下发射线,把脚的 IK 末端目标钉在地表(含斜坡)。
    ///
    /// 横向移动时,射线方向绕髋前后摆动(钟摆),落点随之前后迈步;摆动相位由髋的水平
    /// 位移驱动,所以走多快摆多快、停下即收脚,不会出现脚在地面滑动。两条腿设为反相
    /// (一只勾 Opposite Phase),即成左右交替的自然步态。
    ///
    /// 本组件只移动 target 的位置;真正让大腿/小腿弯曲的是 TwoBoneIK2D。放在 Update
    /// 刷新,保证先于 TwoBoneIK2D 的 LateUpdate 求解。仅在运行时生效(步态依赖平滑的
    /// 逐帧位移,编辑模式下拖动会产生突变位移导致相位混叠,故不在编辑模式运行)。
    public class FootPlantIK2D : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("被驱动的脚部 IK 目标(即 TwoBoneIK2D 的 Target)。")]
        [SerializeField] Transform footTarget = null!;

        [Tooltip("射线支点,一般是髋 / 身体;留空则用自身 Transform。")]
        [SerializeField] Transform castOrigin = null!;

        [Header("射线")]
        [Tooltip("相对支点的站位偏移;X 控制左右脚横向间距,Y 微调支点高度。")]
        [SerializeField] Vector2 castOffset = Vector2.zero;

        [Tooltip("射线起点沿反方向回退多少(容忍脚略高于支点的情况)。")]
        [SerializeField] float castHeight = 1f;

        [Tooltip("从支点向下最多探测多远。")]
        [SerializeField] float castDistance = 3f;

        [Tooltip("地面层。默认 Everything;建议单独建 Ground 层只勾它。")]
        [SerializeField] LayerMask groundMask = ~0;

        [Header("落点")]
        [Tooltip("落点沿地表法线抬高,避免脚陷进地里。")]
        [SerializeField] float footHeightOffset;

        [Tooltip("勾选:探测不到地面时脚垂到最大距离处;取消则保持原位。")]
        [SerializeField] bool hangWhenNoGround = true;

        [Header("走路步态")]
        [Tooltip("启用步态摆动;关闭则射线始终竖直向下。")]
        [SerializeField] bool enableGait = true;

        [Tooltip("射线绕髋前后摆动的最大角度(度)。")]
        [SerializeField] float maxSwingAngle = 25f;

        [Tooltip("髋每水平走过这么远,该脚完成一个完整摆动周期。步频 ≈ 移动速度 / 本值,增大本值放慢步伐。")]
        [SerializeField] float strideLength = 1.5f;

        [Tooltip("与另一只脚反相:左右脚各留一只不勾、一只勾,即成交替迈步。")]
        [SerializeField] bool oppositePhase;

        [Tooltip("前送阶段抬脚高度(0 = 不抬,纯地面滑移)。")]
        [SerializeField] float liftHeight = 0.1f;

        [Tooltip("起步/收脚的过渡速度:移动时幅度涨到满,停下时收回中立站姿。")]
        [SerializeField] float gaitSettleSpeed = 8f;

        [Tooltip("低于此水平速度(单位/秒)视为站立,不推进步态,避免原地抖脚。")]
        [SerializeField] float idleSpeedThreshold = 0.05f;

        /// 当前是否踩到地面。
        public bool IsGrounded { get; private set; }

        /// 最近一次落点的地表法线(悬空时为 Vector2.up)。
        public Vector2 GroundNormal { get; private set; } = Vector2.up;

        /// 最近一次射线命中的地面点(不含抬脚偏移),供骨盆对齐等使用更稳定。
        public Vector2 GroundPoint { get; private set; }

        float gaitPhase;
        float lastPivotX;
        bool hasLastPivotX;
        float strideBlend;

        void OnEnable() => Initialize();

        public void Initialize()
        {
            GroundNormal = Vector2.up;
            hasLastPivotX = false;
            gaitPhase = 0f;
            strideBlend = 0f;
        }

        void Update() => UpdateFoot();

        /// 刷新一次脚部目标位置。可从外部主动调用以控制时序。
        public void UpdateFoot()
        {
            if (footTarget == null)
            {
                return;
            }

            Transform basis = castOrigin != null ? castOrigin : transform;
            Vector2 pivot = (Vector2)basis.position + castOffset;

            (float swing, float lift) = AdvanceGait(pivot.x);

            Vector2 rayDir = (Vector2)(Quaternion.Euler(0f, 0f, swing) * Vector3.down);
            Vector2 rayStart = pivot - rayDir * castHeight;
            float rayLength = castHeight + castDistance;

            RaycastHit2D hit = Physics2D.Raycast(rayStart, rayDir, rayLength, groundMask);
            if (hit.collider != null)
            {
                IsGrounded = true;
                GroundNormal = hit.normal;
                GroundPoint = hit.point;
                MoveTarget(hit.point + hit.normal * footHeightOffset + Vector2.up * lift);
                return;
            }

            IsGrounded = false;
            GroundNormal = Vector2.up;
            Vector2 fallback = pivot + rayDir * castDistance;
            GroundPoint = fallback;
            if (hangWhenNoGround)
            {
                MoveTarget(fallback + Vector2.up * lift);
            }
        }

        /// 根据髋的水平位移推进步态相位,返回本帧的摆动角与抬脚高度。
        (float swing, float lift) AdvanceGait(float pivotX)
        {
            if (!enableGait)
            {
                strideBlend = 0f;
                return (0f, 0f);
            }

            if (!hasLastPivotX)
            {
                lastPivotX = pivotX;
                hasLastPivotX = true;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float deltaX = pivotX - lastPivotX;
            lastPivotX = pivotX;

            // 用水平速度判定是否在走,过滤站立时的微小抖动(如骨盆对齐带来的位移)。
            float speed = Mathf.Abs(deltaX) / dt;
            bool moving = speed > idleSpeedThreshold;

            if (moving && strideLength > 1e-4f)
            {
                // 防混叠:单帧相位推进钳制在半个周期内,避免瞬移/大位移导致步态乱跳。
                float phaseStep = deltaX / strideLength * (Mathf.PI * 2f);
                gaitPhase += Mathf.Clamp(phaseStep, -Mathf.PI, Mathf.PI);
            }

            // 移动时幅度涨到满,停下时平滑收回中立站姿,避免定格在半步姿势。
            strideBlend = Mathf.MoveTowards(strideBlend, moving ? 1f : 0f, gaitSettleSpeed * dt);

            float phase = gaitPhase + (oppositePhase ? Mathf.PI : 0f);
            float swing = maxSwingAngle * Mathf.Sin(phase) * strideBlend;
            float lift = Mathf.Max(0f, Mathf.Cos(phase)) * liftHeight * strideBlend;
            return (swing, lift);
        }

        void MoveTarget(Vector2 worldPosition)
        {
            Vector3 next = worldPosition;
            next.z = footTarget.position.z;
            footTarget.position = next;
        }

        void OnDrawGizmosSelected()
        {
            Transform basis = castOrigin != null ? castOrigin : transform;
            Vector2 pivot = (Vector2)basis.position + castOffset;
            float swing = enableGait ? maxSwingAngle * Mathf.Sin(gaitPhase + (oppositePhase ? Mathf.PI : 0f)) * strideBlend : 0f;
            Vector2 rayDir = (Vector2)(Quaternion.Euler(0f, 0f, swing) * Vector3.down);

            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawLine(pivot - rayDir * castHeight, pivot + rayDir * castDistance);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(pivot, 0.04f);
            if (footTarget != null)
            {
                Gizmos.DrawWireSphere(footTarget.position, 0.06f);
            }
        }
    }
}
