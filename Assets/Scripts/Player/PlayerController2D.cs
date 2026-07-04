#nullable enable

using UnityEngine;
using UnityEngine.InputSystem;

namespace CGJ2026.Player
{
    /// 简单的 2D WASD + 跳跃控制器(Input System)。A/D 或 ←/→ 横向移动,Space/W/↑ 跳。
    /// 用 Rigidbody2D 驱动,横向位移会带动髋部,IK 步态随之自动播放。
    /// 含土狼时间与跳跃缓冲,起跳手感更宽容。
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController2D : MonoBehaviour
    {
        [Header("移动")]
        [Tooltip("横向移动速度(单位/秒)。")]
        [SerializeField] float moveSpeed = 5f;

        [Header("跳跃")]
        [Tooltip("起跳瞬间赋予的垂直速度。")]
        [SerializeField] float jumpVelocity = 10f;

        [Tooltip("离开地面后仍可起跳的宽容时间(秒)。")]
        [SerializeField] float coyoteTime = 0.1f;

        [Tooltip("落地前提前按跳的缓冲时间(秒)。")]
        [SerializeField] float jumpBufferTime = 0.1f;

        [Tooltip("松开跳跃键时若仍在上升,按此比例削减上升速度,实现短跳。")]
        [SerializeField, Range(0f, 1f)] float shortJumpDamp = 0.5f;

        [Header("落地检测")]
        [Tooltip("脚底检测点;放在角色最低处略靠下。留空则用刚体位置(不推荐)。")]
        [SerializeField] Transform groundCheck = null!;

        [Tooltip("从检测点向下的探测距离。")]
        [SerializeField] float groundCheckDistance = 0.2f;

        [Tooltip("地面层。默认 Everything;建议单独建 Ground 层只勾它。")]
        [SerializeField] LayerMask groundMask = ~0;

        Rigidbody2D body = null!;
        float moveInput;
        float lastGroundedTime = -999f;
        float lastJumpPressedTime = -999f;
        bool jumpHeld;

        /// 当前是否踩在地面。
        public bool IsGrounded { get; private set; }

        /// 朝向:+1 右,-1 左;静止时保持上一次朝向。
        public int Facing { get; private set; } = 1;

        void OnEnable() => Initialize();

        public void Initialize()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }
        }

        void Update()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                moveInput = 0f;
                jumpHeld = false;
                return;
            }

            bool left = keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed;
            bool right = keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed;
            moveInput = (right ? 1f : 0f) - (left ? 1f : 0f);
            if (moveInput > 0f)
            {
                Facing = 1;
            }
            else if (moveInput < 0f)
            {
                Facing = -1;
            }

            jumpHeld =
                keyboard.spaceKey.isPressed || keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed;

            bool jumpPressed =
                keyboard.spaceKey.wasPressedThisFrame ||
                keyboard.wKey.wasPressedThisFrame ||
                keyboard.upArrowKey.wasPressedThisFrame;
            if (jumpPressed)
            {
                lastJumpPressedTime = Time.time;
            }
        }

        void FixedUpdate()
        {
            RefreshGrounded();

            Vector2 velocity = body.linearVelocity;
            velocity.x = moveInput * moveSpeed;

            bool canCoyote = Time.time - lastGroundedTime <= coyoteTime;
            bool bufferedJump = Time.time - lastJumpPressedTime <= jumpBufferTime;
            if (canCoyote && bufferedJump)
            {
                velocity.y = jumpVelocity;
                lastJumpPressedTime = -999f;
                lastGroundedTime = -999f;
            }
            else if (!jumpHeld && velocity.y > 0f)
            {
                velocity.y *= shortJumpDamp;
            }

            body.linearVelocity = velocity;
        }

        void RefreshGrounded()
        {
            Vector2 origin = groundCheck != null ? (Vector2)groundCheck.position : body.position;
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, groundCheckDistance, groundMask);
            IsGrounded = hit.collider != null;
            if (IsGrounded)
            {
                lastGroundedTime = Time.time;
            }
        }

        void OnDrawGizmosSelected()
        {
            if (groundCheck == null)
            {
                return;
            }

            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Vector3 origin = groundCheck.position;
            Gizmos.DrawLine(origin, origin + Vector3.down * groundCheckDistance);
        }
    }
}
