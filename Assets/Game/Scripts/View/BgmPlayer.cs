#nullable enable
using UnityEngine;

namespace CGJ2026.View
{
    // 背景音乐播放器:
    // 1) 进入场景后延迟 startDelay 秒开始第一次播放;
    // 2) 可选按固定间隔重复播放(从每次播放开始计时,曲子未放完时不会叠放)。
    // 主菜单:startDelay=3、repeat=false;
    // Map 场景:startDelay=60、repeat=true、repeatInterval=60(刚进入不播,每分钟一次)。
    [RequireComponent(typeof(AudioSource))]
    public sealed class BgmPlayer : MonoBehaviour
    {
        [Header("音频")]
        [SerializeField, InspectorName("BGM 音频")] AudioClip? clip;
        [Tooltip("为空时从 Resources 按此路径加载(不含扩展名)。")]
        [SerializeField, InspectorName("资源路径")] string clipResourcePath = "Duster_-_Me_and_the_Birds_ext_(mp3.pm)";
        [Range(0f, 1f)]
        [SerializeField, InspectorName("音量")] float volume = 1f;
        [Tooltip("单次播放是否循环(通常配合 repeat=false 用作持续 BGM)。")]
        [SerializeField, InspectorName("单次循环")] bool loopClip;

        [Header("时序")]
        [Tooltip("进入场景后延迟多少秒开始第一次播放。")]
        [SerializeField, InspectorName("首次延迟(秒)")] float startDelay = 3f;
        [Tooltip("是否按固定间隔重复播放。")]
        [SerializeField, InspectorName("重复播放")] bool repeat;
        [Tooltip("重复播放的间隔秒数(从上一次开始播放算起,曲子放完后才会再次触发)。")]
        [SerializeField, InspectorName("重复间隔(秒)")] float repeatInterval = 60f;

        AudioSource source = null!;
        float timer;
        bool startedFirstPlay;

        void Start()
        {
            Initialize();
        }

        void Initialize()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loopClip;
            // 2D 混音,不随相机距离衰减,契合本作正交 2D 场景。
            source.spatialBlend = 0f;

            if (clip == null && !string.IsNullOrEmpty(clipResourcePath))
            {
                clip = Resources.Load<AudioClip>(clipResourcePath);
            }

            if (clip == null)
            {
                Debug.LogWarning($"[BgmPlayer] 未找到 BGM 音频(资源路径:\"{clipResourcePath}\"),不会播放。", this);
            }

            source.clip = clip;
            source.volume = volume;
            timer = 0f;
            startedFirstPlay = false;
        }

        void Update()
        {
            if (clip == null)
            {
                return;
            }

            // 用不受 timeScale 影响的时间,避免暂停(timeScale=0)时计时停摆。
            timer += Time.unscaledDeltaTime;

            if (!startedFirstPlay)
            {
                if (timer >= startDelay)
                {
                    PlayOnce();
                }

                return;
            }

            if (repeat && !source.isPlaying && timer >= repeatInterval)
            {
                PlayOnce();
            }
        }

        void PlayOnce()
        {
            source.volume = volume;
            source.Play();
            timer = 0f;
            startedFirstPlay = true;
        }

        void OnValidate()
        {
            volume = Mathf.Clamp01(volume);
            startDelay = Mathf.Max(0f, startDelay);
            repeatInterval = Mathf.Max(0.01f, repeatInterval);
        }
    }
}
