#nullable enable

using UnityEngine;

namespace CGJ2026.IK
{
    /// 骨盆随坡度倾斜:用左右两脚的落点连成一条线,把骨盆旋转到与之平行,
    /// 于是两髋连线随斜坡起伏而倾斜。脚的落点由 FootPlantIK2D 贴在地表提供。
    ///
    /// 用法:把左右髋(L_Hip / R_Hip)放到一个 Pelvis 空物体下,旋转这个 Pelvis。
    /// 执行顺序设为较早,保证本组件的 LateUpdate 先于 TwoBoneIK2D 求解,腿不会晚一帧。
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class PelvisSlopeAlign2D : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("要旋转的骨盆(两髋的共同父物体)。")]
        [SerializeField] Transform pelvis = null!;

        [Tooltip("左脚落点参考(一般是左腿 IK 的 Target)。")]
        [SerializeField] Transform leftContact = null!;

        [Tooltip("右脚落点参考(一般是右腿 IK 的 Target)。")]
        [SerializeField] Transform rightContact = null!;

        [Header("倾斜")]
        [Tooltip("最大倾斜角(度),防止极端坡度把骨盆转翻。")]
        [SerializeField] float maxTiltAngle = 40f;

        [Tooltip("角度补偿:若骨盆的 local X 不是沿左右髋方向,可在此修正。")]
        [SerializeField] float angleOffset;

        [Tooltip("旋转平滑速度(越大越跟手,0 = 瞬间对齐)。仅运行时生效。")]
        [SerializeField] float smoothSpeed = 12f;

        /// 当前坡度角(度),供外部读取(如身体前倾表现)。
        public float CurrentTiltAngle { get; private set; }

        void LateUpdate() => Align();

        /// 依据两脚落点刷新骨盆倾斜。可从外部主动调用以控制时序。
        public void Align()
        {
            if (pelvis == null || leftContact == null || rightContact == null)
            {
                return;
            }

            Vector2 delta = (Vector2)rightContact.position - (Vector2)leftContact.position;
            if (delta.sqrMagnitude < 1e-6f)
            {
                return;
            }

            float target = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + angleOffset;
            target = Mathf.Clamp(target, -maxTiltAngle, maxTiltAngle);

            float current = pelvis.eulerAngles.z;
            float next = smoothSpeed > 0f && Application.isPlaying
                ? Mathf.LerpAngle(current, target, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime))
                : target;

            CurrentTiltAngle = next;

            Vector3 euler = pelvis.eulerAngles;
            euler.z = next;
            pelvis.eulerAngles = euler;
        }

        void OnDrawGizmosSelected()
        {
            if (leftContact == null || rightContact == null)
            {
                return;
            }

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(leftContact.position, rightContact.position);
            Gizmos.DrawWireSphere(leftContact.position, 0.05f);
            Gizmos.DrawWireSphere(rightContact.position, 0.05f);
        }
    }
}
