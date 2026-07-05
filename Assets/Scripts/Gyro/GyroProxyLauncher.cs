#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CGJ2026.Gyro
{
    /// 进 menu 时自动拉起 StreamingAssets 里的 tls-proxy.py:
    ///  ① 探测本机局域网 IP,拼出手机要访问的 https URL;
    ///  ② 把 IP 与可写证书目录通过环境变量传给代理,代理据此自动生成/复用证书;
    ///  ③ 用系统 python 启动代理进程,退出时一并结束它。
    ///
    /// 打包安全:脚本随 StreamingAssets 带出,证书写到 persistentDataPath(可写);
    /// python/openssl 依赖演示机环境(方案 A 前提)。找不到 python 时优雅降级,
    /// 只在日志里给出手动启动命令,不影响游戏本体运行。
    public class GyroProxyLauncher : MonoBehaviour
    {
        [Header("端口")]
        [Tooltip("对外 TLS 端口(手机访问的端口)。")]
        [SerializeField] int tlsPort = 8443;

        [Tooltip("后端明文端口,需与 GyroServer 的 port 一致。")]
        [SerializeField] int backendPort = 8080;

        [Header("行为")]
        [Tooltip("进 menu 是否自动启动代理。关掉则只计算 URL,由你手动跑脚本。")]
        [SerializeField] bool autoLaunch = true;

        [Tooltip("把代理的 stdout/stderr 转发到 Unity Console,便于观察握手/证书日志。")]
        [SerializeField] bool forwardProcessLog = true;

        Process? proxyProcess;

        /// 手机应访问的完整 URL(拼好当前 IP)。
        public string PublicUrl { get; private set; } = string.Empty;

        /// 探测到的本机局域网 IP。
        public string LanIp { get; private set; } = "127.0.0.1";

        /// 手动启动命令提示(自动启动失败时给用户照抄)。
        public string ManualCommandHint { get; private set; } = string.Empty;

        /// 代理是否已成功拉起。
        public bool ProxyLaunched => proxyProcess is { HasExited: false };

        void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            LanIp = LanAddress.GetLanIPv4();
            PublicUrl = $"https://{LanIp}:{tlsPort}/gyro.html";

            string scriptPath = Path.Combine(Application.streamingAssetsPath, "gyro", "tls-proxy.py");
            ManualCommandHint = $"python3 \"{scriptPath}\" {tlsPort} {backendPort}";

            Debug.Log($"[GyroProxyLauncher] 手机访问:{PublicUrl}\n手动启动命令:{ManualCommandHint}");

            if (!autoLaunch)
            {
                return;
            }

            if (!File.Exists(scriptPath))
            {
                Debug.LogWarning($"[GyroProxyLauncher] 找不到代理脚本:{scriptPath}(StreamingAssets 是否已带出?)");
                return;
            }

            TryLaunch(scriptPath);
        }

        void TryLaunch(string scriptPath)
        {
            string certDir = Path.Combine(Application.persistentDataPath, "certs");
            try
            {
                Directory.CreateDirectory(certDir);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GyroProxyLauncher] 证书目录创建失败:{ex.Message}");
            }

            foreach ((string exe, string prefixArgs) in PythonCandidates())
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = $"{prefixArgs}\"{scriptPath}\" {tlsPort} {backendPort}",
                        WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? Application.streamingAssetsPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = forwardProcessLog,
                        RedirectStandardError = forwardProcessLog
                    };
                    if (forwardProcessLog)
                    {
                        // 代理日志含中文,强制按 UTF-8 解码,否则 Console 里是 ???。
                        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
                        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
                    }
                    psi.EnvironmentVariables["GYRO_IP"] = LanIp;
                    psi.EnvironmentVariables["GYRO_CERT_DIR"] = certDir;
                    psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                    Process process = new Process { StartInfo = psi, EnableRaisingEvents = true };

                    if (forwardProcessLog)
                    {
                        process.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log($"[tls-proxy] {e.Data}"); };
                        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.LogWarning($"[tls-proxy] {e.Data}"); };
                    }

                    if (!process.Start())
                    {
                        process.Dispose();
                        continue;
                    }

                    if (forwardProcessLog)
                    {
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                    }

                    proxyProcess = process;
                    Debug.Log($"[GyroProxyLauncher] 已用 '{exe}' 启动 TLS 代理(IP={LanIp},端口 {tlsPort}->{backendPort})。");
                    return;
                }
                catch (Exception)
                {
                    // 该 python 候选不可用,试下一个。
                }
            }

            Debug.LogWarning("[GyroProxyLauncher] 未能自动启动代理(未找到可用 python)。\n" +
                             $"请在演示机上手动运行:\n{ManualCommandHint}");
        }

        // 不同平台/安装方式下 python 的常见位置;逐个尝试直到能启动。
        static IEnumerable<(string exe, string prefixArgs)> PythonCandidates()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            yield return ("py", "-3 ");
            yield return ("python", string.Empty);
            yield return ("python3", string.Empty);
#else
            yield return ("/opt/homebrew/bin/python3", string.Empty);
            yield return ("/usr/local/bin/python3", string.Empty);
            yield return ("/usr/bin/python3", string.Empty);
            yield return ("python3", string.Empty);
#endif
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
            if (proxyProcess == null)
            {
                return;
            }

            try
            {
                if (!proxyProcess.HasExited)
                {
                    proxyProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GyroProxyLauncher] 结束代理进程时异常:{ex.Message}");
            }
            finally
            {
                proxyProcess.Dispose();
                proxyProcess = null;
            }
        }
    }
}
