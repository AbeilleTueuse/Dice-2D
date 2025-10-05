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
    private Label countdownLabel;
    private VisualElement numPad;
    private Label numberLabel;
    private VisualElement resultsView;
    private Button readyButton;
    private Label readyCount;

    private void Awake()
    {
        root = uiDocument.rootVisualElement;
        countdownLabel = root.Q<Label>("CountdownLabel");
        numPad = root.Q<VisualElement>("NumPad");
        numberLabel = root.Q<Label>("NumberLabel");
        resultsView = root.Q<VisualElement>("ResultsView");
        readyButton = root.Q<Button>("ReadyButton");
        readyCount = root.Q<Label>("ReadyCount");

        numPad.RegisterCallback<ClickEvent>(OnNumPadClick);
        readyButton.clicked += () =>
        {
            if (GameManager.Instance.Rounds.IsLastRound)
            {
                GameManager.Instance.EndGame();
                return;
            }
            readyButton.SetEnabled(false);
            PhotonView photonView = PhotonView.Get(GameManager.Instance.Net);
            photonView.RPC(nameof(NetworkManager.RPC_PlayerReady), RpcTarget.MasterClient);
        };
        root.Q<Button>("BackToMenuButton").clicked += () => GameManager.Instance.EndGame();
    }

    public IEnumerator ShowCountdown(int from)
    {
        countdownLabel.RemoveFromClassList("hide");
        for (int i = from; i > 0; i--)
        {
            countdownLabel.text = i.ToString();
            yield return _waitForSeconds1;
        }
        countdownLabel.text = "GO!";
        yield return _waitForSeconds1;
        countdownLabel.AddToClassList("hide");
    }

    private void OnNumPadClick(ClickEvent evt)
    {
        if (evt.target is not Button b)
            return;

        if (b.name == "ButtonValidate")
        {
            if (int.TryParse(numberLabel.text, out int answer))
                GameManager.Instance.Net.SendAnswer(answer);
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
        yield return _waitForSeconds1;
    }

    public IEnumerator ShowResultsCoroutine(Dictionary<int, PlayerStats> stats)
    {
        // resultsPanel.Clear();
        // resultsPanel.RemoveFromClassList("hide");
        // resultsPanel.Add(new Label("🏆 Classement final 🏆"));

        // foreach (var s in stats.Values)
        //     resultsPanel.Add(new Label($"{s.Name} : {s.Score} pts ({s.TotalTime:F2}s)"));

        yield return _waitForSeconds10;
        GameManager.Instance.EndGame();
    }

    public void StartRound()
    {
        numberLabel.text = "";
        numPad.RemoveFromClassList("hide");
        resultsView.AddToClassList("hide");
        readyButton.SetEnabled(true);
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
        Debug.Log($"Ready count updated: {ready}/{total}");
    }
}
