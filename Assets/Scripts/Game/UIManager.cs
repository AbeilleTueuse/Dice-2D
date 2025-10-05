using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
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
    private VisualElement numPad;
    private Label numberLabel;
    private VisualElement resultsView;
    private Button initReady;
    private Label initReadyCount;
    private Button readyButton;
    private Label readyCount;
    private MultiColumnListView roundResultsTable;
    private List<PlayerResult> roundResults = new();

    private void OnEnable()
    {
        root = uiDocument.rootVisualElement;
        countdownView = root.Q<VisualElement>("CountdownView");
        countdownLabel = root.Q<Label>("CountdownLabel");
        numPad = root.Q<VisualElement>("NumPad");
        numberLabel = root.Q<Label>("NumberLabel");
        resultsView = root.Q<VisualElement>("ResultsView");
        initReady = root.Q<Button>("InitReadyButton");
        initReadyCount = root.Q<Label>("InitReadyCount");
        readyButton = root.Q<Button>("ReadyButton");
        readyCount = root.Q<Label>("ReadyCount");
        roundResultsTable = root.Q<MultiColumnListView>("RoundResultsTable");

        numPad.RegisterCallback<ClickEvent>(OnNumPadClick);
        initReady.clicked += OnReadyButtonClick;
        readyButton.clicked += OnReadyButtonClick;
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

    private string IndexToRank(int index)
    {
        string rank = index switch
        {
            0 => "🥇",
            1 => "🥈",
            2 => "🥉",
            _ => $"{index + 1}e",
        };
        return rank;
    }

    public void InitializeResultsTable()
    {
        roundResultsTable = root.Q<MultiColumnListView>("RoundResultsTable");

        roundResultsTable.columns.Add(
            new Column
            {
                title = "Rang",
                makeCell = () => new Label(),
                bindCell = (element, i) =>
                {
                    ((Label)element).text = IndexToRank(i);
                    var row = element.parent?.parent;
                    if (row != null)
                    {
                        row.style.backgroundColor = roundResults[i].IsCorrect
                            ? new Color(0.2f, 0.6f, 0.2f, 0.3f)
                            : new Color(0.6f, 0.2f, 0.2f, 0.3f);
                    }
                },
                width = new Length(25, LengthUnit.Percent),
            }
        );

        roundResultsTable.columns.Add(
            new Column
            {
                title = "Joueur",
                makeCell = () => new Label(),
                bindCell = (element, i) =>
                {
                    ((Label)element).text = roundResults[i].PlayerName;
                },
                width = new Length(25, LengthUnit.Percent),
            }
        );

        roundResultsTable.columns.Add(
            new Column
            {
                title = "Réponse",
                makeCell = () => new Label(),
                bindCell = (element, i) =>
                {
                    ((Label)element).text = roundResults[i].Answer.ToString();
                },
                width = new Length(25, LengthUnit.Percent),
            }
        );

        roundResultsTable.columns.Add(
            new Column
            {
                title = "Temps",
                makeCell = () => new Label(),
                bindCell = (element, i) =>
                {
                    ((Label)element).text = $"{roundResults[i].ResponseTime:F1}s";
                },
                width = new Length(25, LengthUnit.Percent),
            }
        );
    }

    public IEnumerator ShowCountdown(int from)
    {
        countdownView.RemoveFromClassList("hide");
        countdownLabel.RemoveFromClassList("hide");
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

    private void OnNumPadClick(ClickEvent evt)
    {
        if (evt.target is not Button b)
            return;

        if (b.name == "ButtonValidate")
        {
            if (int.TryParse(numberLabel.text, out int answer))
            {
                GameManager.Instance.Net.SendAnswer(answer);
                numPad.SetEnabled(false);
            }
        }
        else if (b.name == "ButtonDelete")
        {
            numberLabel.text = numberLabel.text.Length > 0 ? numberLabel.text[..^1] : "";
        }
        else
        {
            numberLabel.text += b.text;
        }
    }

    public IEnumerator ShowRoundResultsCoroutine(List<PlayerResult> roundResults)
    {
        EndRound();

        this.roundResults = roundResults;
        roundResultsTable.itemsSource = this.roundResults;
        roundResultsTable.Rebuild();

        yield return _waitForSeconds1;
    }

    public void StartRound()
    {
        numberLabel.text = "";
        numPad.RemoveFromClassList("hide");
        resultsView.AddToClassList("hide");
        readyButton.SetEnabled(true);
        numPad.SetEnabled(true);
    }

    public void EndRound()
    {
        numPad.AddToClassList("hide");
        resultsView.RemoveFromClassList("hide");

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
    }
}
