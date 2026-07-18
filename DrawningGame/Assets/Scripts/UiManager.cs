using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects GameManager/ChatGuessManager events to your UI elements.
/// Drag the relevant UI references in the Inspector.
/// This is a plain MonoBehaviour (not networked) — it just reads
/// NetworkVariables and reacts to ClientRpc calls forwarded to it.
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Word / Timer")]
    [SerializeField] private TMP_Text wordDisplayText;
    [SerializeField] private TMP_Text timerText;

    [Header("Chat")]
    [SerializeField] private TMP_Text chatLogText;
    [SerializeField] private TMP_InputField chatInputField;
    [SerializeField] private ScrollRect chatScrollRect;

    [Header("Word Choice Panel")]
    [SerializeField] private GameObject wordChoicePanel;
    [SerializeField] private Button[] wordChoiceButtons; // size 3

    [Header("Scoreboard")]
    [SerializeField] private Transform scoreboardContainer;
    [SerializeField] private GameObject scoreboardRowPrefab; // simple prefab with a TMP_Text

    private void Awake()
    {
        Instance = this;
        wordChoicePanel.SetActive(false);
    }

    private void Update()
    {
        if (GameManager.Instance == null) return;

        // Timer
        if (timerText != null)
            timerText.text = Mathf.CeilToInt(GameManager.Instance.TimeRemaining.Value).ToString();

        // Masked word for non-drawers (updates automatically via NetworkVariable)
        if (wordDisplayText != null && GameManager.Instance.CurrentDrawerClientId.Value != NetworkManager.Singleton.LocalClientId)
            wordDisplayText.text = GameManager.Instance.MaskedWord.Value.ToString();

        RefreshScoreboard();
    }

    // ---------- Chat ----------

    public void OnChatInputSubmit()
    {
        if (chatInputField == null || string.IsNullOrWhiteSpace(chatInputField.text)) return;
        ChatGuessManager.Instance.SubmitGuess(chatInputField.text);
        chatInputField.text = "";
        chatInputField.ActivateInputField();
    }

    public void AppendChatMessage(string senderName, string message, bool isSystem)
    {
        if (chatLogText == null) return;

        if (isSystem)
            chatLogText.text += $"\n<color=#4CAF50><i>{message}</i></color>";
        else
            chatLogText.text += $"\n<b>{senderName}:</b> {message}";

        // Snap chat scroll to bottom next frame
        Canvas.ForceUpdateCanvases();
        if (chatScrollRect != null) chatScrollRect.verticalNormalizedPosition = 0f;
    }

    // ---------- Word display ----------

    public void SetLocalWordDisplay(string word, bool isDrawer)
    {
        if (wordDisplayText == null) return;
        wordDisplayText.text = isDrawer ? $"Draw: {word}" : word;
    }

    // ---------- Word choice panel (shown only to the drawer) ----------

    public void ShowWordChoicePanel(string[] choices)
    {
        wordChoicePanel.SetActive(true);
        for (int i = 0; i < wordChoiceButtons.Length; i++)
        {
            if (i >= choices.Length) { wordChoiceButtons[i].gameObject.SetActive(false); continue; }

            string word = choices[i]; // local copy for closure
            wordChoiceButtons[i].gameObject.SetActive(true);
            wordChoiceButtons[i].GetComponentInChildren<TMP_Text>().text = word;
            wordChoiceButtons[i].onClick.RemoveAllListeners();
            wordChoiceButtons[i].onClick.AddListener(() => OnWordChosen(word));
        }
    }

    private void OnWordChosen(string word)
    {
        wordChoicePanel.SetActive(false);
        GameManager.Instance.SubmitWordChoiceServerRpc(word);
    }

    // ---------- Scoreboard ----------

    private void RefreshScoreboard()
    {
        if (scoreboardContainer == null || scoreboardRowPrefab == null) return;

        // Simple full-rebuild approach; fine for small player counts (skribbl-style lobbies)
        foreach (Transform child in scoreboardContainer)
            Destroy(child.gameObject);

        var rows = new List<(string name, int score)>();
        foreach (var kvp in NetworkManager.Singleton.ConnectedClients)
        {
            var pc = kvp.Value.PlayerObject?.GetComponent<PlayerController>();
            if (pc == null) continue;
            rows.Add((pc.PlayerName.Value.ToString(), pc.Score.Value));
        }
        rows.Sort((a, b) => b.score.CompareTo(a.score));

        foreach (var row in rows)
        {
            var go = Instantiate(scoreboardRowPrefab, scoreboardContainer);
            var text = go.GetComponentInChildren<TMP_Text>();
            if (text != null) text.text = $"{row.name} — {row.score}";
        }
    }
}