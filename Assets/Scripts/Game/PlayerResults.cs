using System;

public class PlayerResult
{
    public string PlayerName { get; set; }
    public int Answer { get; set; }
    public int ActorNumber { get; }
    public bool IsCorrect { get; }
    public float ResponseTime { get; }
    public int Rank { get; set; }

    public PlayerResult(
        string playerName,
        int answer,
        int actor,
        bool correct,
        float time,
        int rank = 0
    )
    {
        PlayerName = playerName;
        Answer = answer;
        ActorNumber = actor;
        IsCorrect = correct;
        ResponseTime = time;
        Rank = rank;
    }
}
