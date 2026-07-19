using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Connects TelephoneGameManager's events to your UI panels. Drag the relevant
/// references in the Inspector. See SETUP_GUIDE.md for the exact scene hierarchy
/// this expects, and for how the optional fields below configure each
/// Gartic-Phone-style mode (Knock-Off, Animation, Exquisite Corpse, etc).
/// </summary>
public class TelephoneUIManager : MonoBehaviour
{
    public static TelephoneUIManager Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject writePromptPanel;
    [SerializeField] private GameObject drawPanel;
    [SerializeField] private GameObject revealPanel;
    [SerializeField] private GameObject lobbyPanel;

    [Header("Write / Caption UI")]
    [SerializeField] private TMP_Text writePromptLabel;
    [SerializeField] private RawImage referenceImageDisplay; // shown only when captioning a drawing
    [SerializeField] private TMP_InputField textInputField;

    [Header("Draw UI")]
    [SerializeField] private TMP_Text drawPromptLabel;
    [SerializeField] private LocalDrawingCanvas localDrawingCanvas;

    [Header("Timer")]
    [SerializeField] private TMP_Text timerText;

    [Header("Reveal UI (default: static list)")]
    [SerializeField] private Transform bookListContainer;
    [SerializeField] private GameObject bookButtonPrefab;
    [SerializeField] private Transform revealEntryContainer;
    [SerializeField] private GameObject revealTextEntryPrefab;
    [SerializeField] private GameObject revealImageEntryPrefab;

    [Header("Reveal UI - Animation / Complement playback (optional)")]
    [Tooltip("Enable for Animation or Complement mode scenes: plays the book's drawings back as a flipbook instead of a static list.")]
    [SerializeField] private bool useAnimationPlayback = false;
    [SerializeField] private RawImage animationPlayerImage;
    [SerializeField] private float animationFrameSeconds = 0.4f;

    [Header("Reveal UI - Exquisite Corpse stitching (optional)")]
    [Tooltip("Enable for Exquisite Corpse mode scenes: stitches the cropped pieces into one tall composite instead of a static list.")]
    [SerializeField] private bool stitchExquisiteCorpse = false;
    [Tooltip("Must match the TelephoneGameManager's Exquisite Corpse Strip Fraction in this same scene.")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float exquisiteCorpseStripFraction = 0.15f;
    [SerializeField] private RawImage stitchedResultImage;

    [Header("Knock-Off mode (optional)")]
    [Tooltip("Seconds before the reference text/image auto-hides, forcing players to work from memory. 0 = never hide.")]
    [SerializeField] private float referencePreviewSeconds = 0f;

    private bool _isCaptionMode;
    private Coroutine _hideReferenceCoroutine;
    private Coroutine _animationCoroutine;
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

    public void ShowWritePrompt(bool isCaption, byte[] referenceImage, string overrideLabel = null)
    {
        drawPanel.SetActive(false);
        writePromptPanel.SetActive(true);
        _isCaptionMode = isCaption;
        textInputField.text = "";

        if (_hideReferenceCoroutine != null) StopCoroutine(_hideReferenceCoroutine);

        if (overrideLabel != null)
        {
            writePromptLabel.text = overrideLabel;
            if (referenceImageDisplay != null) referenceImageDisplay.gameObject.SetActive(false);
            return;
        }

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
            if (referencePreviewSeconds > 0f)
                _hideReferenceCoroutine = StartCoroutine(HideImageAfterDelay());
        }
        else
        {
            writePromptLabel.text = "Write something for the next player to draw!";
            if (referenceImageDisplay != null) referenceImageDisplay.gameObject.SetActive(false);
        }
    }

    public void ShowDrawTask(string referenceText, byte[] startingCanvasImage)
    {
        writePromptPanel.SetActive(false);
        drawPanel.SetActive(true);
        drawPromptLabel.text = referenceText;

        if (startingCanvasImage != null)
            localDrawingCanvas.LoadFromPng(startingCanvasImage);
        else
            localDrawingCanvas.ClearCanvas();

        if (_hideReferenceCoroutine != null) StopCoroutine(_hideReferenceCoroutine);
        if (referencePreviewSeconds > 0f && !string.IsNullOrEmpty(referenceText))
            _hideReferenceCoroutine = StartCoroutine(HideDrawPromptAfterDelay());
    }

    private IEnumerator HideDrawPromptAfterDelay()
    {
        yield return new WaitForSeconds(referencePreviewSeconds);
        drawPromptLabel.text = "(draw from memory!)";
    }

    private IEnumerator HideImageAfterDelay()
    {
        yield return new WaitForSeconds(referencePreviewSeconds);
        if (referenceImageDisplay != null) referenceImageDisplay.gameObject.SetActive(false);
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
            int capturedIndex = bookIndex;
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

        if (useAnimationPlayback && animationPlayerImage != null)
        {
            StartAnimationPlayback(entries);
            return;
        }

        if (stitchExquisiteCorpse && stitchedResultImage != null)
        {
            DisplayStitchedCorpse(entries);
            return;
        }

        DisplayStaticList(entries);
    }

    private void DisplayStaticList(List<TelephoneGameManager.BookEntry> entries)
    {
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

    // ---------- Animation / Complement flipbook playback ----------

    private void StartAnimationPlayback(List<TelephoneGameManager.BookEntry> entries)
    {
        if (_animationCoroutine != null) StopCoroutine(_animationCoroutine);
        var frames = entries.FindAll(e => e.Type == TelephoneGameManager.EntryType.Drawing);
        _animationCoroutine = StartCoroutine(PlayFrames(frames));
    }

    private IEnumerator PlayFrames(List<TelephoneGameManager.BookEntry> frames)
    {
        if (frames.Count == 0) yield break;
        int i = 0;
        while (true)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(frames[i].ImageData);
            animationPlayerImage.texture = tex;
            i = (i + 1) % frames.Count;
            yield return new WaitForSeconds(animationFrameSeconds);
        }
    }

    // ---------- Exquisite Corpse stitching ----------

    private void DisplayStitchedCorpse(List<TelephoneGameManager.BookEntry> entries)
    {
        var drawings = entries.FindAll(e => e.Type == TelephoneGameManager.EntryType.Drawing);
        if (drawings.Count == 0) return;

        var first = new Texture2D(2, 2);
        first.LoadImage(drawings[0].ImageData);
        int w = first.width;
        int stripHeight = Mathf.Clamp(Mathf.RoundToInt(first.height * exquisiteCorpseStripFraction), 1, first.height);

        var pieces = new List<Color[]> { first.GetPixels() };
        var pieceHeights = new List<int> { first.height };
        int totalHeight = first.height;

        for (int i = 1; i < drawings.Count; i++)
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(drawings[i].ImageData);
            int newContentHeight = tex.height - stripHeight;
            if (newContentHeight <= 0) continue;

            Color[] slice = tex.GetPixels(0, 0, w, newContentHeight);
            pieces.Add(slice);
            pieceHeights.Add(newContentHeight);
            totalHeight += newContentHeight;
        }

        var composite = new Texture2D(w, totalHeight, TextureFormat.RGBA32, false);
        int yOffset = totalHeight;
        for (int i = 0; i < pieces.Count; i++)
        {
            yOffset -= pieceHeights[i];
            composite.SetPixels(0, yOffset, w, pieceHeights[i], pieces[i]);
        }
        composite.Apply();

        stitchedResultImage.texture = composite;
    }
}