#nullable enable

using UnityEngine;
using UnityEngine.InputSystem;

namespace CGJ2026.IK
{
    /// 让挂载对象(IK 末端目标)跟随鼠标世界坐标,用于快速验证 TwoBoneIK2D。
    /// 正式玩法里把 target 换成脚下落点 / 够石头方向即可,本脚本仅作调试演示。
    public class IKTargetMouseFollower : MonoBehaviour
    {
        [Tooltip("留空则在 Initialize() 时缓存 Camera.main。")]
        [SerializeField] Camera worldCamera = null!;

        [Tooltip("目标在世界空间的 Z 深度(2D 一般保持 0)。")]
        [SerializeField] float depth;

        void OnEnable() => Initialize();

        public void Initialize()
        {
            if (worldCamera == null)
            {
                worldCamera = Camera.main;
            }
        }

        void Update()
        {
            if (worldCamera == null || Mouse.current == null)
            {
                return;
            }

            Vector2 screen = Mouse.current.position.ReadValue();
            float distance = depth - worldCamera.transform.position.z;
            Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, distance));
            world.z = depth;
            transform.position = world;
        }
    }
}
