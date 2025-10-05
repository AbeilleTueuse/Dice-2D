using Photon.Pun;
using UnityEngine;

public class GameManager : MonoBehaviourPunCallbacks
{
    public static GameManager Instance { get; private set; }

    public RoundManager Rounds { get; private set; }
    public UIManager UI { get; private set; }
    public NetworkManager Net { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        Rounds = GetComponent<RoundManager>();
        UI = GetComponent<UIManager>();
        Net = GetComponent<NetworkManager>();
    }

    private void Start() => StartGame();

    public void StartGame()
    {
        if (Rounds.CurrentRound >= Rounds.MaxRounds)
        {
            Debug.Log("Tous les rounds sont terminés !");
            // StartCoroutine(UI.ShowFinalResultsCoroutine(Net.GlobalStats));
            return;
        }

        Rounds.StartRoundRoutine();
    }

    public void EndGame()
    {
        PhotonNetwork.LeaveRoom();
    }

    public override void OnLeftRoom()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }
}
