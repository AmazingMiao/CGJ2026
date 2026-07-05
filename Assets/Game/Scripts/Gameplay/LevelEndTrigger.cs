using CGJ2026.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CGJ2026.Gameplay
{
    /// <summary>
    /// 终点触发器:玩家进入后加载结局放映场景。挂在带 Is Trigger 碰撞体的 EndPoint 上。
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class LevelEndTrigger : MonoBehaviour
    {
        [Tooltip("玩家到达终点后加载的场景名(结局 PPT),需在 Build Settings 中。")]
        [SerializeField] private string endingSceneName = "Ending";

        private bool triggered;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (triggered || other.attachedRigidbody == null)
            {
                return;
            }

            if (other.attachedRigidbody.GetComponent<PlayerController>() == null)
            {
                return;
            }

            LoadEnding();
        }

        private void LoadEnding()
        {
            if (string.IsNullOrWhiteSpace(endingSceneName) || !Application.CanStreamedLevelBeLoaded(endingSceneName))
            {
                Debug.LogError($"LevelEndTrigger: 场景 \"{endingSceneName}\" 不在 Build Settings 里,无法加载。");
                return;
            }

            triggered = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(endingSceneName);
        }
    }
}
