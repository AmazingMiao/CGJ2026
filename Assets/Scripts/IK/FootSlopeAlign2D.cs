#nullable enable

using UnityEngine;

namespace CGJ2026.IK
{
    /// 脚掌:把脚 sprite 连到小腿的实际末端(TwoBoneIK2D.EndPoint)并旋转贴合坡面。
    ///
    /// 脚必须连到 IK 解算出的小腿末端,而不是 Foot Target ——腿够不到目标时 target 会
    /// 超出可达范围,连 target 会与小腿脱节断裂。执行顺序设为较晚,保证在 TwoBoneIK2D
    /// 求解之后读取到当帧的末端点。
    ///
    /// 用法:脚 sprite 做成独立物体(别再当 Foot Target 的子物体,否则会被重复摆放),
    /// pivot 放在脚踝处;由本组件统一驱动位置与旋转。
    [DefaultExecutionOrder(100)]
    public class FootSlopeAlign2D : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("要驱动的脚 sprite;留空则用自身 Transform。")]
        [SerializeField] Transform foot = null!;

        [Tooltip("该腿的 IK,提供小腿末端(脚踝)落点。")]
        [SerializeField] TwoBoneIK2D leg = null!;

        [Tooltip("该腿的踩地组件,提供地表法线与着地状态。")]
        [SerializeField] FootPlantIK2D footPlant = null!;

        [Header("位置")]
        [Tooltip("脚相对小腿末端的偏移,贴坡坐标系:X 沿坡面切向(前后),Y 沿地表法线(上下)。")]
        [SerializeField] Vector2 footOffset;

        [Header("朝向")]
        [Tooltip("脚 sprite 朝向修正:脚底沿 -Y、脚背朝 +Y 的素材用 0;反了用 180。")]
        [SerializeField] float angleOffset;

        [Tooltip("悬空时脚的目标角度(相对水平,度)。")]
        [SerializeField] float airborneAngle;

        [Tooltip("旋转平滑速度(越大越跟手,0 = 瞬间对齐)。")]
        [SerializeField] float smoothSpeed = 15f;

        void OnEnable() => Initialize();

        public void Initialize()
        {
            if (foot == null)
            {
                foot = transform;
            }
        }

        void LateUpdate() => Align();

        /// 把脚连到小腿末端并对齐坡面。可从外部主动调用以控制时序。
        public void Align()
        {
            if (foot == null || leg == null)
            {
                return;
            }

            // 贴坡基准角(不含 sprite 朝向修正):着地时 +Y 对齐地表法线,悬空时用空中角度。
            float groundAngle;
            if (footPlant != null && footPlant.IsGrounded)
            {
                Vector2 normal = footPlant.GroundNormal;
                groundAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg - 90f;
            }
            else
            {
                groundAngle = airborneAngle;
            }

            float targetAngle = groundAngle + angleOffset;
            float current = foot.eulerAngles.z;
            float next = smoothSpeed > 0f
                ? Mathf.LerpAngle(current, targetAngle, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime))
                : targetAngle;

            // 位置:钉在小腿末端,再按贴坡坐标系加偏移(参考系用当前朝向去掉 sprite 修正,保证与旋转一致)。
            Quaternion offsetFrame = Quaternion.Euler(0f, 0f, next - angleOffset);
            Vector3 position = (Vector3)leg.EndPoint + offsetFrame * (Vector3)footOffset;
            position.z = foot.position.z;
            foot.position = position;

            Vector3 euler = foot.eulerAngles;
            euler.z = next;
            foot.eulerAngles = euler;
        }
    }
}
