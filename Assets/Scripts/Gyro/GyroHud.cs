#nullable enable

using UnityEngine;

namespace CGJ2026.Gyro
{
    /// 轻量屏幕调试 HUD(IMGUI):显示当前来源、倾角、重力方向与 UDP 收包数。
    /// 仅用于开发期验证链路(Unity Remote 或 UDP),验通后可关掉或删除本组件。
    public class GyroHud : MonoBehaviour
    {
        [SerializeField] GyroInput? gyroInput;
        [SerializeField] UdpGyroServer? udpServer;

        [Tooltip("HUD 文字大小。")]
        [SerializeField] int fontSize = 22;

        GUIStyle? style;

        void OnGUI()
        {
            if (gyroInput == null)
            {
                return;
            }

            style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = Color.white }
            };

            TiltMessage? tilt = gyroInput.LatestTilt;
            Vector2 dir = gyroInput.GravityDirection;

            string udpLine = udpServer == null
                ? "UDP:未挂载"
                : $"UDP:{(udpServer.IsRunning ? "监听中" : "未启动")} :{udpServer.Port}  收包 {gyroInput.UdpPacketCount}";

            string localLine = $"本地传感器(Unity Remote/真机):{(gyroInput.LocalSensorAvailable ? "可用" : "无")}";

            string tiltLine = tilt == null
                ? "倾角:等待数据…"
                : $"Beta(前后) {tilt.Beta,6:F1}   Gamma(左右) {tilt.Gamma,6:F1}   Alpha {tilt.Alpha,6:F1}";

            string text =
                $"来源:{gyroInput.ActiveSource}\n" +
                $"{localLine}\n" +
                $"{udpLine}\n" +
                $"{tiltLine}\n" +
                $"重力方向:({dir.x:F2}, {dir.y:F2})";

            GUI.Label(new Rect(16, 16, 720, 220), text, style);
        }
    }
}
