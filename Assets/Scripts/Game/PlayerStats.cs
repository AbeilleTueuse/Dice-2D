public class PlayerStats
{
    public string Name { get; }
    public int Score;
    public float TotalTime;
    public int Rank;

    public PlayerStats(string name)
    {
        Name = name;
        Score = 0;
        TotalTime = 0;
        Rank = 0;
    }
}
