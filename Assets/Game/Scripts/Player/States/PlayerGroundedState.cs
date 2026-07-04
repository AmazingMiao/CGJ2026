namespace CGJ2026.Player.States
{
    public sealed class PlayerGroundedState : IPlayerState
    {
        private readonly PlayerController controller;

        public PlayerGroundedState(PlayerController controller)
        {
            this.controller = controller;
        }

        public string Name => "Grounded";

        public void Enter()
        {
        }

        public void Tick()
        {
        }

        public void FixedTick()
        {
            controller.Motor.ApplyHorizontalMovement(controller.MoveInput.x, true);

            if (!controller.Motor.IsGrounded)
            {
                controller.ChangeToAirborneState();
                return;
            }

            if (controller.ConsumeJumpPressed())
            {
                controller.Motor.Jump();
                controller.ChangeToAirborneState();
            }
        }

        public void Exit()
        {
        }
    }
}
