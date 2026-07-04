#nullable enable

using System;
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
    ///  - 手机没连上(LatestTilt 为 null)时默认朝下,巨石表现为正常自由落体;
    ///  - 手机在 X/Y 平面被快速甩动时,沿“甩动起始方向”给巨石一次冲刺冲量。
    ///
    /// 冲刺检测为什么要逐包(订阅 TiltReceived)而非在 FixedUpdate 读 LatestTilt:
    /// 一次甩动含“启动峰”和反向的“急停峰”,FixedUpdate 采样会漏掉瞬时的启动峰、
    /// 常只抓到该帧最后一个包(急停峰),导致方向反了。逐包 + 只在加速度模“上穿阈值”
    /// 的那一刻触发,抓到的才是启动方向 = 玩家真正的移动方向;急停峰落在冷却里被忽略。
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

        [Tooltip("倾角死区(度):Beta/Gamma 各自独立判断,某轴倾角小于此值时该轴重力分量归零。两轴都在死区 = 无重力。")]
        [SerializeField] float deadZoneDegrees = 10f;

        [Header("阻尼(让速度逐渐衰减)")]
        [Tooltip("无重力(两轴都在死区)时的线性阻尼;越大停得越快,让漂移的石头逐渐减速停下。")]
        [SerializeField] float zeroGravityDamping = 1.5f;

        [Tooltip("有重力时的线性阻尼;默认 0,保持自由下落手感。想整体更“黏”可调大。")]
        [SerializeField] float activeDamping;

        [Header("倾角映射微调(按真机持机方向翻转符号)")]
        [Tooltip("翻转左右倾(Gamma → X)。")]
        [SerializeField] bool invertGamma;

        [Tooltip("翻转前后倾(Beta → Y)。")]
        [SerializeField] bool invertBeta;

        [Header("冲刺(手机快速甩动触发)")]
        [Tooltip("总开关:关掉后手机甩动不再触发巨石冲刺。")]
        [SerializeField] bool enableDash = true;

        [Tooltip("触发冲刺的线性加速度阈值(m/s²);越大越难触发。用去重力线性加速度的模判断。")]
        [SerializeField] float dashAccelThreshold = 18f;

        [Tooltip("冲刺速度(m/s):触发时沿甩动方向瞬间给巨石叠加的速度,已按质量归一化。")]
        [SerializeField] float dashSpeed = 8f;

        [Tooltip("冲刺冷却(秒):一次甩动会持续几十毫秒的高加速度,冷却避免连发。")]
        [SerializeField] float dashCooldown = 0.4f;

        [Tooltip("翻转冲刺左右方向(手机 X)。")]
        [SerializeField] bool invertDashX;

        [Tooltip("翻转冲刺上下方向(手机 Y)。")]
        [SerializeField] bool invertDashY;

        Rigidbody2D body = null!;
        Vector2 smoothedDir;
        Vector2 dirVelocity;

        float prevFlickMagnitude;
        float lastDashTime = -999f;
        Vector2 pendingDashDir;
        bool hasPendingDash;
        bool subscribed;

        /// 当前施加的重力方向(已平滑,单位向量),供 HUD / 雷达显示。
        public Vector2 GravityDirection => smoothedDir;

        /// 触发冲刺时回调(参数为冲刺方向,单位向量),供震动/特效反馈用。
        public event Action<Vector2>? Dashed;

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
            Subscribe();
        }

        void OnEnable()
        {
            Subscribe();
        }

        void OnDisable()
        {
            if (subscribed && receiver != null)
            {
                receiver.TiltReceived -= OnTiltReceived;
            }

            subscribed = false;
        }

        void Subscribe()
        {
            if (subscribed || receiver == null)
            {
                return;
            }

            receiver.TiltReceived += OnTiltReceived;
            subscribed = true;
        }

        void FixedUpdate()
        {
            Vector2 target = TargetDirection();
            smoothedDir = smoothTime > 0f
                ? Vector2.SmoothDamp(smoothedDir, target, ref dirVelocity, smoothTime)
                : target;

            // 目标方向为零 = 处于无重力状态:加大阻尼让漂移的石头逐渐减速,而非匀速直线飞行。
            bool zeroGravity = target.sqrMagnitude < 1e-4f;
            body.linearDamping = zeroGravity ? zeroGravityDamping : activeDamping;

            body.AddForce(smoothedDir * (gravityStrength * body.mass), ForceMode2D.Force);

            if (hasPendingDash)
            {
                hasPendingDash = false;
                // 按质量归一化的冲量:velocityChange = impulse / mass = dashSpeed,冲刺速度与质量无关。
                body.AddForce(pendingDashDir * (dashSpeed * body.mass), ForceMode2D.Impulse);
                Dashed?.Invoke(pendingDashDir);
            }
        }

        /// 逐包冲刺检测(主线程,由 GyroReceiver.TiltReceived 触发)。
        /// 只在去重力线性加速度的模“从阈值下方上穿到上方”的那一刻触发,取该瞬间的方向 =
        /// 甩动起始方向;带冷却屏蔽随后的急停反向峰,避免一次甩动连发或方向反了。
        void OnTiltReceived(TiltMessage tilt)
        {
            if (!enableDash)
            {
                prevFlickMagnitude = 0f;
                return;
            }

            Vector2 flick = new Vector2(
                invertDashX ? -tilt.LinAccelX : tilt.LinAccelX,
                invertDashY ? -tilt.LinAccelY : tilt.LinAccelY);
            float magnitude = flick.magnitude;

            bool risingEdge = prevFlickMagnitude < dashAccelThreshold && magnitude >= dashAccelThreshold;
            prevFlickMagnitude = magnitude;

            if (!risingEdge || Time.time - lastDashTime < dashCooldown)
            {
                return;
            }

            pendingDashDir = flick / magnitude;
            hasPendingDash = true;
            lastDashTime = Time.time;
        }

        Vector2 TargetDirection()
        {
            if (receiver == null || receiver.LatestTilt == null)
            {
                return Vector2.zero;
            }

            TiltMessage tilt = receiver.LatestTilt;
            float gamma = invertGamma ? -tilt.Gamma : tilt.Gamma;
            float beta = invertBeta ? -tilt.Beta : tilt.Beta;

            float betaRad = beta * Mathf.Deg2Rad;

            // 用重力在屏幕平面上的投影(而非各轴独立取 sin),避免手机翻转临界点上
            // gamma 从 ±89 瞬间跳变导致方向突然翻转:
            //   左右分量 x = sin(gamma)·cos(beta)  —— cos(beta) 与 gamma 的符号跳变同步,结果连续;
            //   上下分量 y = sin(beta)。
            // 只有把手机彻底翻个面(beta 越过 ±90)方向才会自然反向。
            float x = Mathf.Abs(gamma) < deadZoneDegrees
                ? 0f
                : Mathf.Sin(gamma * Mathf.Deg2Rad) * Mathf.Cos(betaRad);
            float y = Mathf.Abs(beta) < deadZoneDegrees ? 0f : Mathf.Sin(betaRad);
            Vector2 dir = new Vector2(x, y);
            return dir.sqrMagnitude < 1e-4f ? Vector2.zero : dir.normalized;
        }
    }
}
