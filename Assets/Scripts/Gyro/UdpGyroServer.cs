#nullable enable

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CGJ2026.Gyro
{
    /// PC 端 UDP 接收端:监听某端口,收手机(或模拟器)发来的陀螺仪 JSON。
    ///
    /// 与 WebSocket 方案的取舍:UDP 无握手、无 TLS、无连接状态,延迟低、实现简单,
    /// 特别适合"高频、可丢包、只要最新一帧"的姿态流。代价是浏览器无法直接发 UDP,
    /// 所以发送端必须是原生 App 或桌面模拟器;编辑器内调试可直接用 Unity Remote
    /// 读本地传感器(见 GyroInput),根本不用发包。
    ///
    /// 收包在后台线程完成,原始 JSON 压入线程安全队列,由 GyroInput 在主线程出队解析。
    public class UdpGyroServer : MonoBehaviour
    {
        [Header("网络")]
        [Tooltip("UDP 监听端口。手机/模拟器把姿态包发到 PC 的这个端口。")]
        [SerializeField] int port = 9050;

        [Tooltip("队列积压上限;超过则丢弃最旧的包,避免主线程卡顿时无限堆积内存。")]
        [SerializeField] int maxQueue = 256;

        [Tooltip("仅用于日志提示,填 PC 热点/局域网 IP,方便照着在发送端填写。")]
        [SerializeField] string pcIpForLog = "172.20.10.2";

        readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();

        UdpClient? client;
        Thread? receiveThread;
        volatile bool isRunning;

        /// 供 GyroInput 在主线程取包。
        public ConcurrentQueue<string> Incoming => incoming;

        public bool IsRunning => isRunning;

        public int Port => port;

        void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (isRunning)
            {
                return;
            }

            try
            {
                client = new UdpClient(AddressFamily.InterNetwork);
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, port));

                isRunning = true;
                receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "UdpGyroServer"
                };
                receiveThread.Start();

                Debug.Log($"[UdpGyroServer] 已监听 UDP 0.0.0.0:{port}。\n" +
                          $"发送端把姿态 JSON 发到:{pcIpForLog}:{port}\n" +
                          $"编辑器内调试可直接用 Unity Remote(无需发包)。");
            }
            catch (Exception ex)
            {
                isRunning = false;
                Debug.LogError($"[UdpGyroServer] 启动失败:{ex.Message}");
            }
        }

        void ReceiveLoop()
        {
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (isRunning)
            {
                try
                {
                    byte[] data = client!.Receive(ref remote);
                    if (data.Length == 0)
                    {
                        continue;
                    }

                    incoming.Enqueue(Encoding.UTF8.GetString(data));

                    // 防止主线程卡顿时队列无限增长:只保留最新的一批。
                    while (incoming.Count > maxQueue && incoming.TryDequeue(out _))
                    {
                    }
                }
                catch (SocketException)
                {
                    if (!isRunning)
                    {
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (isRunning)
                    {
                        Debug.LogWarning($"[UdpGyroServer] 接收异常:{ex.Message}");
                    }
                }
            }
        }

        void OnDestroy()
        {
            Shutdown();
        }

        void OnApplicationQuit()
        {
            Shutdown();
        }

        void Shutdown()
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;

            try
            {
                client?.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UdpGyroServer] 关闭时异常:{ex.Message}");
            }
            finally
            {
                client?.Dispose();
                client = null;
            }

            // 后台线程 Receive 被打断后会自行退出;主线程不阻塞等待。
            receiveThread = null;
        }
    }
}
