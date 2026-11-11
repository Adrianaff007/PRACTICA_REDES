using Fusion;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class FusionConnector : MonoBehaviour
{
    public string LocalPlayerName
    {
        get => _localPlayerName;
        set => _localPlayerName = value?.Trim();
    }
    private string _localPlayerName;

    private string _localRoomName;
    public string LocalRoomName
    {
        get => _localRoomName;
        set => _localRoomName = value?.Trim().ToLowerInvariant();
    }

    [SerializeField, Tooltip("The network runner prefab that will be instantiated when looking starting the game.")]
    private NetworkRunner _networkRunnerPrefab;

    [Tooltip("The canvas group that handles interactivity for the game.")]
    public CanvasGroup canvasGroup;

    [Tooltip("The GameObject that contains the main menu.")]
    public GameObject mainMenuObject;

    [Tooltip("The Game Object that handles the game itself")]
    public GameObject mainGameObject;

    [Tooltip("GameObject that appears if there is a network error when trying to join a room.")]
    public GameObject errorMessageObject;

    [Tooltip("The GameObject that displays the button to start the game.")]
    public GameObject showGameButton;

    [Tooltip("Text object that displays the room name.")]
    public TextMeshProUGUI roomName;

    [Tooltip("Prefab for the trivia game itself.")]
    public NetworkObject triviaGamePrefab;

    public Transform playerContainer;

    [Tooltip("The message shown before starting the game.")]
    public TextMeshProUGUI preGameMessage;

    public static FusionConnector Instance { get; private set; }

    private void Awake()
    {
        Application.targetFrameRate = 60;

        if (Instance != null)
        {
            Destroy(gameObject);
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        Instance = null;
    }

    public async void StartGame(bool createRoom)
    {
        if (string.IsNullOrEmpty(LocalRoomName))
        {
            ShowError("Escribe un nombre de sala.");
            return;
        }

        canvasGroup.interactable = false;

        StartGameArgs startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = LocalRoomName,   // ya normalizado a minúsculas (rama 2)
            PlayerCount = 20,
        };

        NetworkRunner newRunner = Instantiate(_networkRunnerPrefab);
        StartGameResult result = await newRunner.StartGame(startGameArgs);

        if (!result.Ok)
        {
            roomName.text = string.Empty;
            GoToMainMenu();
            ShowError(result.ErrorMessage);
            Debug.LogError(result.ErrorMessage);
        }
        else
        {
            bool roomWasEmpty = newRunner.IsSharedModeMasterClient;

            if (createRoom && !roomWasEmpty)
            {
                LeaveWithError(newRunner, $"La sala \"{LocalRoomName}\" ya existe.");
            }
            else if (!createRoom && roomWasEmpty)
            {
                LeaveWithError(newRunner, $"La sala \"{LocalRoomName}\" no existe.");
            }
            else
            {
                roomName.text = "Room:  " + newRunner.SessionInfo.Name;
                GoToGame();
            }
        }

        canvasGroup.interactable = true;
    }

    public void GoToMainMenu()
    {
        mainMenuObject.SetActive(true);
        mainGameObject.SetActive(false);
    }

    public void GoToGame()
    {
        mainMenuObject.SetActive(false);
        mainGameObject.SetActive(true);
        
    }

    internal void OnPlayerJoin(NetworkRunner runner)
    {
        // Only set pregame messages if the game hasn't started.
        if (TriviaManager.TriviaManagerPresent)
        {
            return;
        }

        if (runner.IsSharedModeMasterClient == true)
        {
            SetPregameMessage("Game Is Ready To Start");
        }
        else
        {
            SetPregameMessage("Waiting for master client to start game.");
        }
    }
    public void SetPregameMessage(string message)
    {
        preGameMessage.text = message;
    }

    public void StartTriviaGame()
    {
        NetworkRunner runner = null;
        // If no runner has been assigned, we cannot start the game
        if (NetworkRunner.Instances.Count > 0)
        {
            runner = NetworkRunner.Instances[0];
        }

        if (runner == null)
        {
            Debug.Log("No runner found.");
            return;
        }



        // If no trivia manager has been made and we are the master mode client.
        // Redundant but being safe.
        if (runner.IsSharedModeMasterClient && !TriviaManager.TriviaManagerPresent)
        {
            runner.Spawn(triviaGamePrefab);
            showGameButton.SetActive(false);
        }
    }
    public void ShowError(string message)
    {
        errorMessageObject.SetActive(true);
        TextMeshProUGUI gui = errorMessageObject.GetComponentInChildren<TextMeshProUGUI>();
        if (gui) gui.text = message;
    }

    public async void LeaveWithError(NetworkRunner runner, string message)
    {
        if (runner != null)
            await runner.Shutdown(true, ShutdownReason.Ok);

        roomName.text = string.Empty;
        SetPregameMessage(string.Empty);
        showGameButton.SetActive(false);
        GoToMainMenu();
        ShowError(message);
    }
}
