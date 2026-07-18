using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects TelephoneGameManager's events to your UI panels. Drag the relevant
/// references in the Inspector. See SETUP_GUIDE.md section on Telephone mode
/// for the exact scene hierarchy this expects.
/// </summary>
public class TelephoneUIManager : MonoBehaviour
{
    public static TelephoneUIManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject writePromptPanel;
    [SerializeField] private GameObject drawPanel;
    [SerializeField] private GameObject revealPanel;
    [SerializeField] private GameObject lobbyPanel; // shown before the game starts

    [Header("Write / Caption UI")]
    [SerializeField] private TMP_Text writePromptLabel;
    [SerializeField] private RawImage referenceImageDisplay; // shown only when captioning a drawing
    [SerializeField] private TMP_InputField textInputField;

    [Header("Draw UI")]
    [SerializeField] private TMP_Text drawPromptLabel;
    [SerializeField] private LocalDrawingCanvas localDrawingCanvas;

    [Header("Timer")]
    [SerializeField] private TMP_Text timerText;

    [Header("Reveal UI")]
    [SerializeField] private Transform bookListContainer;
    [SerializeField] private GameObject bookButtonPrefab;      // simple prefab: Button + child TMP_Text
    [SerializeField] private Transform revealEntryContainer;   // parent that holds the entries of the selected book, in order
    [SerializeField] private GameObject revealTextEntryPrefab; // prefab with a TMP_Text
    [SerializeField] private GameObject revealImageEntryPrefab; // prefab with a RawImage

    private bool _isCaptionMode;
    private readonly Dictionary<int, List<TelephoneGameManager.BookEntry>> _revealedBooks = new Dictionary<int, List<TelephoneGameManager.BookEntry>>();

    private void Awake()
    {
        Instance = this;
        writePromptPanel.SetActive(false);
        drawPanel.SetActive(false);
        revealPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);
    }

    private void Update()
    {
        if (TelephoneGameManager.Instance == null) return;

        if (timerText != null && !TelephoneGameManager.Instance.InRevealPhase.Value)
            timerText.text = Mathf.CeilToInt(TelephoneGameManager.Instance.RoundTimeRemaining.Value).ToString();

        if (TelephoneGameManager.Instance.GameStarted.Value && lobbyPanel != null && lobbyPanel.activeSelf)
            lobbyPanel.SetActive(false);

        if (TelephoneGameManager.Instance.InRevealPhase.Value && !revealPanel.activeSelf)
            ShowRevealPanel();
    }

    // ---------- Task display (called via ClientRpc from TelephoneGameManager) ----------

    public void ShowWritePrompt(bool isCaption, byte[] referenceImage)
    {
        drawPanel.SetActive(false);
        writePromptPanel.SetActive(true);
        _isCaptionMode = isCaption;
        textInputField.text = "";

        if (isCaption)
        {
            writePromptLabel.text = "What's happening in this drawing?";
            if (referenceImageDisplay != null)
            {
                referenceImageDisplay.gameObject.SetActive(true);
                var tex = new Texture2D(2, 2);
                tex.LoadImage(referenceImage);
                referenceImageDisplay.texture = tex;
            }
        }
        else
        {
            writePromptLabel.text = "Write something for the next player to draw!";
            if (referenceImageDisplay != null) referenceImageDisplay.gameObject.SetActive(false);
        }
    }

    public void ShowDrawTask(string referenceText)
    {
        writePromptPanel.SetActive(false);
        drawPanel.SetActive(true);
        drawPromptLabel.text = referenceText;
        localDrawingCanvas.ClearCanvas();
    }

    // ---------- Submit buttons (wire these in the Inspector) ----------

    /// <summary>Wire to the write/caption panel's Submit button.</summary>
    public void OnSubmitText()
    {
        string text = string.IsNullOrWhiteSpace(textInputField.text) ? "..." : textInputField.text;

        if (_isCaptionMode)
            TelephoneGameManager.Instance.SubmitCaptionServerRpc(text);
        else
            TelephoneGameManager.Instance.SubmitPromptServerRpc(text);

        writePromptPanel.SetActive(false);
    }

    /// <summary>Wire to the draw panel's Submit Drawing button.</summary>
    public void OnSubmitDrawing()
    {
        byte[] png = localDrawingCanvas.GetPngBytes();
        TelephoneGameManager.Instance.SubmitDrawingServerRpc(png);
        drawPanel.SetActive(false);
    }

    // ---------- Reveal ----------

    public void OnBookRevealed(int bookIndex, List<TelephoneGameManager.BookEntry> entries)
    {
        _revealedBooks[bookIndex] = entries;

        if (bookListContainer != null && bookButtonPrefab != null)
        {
            var go = Instantiate(bookButtonPrefab, bookListContainer);
            var button = go.GetComponent<Button>();
            var label = go.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = $"Book {bookIndex + 1}";
            int capturedIndex = bookIndex; // avoid closure-over-loop-variable bugs
            button.onClick.AddListener(() => DisplayBook(capturedIndex));
        }
    }

    private void ShowRevealPanel()
    {
        writePromptPanel.SetActive(false);
        drawPanel.SetActive(false);
        revealPanel.SetActive(true);
    }

    /// <summary>Wire to each book-select button (done automatically in OnBookRevealed above).</summary>
    public void DisplayBook(int bookIndex)
    {
        if (!_revealedBooks.TryGetValue(bookIndex, out var entries)) return;
        if (revealEntryContainer == null) return;

        foreach (Transform child in revealEntryContainer)
            Destroy(child.gameObject);

        foreach (var entry in entries)
        {
            if (entry.Type == TelephoneGameManager.EntryType.Drawing)
            {
                var go = Instantiate(revealImageEntryPrefab, revealEntryContainer);
                var img = go.GetComponentInChildren<RawImage>();
                if (img != null)
                {
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(entry.ImageData);
                    img.texture = tex;
                }
            }
            else
            {
                var go = Instantiate(revealTextEntryPrefab, revealEntryContainer);
                var text = go.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = entry.Text;
            }
        }
    }
}