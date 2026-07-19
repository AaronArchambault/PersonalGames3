using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative chain game manager covering the "Telephone" family of
/// Gartic-Phone-style modes. A single FlowMode setting (configured per scene)
/// determines the round structure, what gets forwarded between players, and
/// what the reveal looks like:
///
///  - Classic:         write -> draw -> caption -> draw -> caption ... (default)
///  - KnockOff:        same as Classic, but the timer shrinks each round and
///                      the UI hides the reference after a short preview
///                      (hiding is handled in TelephoneUIManager, not here).
///  - Animation:        every round is a drawing round; each canvas preloads
///                      the previous full frame to trace/tweak. Reveal plays
///                      the frames back like a flipbook.
///  - ExquisiteCorpse:  every round is a drawing round; each player only sees
///                      a cropped strip from the bottom of the previous image.
///  - Complement:       every round is a drawing round, drawing continues on
///                      the SAME evolving image (not a fresh trace).
///  - Story:            every round is a writing round; each player only sees
///                      the immediately preceding sentence.
///  - MissingPiece:     every round is a drawing round; the server erases a
///                      random region from the previous image before forwarding it.
/// </summary>
public class TelephoneGameManager : NetworkBehaviour
{
    public enum EntryType : byte { Prompt = 0, Drawing = 1, Caption = 2 }

    public enum FlowMode { Classic, KnockOff, Animation, ExquisiteCorpse, Complement, Story, MissingPiece }

    public class BookEntry
    {
        public EntryType Type;
        public string Text;
        public byte[] ImageData;
        public ulong AuthorClientId;
    }

    public static TelephoneGameManager Instance { get; private set; }

    [Header("Mode")]
    [SerializeField] private FlowMode flowMode = FlowMode.Classic;
    [Tooltip("Lets a mode run more rounds than there are players (e.g. more animation frames).")]
    [SerializeField] private int roundMultiplier = 1;

    [Header("Timing")]
    [SerializeField] private float writingRoundDuration = 45f;
    [SerializeField] private float drawingRoundDuration = 75f;
    [Tooltip("KnockOff only: seconds subtracted from the round duration each round.")]
    [SerializeField] private float knockOffTimeDecrement = 8f;
    [SerializeField] private float knockOffMinDuration = 15f;

    [Header("Exquisite Corpse")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float exquisiteCorpseStripFraction = 0.15f;

    [Header("Missing Piece")]
    [Range(0.1f, 0.5f)]
    [SerializeField] private float missingPieceEraseFraction = 0.2f;

    public NetworkVariable<int> CurrentRound = new NetworkVariable<int>(0);
    public NetworkVariable<int> TotalRounds = new NetworkVariable<int>(0);
    public NetworkVariable<float> RoundTimeRemaining = new NetworkVariable<float>(0f);
    public NetworkVariable<bool> InRevealPhase = new NetworkVariable<bool>(false);
    public NetworkVariable<bool> GameStarted = new NetworkVariable<bool>(false);

    private List<ulong> _playerOrder = new List<ulong>();
    private List<List<BookEntry>> _books = new List<List<BookEntry>>();
    private readonly HashSet<ulong> _submittedThisRound = new HashSet<ulong>();
    private Coroutine _roundCoroutine;

    private void Awake() => Instance = this;

    /// <summary>Wire to a "Start Game" button — only does anything for the host/server.</summary>
    public void HostStartGame()
    {
        if (!IsServer) return;
        BeginGameServer();
    }

    private void BeginGameServer()
    {
        _playerOrder = NetworkManager.Singleton.ConnectedClientsIds.ToList();
        int n = _playerOrder.Count;
        if (n < 2)
        {
            Debug.LogWarning("Telephone mode needs at least 2 players.");
            return;
        }

        _books = new List<List<BookEntry>>();
        for (int i = 0; i < n; i++) _books.Add(new List<BookEntry>());

        TotalRounds.Value = n * Mathf.Max(1, roundMultiplier);
        CurrentRound.Value = 0;
        InRevealPhase.Value = false;
        GameStarted.Value = true;

        BeginRound();
    }

    // ---------- Round content rules per mode ----------

    private bool IsDrawingRound(int r)
    {
        switch (flowMode)
        {
            case FlowMode.Story: return false;
            case FlowMode.Animation:
            case FlowMode.ExquisiteCorpse:
            case FlowMode.Complement:
            case FlowMode.MissingPiece:
                return true;
            default: // Classic, KnockOff
                return r % 2 == 1;
        }
    }

    private float GetRoundDuration(int r, bool isDrawingRound)
    {
        float baseDuration = isDrawingRound ? drawingRoundDuration : writingRoundDuration;
        if (flowMode == FlowMode.KnockOff)
            return Mathf.Max(knockOffMinDuration, baseDuration - r * knockOffTimeDecrement);
        return baseDuration;
    }

    /// <summary>What image (if any) should preload the next drawer's canvas.</summary>
    private byte[] ComputeStartingCanvas(byte[] previousImage)
    {
        if (previousImage == null) return null;
        switch (flowMode)
        {
            case FlowMode.Animation:
            case FlowMode.Complement:
                return previousImage; // trace/continue on the whole image
            case FlowMode.ExquisiteCorpse:
                return CropBottomStripOntoBlankCanvas(previousImage, exquisiteCorpseStripFraction);
            case FlowMode.MissingPiece:
                return EraseRandomRegion(previousImage, missingPieceEraseFraction);
            default:
                return null; // Classic/KnockOff/Story never preload an image
        }
    }

    // ---------- Round flow ----------

    private void BeginRound()
    {
        _submittedThisRound.Clear();
        int n = _playerOrder.Count;
        int r = CurrentRound.Value;
        bool isDrawingRound = IsDrawingRound(r);

        RoundTimeRemaining.Value = GetRoundDuration(r, isDrawingRound);

        for (int p = 0; p < n; p++)
        {
            ulong clientId = _playerOrder[p];
            int bookIndex = ((p - r) % n + n) % n;
            BookEntry reference = r == 0 ? null : _books[bookIndex][r - 1];

            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };

            if (isDrawingRound)
            {
                string refText = (reference != null && reference.Type != EntryType.Drawing) ? reference.Text : "";
                byte[] startingCanvas = reference != null ? ComputeStartingCanvas(reference.ImageData) : null;
                AssignDrawClientRpc(refText, startingCanvas, clientParams);
            }
            else if (reference == null)
            {
                AssignWritePromptClientRpc(clientParams);
            }
            else if (reference.Type == EntryType.Drawing)
            {
                AssignCaptionClientRpc(reference.ImageData, clientParams);
            }
            else
            {
                // Story mode: writing round whose reference is TEXT, not an image.
                AssignWriteNextSentenceClientRpc(reference.Text, clientParams);
            }
        }

        if (_roundCoroutine != null) StopCoroutine(_roundCoroutine);
        _roundCoroutine = StartCoroutine(RoundTimer());
    }

    private IEnumerator RoundTimer()
    {
        while (RoundTimeRemaining.Value > 0f && _submittedThisRound.Count < _playerOrder.Count)
        {
            RoundTimeRemaining.Value -= Time.deltaTime;
            yield return null;
        }
        FillMissingSubmissions();
        AdvanceRound();
    }

    private void FillMissingSubmissions()
    {
        int n = _playerOrder.Count;
        int r = CurrentRound.Value;
        bool isDrawingRound = IsDrawingRound(r);

        for (int p = 0; p < n; p++)
        {
            ulong clientId = _playerOrder[p];
            if (_submittedThisRound.Contains(clientId)) continue;

            int bookIndex = ((p - r) % n + n) % n;
            var entry = new BookEntry
            {
                AuthorClientId = clientId,
                Type = isDrawingRound ? EntryType.Drawing : (r == 0 ? EntryType.Prompt : EntryType.Caption),
                Text = isDrawingRound ? "" : "(no answer)",
                ImageData = isDrawingRound ? CreateBlankImage() : null
            };
            _books[bookIndex].Add(entry);
        }
    }

    private static byte[] CreateBlankImage()
    {
        var tex = new Texture2D(4, 4);
        var pixels = new Color[16];
        for (int i = 0; i < 16; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();
        return tex.EncodeToPNG();
    }

    private void AdvanceRound()
    {
        CurrentRound.Value++;
        if (CurrentRound.Value >= TotalRounds.Value)
            EnterRevealPhase();
        else
            BeginRound();
    }

    private void EnterRevealPhase()
    {
        InRevealPhase.Value = true;
        for (int b = 0; b < _books.Count; b++)
            RevealBookClientRpc(b, SerializeBook(_books[b]));
    }

    // ---------- Submissions (called from clients) ----------

    [ServerRpc(RequireOwnership = false)]
    public void SubmitPromptServerRpc(string text, ServerRpcParams rpcParams = default)
        => RecordSubmission(rpcParams.Receive.SenderClientId, EntryType.Prompt, text, null);

    [ServerRpc(RequireOwnership = false)]
    public void SubmitCaptionServerRpc(string text, ServerRpcParams rpcParams = default)
        => RecordSubmission(rpcParams.Receive.SenderClientId, EntryType.Caption, text, null);

    [ServerRpc(RequireOwnership = false)]
    public void SubmitDrawingServerRpc(byte[] pngData, ServerRpcParams rpcParams = default)
        => RecordSubmission(rpcParams.Receive.SenderClientId, EntryType.Drawing, "", pngData);

    private void RecordSubmission(ulong clientId, EntryType type, string text, byte[] image)
    {
        if (_submittedThisRound.Contains(clientId)) return;

        int p = _playerOrder.IndexOf(clientId);
        if (p < 0) return;

        int r = CurrentRound.Value;
        int n = _playerOrder.Count;
        int bookIndex = ((p - r) % n + n) % n;

        _books[bookIndex].Add(new BookEntry { Type = type, Text = text, ImageData = image, AuthorClientId = clientId });
        _submittedThisRound.Add(clientId);

        if (_submittedThisRound.Count >= n)
        {
            if (_roundCoroutine != null) StopCoroutine(_roundCoroutine);
            AdvanceRound();
        }
    }

    // ---------- Image manipulation helpers ----------

    private static byte[] CropBottomStripOntoBlankCanvas(byte[] previousPng, float stripFraction)
    {
        var src = new Texture2D(2, 2);
        src.LoadImage(previousPng);
        int w = src.width, h = src.height;
        int stripHeight = Mathf.Clamp(Mathf.RoundToInt(h * stripFraction), 1, h);

        var dest = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var blank = new Color[w * h];
        for (int i = 0; i < blank.Length; i++) blank[i] = Color.white;
        dest.SetPixels(blank);

        // Row 0 = visual bottom in Unity's texture space. Take the bottom strip
        // of the source and paste it at the TOP of the new (mostly blank) canvas.
        Color[] strip = src.GetPixels(0, 0, w, stripHeight);
        dest.SetPixels(0, h - stripHeight, w, stripHeight, strip);
        dest.Apply();
        return dest.EncodeToPNG();
    }

    private static byte[] EraseRandomRegion(byte[] previousPng, float eraseFraction)
    {
        var tex = new Texture2D(2, 2);
        tex.LoadImage(previousPng);
        int w = tex.width, h = tex.height;

        int regionW = Mathf.Clamp(Mathf.RoundToInt(w * Mathf.Sqrt(eraseFraction)), 1, w);
        int regionH = Mathf.Clamp(Mathf.RoundToInt(h * Mathf.Sqrt(eraseFraction)), 1, h);
        int x = Random.Range(0, Mathf.Max(1, w - regionW));
        int y = Random.Range(0, Mathf.Max(1, h - regionH));

        var blank = new Color[regionW * regionH];
        for (int i = 0; i < blank.Length; i++) blank[i] = Color.white;
        tex.SetPixels(x, y, regionW, regionH, blank);
        tex.Apply();
        return tex.EncodeToPNG();
    }

    // ---------- Serialization for the reveal phase ----------

    private static byte[] SerializeBook(List<BookEntry> book)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(book.Count);
        foreach (var entry in book)
        {
            writer.Write((byte)entry.Type);
            writer.Write(entry.AuthorClientId);
            byte[] textBytes = Encoding.UTF8.GetBytes(entry.Text ?? "");
            writer.Write(textBytes.Length);
            writer.Write(textBytes);
            byte[] imgBytes = entry.ImageData ?? System.Array.Empty<byte>();
            writer.Write(imgBytes.Length);
            writer.Write(imgBytes);
        }
        return stream.ToArray();
    }

    private static List<BookEntry> DeserializeBook(byte[] data)
    {
        var result = new List<BookEntry>();
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            var entry = new BookEntry { Type = (EntryType)reader.ReadByte(), AuthorClientId = reader.ReadUInt64() };
            int textLen = reader.ReadInt32();
            entry.Text = Encoding.UTF8.GetString(reader.ReadBytes(textLen));
            int imgLen = reader.ReadInt32();
            entry.ImageData = imgLen > 0 ? reader.ReadBytes(imgLen) : null;
            result.Add(entry);
        }
        return result;
    }

    // ---------- Client RPCs ----------

    [ClientRpc]
    private void AssignWritePromptClientRpc(ClientRpcParams rpcParams = default)
        => TelephoneUIManager.Instance?.ShowWritePrompt(isCaption: false, referenceImage: null);

    [ClientRpc]
    private void AssignWriteNextSentenceClientRpc(string previousSentence, ClientRpcParams rpcParams = default)
        => TelephoneUIManager.Instance?.ShowWritePrompt(isCaption: false, referenceImage: null,
            overrideLabel: $"Continue the story from: \"{previousSentence}\"");

    [ClientRpc]
    private void AssignDrawClientRpc(string referenceText, byte[] startingCanvasImage, ClientRpcParams rpcParams = default)
        => TelephoneUIManager.Instance?.ShowDrawTask(referenceText, startingCanvasImage);

    [ClientRpc]
    private void AssignCaptionClientRpc(byte[] referenceImage, ClientRpcParams rpcParams = default)
        => TelephoneUIManager.Instance?.ShowWritePrompt(isCaption: true, referenceImage: referenceImage);

    [ClientRpc]
    private void RevealBookClientRpc(int bookIndex, byte[] serializedBook)
        => TelephoneUIManager.Instance?.OnBookRevealed(bookIndex, DeserializeBook(serializedBook));
}