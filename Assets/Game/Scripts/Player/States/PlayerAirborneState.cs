namespace CGJ2026.Player.States
{
    public sealed class PlayerAirborneState : IPlayerState
    {
        private readonly PlayerController controller;

        public PlayerAirborneState(PlayerController controller)
        {
            this.controller = controller;
        }

        public string Name => "Airborne";

        public void Enter()
        {
        }

        public void Tick()
        {
        }

        public void FixedTick()
        {
            // Jump presses made while airborne must not survive until landing and trigger an automatic hop.
            controller.DiscardJumpPressed();
            controller.Motor.ApplyHorizontalMovement(controller.MoveInput.x, false);

            // The ground ray can still reach the floor during the first part of an ascent.
            if (controller.Motor.IsGrounded && controller.Motor.Velocity.y <= 0f)
            {
                controller.ChangeToGroundedState();
            }
        }

        public void Exit()
        {
        }
    }
}
