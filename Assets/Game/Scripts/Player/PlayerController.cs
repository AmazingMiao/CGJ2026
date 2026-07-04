using CGJ2026.Input;
using CGJ2026.Player.States;
using UnityEngine;

namespace CGJ2026.Player
{
    [RequireComponent(typeof(CharacterMotor2D))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameInputReader inputReader;
        [SerializeField] private CharacterMotor2D motor;

        private PlayerStateMachine stateMachine;
        private PlayerGroundedState groundedState;
        private PlayerAirborneState airborneState;

        public Vector2 MoveInput => inputReader != null ? inputReader.PlayerMove : Vector2.zero;
        public CharacterMotor2D Motor => motor;
        public string CurrentStateName => stateMachine != null ? stateMachine.CurrentStateName : string.Empty;

        private void Reset()
        {
            motor = GetComponent<CharacterMotor2D>();
        }

        private void Awake()
        {
            if (motor == null)
            {
                motor = GetComponent<CharacterMotor2D>();
            }

            if (inputReader == null)
            {
                inputReader = FindFirstObjectByType<GameInputReader>();
            }

            stateMachine = new PlayerStateMachine();
            groundedState = new PlayerGroundedState(this);
            airborneState = new PlayerAirborneState(this);
            stateMachine.Initialize(groundedState);
        }

        private void Update()
        {
            stateMachine.Tick();
        }

        private void FixedUpdate()
        {
            motor.RefreshGrounded();
            motor.ApplyGravityScale();
            stateMachine.FixedTick();
            motor.ClampFallSpeed();
        }

        public bool ConsumeJumpPressed()
        {
            return inputReader != null && inputReader.ConsumeJumpPressed();
        }

        public void DiscardJumpPressed()
        {
            inputReader?.DiscardJumpPressed();
        }

        public void ChangeToGroundedState()
        {
            stateMachine.ChangeState(groundedState);
        }

        public void ChangeToAirborneState()
        {
            stateMachine.ChangeState(airborneState);
        }
    }
}
