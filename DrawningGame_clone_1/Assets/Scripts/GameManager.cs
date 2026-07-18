using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central authority for round flow: picking the drawer, picking the word,
/// running the timer, and ending rounds. Lives on a NetworkObject that is
/// spawned once by the server (e.g. attach to your NetworkManager GameObject
/// or a dedicated "GameManager" prefab spawned at server start).
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Round Settings")]
    [SerializeField] private float roundDuration = 80f;
    [SerializeField] private float wordChoiceDuration = 12f;
    [SerializeField] private int pointsForCorrectGuessBase = 50;
    [SerializeField] private int pointsForDrawerPerGuesser = 25;

    // Networked state everyone can read
    public NetworkVariable<ulong> CurrentDrawerClientId = new NetworkVariable<ulong>(ulong.MaxValue);
    public NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(0f);
    public NetworkVariable<FixedString64Bytes> MaskedWord = new NetworkVariable<FixedString64Bytes>(""); // e.g. "_ _ _ _"
    public NetworkVariable<bool> RoundActive = new NetworkVariable<bool>(false);

    private string _currentWord = ""; // server-only, real word
    private List<ulong> _turnOrder = new List<ulong>();
    private int _turnIndex = -1;
    private int _correctGuessersThisRound = 0;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    // ---------- Player registration (called by PlayerController) ----------

    public void RegisterPlayer(ulong clientId)
    {
        if (!_turnOrder.Contains(clientId))
            _turnOrder.Add(clientId);

        // First player to join kicks things off once there are 2+ players
        if (!RoundActive.Value && _turnOrder.Count >= 2)
        {
            StartNextRound();
        }
    }

    public void UnregisterPlayer(ulong clientId)
    {
        _turnOrder.Remove(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        UnregisterPlayer(clientId);
        if (clientId == CurrentDrawerClientId.Value && RoundActive.Value)
        {
            EndRound();
        }
    }

    // ---------- Round flow (server only) ----------

    private void StartNextRound()
    {
        if (_turnOrder.Count == 0) return;

        _turnIndex = (_turnIndex + 1) % _turnOrder.Count;
        CurrentDrawerClientId.Value = _turnOrder[_turnIndex];
        _correctGuessersThisRound = 0;

        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            var player = kvp.Value.PlayerObject?.GetComponent<PlayerController>();
            player?.ResetRoundState();
        }

        // Clear the shared canvas for everyone
        DrawingCanvas.Instance?.RequestClearServerRpc();

        StartCoroutine(WordChoicePhase());
    }

    private IEnumerator WordChoicePhase()
    {
        RoundActive.Value = false;
        List<string> choices = WordBank.GetWordChoices(3);

        // Send choices privately to the drawer only.
        // RPCs can't serialize string[] directly, so join into one delimited string.
        var clientParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { CurrentDrawerClientId.Value } }
        };
        ShowWordChoicesClientRpc(string.Join("|", choices), clientParams);

        // Default pick if the drawer doesn't choose in time
        _pendingWordChoice = choices[0];
        float t = 0f;
        while (t < wordChoiceDuration && string.IsNullOrEmpty(_confirmedWord))
        {
            t += Time.deltaTime;
            yield return null;
        }

        _currentWord = string.IsNullOrEmpty(_confirmedWord) ? _pendingWordChoice : _confirmedWord;
        _confirmedWord = null;

        BeginRound();
    }

    private string _pendingWordChoice;
    private string _confirmedWord;

    /// <summary>Called via ServerRpc from the drawer's client when they pick a word.</summary>
    public void ConfirmWordChoice(string word)
    {
        _confirmedWord = word;
    }

    private void BeginRound()
    {
        RoundActive.Value = true;
        TimeRemaining.Value = roundDuration;
        MaskedWord.Value = BuildMask(_currentWord, revealAll: false);

        // Tell only the drawer the real word
        var clientParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { CurrentDrawerClientId.Value } }
        };
        RevealWordToDrawerClientRpc(_currentWord, clientParams);

        StartCoroutine(RoundTimer());
    }

    private IEnumerator RoundTimer()
    {
        while (TimeRemaining.Value > 0 && RoundActive.Value)
        {
            TimeRemaining.Value -= Time.deltaTime;

            // Stop early if everyone (except the drawer) has guessed
            if (_correctGuessersThisRound >= _turnOrder.Count - 1)
                break;

            yield return null;
        }
        EndRound();
    }

    private void EndRound()
    {
        if (!RoundActive.Value) return;
        RoundActive.Value = false;
        MaskedWord.Value = BuildMask(_currentWord, revealAll: true);
        RevealFullWordClientRpc(_currentWord);

        StartCoroutine(DelayThenNextRound());
    }

    private IEnumerator DelayThenNextRound()
    {
        yield return new WaitForSeconds(5f);
        StartNextRound();
    }

    // ---------- Called by ChatGuessManager when someone guesses correctly ----------

    public void OnCorrectGuess(ulong guesserClientId)
    {
        _correctGuessersThisRound++;

        // Score scales with time remaining — faster guesses are worth more
        int points = Mathf.RoundToInt(pointsForCorrectGuessBase * (TimeRemaining.Value / roundDuration) + 10);

        var guesser = NetworkManager.Singleton.ConnectedClients[guesserClientId].PlayerObject.GetComponent<PlayerController>();
        guesser.AddScore(points);
        guesser.HasGuessedCorrectly.Value = true;

        var drawer = NetworkManager.Singleton.ConnectedClients[CurrentDrawerClientId.Value].PlayerObject.GetComponent<PlayerController>();
        drawer.AddScore(pointsForDrawerPerGuesser);
    }

    public string GetCurrentWordForValidation() => _currentWord;

    // ---------- Word masking ----------

    private string BuildMask(string word, bool revealAll)
    {
        if (string.IsNullOrEmpty(word)) return "";
        if (revealAll) return word;

        var chars = new char[word.Length];
        for (int i = 0; i < word.Length; i++)
            chars[i] = word[i] == ' ' ? ' ' : '_';
        return string.Join(" ", new string(chars).ToCharArray());
    }

    // ---------- Client RPCs ----------

    [ClientRpc]
    private void ShowWordChoicesClientRpc(string choicesJoined, ClientRpcParams rpcParams = default)
    {
        string[] choices = choicesJoined.Split('|');
        UIManager.Instance?.ShowWordChoicePanel(choices);
    }

    [ClientRpc]
    private void RevealWordToDrawerClientRpc(string word, ClientRpcParams rpcParams = default)
    {
        UIManager.Instance?.SetLocalWordDisplay(word, isDrawer: true);
    }

    [ClientRpc]
    private void RevealFullWordClientRpc(string word)
    {
        UIManager.Instance?.SetLocalWordDisplay(word, isDrawer: false);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubmitWordChoiceServerRpc(string word, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != CurrentDrawerClientId.Value) return;
        ConfirmWordChoice(word);
    }
}