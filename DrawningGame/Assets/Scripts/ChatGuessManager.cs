using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles chat messages and guess validation. Every non-drawer message
/// is checked against the current word server-side. Attach to the same
/// GameObject as GameManager, or anywhere with network presence.
/// </summary>
public class ChatGuessManager : NetworkBehaviour
{
    public static ChatGuessManager Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>Call this from your chat input field's Submit handler.</summary>
    public void SubmitGuess(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        SubmitGuessServerRpc(text);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitGuessServerRpc(string text, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        string senderName = NetworkManager.Singleton.ConnectedClients[senderId]
            .PlayerObject.GetComponent<PlayerController>().PlayerName.Value.ToString();

        // The drawer can't guess their own word
        bool isDrawer = senderId == GameManager.Instance.CurrentDrawerClientId.Value;

        var playerController = NetworkManager.Singleton.ConnectedClients[senderId]
            .PlayerObject.GetComponent<PlayerController>();

        if (!isDrawer && GameManager.Instance.RoundActive.Value && !playerController.HasGuessedCorrectly.Value)
        {
            string word = GameManager.Instance.GetCurrentWordForValidation();
            if (!string.IsNullOrEmpty(word) && text.Trim().Equals(word, System.StringComparison.OrdinalIgnoreCase))
            {
                GameManager.Instance.OnCorrectGuess(senderId);

                // Everyone sees "PlayerX guessed the word!" without the word itself
                BroadcastSystemMessageClientRpc($"{senderName} guessed the word!");
                // The guesser gets a special "you got it" confirmation client-side via their own HasGuessedCorrectly flag
                return;
            }
        }

        // Not a correct guess (or drawer talking) — broadcast as a normal chat message
        BroadcastChatMessageClientRpc(senderName, text);
    }

    [ClientRpc]
    private void BroadcastChatMessageClientRpc(string senderName, string message)
    {
        UIManager.Instance?.AppendChatMessage(senderName, message, isSystem: false);
    }

    [ClientRpc]
    private void BroadcastSystemMessageClientRpc(string message)
    {
        UIManager.Instance?.AppendChatMessage("", message, isSystem: true);
    }
}