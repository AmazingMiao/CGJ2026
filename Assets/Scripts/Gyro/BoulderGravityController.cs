#nullable enable

using UnityEngine;

namespace CGJ2026.Gyro
{
    /// 巨石重力控制器:读取手机陀螺仪倾角(经 GyroReceiver 的 WebSocket 链路),
    /// 换算成 2D 重力方向后,以“加速度”的形式施加到巨石的 Rigidbody2D 上。
    ///
    /// 设计要点:
    ///  - 关掉 Rigidbody2D 内置的向下重力(gravityScale = 0),完全由本控制器接管方向;
    ///  - 施力用 dir * strength * mass,等效于一个大小恒定、方向可变的重力加速度,
    ///    这样不同质量的巨石都获得一致的“下落”手感;
    ///  - 对方向做 SmoothDamp 平滑,过滤手机抖动与网络抖包,避免巨石方向乱颤;
    ///  - 手机没连上(LatestTilt 为 null)时默认朝下,巨石表现为正常自由落体。
    ///
    /// 物理施力放 FixedUpdate,符合 2D 物理时序。
    [RequireComponent(typeof(Rigidbody2D))]
    public class BoulderGravityController : MonoBehaviour
    {
        [Header("数据来源")]
        [Tooltip("提供手机倾角的接收器(挂在 GyroNet 上的 GyroReceiver)。")]
        [SerializeField] GyroReceiver receiver = null!;

        [Header("重力")]
        [Tooltip("重力加速度大小(m/s²);地球约 9.8,可按手感放大。")]
        [SerializeField] float gravityStrength = 9.8f;

        [Tooltip("方向平滑时间(秒);越大越稳越迟钝,0 表示不平滑。")]
        [SerializeField] float smoothTime = 0.08f;

        [Header("倾角映射微调(按真机持机方向翻转符号)")]
        [Tooltip("翻转左右倾(Gamma → X)。")]
        [SerializeField] bool invertGamma;

        [Tooltip("翻转前后倾(Beta → Y)。")]
        [SerializeField] bool invertBeta;

        Rigidbody2D body = null!;
        Vector2 smoothedDir;
        Vector2 dirVelocity;

        /// 当前施加的重力方向(已平滑,单位向量),供 HUD / 雷达显示。
        public Vector2 GravityDirection => smoothedDir;

        void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            smoothedDir = Vector2.down;
            dirVelocity = Vector2.zero;
        }

        void FixedUpdate()
        {
            Vector2 target = TargetDirection();
            smoothedDir = smoothTime > 0f
                ? Vector2.SmoothDamp(smoothedDir, target, ref dirVelocity, smoothTime)
                : target;

            body.AddForce(smoothedDir * (gravityStrength * body.mass), ForceMode2D.Force);
        }

        Vector2 TargetDirection()
        {
            if (receiver == null || receiver.LatestTilt == null)
            {
                return Vector2.down;
            }

            TiltMessage tilt = receiver.LatestTilt;
            float gamma = invertGamma ? -tilt.Gamma : tilt.Gamma;
            float beta = invertBeta ? -tilt.Beta : tilt.Beta;

            float x = Mathf.Sin(gamma * Mathf.Deg2Rad);
            float y = Mathf.Sin(beta * Mathf.Deg2Rad);
            Vector2 dir = new Vector2(x, y);
            return dir.sqrMagnitude < 1e-4f ? Vector2.down : dir.normalized;
        }
    }
}
