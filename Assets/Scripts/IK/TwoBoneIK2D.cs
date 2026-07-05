#nullable enable

using UnityEngine;

namespace CGJ2026.IK
{
    /// 2D 两骨骼解析 IK(圆-圆交点解法)。
    ///
    /// 与「父子层级旋转」写法不同,这里不要求 upper/lower 互为父子,也不要求 sprite
    /// pivot 在关节处:每帧先用两段固定长度求出中间关节位置(膝/肘),再把两段 sprite
    /// 直接摆到 root→joint、joint→end 之间。因此场景里扁平摆放的同款矩形 sprite 直接可用。
    ///
    /// 适用于大腿-小腿、大臂-小臂这类定长两段链;末端够不到目标时链条自动伸直、不抖动。
    [ExecuteAlways]
    public class TwoBoneIK2D : MonoBehaviour
    {
        [Header("骨骼引用")]
        [Tooltip("肩 / 髋 关节锚点(链条起点)。")]
        [SerializeField] Transform origin = null!;

        [Tooltip("上骨 sprite:大臂 / 大腿。")]
        [SerializeField] Transform upperBone = null!;

        [Tooltip("下骨 sprite:小臂 / 小腿。")]
        [SerializeField] Transform lowerBone = null!;

        [Tooltip("末端目标:手 / 脚要够到的点。")]
        [SerializeField] Transform target = null!;

        [Header("链条参数")]
        [Tooltip("上骨长度(世界单位)。")]
        [SerializeField] float upperLength = 0.5f;

        [Tooltip("下骨长度(世界单位)。")]
        [SerializeField] float lowerLength = 0.5f;

        [Tooltip("关节弯曲方向:勾选朝 root→target 方向的左侧弯曲,取消则右侧。")]
        [SerializeField] bool bendLeft = true;

        [Header("外观")]
        [Tooltip("拉伸 sprite 的 Y 轴使其正好铺满骨段(sprite 需为中心 pivot、竖直朝向)。")]
        [SerializeField] bool stretchToFit = true;

        [Tooltip("上骨 sprite 朝向修正:竖直(长边沿 +Y)用 -90;水平(长边沿 +X)用 0。")]
        [SerializeField] float upperAngleOffset = -90f;

        [Tooltip("下骨 sprite 朝向修正:竖直(长边沿 +Y)用 -90;水平(长边沿 +X)用 0。")]
        [SerializeField] float lowerAngleOffset = -90f;

        [Tooltip("关节交错量:上下骨各朝共享关节多伸出这么长(世界单位),叠住露出的缝隙。0 = 不交错。需开启 Stretch To Fit。")]
        [SerializeField] float jointOverlap = 0.05f;

        float upperSpriteHeight = 1f;
        float lowerSpriteHeight = 1f;

        /// 下骨实际到达的末端(脚踝 / 手腕)世界坐标。脚 sprite 应连到这里,而非 target
        /// ——腿够不到时 target 会超出可达范围,连 target 会断裂。
        public Vector2 EndPoint { get; private set; }

        /// 中间关节(膝 / 肘)世界坐标。
        public Vector2 JointPoint { get; private set; }

        /// 链条起点锚点(肩 / 髋),供外部物理模拟读取根位置。
        public Transform Origin => origin;

        /// 上骨长度(供外部物理模拟等读取)。
        public float UpperLength => upperLength;

        /// 下骨长度(供外部物理模拟等读取)。
        public float LowerLength => lowerLength;

        /// 由外部接管姿态(如物理双摆):为 true 时跳过自身 IK 解算,改由外部调用 RenderPose。
        public bool ExternalPose { get; set; }

        void OnEnable() => Initialize();

        public void Initialize()
        {
            upperSpriteHeight = MeasureSpriteHeight(upperBone);
            lowerSpriteHeight = MeasureSpriteHeight(lowerBone);
        }

        void LateUpdate() => Solve();

        /// 求解一次并摆放两段骨骼。可从外部主动调用(如在自定义时序里驱动)。
        public void Solve()
        {
            if (ExternalPose)
            {
                return;
            }

            if (origin == null || upperBone == null || lowerBone == null || target == null)
            {
                return;
            }

            Vector2 root = origin.position;
            Vector2 toGoal = (Vector2)target.position - root;

            float chain = upperLength + lowerLength;
            float minReach = Mathf.Abs(upperLength - lowerLength) + 1e-4f;
            float d = Mathf.Clamp(toGoal.magnitude, minReach, chain - 1e-4f);
            Vector2 dir = toGoal.sqrMagnitude > 1e-8f ? toGoal.normalized : Vector2.right;

            // 圆-圆交点:a 为关节在 root→target 轴上的投影距离,h 为偏离该轴的高度。
            float a = (upperLength * upperLength - lowerLength * lowerLength + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - a * a));
            Vector2 perpendicular = new Vector2(-dir.y, dir.x) * (bendLeft ? 1f : -1f);

            Vector2 joint = root + dir * a + perpendicular * h;
            Vector2 end = root + dir * d;

            JointPoint = joint;
            EndPoint = end;

            // 上骨朝关节(to 端)、下骨朝关节(from 端)各多伸 jointOverlap,叠住关节缝隙。
            PlaceBone(upperBone, root, joint, upperSpriteHeight, 0f, jointOverlap, upperAngleOffset);
            PlaceBone(lowerBone, joint, end, lowerSpriteHeight, jointOverlap, 0f, lowerAngleOffset);
        }

        /// 直接按外部给定的关节(肘/膝)与末端(手/脚)世界坐标摆放骨骼,不做 IK 解算。
        /// 供物理双摆等外部驱动使用;需先把 ExternalPose 置为 true 以免被自身 Solve 覆盖。
        public void RenderPose(Vector2 joint, Vector2 end)
        {
            if (origin == null || upperBone == null || lowerBone == null)
            {
                return;
            }

            Vector2 root = origin.position;
            JointPoint = joint;
            EndPoint = end;

            PlaceBone(upperBone, root, joint, upperSpriteHeight, 0f, jointOverlap, upperAngleOffset);
            PlaceBone(lowerBone, joint, end, lowerSpriteHeight, jointOverlap, 0f, lowerAngleOffset);
        }

        void PlaceBone(Transform bone, Vector2 from, Vector2 to, float spriteHeight, float extendFrom, float extendTo, float angleOffset)
        {
            Vector2 raw = to - from;
            Vector2 dir = raw.sqrMagnitude > 1e-8f ? raw.normalized : Vector2.up;

            // 交错只影响绘制:把 sprite 两端各向外延伸,IK 关节几何(from/to)保持不变。
            Vector2 drawnFrom = from - dir * extendFrom;
            Vector2 drawnTo = to + dir * extendTo;
            Vector2 segment = drawnTo - drawnFrom;
            float angle = Mathf.Atan2(segment.y, segment.x) * Mathf.Rad2Deg + angleOffset;

            Vector3 midpoint = (Vector3)((drawnFrom + drawnTo) * 0.5f);
            midpoint.z = bone.position.z;
            bone.position = midpoint;
            bone.rotation = Quaternion.Euler(0f, 0f, angle);

            if (!stretchToFit || spriteHeight <= 1e-4f)
            {
                return;
            }

            // sprite 本地高度乘以父级缩放才是世界长度;反推出令世界长度等于骨段的 localScale.y。
            float parentScaleY = bone.parent != null ? bone.parent.lossyScale.y : 1f;
            if (parentScaleY <= 1e-4f)
            {
                return;
            }

            Vector3 scale = bone.localScale;
            scale.y = segment.magnitude / (spriteHeight * parentScaleY);
            bone.localScale = scale;
        }

        static float MeasureSpriteHeight(Transform? bone)
        {
            if (bone != null && bone.TryGetComponent(out SpriteRenderer renderer) && renderer.sprite != null)
            {
                return renderer.sprite.bounds.size.y;
            }

            return 1f;
        }

        void OnDrawGizmosSelected()
        {
            if (origin == null || target == null)
            {
                return;
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(origin.position, 0.06f);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(target.position, 0.06f);
        }
    }
}
