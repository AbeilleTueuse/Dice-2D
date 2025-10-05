public class PlayerResult
{
    public int ActorNumber { get; }
    public bool IsCorrect { get; }
    public float ResponseTime { get; }

    public PlayerResult(int actor, bool correct, float time)
    {
        ActorNumber = actor;
        IsCorrect = correct;
        ResponseTime = time;
    }
}
