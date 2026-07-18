using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach this to your NetworkManager's Player Prefab.
/// Holds each player's display name and score, and whether they've
/// guessed correctly this round.
/// </summary>
public class PlayerController : NetworkBehaviour
{
    public NetworkVariable<FixedString32Bytes> PlayerName = new NetworkVariable<FixedString32Bytes>(
        "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public NetworkVariable<int> Score = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<bool> HasGuessedCorrectly = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            string name = "Player" + Random.Range(1000, 9999);
            SetNameServerRpc(name);
        }

        // Register with GameManager so it knows about this player
        if (IsServer)
        {
            GameManager.Instance.RegisterPlayer(OwnerClientId);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && GameManager.Instance != null)
        {
            GameManager.Instance.UnregisterPlayer(OwnerClientId);
        }
    }

    [ServerRpc]
    private void SetNameServerRpc(string name)
    {
        PlayerName.Value = name;
    }

    /// <summary>Called on the server to award points.</summary>
    public void AddScore(int amount)
    {
        Score.Value += amount;
    }

    public void ResetRoundState()
    {
        HasGuessedCorrectly.Value = false;
    }
}