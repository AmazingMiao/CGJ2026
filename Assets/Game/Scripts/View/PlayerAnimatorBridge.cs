using CGJ2026.Gameplay;
using CGJ2026.Player;
using UnityEngine;

namespace CGJ2026.View
{
    // Reserved hook for future character animation art: stays a no-op until an Animator with a
    // real AnimatorController is assigned. Expected parameters: "Speed" (float), "Grounded" (bool),
    // "VerticalVelocity" (float), a "Jump" trigger, and a "Death" trigger.
    public sealed class PlayerAnimatorBridge : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private CharacterMotor2D motor;
        [SerializeField] private RespawnService respawnService;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int GroundedParam = Animator.StringToHash("Grounded");
        private static readonly int VerticalVelocityParam = Animator.StringToHash("VerticalVelocity");
        private static readonly int JumpTrigger = Animator.StringToHash("Jump");
        private static readonly int DeathTrigger = Animator.StringToHash("Death");

        private void Awake()
        {
            if (body == null)
            {
                body = GetComponentInParent<Rigidbody2D>();
            }

            if (motor == null)
            {
                motor = GetComponentInParent<CharacterMotor2D>();
            }

            if (motor != null)
            {
                motor.Jumped += NotifyJump;
            }

            if (respawnService != null)
            {
                respawnService.Died += NotifyDeath;
                respawnService.Respawned += ResetAfterRespawn;
            }
        }

        private void OnDestroy()
        {
            if (motor != null)
            {
                motor.Jumped -= NotifyJump;
            }

            if (respawnService != null)
            {
                respawnService.Died -= NotifyDeath;
                respawnService.Respawned -= ResetAfterRespawn;
            }
        }

        private void Update()
        {
            if (animator == null || animator.runtimeAnimatorController == null || body == null)
            {
                return;
            }

            animator.SetFloat(SpeedParam, Mathf.Abs(body.linearVelocity.x));
            animator.SetFloat(VerticalVelocityParam, body.linearVelocity.y);
            if (motor != null)
            {
                animator.SetBool(GroundedParam, motor.IsGrounded);
            }
        }

        private void NotifyJump()
        {
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetTrigger(JumpTrigger);
            }
        }

        private void NotifyDeath(Vector3 _)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                animator.SetTrigger(DeathTrigger);
            }
        }

        private void ResetAfterRespawn()
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            animator.Rebind();
            animator.Update(0f);
        }
    }
}
