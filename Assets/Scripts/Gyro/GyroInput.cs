#nullable enable

using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CGJ2026.Gyro
{
    /// 陀螺仪输入来源。
    public enum GyroSourceMode
    {
        /// 有本地传感器(Unity Remote / 真机)就用它,否则回落到 UDP。
        Auto,

        /// 强制只用本地传感器(Input System),便于用 Unity Remote 调试。
        LocalSensor,

        /// 强制只用 UDP 网络包。
        Udp
    }

    /// 陀螺仪输入路由器:把两种来源统一成 TiltMessage,供巨石重力控制器消费。
    ///  ① UDP:UdpGyroServer 收到的原始 JSON(手机原生 App / 桌面模拟器发来);
    ///  ② 本地传感器:Input System 的 AttitudeSensor / GravitySensor / Accelerometer,
    ///     在编辑器里连 Unity Remote 时即为手机的真实姿态,无需任何网络。
    ///
    /// Beta/Gamma 语义对齐浏览器 DeviceOrientation:Beta 前后倾、Gamma 左右倾(单位度)。
    /// 本地传感器换算出的角度坐标系与浏览器不完全一致,符号/量程用 Inspector 上的
    /// invert 开关按真机手感微调即可。
    public class GyroInput : MonoBehaviour
    {
        [Header("来源")]
        [SerializeField] GyroSourceMode mode = GyroSourceMode.Auto;

        [Tooltip("UDP 来源。用 UDP 或 Auto 模式时需要挂上;纯 Unity Remote 调试可留空。")]
        [SerializeField] UdpGyroServer? udpServer;

        [Header("本地传感器(Unity Remote / 真机)微调")]
        [Tooltip("翻转左右倾符号(不同持机方向可能相反)。")]
        [SerializeField] bool invertGamma;

        [Tooltip("翻转前后倾符号。")]
        [SerializeField] bool invertBeta;

        [Header("调试")]
        [Tooltip("在 Console 打印收到的倾角(限频),验通后可关掉。")]
        [SerializeField] bool logToConsole;

        [Tooltip("打印频率(秒),避免高频刷爆 Console。")]
        [SerializeField] float logInterval = 0.2f;

        float lastLogTime;
        bool localSensorsEnabled;
        long udpPacketCount;

        /// 最近一帧倾角;还没收到任何数据时为 null。
        public TiltMessage? LatestTilt { get; private set; }

        /// 当前实际生效的来源(用于 HUD 显示)。
        public GyroSourceMode ActiveSource { get; private set; }

        /// 累计收到的 UDP 包数(用于 HUD 显示)。
        public long UdpPacketCount => udpPacketCount;

        /// 本地传感器是否可用(Unity Remote 已连或运行在真机上)。
        public bool LocalSensorAvailable =>
            AttitudeSensor.current != null ||
            GravitySensor.current != null ||
            Accelerometer.current != null;

        /// 收到新倾角时触发(主线程)。
        public event Action<TiltMessage>? TiltReceived;

        /// 由倾角推出的 2D 重力方向(单位向量),供 BoulderGravityController 直接用。
        /// Gamma→X(左右),Beta→Y(前后);无数据时默认朝下。
        public Vector2 GravityDirection
        {
            get
            {
                TiltMessage? tilt = LatestTilt;
                if (tilt == null)
                {
                    return Vector2.down;
                }

                float x = Mathf.Sin(tilt.Gamma * Mathf.Deg2Rad);
                float y = Mathf.Sin(tilt.Beta * Mathf.Deg2Rad);
                Vector2 dir = new Vector2(x, y);
                return dir.sqrMagnitude < 1e-4f ? Vector2.down : dir.normalized;
            }
        }

        void Update()
        {
            EnsureLocalSensorsEnabled();

            bool preferLocal =
                mode == GyroSourceMode.LocalSensor ||
                (mode == GyroSourceMode.Auto && LocalSensorAvailable);

            if (preferLocal && TryReadLocalTilt(out TiltMessage localTilt))
            {
                Publish(localTilt, GyroSourceMode.LocalSensor);
                return;
            }

            // LocalSensor 模式下不回落到 UDP;Auto / Udp 模式才尝试 UDP。
            if (mode != GyroSourceMode.LocalSensor && TryReadUdpTilt(out TiltMessage udpTilt))
            {
                Publish(udpTilt, GyroSourceMode.Udp);
            }
        }

        void Publish(TiltMessage tilt, GyroSourceMode source)
        {
            LatestTilt = tilt;
            ActiveSource = source;
            TiltReceived?.Invoke(tilt);

            if (logToConsole && Time.unscaledTime - lastLogTime >= logInterval)
            {
                lastLogTime = Time.unscaledTime;
                Debug.Log($"[GyroInput] 来源={source}  Beta={tilt.Beta:F1}  Gamma={tilt.Gamma:F1}  Alpha={tilt.Alpha:F1}");
            }
        }

        bool TryReadUdpTilt(out TiltMessage tilt)
        {
            tilt = null!;
            if (udpServer == null)
            {
                return false;
            }

            // 姿态流只关心最新一帧:排空队列,保留最后一条有效包。
            string? latest = null;
            while (udpServer.Incoming.TryDequeue(out string? raw))
            {
                if (!string.IsNullOrEmpty(raw))
                {
                    latest = raw;
                    udpPacketCount++;
                }
            }

            if (latest == null)
            {
                return false;
            }

            TiltMessage? parsed = TryParse(latest);
            if (parsed == null)
            {
                return false;
            }

            tilt = parsed;
            return true;
        }

        bool TryReadLocalTilt(out TiltMessage tilt)
        {
            tilt = null!;

            AttitudeSensor? attitude = AttitudeSensor.current;
            if (attitude != null && attitude.enabled)
            {
                Vector3 euler = attitude.attitude.ReadValue().eulerAngles;
                tilt = MakeTilt(Normalize180(euler.x), Normalize180(euler.y), euler.z);
                return true;
            }

            if (TryReadGravity(out Vector3 gravity))
            {
                Vector3 n = gravity.normalized;
                float gamma = Mathf.Asin(Mathf.Clamp(n.x, -1f, 1f)) * Mathf.Rad2Deg;
                float beta = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
                tilt = MakeTilt(beta, gamma, 0f);
                return true;
            }

            return false;
        }

        static bool TryReadGravity(out Vector3 gravity)
        {
            GravitySensor? gravitySensor = GravitySensor.current;
            if (gravitySensor != null && gravitySensor.enabled)
            {
                gravity = gravitySensor.gravity.ReadValue();
                return true;
            }

            Accelerometer? accelerometer = Accelerometer.current;
            if (accelerometer != null && accelerometer.enabled)
            {
                gravity = accelerometer.acceleration.ReadValue();
                return true;
            }

            gravity = Vector3.zero;
            return false;
        }

        TiltMessage MakeTilt(float beta, float gamma, float alpha)
        {
            return new TiltMessage
            {
                Beta = invertBeta ? -beta : beta,
                Gamma = invertGamma ? -gamma : gamma,
                Alpha = alpha
            };
        }

        void EnsureLocalSensorsEnabled()
        {
            if (localSensorsEnabled)
            {
                return;
            }

            bool any = false;
            any |= TryEnable(AttitudeSensor.current);
            any |= TryEnable(GravitySensor.current);
            any |= TryEnable(Accelerometer.current);
            TryEnable(UnityEngine.InputSystem.Gyroscope.current);

            // Unity Remote 可能在运行后才连上,设备届时才出现;出现后只启用一次。
            localSensorsEnabled = any;
        }

        static bool TryEnable(InputDevice? device)
        {
            if (device == null)
            {
                return false;
            }

            if (!device.enabled)
            {
                InputSystem.EnableDevice(device);
            }

            return true;
        }

        static TiltMessage? TryParse(string raw)
        {
            try
            {
                return JsonUtility.FromJson<TiltMessage>(raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GyroInput] UDP 解析失败:{ex.Message} · 原始:{raw}");
                return null;
            }
        }

        static float Normalize180(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees < -180f)
            {
                degrees += 360f;
            }

            return degrees;
        }
    }
}
