using System;
using CGJ2026.Gameplay;
using UnityEngine;
using UnityEngine.Serialization;

namespace CGJ2026.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class CharacterMotor2D : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private GroundRaySensor2D groundSensor;

        [Header("Movement")]
        [Tooltip("Top horizontal speed for the character.")]
        [Min(0f)] public float maxMoveSpeed = 8f;

        [Tooltip("Horizontal force used while grounded. Higher values make movement respond faster.")]
        [FormerlySerializedAs("groundAcceleration")]
        [Min(0f)] public float moveForce = 90f;

        [Tooltip("Horizontal force used while airborne. Keep this lower than moveForce for heavier jumps.")]
        [FormerlySerializedAs("airAcceleration")]
        [Min(0f)] public float airMoveForce = 54f;

        [Tooltip("Impulse applied upward when the character jumps.")]
        [FormerlySerializedAs("jumpVelocity")]
        [Min(0f)] public float jumpForce = 16f;

        [Tooltip("Fastest downward speed allowed while falling.")]
        [Min(0f)] public float maxFallSpeed = 26f;

        [Header("Gravity Feel")]
        [Min(0f)] public float risingGravityScale = 3.5f;
        [Min(0f)] public float fallingGravityScale = 6f;

        [Header("Jump Feel")]
        [Tooltip("Clears downward velocity before applying jump impulse so jumps feel consistent on slopes and moving platforms.")]
        public bool clearDownwardVelocityOnJump = true;

        [Header("Wet And Slippery")]
        [Tooltip("Ground acceleration retained while wet.")]
        [Range(0.05f, 1f)] public float slipperyAccelerationMultiplier = 0.45f;

        [Tooltip("Ground braking and reversing force retained while wet. Lower values produce more overshoot.")]
        [Range(0.01f, 1f)] public float slipperyBrakingMultiplier = 0.08f;

        public event Action Jumped;

        public bool IsGrounded { get; private set; }
        public bool IsSlippery => Time.time < slipperyUntilTime;
        public Vector2 Velocity => body != null ? body.linearVelocity : Vector2.zero;

        private float slipperyUntilTime;

        private void Reset()
        {
            body = GetComponent<Rigidbody2D>();
            groundSensor = GetComponent<GroundRaySensor2D>();
        }

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (groundSensor == null)
            {
                groundSensor = GetComponent<GroundRaySensor2D>();
            }
        }

        public void RefreshGrounded()
        {
            IsGrounded = groundSensor != null && groundSensor.Refresh();
        }

        public void ApplyGravityScale()
        {
            if (body == null)
            {
                return;
            }

            // Falling faster than rising kills the "floaty" arc without shortening jump height as much as raising gravity uniformly would.
            body.gravityScale = body.linearVelocity.y > 0f ? risingGravityScale : fallingGravityScale;
        }

        public void ApplyHorizontalMovement(float inputX, bool useGroundMoveForce)
        {
            if (body == null)
            {
                return;
            }

            float targetSpeed = Mathf.Clamp(inputX, -1f, 1f) * maxMoveSpeed;
            Vector2 velocity = body.linearVelocity;
            float force = useGroundMoveForce ? moveForce : airMoveForce;
            if (useGroundMoveForce && IsSlippery)
            {
                bool isBraking = Mathf.Abs(inputX) < 0.01f
                    || (Mathf.Abs(velocity.x) > 0.05f && Mathf.Sign(inputX) != Mathf.Sign(velocity.x));
                force *= isBraking ? slipperyBrakingMultiplier : slipperyAccelerationMultiplier;
            }

            if (Mathf.Abs(velocity.x) > maxMoveSpeed && Mathf.Sign(velocity.x) == Mathf.Sign(targetSpeed))
            {
                velocity.x = Mathf.Sign(velocity.x) * maxMoveSpeed;
                body.linearVelocity = velocity;
            }

            float speedDelta = targetSpeed - body.linearVelocity.x;
            body.AddForce(Vector2.right * speedDelta * force, ForceMode2D.Force);
        }

        public void ApplySlippery(float duration)
        {
            slipperyUntilTime = Mathf.Max(slipperyUntilTime, Time.time + Mathf.Max(0f, duration));
        }

        public void ClearSlippery()
        {
            slipperyUntilTime = 0f;
        }

        public void Jump()
        {
            if (body == null)
            {
                return;
            }

            Vector2 velocity = body.linearVelocity;
            if (clearDownwardVelocityOnJump && velocity.y < 0f)
            {
                velocity.y = 0f;
                body.linearVelocity = velocity;
            }

            body.AddForce(Vector2.up * jumpForce, ForceMode2D.Impulse);
            IsGrounded = false;
            Jumped?.Invoke();
        }

        public void ClampFallSpeed()
        {
            if (body == null)
            {
                return;
            }

            Vector2 velocity = body.linearVelocity;
            if (velocity.y < -maxFallSpeed)
            {
                velocity.y = -maxFallSpeed;
                body.linearVelocity = velocity;
            }
        }

    }
}
