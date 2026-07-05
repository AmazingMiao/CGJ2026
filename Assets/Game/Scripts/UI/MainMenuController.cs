using UnityEngine;
using UnityEngine.SceneManagement;

namespace CGJ2026.UI
{
    /// <summary>
    /// 主菜单逻辑:开始进入游戏场景、退出游戏。
    /// 把 StartGame / QuitGame 挂到对应按钮的 OnClick 上即可。
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Scene")]
        [Tooltip("开始游戏时加载的场景名。需与 Build Settings 中的场景名一致。")]
        [SerializeField] private string gameSceneName = "Map";

        public void StartGame()
        {
            if (string.IsNullOrWhiteSpace(gameSceneName))
            {
                Debug.LogError("MainMenuController: gameSceneName 未设置,无法开始游戏。");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
            {
                Debug.LogError($"MainMenuController: 场景 \"{gameSceneName}\" 不在 Build Settings 里,无法加载。");
                return;
            }

            Time.timeScale = 1f;
            SceneManager.LoadScene(gameSceneName);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
