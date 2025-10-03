using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UIElements;

public class GameManager : MonoBehaviourPunCallbacks
{
    private static WaitForSeconds _waitForSeconds10 = new WaitForSeconds(10f);

    // ----- Configuration (exposable dans l'inspector) -----
    [Header("References")]
    [SerializeField]
    private UIDocument uiDocument = null!;

    [SerializeField]
    private List<DieSO> dice = null!;

    [SerializeField]
    private GameObject circle = null!;

    [Header("Round settings")]
    [SerializeField, Min(1)]
    private int defaultDiceCount = 5;

    [SerializeField, Min(1f)]
    private float roundDuration = 30f;

    [SerializeField, Min(1)]
    private int countdownFrom = 5;

    [SerializeField, Min(0.1f)]
    private float interStepDelay = 1f;

    [SerializeField, Min(0.1f)]
    private float restartDelay = 3f;

    [Header("Spawn settings")]
    [SerializeField]
    private float spawnRadius = 2f;

    [SerializeField]
    private float minSpawnDistance = 1f;

    [SerializeField]
    private int maxSpawnAttempts = 100;

    // ----- Constants -----
    private const string RoomPropAnsweredCount = "AnsweredCount";
    private const string RoomPropDiceNumber = "DiceNumber";
    private const string RoomPropRoundNumber = "RoundNumber";
    private const string UiClassHidden = "hide";
    private const string UiClassPop = "countdown-animate";

    // ----- Cached UI -----
    private VisualElement root = null!;
    private VisualElement mainView = null!;
    private Label numberLabel = null!;
    private VisualElement numPad = null!;
    private Label countdownLabel = null!;
    private VisualElement resultsPanel = null!;

    // ----- Runtime state -----
    private readonly List<GameObject> spawnedDice = new();
    private readonly List<int> currentDiceValues = new();
    private readonly List<PlayerResult> results = new();
    private readonly Dictionary<int, PlayerStats> globalStats = new();
    private int currentRound = 0;

    [SerializeField]
    private int maxRounds = 10; // Valeur par défaut si pas dans les props

    private Coroutine roundCoroutine;
    private bool hasAnswered;
    private float roundStartTime;

    // Prebuilt WaitForSeconds to avoid allocations each frame
    private static readonly WaitForSeconds Wait1 = new(1f);
    private static readonly WaitForSeconds Wait3 = new(3f);
    private static readonly WaitForSeconds Wait5 = new(5f);

    // ----- Internal types -----
    private class PlayerResult
    {
        public int ActorNumber;
        public bool IsCorrect;
        public float ResponseTime;
    }

    private class PlayerStats
    {
        public string Name;
        public int TotalCorrect = 0;
        public float TotalTime = 0f; // somme des temps pour les réponses correctes
    }

    #region Unity lifecycle

    private void Awake()
    {
        // Basic argument checks (helps catch mis-wired references quickly)
        if (uiDocument == null)
            Debug.LogError("UI Document reference missing", this);
        if (dice == null || dice.Count == 0)
            Debug.LogWarning("Dice list empty — ensure DieSO assets are assigned", this);
        if (circle == null)
            Debug.LogError("Circle reference missing", this);
    }

    public override void OnEnable()
    {
        base.OnEnable();
        CacheUIElements();
        RegisterButtonCallbacks();
    }

    public override void OnDisable()
    {
        base.OnDisable();
        UnregisterButtonCallbacks();
    }

    private void Start()
    {
        StartGame();
    }

    #endregion

    #region UI helpers

    private void CacheUIElements()
    {
        if (uiDocument == null)
            return;

        root = uiDocument.rootVisualElement;

        mainView = root.Q<VisualElement>("MainView");
        numberLabel = root.Q<Label>("NumberLabel");
        numPad = root.Q<VisualElement>("NumPad");
        countdownLabel = root.Q<Label>("CountdownLabel");

        // Safety: if any is null, log a warning to help debug
        if (mainView == null)
            Debug.LogWarning("MainView not found in UI Document");
        if (numberLabel == null)
            Debug.LogWarning("NumberLabel not found in UI Document");
        if (numPad == null)
            Debug.LogWarning("NumPad not found in UI Document");
        if (countdownLabel == null)
            Debug.LogWarning("CountdownLabel not found in UI Document");
    }

    private void RegisterButtonCallbacks()
    {
        if (root == null)
            return;

        var backBtn = root.Q<Button>("BackToMenuButton");
        if (backBtn != null)
            backBtn.clicked += OnBackToMenu;

        if (numPad != null)
            numPad.RegisterCallback<ClickEvent>(OnNumPadClick);
    }

    private void UnregisterButtonCallbacks()
    {
        if (root == null)
            return;

        var backBtn = root.Q<Button>("BackToMenuButton");
        if (backBtn != null)
            backBtn.clicked -= OnBackToMenu;

        if (numPad != null)
            numPad.UnregisterCallback<ClickEvent>(OnNumPadClick);
    }

    private void ShowView(VisualElement targetView)
    {
        if (mainView != null)
            mainView.AddToClassList(UiClassHidden);
        targetView?.RemoveFromClassList(UiClassHidden);
    }

    private void SetNumPadEnabled(bool enabled)
    {
        numPad?.SetEnabled(enabled);
    }

    #endregion

    #region Game flow

    public void StartGame()
    {
        StopRoundCoroutineIfRunning();

        // --- Récupérer le nombre de rounds depuis la room si dispo ---
        int configuredRounds = maxRounds; // valeur fallback
        if (PhotonNetwork.CurrentRoom != null 
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(RoomPropRoundNumber, out object roundVal)
            && roundVal is int rVal)
        {
            configuredRounds = rVal;
        }

        maxRounds = configuredRounds; // on remplace la valeur locale par celle de la room

        currentRound++;
        if (currentRound > maxRounds)
        {
            Debug.Log("Tous les rounds sont terminés !");
            StartCoroutine(ShowFinalResultsCoroutine());
            return;
        }

        Debug.Log($"--- Round {currentRound}/{maxRounds} ---");
        roundCoroutine = StartCoroutine(RoundRoutine());
    }

    private IEnumerator ShowFinalResultsCoroutine()
    {
        if (root == null)
            yield break;

        SetNumPadEnabled(false);
        resultsPanel ??= root.Q<VisualElement>("ResultsPanel");
        resultsPanel?.RemoveFromClassList(UiClassHidden);
        resultsPanel.Clear();

        var title = new Label("🏆 Classement final 🏆");
        title.AddToClassList("results-title");
        resultsPanel.Add(title);

        DisplayGlobalLeaderboard();

        yield return _waitForSeconds10;

        // Retour au menu
        OnBackToMenu();
    }

    private void StopRoundCoroutineIfRunning()
    {
        if (roundCoroutine != null)
        {
            StopCoroutine(roundCoroutine);
            roundCoroutine = null;
        }
    }

    private IEnumerator RoundRoutine()
    {
        // Reset per-round master state
        if (PhotonNetwork.IsMasterClient)
        {
            results.Clear();
            var hashtable = new ExitGames.Client.Photon.Hashtable { [RoomPropAnsweredCount] = 0 };
            PhotonNetwork.CurrentRoom?.SetCustomProperties(hashtable);
        }

        Debug.Log($"Round starting in {countdownFrom} seconds...");
        SetNumPadEnabled(false);
        countdownLabel?.RemoveFromClassList(UiClassHidden);

        int countdown = Mathf.Max(1, countdownFrom);
        while (countdown > 0)
        {
            if (countdownLabel != null)
            {
                countdownLabel.text = countdown.ToString();
                RestartPopAnimation(countdownLabel);
            }
            yield return new WaitForSeconds(interStepDelay);
            countdown--;
        }

        if (countdownLabel != null)
        {
            countdownLabel.text = "GO!";
            RestartPopAnimation(countdownLabel);
        }

        yield return new WaitForSeconds(interStepDelay);

        if (countdownLabel != null)
        {
            countdownLabel.text = "";
            countdownLabel.AddToClassList(UiClassHidden);
        }

        Debug.Log("Round started!");
        StartRound();

        float elapsed = 0f;
        while (elapsed < roundDuration)
        {
            if (AllPlayersAnswered())
            {
                Debug.Log("All players answered, ending round early.");
                break; // On sort de la boucle, EndRound sera appelé après
            }

            yield return null;
            elapsed += Time.deltaTime;
        }

        Debug.Log("Round ended!");
        EndRound(); // <-- ShowResultsCoroutine() sera lancé ici
    }

    private void StartRound()
    {
        // Common per-client setup
        if (numberLabel != null)
            numberLabel.text = "";
        hasAnswered = false;
        SetNumPadEnabled(true);
        roundStartTime = Time.time;

        // Only Master spawns dice & decides the correct combination
        if (!PhotonNetwork.IsMasterClient)
            return;

        int maxDice = defaultDiceCount;
        if (
            PhotonNetwork.CurrentRoom != null
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                RoomPropDiceNumber,
                out object diceVal
            )
            && diceVal is int val
        )
        {
            maxDice = val;
        }

        ClearDice_InternalMasterOnly();

        currentDiceValues.Clear();

        Vector3 center = circle != null ? circle.transform.position : Vector3.zero;
        var usedPositions = new List<Vector3>();

        for (int i = 0; i < maxDice; i++)
        {
            var dieSO = GetRandomDie();
            if (dieSO == null)
                continue;

            Vector3 spawnPos = GetValidSpawnPosition(
                center,
                usedPositions,
                minSpawnDistance,
                maxSpawnAttempts
            );
            Quaternion spawnRot = GetRandomRotation();

            GameObject spawned = PhotonNetwork.Instantiate(dieSO.ResourcePath, spawnPos, spawnRot);
            spawnedDice.Add(spawned);
            usedPositions.Add(spawnPos);
            currentDiceValues.Add(dieSO.Value);
        }
    }

    private IEnumerator ShowResultsCoroutine()
    {
        if (root == null)
            yield break;

        // Cache numpad pendant affichage
        SetNumPadEnabled(false);

        // Crée ou récupère le panel de résultats
        resultsPanel ??= root.Q<VisualElement>("ResultsPanel");
        resultsPanel?.RemoveFromClassList(UiClassHidden);

        // Affiche résultats du round
        DisplayRoundResults();

        // Attend 10 secondes
        yield return _waitForSeconds10;

        // Masque résultats
        resultsPanel?.AddToClassList(UiClassHidden);

        // Relance round
        StartGame();
    }

    private void DisplayRoundResults()
    {
        if (resultsPanel == null)
            return;

        resultsPanel.Clear();

        // Titre
        var roundTitle = new Label("Résultats");
        roundTitle.AddToClassList("results-title");
        resultsPanel.Add(roundTitle);

        // Trie par correct puis temps
        var roundResults = results
            .OrderByDescending(r => r.IsCorrect)
            .ThenBy(r => r.ResponseTime)
            .ToList();

        foreach (var r in roundResults)
        {
            Player player = PhotonNetwork.CurrentRoom?.GetPlayer(r.ActorNumber);
            string name = player?.NickName ?? $"Joueur {r.ActorNumber}";
            string text = r.IsCorrect
                ? $"✅ {name} : {r.ResponseTime:F2}s"
                : $"❌ {name} : incorrect";
            resultsPanel.Add(new Label(text));

            // Met à jour classement global
            if (!globalStats.TryGetValue(r.ActorNumber, out var stat))
            {
                stat = new PlayerStats { Name = name };
                globalStats[r.ActorNumber] = stat;
            }

            if (r.IsCorrect)
            {
                stat.TotalCorrect++;
                stat.TotalTime += r.ResponseTime;
            }
        }

        // Affiche classement global
        DisplayGlobalLeaderboard();
    }

    private void DisplayGlobalLeaderboard()
    {
        if (resultsPanel == null)
            return;

        resultsPanel.Add(new Label("Classement") { name = "LeaderboardTitle" });

        // Trie par TotalCorrect desc, puis TotalTime asc
        var leaderboard = globalStats
            .Values.OrderByDescending(s => s.TotalCorrect)
            .ThenBy(s => s.TotalTime)
            .ToList();

        foreach (var s in leaderboard)
        {
            string text = $"{s.Name} : {s.TotalCorrect} correct, {s.TotalTime:F2}s";
            resultsPanel.Add(new Label(text));
        }
    }

    private void EndRound()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            // Master envoie tous les résultats aux clients
            foreach (var r in results)
            {
                photonView.RPC(
                    nameof(RPC_AddResult),
                    RpcTarget.Others,
                    r.ActorNumber,
                    r.IsCorrect,
                    r.ResponseTime
                );
            }

            // Puis annonce le winner
            var winner = ComputeWinner();
            if (winner != null)
            {
                var player = PhotonNetwork.CurrentRoom?.GetPlayer(winner.ActorNumber);
                string winnerName = player?.NickName ?? $"Joueur {winner.ActorNumber}";
                photonView.RPC(
                    nameof(RPC_AnnounceWinner),
                    RpcTarget.All,
                    winner.ActorNumber,
                    winner.ResponseTime,
                    winnerName
                );
            }
            else
            {
                photonView.RPC(nameof(RPC_NoWinner), RpcTarget.All);
            }
        }

        ClearDice();
        StartCoroutine(ShowResultsCoroutine());
    }

    [PunRPC]
    private void RPC_AddResult(int actorNumber, bool isCorrect, float responseTime)
    {
        results.Add(
            new PlayerResult
            {
                ActorNumber = actorNumber,
                IsCorrect = isCorrect,
                ResponseTime = responseTime,
            }
        );
    }

    #endregion

    #region Spawning helpers

    private DieSO GetRandomDie()
    {
        if (dice == null || dice.Count == 0)
            return null;
        return dice[Random.Range(0, dice.Count)];
    }

    private Vector3 GetValidSpawnPosition(
        Vector3 center,
        List<Vector3> usedPositions,
        float minDistance,
        int maxAttempts
    )
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = center + new Vector3(offset.x, offset.y, 0f);

            bool ok = true;
            foreach (var pos in usedPositions)
            {
                if (Vector3.Distance(pos, candidate) < minDistance)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                return candidate;
        }

        Debug.LogWarning("Spawn position fallback triggered.");
        return center;
    }

    private Quaternion GetRandomRotation()
    {
        return Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
    }

    /// <summary>
    /// Called only on the Master client: destroys spawned dice over the network.
    /// </summary>
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

    /// <summary>
    /// Clears local references. If master, will first destroy networked objects.
    /// </summary>
    private void ClearDice()
    {
        if (PhotonNetwork.IsMasterClient)
            ClearDice_InternalMasterOnly();
        else
            spawnedDice.Clear();

        currentDiceValues.Clear();
    }

    #endregion

    #region Input / UI handlers

    private void OnBackToMenu()
    {
        Debug.Log("Retour au menu demandé.");

        StopRoundCoroutineIfRunning();
        StopAllCoroutines();

        PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

    private void OnNumPadClick(ClickEvent evt)
    {
        if (hasAnswered)
            return;

        if (evt.target is not Button button)
            return;

        string buttonText = button.text;
        if (string.IsNullOrEmpty(buttonText))
            return;

        if (buttonText == "✖")
        {
            if (!string.IsNullOrEmpty(numberLabel?.text))
                numberLabel.text = numberLabel.text.Substring(
                    0,
                    Mathf.Max(0, numberLabel.text.Length - 1)
                );
        }
        else if (buttonText == "✔")
        {
            if (string.IsNullOrEmpty(numberLabel?.text))
                return;

            Debug.Log($"Validate number: {numberLabel.text}");

            if (int.TryParse(numberLabel.text, out int playerNumber))
            {
                float responseTime = Time.time - roundStartTime;

                // Send raw answer and time to Master — Master will validate
                photonView.RPC(
                    nameof(RPC_SendResultToMaster),
                    RpcTarget.MasterClient,
                    PhotonNetwork.LocalPlayer.ActorNumber,
                    playerNumber,
                    responseTime
                );
            }

            hasAnswered = true;
            SetNumPadEnabled(false);
            IncrementAnsweredCount();
        }
        else
        {
            // Append digit and prevent leading zeros
            var newText = (numberLabel?.text ?? "") + buttonText;
            if (int.TryParse(newText, out _))
            {
                if (newText.Length > 1 && newText.StartsWith("0"))
                    newText = newText.TrimStart('0');

                if (numberLabel != null)
                    numberLabel.text = newText;
            }
        }
    }

    private void RestartPopAnimation(VisualElement element)
    {
        if (element == null)
            return;

        element.RemoveFromClassList(UiClassPop);
        // Reapply on next frame to force transition
        element.schedule.Execute(() => element.AddToClassList(UiClassPop)).StartingIn(1);
    }

    #endregion

    #region Answer counting / room props

    private void IncrementAnsweredCount()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            int answeredCount = 0;
            if (
                PhotonNetwork.CurrentRoom?.CustomProperties.TryGetValue(
                    RoomPropAnsweredCount,
                    out object val
                ) == true
                && val is int count
            )
            {
                answeredCount = count;
            }

            answeredCount++;
            var props = new ExitGames.Client.Photon.Hashtable
            {
                [RoomPropAnsweredCount] = answeredCount,
            };
            PhotonNetwork.CurrentRoom?.SetCustomProperties(props);
        }
        else
        {
            // Ask master to increment
            photonView.RPC(nameof(RPC_RequestIncrementAnswered), RpcTarget.MasterClient);
        }
    }

    [PunRPC]
    private void RPC_RequestIncrementAnswered()
    {
        // executed on master
        IncrementAnsweredCount();
    }

    private bool AllPlayersAnswered()
    {
        if (PhotonNetwork.CurrentRoom == null)
            return false;

        int answeredCount = 0;
        if (
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                RoomPropAnsweredCount,
                out object val
            ) && val is int c
        )
            answeredCount = c;

        return answeredCount >= PhotonNetwork.CurrentRoom.PlayerCount;
    }

    #endregion

    #region RPCs (results & announcements)

    /// <summary>
    /// Called by clients to send their raw answer to the master.
    /// Master will compute correctness and store result.
    /// </summary>
    [PunRPC]
    private void RPC_SendResultToMaster(int actorNumber, int playerAnswer, float responseTime)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        int product = 1;
        foreach (var v in currentDiceValues)
            product *= v;

        bool isCorrect = (playerAnswer == product);

        if (isCorrect)
            Debug.Log($"[MASTER] Player {actorNumber} correct! ({responseTime:F2}s)");
        else
            Debug.Log(
                $"[MASTER] Player {actorNumber} wrong! Expected {product}, got {playerAnswer}"
            );

        results.Add(
            new PlayerResult
            {
                ActorNumber = actorNumber,
                IsCorrect = isCorrect,
                ResponseTime = responseTime,
            }
        );
    }

    /// <summary>
    /// Broadcast winner announcement to all clients (called by master).
    /// </summary>
    [PunRPC]
    private void RPC_AnnounceWinner(int actorNumber, float responseTime, string winnerName)
    {
        Debug.Log($"🏆 Winner: {winnerName} ({responseTime:F2}s)");
        // Here you can also update UI on each client to show the winner (e.g. show a popup)
        // Example: root?.Q<Label>("WinnerLabel")?.SetValueWithoutNotify($"{winnerName} won in {responseTime:F2}s");
    }

    [PunRPC]
    private void RPC_NoWinner()
    {
        Debug.Log("❌ No winner this round (no correct answers).");
    }

    #endregion

    #region Utilities

    private PlayerResult ComputeWinner()
    {
        PlayerResult winner = null;
        foreach (var r in results)
        {
            if (!r.IsCorrect)
                continue;
            if (winner == null || r.ResponseTime < winner.ResponseTime)
                winner = r;
        }
        return winner;
    }

    #endregion
}
