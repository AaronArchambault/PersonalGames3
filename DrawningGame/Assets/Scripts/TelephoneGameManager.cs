using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Implements the classic "Telephone" / Gartic-Phone-style chain game:
/// Round 0: every player writes a starting prompt.
/// Round 1: every player draws the prompt they were handed (from another player).
/// Round 2: every player writes a caption for the drawing they were handed.
/// ...alternating write/draw for as many rounds as there are players...
/// Reveal: every player's full chain ("book") is shown to everyone as a slideshow.
///
/// Other Gartic Phone variants (Movie, Word Combo, etc.) are built the same way —
/// just change what's alternated each round and how books are scored/sliced.
/// See the setup guide for notes on adapting this.
/// </summary>
public class TelephoneGameManager : NetworkBehaviour
{
    public enum EntryType : byte { Prompt = 0, Drawing = 1, Caption = 2 }

    public class BookEntry
    {
        public EntryType Type;
        public string Text;
        public byte[] ImageData;
        public ulong AuthorClientId;
    }

    public static TelephoneGameManager Instance { get; private set; }

    [Header("Timing")]
    [SerializeField] private float writingRoundDuration = 45f;
    [SerializeField] private float drawingRoundDuration = 75f;

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

        TotalRounds.Value = n;
        CurrentRound.Value = 0;
        InRevealPhase.Value = false;
        GameStarted.Value = true;

        BeginRound();
    }

    private void BeginRound()
    {
        _submittedThisRound.Clear();
        int n = _playerOrder.Count;
        int r = CurrentRound.Value;
        bool isDrawingRound = r % 2 == 1;

        RoundTimeRemaining.Value = isDrawingRound ? drawingRoundDuration : writingRoundDuration;

        for (int p = 0; p < n; p++)
        {
            ulong clientId = _playerOrder[p];
            int bookIndex = ((p - r) % n + n) % n;
            BookEntry reference = r == 0 ? null : _books[bookIndex][r - 1];

            var clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };

            if (r == 0)
                AssignWritePromptClientRpc(clientParams);
            else if (isDrawingRound)
                AssignDrawClientRpc(reference.Text, clientParams);
            else
                AssignCaptionClientRpc(reference.ImageData, clientParams);
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
        bool isDrawingRound = r % 2 == 1;

        for (int p = 0; p < n; p++)
        {
            ulong clientId = _playerOrder[p];
            if (_submittedThisRound.Contains(clientId)) continue;

            int bookIndex = ((p - r) % n + n) % n;
            var entry = new BookEntry
            {
                AuthorClientId = clientId,
                Type = r == 0 ? EntryType.Prompt : (isDrawingRound ? EntryType.Drawing : EntryType.Caption),
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
    private void AssignDrawClientRpc(string referenceText, ClientRpcParams rpcParams = default)
        => TelephoneUIManager.Instance?.ShowDrawTask(referenceText);

    [ClientRpc]
    private void AssignCaptionClientRpc(byte[] referenceImage, ClientRpcParams rpcParams = default)
        => TelephoneUIManager.Instance?.ShowWritePrompt(isCaption: true, referenceImage: referenceImage);

    [ClientRpc]
    private void RevealBookClientRpc(int bookIndex, byte[] serializedBook)
        => TelephoneUIManager.Instance?.OnBookRevealed(bookIndex, DeserializeBook(serializedBook));
}