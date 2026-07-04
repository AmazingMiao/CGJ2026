namespace CGJ2026.Player.States
{
    public interface IPlayerState
    {
        string Name { get; }
        void Enter();
        void Tick();
        void FixedTick();
        void Exit();
    }
}
