using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;

public class RoundManager : MonoBehaviourPun
{
    [Header("Dice")]
    [SerializeField]
    private List<DieSO> dice;

    [SerializeField]
    private GameObject circle;

    private readonly int countdownFrom = 3;

    public float RoundStartTime { get; private set; }
    public bool RoundStarted { get; private set; }

    public int CurrentRound { get; private set; } = 0;
    public int MaxRounds => GameManager.Instance.Net.MaxRounds;
    public bool IsLastRound => CurrentRound >= MaxRounds;
    private readonly List<GameObject> spawnedDice = new();
    public List<int> CurrentDiceValues { get; private set; } = new();
    public int CurrentCorrectAnswer => CurrentDiceValues.Aggregate(1, (a, b) => a * b);
    private Coroutine roundCoroutine;

    private void OnEnable()
    {
        DieSpawnNotifier.OnFirstDieSpawned += HandleFirstDieSpawned;
    }

    private void OnDisable()
    {
        DieSpawnNotifier.OnFirstDieSpawned -= HandleFirstDieSpawned;
    }

    private void HandleFirstDieSpawned()
    {
        if (RoundStarted)
            return;

        RoundStarted = true;
        RoundStartTime = Time.time;
    }

    public void StartRoundRoutine()
    {
        if (roundCoroutine != null)
            StopCoroutine(roundCoroutine);
        roundCoroutine = StartCoroutine(RoundRoutine());
    }

    private IEnumerator RoundRoutine()
    {
        CurrentRound++;
        GameManager.Instance.UI.StartRound();
        ClearDice();
        GameManager.Instance.Net.ResetReadyCount();
        yield return GameManager.Instance.UI.ShowCountdown(countdownFrom);
        StartRound();

        // Attend que tous les joueurs aient répondu
        while (!GameManager.Instance.Net.AllPlayersAnswered())
            yield return null;

        GameManager.Instance.Net.EndRound();
        RoundStarted = false;
    }

    private void StartRound()
    {
        var usedPositions = new List<Vector3>();

        int diceToSpawn = GameManager.Instance.Net.DiceCount;

        if (!PhotonNetwork.IsMasterClient)
            return;

        Vector3 center = circle ? circle.transform.position : Vector3.zero;

        for (int i = 0; i < diceToSpawn; i++)
        {
            var dieSO = dice[Random.Range(0, dice.Count)];
            Vector3 pos = GetRandomPosition(center, usedPositions);
            var obj = PhotonNetwork.Instantiate(dieSO.ResourcePath, pos, Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
            spawnedDice.Add(obj);
            CurrentDiceValues.Add(dieSO.Value);
        }
    }

    private Vector3 GetRandomPosition(Vector3 center, List<Vector3> used)
    {
        for (int i = 0; i < 100; i++)
        {
            Vector2 offset = Random.insideUnitCircle * 2f;
            Vector3 candidate = center + new Vector3(offset.x, offset.y, 0);
            if (used.All(p => Vector3.Distance(p, candidate) >= 1f))
            {
                used.Add(candidate);
                return candidate;
            }
        }
        return center;
    }

    private void ClearDice_InternalMasterOnly()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;
        foreach (var obj in spawnedDice)
        {
            if (obj != null)
                PhotonNetwork.Destroy(obj);
        }
        spawnedDice.Clear();
    }

    private void ClearDice()
    {
        if (PhotonNetwork.IsMasterClient)
            ClearDice_InternalMasterOnly();
        else
            spawnedDice.Clear();

        CurrentDiceValues.Clear();
    }
}
