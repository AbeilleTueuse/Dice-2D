public class PlayerResult
{
    public string PlayerName { get; set; }
    public int Answer { get; set; }
    public int ActorNumber { get; }
    public bool IsCorrect { get; }
    public float ResponseTime { get; }

    public PlayerResult(string playerName, int answer, int actor, bool correct, float time)
    {
        PlayerName = playerName;
        Answer = answer;
        ActorNumber = actor;
        IsCorrect = correct;
        ResponseTime = time;
    }
}
