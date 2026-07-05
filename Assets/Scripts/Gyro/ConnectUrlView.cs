#nullable enable

using TMPro;
using UnityEngine;

namespace CGJ2026.Gyro
{
    /// 在 menu 里显示手机应访问的连接网址(来自 GyroProxyLauncher.PublicUrl)。
    /// 优先写入指定的 TMP 文本;未指定时用 OnGUI 直接画在屏幕上(免场景布线)。
    public class ConnectUrlView : MonoBehaviour
    {
        [Tooltip("可选:直接引用启动器。留空时自动从持久单例 GyroNet.Instance 取 URL。")]
        [SerializeField] GyroProxyLauncher? launcher;

        [Tooltip("可选:把网址写进这个 TMP 文本。留空则用 OnGUI 兜底显示。")]
        [SerializeField] TMP_Text? urlLabel;

        [Tooltip("是否显示提示语(用 Safari 打开、需信任证书等)。")]
        [SerializeField] bool showHint = true;

        [Tooltip("OnGUI 兜底时的字号。")]
        [SerializeField] int fontSize = 26;

        GUIStyle? style;
        string cachedUrl = string.Empty;

        // URL 优先取直接引用的启动器,留空时回落到持久单例 GyroNet.Instance。
        string CurrentUrl
        {
            get
            {
                if (launcher != null)
                {
                    return launcher.PublicUrl;
                }

                return GyroNet.Instance != null ? GyroNet.Instance.PublicUrl : string.Empty;
            }
        }

        void Update()
        {
            if (urlLabel == null)
            {
                return;
            }

            string url = CurrentUrl;
            if (url != cachedUrl)
            {
                cachedUrl = url;
                urlLabel.text = BuildText();
            }
        }

        void OnGUI()
        {
            if (urlLabel != null)
            {
                return;
            }

            style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            GUI.Label(new Rect(24, 24, Screen.width - 48, 260), BuildText(), style);
        }

        string BuildText()
        {
            string url = CurrentUrl;
            if (string.IsNullOrEmpty(url))
            {
                return "正在获取本机 IP…";
            }

            string text = $"手机浏览器打开:\n{url}";
            if (showHint)
            {
                text += "\n\n· 用 Safari 打开,首次需信任证书\n· 手机与本机连同一热点/局域网";
            }

            return text;
        }
    }
}
