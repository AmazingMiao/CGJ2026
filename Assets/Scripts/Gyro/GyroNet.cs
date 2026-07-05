#nullable enable

using System;
using UnityEngine;

namespace CGJ2026.Gyro
{
    /// 跨场景持久的陀螺仪网络单例:在 menu 创建后 DontDestroyOnLoad,穿过 intro → map → end
    /// 全程存活,连接不断。消费 GyroServer 收到的 WebSocket 帧,解析成 TiltMessage 供各场景读取。
    ///
    /// 巨石等消费者用 GyroNet.Instance 直接取数据,无需跨场景手动拖引用。取不到实例或断线时
    /// 消费者应走 Plan B(见 BoulderGravityController)。
    public class GyroNet : MonoBehaviour
    {
        public static GyroNet? Instance { get; private set; }

        [Header("依赖(同物体)")]
        [SerializeField] GyroServer server = null!;
        [SerializeField] GyroProxyLauncher? launcher;

        [Header("断线判定")]
        [Tooltip("多久没收到帧就视为断线(秒),供消费者切 Plan B。")]
        [SerializeField] float staleTimeout = 1.5f;

        float lastReceiveTime = -999f;

        /// 最近一帧倾角;还没收到时为 null。
        public TiltMessage? LatestTilt { get; private set; }

        /// 近期是否有数据(未超时)。
        public bool IsConnected => Time.unscaledTime - lastReceiveTime <= staleTimeout;

        /// 手机应访问的连接 URL(来自代理启动器)。
        public string PublicUrl => launcher != null ? launcher.PublicUrl : string.Empty;

        /// 本机局域网 IP。
        public string LanIp => launcher != null ? launcher.LanIp : "127.0.0.1";

        /// 收到新倾角时触发(主线程)。
        public event Action<TiltMessage>? TiltReceived;

        // 单例身份与跨场景持久必须在 Awake 完成(早于任何 Start / 其它组件读取 Instance),
        // 这是单例模式对"优先用 Initialize、避免 Awake"约定的合理例外。
        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
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
                lastReceiveTime = Time.unscaledTime;
                TiltReceived?.Invoke(tilt);
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
                Debug.LogWarning($"[GyroNet] 解析失败:{ex.Message} · 原始:{raw}");
                return null;
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
