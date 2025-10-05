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

    // clés pour sauvegarde PlayerPrefs
    private const string DiceNumberPrefKey = "Pref_DiceNumber";
    private const string RoundNumberPrefKey = "Pref_RoundNumber";

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

    // Gestion des sliders de paramètres
    private readonly Dictionary<string, SliderInt> _roomParameters = new();

    // Constantes de clés pour les paramètres Photon
    private const string DiceNumberKey = "DiceNumber";
    private const string RoundNumberKey = "RoundNumber"; // déjà dans ton UI

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
        RestoreSlidersDefaults();
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

        _roomParameters.Clear();
        _roomParameters[DiceNumberKey] = _roomView.Q<SliderInt>("DiceNumber");
        _roomParameters[RoundNumberKey] = _roomView.Q<SliderInt>("RoundNumber");

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

    private void RestoreSlidersDefaults()
    {
        // Restaure depuis PlayerPrefs si existants
        if (_roomParameters.TryGetValue(DiceNumberKey, out var diceSlider))
        {
            int saved = PlayerPrefs.GetInt(DiceNumberPrefKey, diceSlider.lowValue);
            diceSlider.SetValueWithoutNotify(saved);
        }

        if (_roomParameters.TryGetValue(RoundNumberKey, out var roundSlider))
        {
            int saved = PlayerPrefs.GetInt(RoundNumberPrefKey, roundSlider.lowValue);
            roundSlider.SetValueWithoutNotify(saved);
        }
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

        foreach (var kvp in _roomParameters)
        {
            string key = kvp.Key;
            SliderInt slider = kvp.Value;

            slider.SetEnabled(PhotonNetwork.IsMasterClient);

            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out object value))
                slider.SetValueWithoutNotify((int)value);

            slider.UnregisterValueChangedCallback(evt => OnRoomParameterChanged(key, evt.newValue));

            if (PhotonNetwork.IsMasterClient)
                slider.RegisterValueChangedCallback(evt =>
                    OnRoomParameterChanged(key, evt.newValue)
                );
        }
    }

    public override void OnJoinedRoom()
    {
        ShowView(_roomView);
        UpdateRoomUI();

        // initialise room props avec valeurs actuelles des sliders
        Hashtable props = new();
        foreach (var kvp in _roomParameters)
            props[kvp.Key] = kvp.Value.value;

        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer) => UpdateRoomUI();

    public override void OnPlayerLeftRoom(Player otherPlayer) => UpdateRoomUI();

    public override void OnLeftRoom() => ShowView(_mainView);

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        base.OnRoomPropertiesUpdate(propertiesThatChanged);

        foreach (var kvp in _roomParameters)
        {
            string key = kvp.Key;
            if (propertiesThatChanged.ContainsKey(key))
            {
                int newValue = (int)propertiesThatChanged[key];
                kvp.Value.SetValueWithoutNotify(newValue);

                // Sauvegarde dans PlayerPrefs quand un param change
                if (key == DiceNumberKey)
                    PlayerPrefs.SetInt(DiceNumberPrefKey, newValue);
                else if (key == RoundNumberKey)
                    PlayerPrefs.SetInt(RoundNumberPrefKey, newValue);

                PlayerPrefs.Save();
            }
        }
    }

    private void OnRoomParameterChanged(string key, int newValue)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        Hashtable props = new() { { key, newValue } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
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
}
