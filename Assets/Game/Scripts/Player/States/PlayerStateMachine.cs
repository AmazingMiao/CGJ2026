namespace CGJ2026.Player.States
{
    public sealed class PlayerStateMachine
    {
        private IPlayerState currentState;

        public IPlayerState CurrentState => currentState;
        public string CurrentStateName => currentState != null ? currentState.Name : string.Empty;

        public void Initialize(IPlayerState startingState)
        {
            currentState = startingState;
            currentState.Enter();
        }

        public void ChangeState(IPlayerState nextState)
        {
            if (nextState == currentState)
            {
                return;
            }

            currentState?.Exit();
            currentState = nextState;
            currentState.Enter();
        }

        public void Tick()
        {
            currentState?.Tick();
        }

        public void FixedTick()
        {
            currentState?.FixedTick();
        }
    }
}
