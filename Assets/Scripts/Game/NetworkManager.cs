using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class NetworkManager : MonoBehaviourPunCallbacks
{
    // Constantes des propriétés de room
    private const string RoomPropAnsweredCount = "AnsweredCount";
    private const string RoomPropDiceNumber = "DiceNumber";
    private const string RoomPropRoundNumber = "RoundNumber";
    public const string RoomPropReadyCount = "ReadyCount";

    public readonly Dictionary<int, PlayerStats> GlobalStats = new();
    private readonly List<PlayerResult> roundResults = new();

    // Accès rapide à la room
    private Room CurrentRoom => PhotonNetwork.CurrentRoom;

    public int AnsweredCount
    {
        get
        {
            if (
                CurrentRoom?.CustomProperties.TryGetValue(RoomPropAnsweredCount, out var val)
                    == true
                && val is int count
            )
                return count;
            return 0;
        }
    }

    public int DiceCount
    {
        get
        {
            if (
                CurrentRoom?.CustomProperties.TryGetValue(RoomPropDiceNumber, out var val) == true
                && val is int count
            )
                return count;
            return 5; // défaut
        }
    }

    public int MaxRounds
    {
        get
        {
            if (
                CurrentRoom?.CustomProperties.TryGetValue(RoomPropRoundNumber, out var val) == true
                && val is int count
            )
                return count;
            return 10; // défaut
        }
    }

    #region --- Réponses des joueurs ---

    public void SendAnswer(int value)
    {
        float responseTime = Time.time - GameManager.Instance.Rounds.RoundStartTime;

        photonView.RPC(
            nameof(RPC_SendAnswerToMaster),
            RpcTarget.MasterClient,
            PhotonNetwork.LocalPlayer.ActorNumber,
            value,
            responseTime
        );

        IncrementAnsweredCount();
    }

    [PunRPC]
    private void RPC_SendAnswerToMaster(int actorNumber, int playerAnswer, float responseTime)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        var diceValues = GameManager.Instance.Rounds.CurrentDiceValues;
        int product = diceValues.Aggregate(1, (a, b) => a * b);
        bool isCorrect = playerAnswer == product;

        Debug.Log(
            $"[MASTER] Player {actorNumber}: {(isCorrect ? "✔️" : "❌")} ({responseTime:F2}s)"
        );

        roundResults.Add(new PlayerResult(actorNumber, isCorrect, responseTime));
    }

    #endregion

    #region --- Comptage des réponses synchronisées ---

    public void IncrementAnsweredCount()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            int newCount = AnsweredCount + 1;
            var props = new ExitGames.Client.Photon.Hashtable
            {
                [RoomPropAnsweredCount] = newCount,
            };
            CurrentRoom?.SetCustomProperties(props);
        }
        else
        {
            photonView.RPC(nameof(RPC_RequestIncrementAnswered), RpcTarget.MasterClient);
        }
    }

    [PunRPC]
    private void RPC_RequestIncrementAnswered()
    {
        IncrementAnsweredCount();
    }

    public bool AllPlayersAnswered()
    {
        if (CurrentRoom == null)
            return false;
        return AnsweredCount >= CurrentRoom.PlayerCount;
    }

    #endregion

    #region --- Fin de round et résultats ---

    public void EndRound()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        // Prépare les tableaux à envoyer
        var actors = roundResults.Select(r => r.ActorNumber).ToArray();
        var corrects = roundResults.Select(r => r.IsCorrect ? 1 : 0).ToArray();
        var times = roundResults.Select(r => r.ResponseTime).ToArray();

        photonView.RPC(nameof(RPC_ShowResults), RpcTarget.All, actors, corrects, times);
    }

    [PunRPC]
    private void RPC_ShowResults(int[] actorNumbers, int[] isCorrect, float[] responseTimes)
    {
        roundResults.Clear();
        for (int i = 0; i < actorNumbers.Length; i++)
            roundResults.Add(
                new PlayerResult(actorNumbers[i], isCorrect[i] != 0, responseTimes[i])
            );

        UpdateGlobalStats(roundResults);

        StartCoroutine(GameManager.Instance.UI.ShowRoundResultsCoroutine(roundResults));

        // Réinitialise le compteur pour le prochain round
        if (PhotonNetwork.IsMasterClient)
        {
            var props = new ExitGames.Client.Photon.Hashtable { [RoomPropAnsweredCount] = 0 };
            CurrentRoom?.SetCustomProperties(props);
        }
    }

    private void UpdateGlobalStats(List<PlayerResult> results)
    {
        var winner = results.Where(r => r.IsCorrect).OrderBy(r => r.ResponseTime).FirstOrDefault();
        foreach (var r in results)
        {
            if (!GlobalStats.TryGetValue(r.ActorNumber, out var stats))
                GlobalStats[r.ActorNumber] = stats = new PlayerStats($"Joueur {r.ActorNumber}");

            if (r.IsCorrect)
            {
                stats.TotalTime += r.ResponseTime;
                if (winner != null && r.ActorNumber == winner.ActorNumber)
                    stats.Score++;
            }
        }
    }

    public void ResetReadyCount()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        var props = new ExitGames.Client.Photon.Hashtable { [RoomPropReadyCount] = 0 };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    [PunRPC]
    public void RPC_PlayerReady()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        int readyCount = 0;
        if (
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomPropReadyCount, out var val)
            && val is int c
        )
            readyCount = c;

        readyCount++;

        var props = new ExitGames.Client.Photon.Hashtable { [RoomPropReadyCount] = readyCount };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);

        Debug.Log($"🔹 {readyCount}/{PhotonNetwork.CurrentRoom.PlayerCount} joueurs sont prêts.");

        if (readyCount >= PhotonNetwork.CurrentRoom.PlayerCount)
        {
            Debug.Log("✅ Tous les joueurs sont prêts ! Lancement du prochain round...");
            GameManager.Instance.StartGame();
        }
    }

    public override void OnRoomPropertiesUpdate(
        ExitGames.Client.Photon.Hashtable propertiesThatChanged
    )
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        if (propertiesThatChanged.ContainsKey(RoomPropReadyCount))
        {
            int readyCount = (int)PhotonNetwork.CurrentRoom.CustomProperties[RoomPropReadyCount];
            int total = PhotonNetwork.CurrentRoom.PlayerCount;

            // Informe l’UI
            GameManager.Instance.UI.UpdateReadyCountLabel(readyCount, total);
        }
    }

    #endregion
}
