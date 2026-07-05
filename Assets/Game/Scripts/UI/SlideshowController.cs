using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CGJ2026.UI
{
    /// <summary>
    /// 开场剧情放映:每页一张图配若干句台词。每次点击 / 空格 / 回车推进一句;
    /// 当前页台词说完后,再点一次才换到下一张图并显示它的第一句;全部放完后加载游戏场景。
    /// </summary>
    public sealed class SlideshowController : MonoBehaviour
    {
        [Serializable]
        public sealed class Page
        {
            [Tooltip("这一页显示的图片。")]
            public Sprite Image;

            [Tooltip("这一页的台词:一行写一句,空行忽略。点击逐句显示,全说完再点才换图。")]
            [TextArea(2, 8)]
            public string Script;
        }

        [Header("References")]
        [Tooltip("铺满屏幕、用来显示每页图片的 UI Image。")]
        [SerializeField] private Image pageImage;
        [Tooltip("显示当前这句台词的 TMP 文本。")]
        [SerializeField] private TMP_Text sentenceText;

        [Header("Content")]
        [Tooltip("按顺序配置每一页:图片 + 若干句台词。")]
        [SerializeField] private Page[] pages;

        [Header("Flow")]
        [Tooltip("全部放完后加载的场景名,需在 Build Settings 中。")]
        [SerializeField] private string nextSceneName = "Map";
        [Tooltip("防止一次点击连翻多句的最小间隔(秒)。")]
        [SerializeField] private float minAdvanceInterval = 0.15f;
        [Tooltip("放完最后一页后,停顿多少秒再加载下一场景(结局用 3,开场用 0)。")]
        [SerializeField] private float endHoldSeconds;

        private int pageIndex;
        private int sentenceIndex;
        private float lastAdvanceTime;
        private bool isFinishing;

        private void Start()
        {
            if (pages == null || pages.Length == 0)
            {
                Debug.LogWarning("SlideshowController: 未配置任何页面,直接进入下一场景。");
                LoadNextScene();
                return;
            }

            pageIndex = 0;
            sentenceIndex = 0;
            Refresh();
        }

        private void Update()
        {
            if (WasAdvancePressed() && Time.unscaledTime - lastAdvanceTime >= minAdvanceInterval)
            {
                Advance();
            }
        }

        // 供全屏按钮的 OnClick 直接调用的备用入口。
        public void Advance()
        {
            if (isFinishing)
            {
                return;
            }

            lastAdvanceTime = Time.unscaledTime;

            int sentenceCount = GetSentenceCount(pageIndex);
            if (sentenceIndex + 1 < sentenceCount)
            {
                sentenceIndex++;
                RefreshSentence();
                return;
            }

            pageIndex++;
            sentenceIndex = 0;

            if (pageIndex >= pages.Length)
            {
                LoadNextScene();
                return;
            }

            Refresh();
        }

        private void Refresh()
        {
            if (pageImage != null)
            {
                Sprite sprite = pages[pageIndex].Image;
                pageImage.sprite = sprite;
                pageImage.enabled = sprite != null;
            }

            RefreshSentence();
        }

        private void RefreshSentence()
        {
            if (sentenceText == null)
            {
                return;
            }

            string[] sentences = GetSentences(pageIndex);
            sentenceText.text = sentenceIndex < sentences.Length
                ? sentences[sentenceIndex]
                : string.Empty;
        }

        private int GetSentenceCount(int index)
        {
            return GetSentences(index).Length;
        }

        private static readonly char[] lineSeparators = { '\n', '\r' };

        private static string[] GetSentences(Page page)
        {
            if (page == null || string.IsNullOrWhiteSpace(page.Script))
            {
                return Array.Empty<string>();
            }

            return page.Script.Split(lineSeparators, StringSplitOptions.RemoveEmptyEntries);
        }

        private string[] GetSentences(int index)
        {
            return GetSentences(pages[index]);
        }

        private static bool WasAdvancePressed()
        {
            bool mouseClicked = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool keyPressed = Keyboard.current != null
                && (Keyboard.current.spaceKey.wasPressedThisFrame
                    || Keyboard.current.enterKey.wasPressedThisFrame);
            return mouseClicked || keyPressed;
        }

        private void LoadNextScene()
        {
            if (isFinishing)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(nextSceneName) || !Application.CanStreamedLevelBeLoaded(nextSceneName))
            {
                Debug.LogError($"SlideshowController: 场景 \"{nextSceneName}\" 不在 Build Settings 里,无法加载。");
                return;
            }

            isFinishing = true;
            if (endHoldSeconds > 0f)
            {
                StartCoroutine(LoadNextSceneDelayed());
            }
            else
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(nextSceneName);
            }
        }

        private IEnumerator LoadNextSceneDelayed()
        {
            yield return new WaitForSecondsRealtime(endHoldSeconds);
            Time.timeScale = 1f;
            SceneManager.LoadScene(nextSceneName);
        }
    }
}
