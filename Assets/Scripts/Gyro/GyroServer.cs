#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace CGJ2026.Gyro
{
    /// PC 端权威通信服务端(明文,仅 WebSocket)。只提供 /ws 服务收手机陀螺仪倾角。
    ///
    /// 为什么只跑 WebSocketServer、不跑 HttpServer:
    ///  - Unity 编辑器内的 SslStream 走 UnityTLS,无法与 iOS Safari 握手,故 TLS 交给
    ///    tools/tls-proxy.py 用系统 OpenSSL 终止;
    ///  - 本机这套 websocket-sharp 的 HttpServer 请求分发有问题(GET 与 /ws 一律回 404,
    ///    既不触发 OnGet 也不进 WebSocket 握手),故不再用 HttpServer;
    ///  - gyro.html 静态页面改由 tls-proxy.py 直接发,Unity 端只保留最成熟稳定的
    ///    WebSocketServer 处理 /ws。
    ///
    /// 只监听回环地址,外部流量必须经代理进来。收到的原始 JSON 帧压入线程安全队列,
    /// 由 GyroReceiver 在主线程出队解析。
    public class GyroServer : MonoBehaviour
    {
        [Header("网络(明文 WS,仅回环;对外由 tls-proxy.py 终止 TLS)")]
        [Tooltip("明文 WS 端口。tls-proxy.py 默认把 :8443 的 /ws 透传到这里。")]
        [SerializeField] int port = 8080;

        [Tooltip("仅用于日志提示,填 PC 热点/局域网 IP,方便照着在手机上输入。")]
        [SerializeField] string pcIpForLog = "172.20.10.2";

        [Tooltip("对外 TLS 端口:tls-proxy.py 监听它并终止 TLS,再透传到上面的明文端口。")]
        [SerializeField] int tlsPort = 8443;

        [Header("自动代理(场景初始化时拉起 tls-proxy.py,停止播放时自动结束)")]
        [Tooltip("勾选后场景启动会自动运行 python3 tools/tls-proxy.py;取消则需手动运行。")]
        [SerializeField] bool autoStartTlsProxy = true;

        [Tooltip("python 可执行文件。若自动启动报“找不到 python3”,填绝对路径(终端里 which python3 查看)。")]
        [SerializeField] string pythonExecutable = "python3";

        const string webSocketPath = "/ws";

        readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();

        WebSocketServer webSocketServer = null!;
        Process? tlsProxyProcess;
        bool isRunning;

        /// 供 GyroReceiver 在主线程取包。
        public ConcurrentQueue<string> Incoming => incoming;

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
                // 只绑回环:外部经 tls-proxy.py 进来。明文 WS,绕开 UnityTLS。
                webSocketServer = new WebSocketServer(IPAddress.Loopback, port);

                webSocketServer.AddWebSocketService<TiltSocketBehavior>(webSocketPath, behavior => behavior.Bind(incoming));

                webSocketServer.Start();
                isRunning = webSocketServer.IsListening;

                if (autoStartTlsProxy)
                {
                    StartTlsProxy();
                }

                Debug.Log($"[GyroServer] WebSocketServer 已启动(明文 127.0.0.1:{port}{webSocketPath})。\n" +
                          (autoStartTlsProxy ? "已自动拉起 tls-proxy.py。\n" : "请手动运行:python3 tools/tls-proxy.py\n") +
                          $"iPhone 打开:https://{pcIpForLog}:{tlsPort}/gyro.html");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GyroServer] 启动失败:{ex.Message}");
            }
        }

        /// 用登录 shell 以 exec 方式启动代理:exec 让 shell 进程被 python 取代(同一 PID),
        /// 这样后面 Kill 能真正结束 python、不会留下占用端口的孤儿进程。
        void StartTlsProxy()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string scriptPath = Path.Combine(projectRoot, "tools", "tls-proxy.py");
                if (!File.Exists(scriptPath))
                {
                    Debug.LogWarning($"[GyroServer] 未找到代理脚本,跳过自动启动:{scriptPath}");
                    return;
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                bool isWindows = Application.platform == RuntimePlatform.WindowsEditor ||
                                 Application.platform == RuntimePlatform.WindowsPlayer;
                if (isWindows)
                {
                    psi.FileName = pythonExecutable;
                    psi.ArgumentList.Add(scriptPath);
                    psi.ArgumentList.Add(tlsPort.ToString());
                    psi.ArgumentList.Add(port.ToString());
                }
                else
                {
                    string command = $"exec {ShellQuote(pythonExecutable)} {ShellQuote(scriptPath)} {tlsPort} {port}";
                    psi.FileName = "/bin/zsh";
                    psi.ArgumentList.Add("-lc");
                    psi.ArgumentList.Add(command);
                }

                Process proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.Log($"[tls-proxy] {e.Data}");
                    }
                };
                proc.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Debug.LogWarning($"[tls-proxy] {e.Data}");
                    }
                };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                tlsProxyProcess = proc;
                Debug.Log($"[GyroServer] 已自动启动 tls-proxy.py(TLS :{tlsPort} -> 127.0.0.1:{port})。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GyroServer] 自动启动代理失败:{ex.Message}\n" +
                               "可在 Inspector 关掉 autoStartTlsProxy 改为手动运行,或把 pythonExecutable 填成绝对路径。");
            }
        }

        void StopTlsProxy()
        {
            if (tlsProxyProcess == null)
            {
                return;
            }

            try
            {
                if (!tlsProxyProcess.HasExited)
                {
                    tlsProxyProcess.Kill();
                    tlsProxyProcess.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GyroServer] 关闭代理异常:{ex.Message}");
            }
            finally
            {
                tlsProxyProcess.Dispose();
                tlsProxyProcess = null;
            }
        }

        static string ShellQuote(string s)
        {
            return "'" + s.Replace("'", "'\\''") + "'";
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
            StopTlsProxy();

            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            try
            {
                webSocketServer.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GyroServer] 关闭时异常:{ex.Message}");
            }
        }

        /// websocket-sharp 在后台线程实例化并回调本类;此处只做最轻量的入队,解析放主线程。
        sealed class TiltSocketBehavior : WebSocketBehavior
        {
            ConcurrentQueue<string> queue = null!;

            public void Bind(ConcurrentQueue<string> sharedQueue)
            {
                queue = sharedQueue;
            }

            protected override void OnMessage(MessageEventArgs e)
            {
                if (e.IsText)
                {
                    queue.Enqueue(e.Data);
                }
            }
        }
    }
}
