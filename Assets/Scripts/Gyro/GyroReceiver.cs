#nullable enable

using System;
using UnityEngine;

namespace CGJ2026.Gyro
{
    /// 主线程消费者:每帧把 GyroServer 收到的原始 JSON 出队解析成 TiltMessage。
    /// 阶段 1 只做 Debug.Log 验证链路;阶段 2 由 BoulderGravityController 读取 LatestTilt。
    public class GyroReceiver : MonoBehaviour
    {
        [SerializeField] GyroServer server = null!;

        [Tooltip("阶段 1 开启后在 Console 打印收到的倾角,验通后可关掉减少刷屏。")]
        [SerializeField] bool logToConsole = true;

        [Tooltip("限制打印频率(秒),避免 50Hz 刷爆 Console。")]
        [SerializeField] float logInterval = 0.2f;

        float lastLogTime;

        /// 最近一帧倾角;无数据时为 null。
        public TiltMessage? LatestTilt { get; private set; }

        /// 收到新倾角时触发(主线程)。
        public event Action<TiltMessage>? TiltReceived;

        /// 由倾角推出的 2D 重力方向(单位向量),供 BoulderGravityController 直接用。
        /// Gamma→X(左右),Beta→Y(前后);无数据/近水平时默认朝下。
        /// 语义与 GyroInput.GravityDirection 保持一致。
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
            if (server == null)
            {
                return;
            }

            while (server.Incoming.TryDequeue(out string? raw))
            {
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                TiltMessage? tilt = TryParse(raw);
                if (tilt == null)
                {
                    continue;
                }

                LatestTilt = tilt;
                TiltReceived?.Invoke(tilt);

                if (logToConsole && Time.unscaledTime - lastLogTime >= logInterval)
                {
                    lastLogTime = Time.unscaledTime;
                    Debug.Log($"[GyroReceiver] 倾角 β={tilt.Beta:F1} γ={tilt.Gamma:F1} α={tilt.Alpha:F1} · " +
                              $"加速度(含重力)=({tilt.AccelX:F1},{tilt.AccelY:F1},{tilt.AccelZ:F1}) " +
                              $"去重力=({tilt.LinAccelX:F1},{tilt.LinAccelY:F1},{tilt.LinAccelZ:F1})");
                }
            }
        }

        static TiltMessage? TryParse(string raw)
        {
            try
            {
                return JsonUtility.FromJson<TiltMessage>(raw);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GyroReceiver] 解析失败:{ex.Message} · 原始:{raw}");
                return null;
            }
        }
    }
}
