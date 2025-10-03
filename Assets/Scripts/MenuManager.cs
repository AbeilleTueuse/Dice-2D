using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UIElements;

public class MenuManager : MonoBehaviourPunCallbacks
{
    private const string PlayerNameKey = "PlayerName";
    private const int MaxPlayersPerRoom = 4;

    [SerializeField]
    private UIDocument _uiDocument;

    private VisualElement _root;

    // Views
    private VisualElement _initView;
    private VisualElement _mainView;
    private VisualElement _roomListView;
    private VisualElement _roomView;

    // UI Elements
    private TextField _nameField;
    private Button _createRoomButton;
    private Button _joinRoomButton;
    private ScrollView _roomListContainer;
    private Label _roomNameLabel;
    private ScrollView _playerList;
    private Button _startGameButton;
    private SliderInt _diceNumberSlider;
    private const string DiceNumberKey = "DiceNumber";

    private readonly Dictionary<Button, bool> _buttonLocks = new();

    private void Start()
    {
        if (PhotonNetwork.IsConnected)
            return;

        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.AutomaticallySyncScene = true;
    }

    public override void OnEnable()
    {
        base.OnEnable();
        CacheUIElements();
        RegisterButtonCallbacks();
        RestorePlayerNameIfExists();
        _createRoomButton.SetEnabled(false);
        _joinRoomButton.SetEnabled(false);
    }

    private void CacheUIElements()
    {
        _root = _uiDocument.rootVisualElement;

        _initView = _root.Q<VisualElement>("InitView");
        _mainView = _root.Q<VisualElement>("MainView");
        _roomListView = _root.Q<VisualElement>("RoomListView");
        _roomView = _root.Q<VisualElement>("RoomView");

        _nameField = _root.Q<TextField>("NameField");
        _createRoomButton = _root.Q<Button>("CreateRoomButton");
        _joinRoomButton = _root.Q<Button>("JoinRoomButton");
        _roomListContainer = _roomListView.Q<ScrollView>("RoomList");
        _roomNameLabel = _roomView.Q<Label>("RoomName");
        _playerList = _roomView.Q<ScrollView>("PlayerList");
        _startGameButton = _roomView.Q<Button>("StartGameButton");
        _diceNumberSlider = _roomView.Q<SliderInt>("DiceNumber");

        // init locks
        _buttonLocks[_createRoomButton] = false;
        _buttonLocks[_joinRoomButton] = false;
        _buttonLocks[_startGameButton] = false;
    }

    private void RegisterButtonCallbacks()
    {
        _root.Q<Button>("ConfirmNameButton").clicked += OnConfirmName;

        _createRoomButton.clicked += () => HandleSafeClick(_createRoomButton, CreateRoom);
        _joinRoomButton.clicked += () =>
            HandleSafeClick(_joinRoomButton, () => ShowView(_roomListView));
        _root.Q<Button>("LeaveRoomButton").clicked += () => PhotonNetwork.LeaveRoom();
        _root.Q<Button>("QuitGameButton").clicked += OnQuitButtonClicked;
        _root.Q<Button>("LeaveRoomListButton").clicked += () => ShowView(_mainView);
        _startGameButton.clicked += () => HandleSafeClick(_startGameButton, OnClickStartGame);
    }

    private void HandleSafeClick(Button button, System.Action action)
    {
        if (_buttonLocks.ContainsKey(button) && _buttonLocks[button])
            return;

        _buttonLocks[button] = true;
        button.SetEnabled(false);

        action?.Invoke();
    }

    private void RestorePlayerNameIfExists()
    {
        if (!PlayerPrefs.HasKey(PlayerNameKey))
            return;

        PhotonNetwork.NickName = PlayerPrefs.GetString(PlayerNameKey);
        ShowView(_mainView);
    }

    #region Photon Callbacks

    public override void OnConnectedToMaster()
    {
        PhotonNetwork.JoinLobby();
        _createRoomButton.SetEnabled(true);
        _joinRoomButton.SetEnabled(true);

        _buttonLocks[_createRoomButton] = false;
        _buttonLocks[_joinRoomButton] = false;
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        _roomListContainer.Clear();

        foreach (RoomInfo info in roomList)
        {
            if (!info.IsOpen || !info.IsVisible || info.RemovedFromList)
                continue;

            Button roomButton = new()
            {
                text = $"{info.Name} ({info.PlayerCount}/{info.MaxPlayers})",
            };

            roomButton.AddToClassList("room-button");

            // sécurisation aussi ici
            roomButton.clicked += () =>
                HandleSafeClick(roomButton, () => PhotonNetwork.JoinRoom(info.Name));

            _roomListContainer.Add(roomButton);
            _buttonLocks[roomButton] = false;
        }
    }

    private void UpdateRoomUI()
    {
        if (!PhotonNetwork.InRoom)
            return;

        _roomNameLabel.text = PhotonNetwork.CurrentRoom.Name;

        _playerList.Clear();
        foreach (Player p in PhotonNetwork.PlayerList)
        {
            Button playerButton = new() { text = p.NickName + (p.IsMasterClient ? " (hôte)" : "") };
            playerButton.AddToClassList("player-button");
            _playerList.Add(playerButton);
        }

        _startGameButton.SetEnabled(PhotonNetwork.IsMasterClient);
        _buttonLocks[_startGameButton] = false;

        _diceNumberSlider.SetEnabled(PhotonNetwork.IsMasterClient);

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(DiceNumberKey, out object value))
            _diceNumberSlider.SetValueWithoutNotify((int)value);
        else
            _diceNumberSlider.SetValueWithoutNotify(3);

        _diceNumberSlider.UnregisterValueChangedCallback(OnDiceNumberChanged);
        if (PhotonNetwork.IsMasterClient)
            _diceNumberSlider.RegisterValueChangedCallback(OnDiceNumberChanged);
    }

    public override void OnJoinedRoom()
    {
        ShowView(_roomView);
        UpdateRoomUI();
        Hashtable props = new() { { DiceNumberKey, _diceNumberSlider.value } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) => UpdateRoomUI();

    public override void OnPlayerLeftRoom(Player otherPlayer) => UpdateRoomUI();

    public override void OnLeftRoom() => ShowView(_mainView);

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        if (propertiesThatChanged.ContainsKey(DiceNumberKey) && _diceNumberSlider != null)
        {
            int newValue = (int)propertiesThatChanged[DiceNumberKey];
            _diceNumberSlider.SetValueWithoutNotify(newValue);
        }
    }

    #endregion

    #region Room Management

    private void CreateRoom()
    {
        string roomName = $"Partie de {PhotonNetwork.NickName}";
        RoomOptions options = new() { MaxPlayers = MaxPlayersPerRoom };
        PhotonNetwork.CreateRoom(roomName, options);
    }

    private void OnClickStartGame()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
            PhotonNetwork.CurrentRoom.RemovedFromList = true;
            PhotonNetwork.LoadLevel("Game");
        }
    }

    #endregion

    #region UI Logic

    private void ShowView(VisualElement targetView)
    {
        _initView.AddToClassList("hide");
        _mainView.AddToClassList("hide");
        _roomListView.AddToClassList("hide");
        _roomView.AddToClassList("hide");

        targetView.RemoveFromClassList("hide");

        if (targetView == _mainView)
        {
            _createRoomButton.SetEnabled(true);
            _joinRoomButton.SetEnabled(true);
            _buttonLocks[_createRoomButton] = false;
            _buttonLocks[_joinRoomButton] = false;
        }
    }

    private void OnConfirmName()
    {
        string name = _nameField.value.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Debug.LogWarning("Veuillez entrer un pseudo valide !");
            return;
        }

        PlayerPrefs.SetString(PlayerNameKey, name);
        PlayerPrefs.Save();

        PhotonNetwork.NickName = name;

        ShowView(_mainView);
    }

    private void OnQuitButtonClicked()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    #endregion

    #region DiceNumber Logic

    private void OnDiceNumberChanged(ChangeEvent<int> evt)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        Hashtable props = new() { { DiceNumberKey, evt.newValue } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    #endregion
}
