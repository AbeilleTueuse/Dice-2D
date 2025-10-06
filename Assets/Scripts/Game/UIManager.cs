using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

public class UIManager : MonoBehaviour
{
    private static readonly WaitForSeconds _waitForSeconds10 = new(10f);
    private static readonly WaitForSeconds _waitForSeconds1 = new(1f);

    [SerializeField]
    private UIDocument uiDocument;
    private VisualElement root;
    private VisualElement countdownView;
    private Label countdownLabel;
    private Label roundInfo;
    private VisualElement numPad;
    private Label numberLabel;
    private VisualElement resultsView;
    private VisualElement resultsRound;
    private Button quitResultsButton;
    private Button initReady;
    private Label initReadyCount;
    private Button readyButton;
    private Label readyCount;
    private ListView roundResultsTable;
    private Button showResults;
    private Label correctAnswerLabel;
    private List<PlayerResult> roundResults = new();

    private void OnEnable()
    {
        root = uiDocument.rootVisualElement;
        countdownView = root.Q<VisualElement>("CountdownView");
        countdownLabel = root.Q<Label>("CountdownLabel");
        roundInfo = root.Q<Label>("RoundInfo");
        numPad = root.Q<VisualElement>("NumPad");
        numberLabel = root.Q<Label>("NumberLabel");
        resultsView = root.Q<VisualElement>("ResultsView");
        resultsRound = root.Q<VisualElement>("ResultsRound");
        quitResultsButton = root.Q<Button>("QuitResultsButton");
        initReady = root.Q<Button>("InitReadyButton");
        initReadyCount = root.Q<Label>("InitReadyCount");
        readyButton = root.Q<Button>("ReadyButton");
        readyCount = root.Q<Label>("ReadyCount");
        showResults = root.Q<Button>("ShowResults");
        correctAnswerLabel = root.Q<Label>("CorrectAnswerLabel");
        roundResultsTable = root.Q<ListView>("RoundResultsTable");

        numPad.RegisterCallback<ClickEvent>(OnNumPadClick);
        initReady.clicked += OnReadyButtonClick;
        readyButton.clicked += OnReadyButtonClick;
        showResults.clicked += OnShowResultsClick;
        quitResultsButton.clicked += OnQuitResultsClick;
        root.Q<Button>("BackToMenuButton").clicked += () => GameManager.Instance.EndGame();
        InitializeResultsTable();
    }

    private void OnReadyButtonClick()
    {
        if (GameManager.Instance.Rounds.IsLastRound)
        {
            GameManager.Instance.EndGame();
            return;
        }
        readyButton.SetEnabled(false);
        initReady.SetEnabled(false);
        PhotonView photonView = PhotonView.Get(GameManager.Instance.Net);
        photonView.RPC(nameof(NetworkManager.RPC_PlayerReady), RpcTarget.MasterClient);
    }

    private void OnShowResultsClick()
    {
        showResults.AddToClassList("hide");
        resultsRound.RemoveFromClassList("hide");
    }

    private void OnQuitResultsClick()
    {
        showResults.RemoveFromClassList("hide");
        resultsRound.AddToClassList("hide");
    }

    private string IndexToRank(int index)
    {
        string rank = index switch
        {
            1 => "🥇",
            2 => "🥈",
            3 => "🥉",
            _ => $"{index}e",
        };
        return rank;
    }

    public void InitializeResultsTable()
    {
        roundResultsTable.makeItem = () =>
        {
            // Conteneur principal (vertical)
            var container = new VisualElement();
            container.AddToClassList("results-item");

            // Ligne 1 : rang + nom
            var topRow = new Label
            {
                name = "topRow",
                style = { unityFontStyleAndWeight = FontStyle.Bold },
            };
            // Ligne 2 : réponse + temps
            var bottomRow = new Label
            {
                name = "bottomRow",
                style = { color = new Color(0.8f, 0.8f, 0.8f) },
            };

            container.Add(topRow);
            container.Add(bottomRow);

            topRow.style.paddingTop = 20;

            return container;
        };

        roundResultsTable.bindItem = (element, i) =>
        {
            var top = element.Q<Label>("topRow");
            var bottom = element.Q<Label>("bottomRow");
            var r = roundResults[i];
            var formattedAnswer = r.Answer.ToString("N0", GameManager.Instance.GameCulture);

            top.text = $"{IndexToRank(r.Rank)}  {r.PlayerName}";
            bottom.text = $"Réponse : {formattedAnswer} en {r.ResponseTime:F1} s";

            // Couleur de fond selon la correction
            element.style.backgroundColor = r.IsCorrect
                ? new Color(0.2f, 0.6f, 0.2f, 0.25f)
                : new Color(0.6f, 0.2f, 0.2f, 0.25f);

            element.style.borderTopColor = r.IsCorrect
                ? new Color(0.2f, 0.6f, 0.2f, 0.75f)
                : new Color(0.6f, 0.2f, 0.2f, 0.75f);
            element.style.borderBottomColor = r.IsCorrect
                ? new Color(0.2f, 0.6f, 0.2f, 0.75f)
                : new Color(0.6f, 0.2f, 0.2f, 0.75f);
            element.style.borderLeftColor = r.IsCorrect
                ? new Color(0.2f, 0.6f, 0.8f, 0.75f)
                : new Color(0.6f, 0.2f, 0.8f, 0.75f);
            element.style.borderRightColor = r.IsCorrect
                ? new Color(0.2f, 0.6f, 0.8f, 0.75f)
                : new Color(0.6f, 0.2f, 0.8f, 0.75f);
        };
    }

    public IEnumerator ShowCountdown(int from)
    {
        countdownView.RemoveFromClassList("hide");
        countdownLabel.RemoveFromClassList("hide");
        roundInfo.text =
            $"Manche {GameManager.Instance.Rounds.CurrentRound}/{GameManager.Instance.Rounds.MaxRounds}";
        roundInfo.RemoveFromClassList("hide");
        initReady.AddToClassList("hide");
        initReadyCount.AddToClassList("hide");
        for (int i = from; i > 0; i--)
        {
            countdownLabel.text = i.ToString();
            yield return _waitForSeconds1;
        }
        countdownLabel.text = "GO!";
        yield return _waitForSeconds1;
        countdownView.AddToClassList("hide");
    }

    private void UpdateNumberLabel(string input, string action)
    {
        // 1) Nettoie : supprime tous les caractères d'espacement Unicode
        string raw = numberLabel.text ?? "";
        raw = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());

        // 2) Parse en entier (sécurisé)
        long current = 0;
        if (!string.IsNullOrEmpty(raw))
            long.TryParse(raw, out current);

        // 3) Applique l'action
        switch (action)
        {
            case "add":
                if (int.TryParse(input, out int digit) && digit >= 0 && digit <= 9)
                {
                    // évite overflow : limite arbitraire (ici int.MaxValue)
                    if (current <= (int.MaxValue - digit) / 10)
                        current = current * 10 + digit;
                }
                break;

            case "delete":
                current /= 10;
                break;

            case "reset":
                current = 0;
                break;
        }

        // 4) Met à jour l'affichage formaté (utilise la culture du jeu)
        var culture = GameManager.Instance.GameCulture;
        numberLabel.text = ((int)current).ToString("N0", culture);
    }

    private bool TryGetNumberLabel(out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(numberLabel.text))
            return false;

        // Supprime tous les espaces (y compris insécables)
        string cleaned = new(numberLabel.text.Where(c => !char.IsWhiteSpace(c)).ToArray());

        return int.TryParse(
            cleaned,
            NumberStyles.Integer,
            GameManager.Instance.GameCulture,
            out value
        );
    }

    private void OnNumPadClick(ClickEvent evt)
    {
        if (evt.target is not Button b)
            return;

        switch (b.name)
        {
            case "ButtonValidate":
                if (TryGetNumberLabel(out int answer))
                {
                    GameManager.Instance.Net.SendAnswer(answer);
                    numPad.SetEnabled(false);
                    Debug.Log($"Answer sent: {answer}");
                }
                break;

            case "ButtonDelete":
                UpdateNumberLabel("", "delete");
                break;

            default:
                UpdateNumberLabel(b.text, "add");
                break;
        }
    }

    public void ShowRoundResultsCoroutine(List<PlayerResult> roundResults, int correctAnswer)
    {
        EndRound(correctAnswer);

        this.roundResults = roundResults;
        roundResultsTable.itemsSource = this.roundResults;
        roundResultsTable.Rebuild();
    }

    public void StartRound()
    {
        numberLabel.text = "0";
        numPad.RemoveFromClassList("hide");
        resultsView.AddToClassList("hide");
        readyButton.SetEnabled(true);
        numPad.SetEnabled(true);
    }

    public void EndRound(int correctAnswer)
    {
        numPad.AddToClassList("hide");
        resultsView.RemoveFromClassList("hide");
        resultsRound.RemoveFromClassList("hide");
        showResults.AddToClassList("hide");
        var culture = GameManager.Instance.GameCulture;
        correctAnswerLabel.text =
            $"La bonne réponse était {correctAnswer.ToString("N0", culture)}.";

        if (GameManager.Instance.Rounds.IsLastRound)
        {
            readyButton.text = "Quitter";
            readyCount.AddToClassList("hide");
        }
    }

    public void UpdateReadyCountLabel(int ready, int total)
    {
        readyCount.text = $"{ready}/{total}";
        initReadyCount.text = $"{ready}/{total}";
        Debug.Log($"Ready count updated: {ready}/{total}");
    }

    public void ShowReadyButton()
    {
        initReady.RemoveFromClassList("hide");
        initReadyCount.RemoveFromClassList("hide");
        countdownLabel.AddToClassList("hide");
        roundInfo.AddToClassList("hide");
    }
}
