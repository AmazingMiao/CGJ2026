using UnityEngine;

namespace CGJ2026.View
{
    /// <summary>
    /// 当目标(巨石)离开相机视野时,把本物体的 Sprite 贴到屏幕边缘,并旋转指向目标方位;
    /// 目标回到视野内时自动隐藏。挂在带 <see cref="SpriteRenderer"/> 的独立物体上使用。
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class OffscreenTargetIndicator : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("要指示的目标(巨石)。为空时按类型自动查找一次 BoulderGravityController。")]
        [SerializeField] private Transform target;
        [Tooltip("取景相机。为空时初始化阶段回退到 Camera.main(仅取一次,避免频繁调用)。")]
        [SerializeField] private Camera viewCamera;
        [SerializeField] private SpriteRenderer spriteRenderer = null!;

        [Header("Layout")]
        [Tooltip("视口边缘留白比例(0.5 为屏幕中心)。越大越靠近画面中央。")]
        [Range(0f, 0.45f)]
        [SerializeField] private float edgePadding = 0.06f;
        [Tooltip("Sprite 相对相机的前向距离(世界单位)。2D 正交相机通常等于相机到 z=0 平面的距离。")]
        [SerializeField] private float cameraDistance = 10f;

        [Header("Rotation")]
        [Tooltip("是否让 Sprite 旋转指向目标方位。")]
        [SerializeField] private bool rotateTowardTarget = true;
        [Tooltip("Sprite 素材默认朝向的角度补偿(度)。素材默认朝右 +X 时填 0,朝上填 -90。")]
        [SerializeField] private float rotationOffsetDegrees = 0f;

        private bool referencesResolved;

        /// <summary>
        /// 显式注入目标与相机,便于由上层管理器在场景加载后调用。
        /// </summary>
        public void Initialize(Transform indicatorTarget, Camera indicatorCamera)
        {
            target = indicatorTarget;
            viewCamera = indicatorCamera;
            referencesResolved = false;
            EnsureReferences();
        }

        public void SetTarget(Transform indicatorTarget)
        {
            target = indicatorTarget;
        }

        private void Reset()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            EnsureReferences();

            if (target == null || viewCamera == null || spriteRenderer == null)
            {
                SetVisible(false);
                return;
            }

            Vector3 viewportPoint = viewCamera.WorldToViewportPoint(target.position);
            bool isBehind = viewportPoint.z < 0f;
            bool isOnScreen = !isBehind
                && viewportPoint.x >= 0f && viewportPoint.x <= 1f
                && viewportPoint.y >= 0f && viewportPoint.y <= 1f;

            if (isOnScreen)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            PlaceOnScreenEdge(viewportPoint, isBehind);
        }

        private void PlaceOnScreenEdge(Vector3 viewportPoint, bool isBehind)
        {
            Vector2 fromCenter = new Vector2(viewportPoint.x - 0.5f, viewportPoint.y - 0.5f);

            // 目标在相机背后时,投影方向会翻转,这里翻回真实方位。
            if (isBehind)
            {
                fromCenter = -fromCenter;
            }

            if (fromCenter.sqrMagnitude < 0.000001f)
            {
                fromCenter = Vector2.up;
            }

            // 把方向缩放到留白后的方框边缘:令 |x|、|y| 中较大者恰好落在边界上。
            float half = 0.5f - edgePadding;
            float maxComponent = Mathf.Max(Mathf.Abs(fromCenter.x), Mathf.Abs(fromCenter.y));
            Vector2 clamped = fromCenter * (half / maxComponent);

            Vector3 edgeViewport = new Vector3(0.5f + clamped.x, 0.5f + clamped.y, cameraDistance);
            transform.position = viewCamera.ViewportToWorldPoint(edgeViewport);

            if (rotateTowardTarget)
            {
                float angle = Mathf.Atan2(fromCenter.y, fromCenter.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle + rotationOffsetDegrees);
            }
        }

        private void SetVisible(bool visible)
        {
            if (spriteRenderer != null && spriteRenderer.enabled != visible)
            {
                spriteRenderer.enabled = visible;
            }
        }

        private void EnsureReferences()
        {
            if (referencesResolved)
            {
                return;
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            if (target == null)
            {
                Boulder.BoulderGravityController boulder = FindFirstObjectByType<Boulder.BoulderGravityController>();
                if (boulder != null)
                {
                    target = boulder.transform;
                }
            }

            referencesResolved = viewCamera != null && target != null;
        }

        private void OnValidate()
        {
            edgePadding = Mathf.Clamp(edgePadding, 0f, 0.45f);
        }
    }
}
