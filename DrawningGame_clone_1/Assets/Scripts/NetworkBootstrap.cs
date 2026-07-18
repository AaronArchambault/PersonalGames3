using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Wraps NetworkManager's Start methods as void-returning functions so they
/// can be wired directly into UI Button OnClick() events in the Inspector.
/// (NetworkManager.StartHost/StartClient/StartServer return bool, which is
/// why they don't show up in the OnClick dropdown on their own.)
///
/// Attach this to any GameObject in the scene (e.g. your UIManager object),
/// then drag it into your Host/Join buttons' OnClick() list and pick
/// HostGame() / JoinGame() from the dropdown.
/// </summary>
public class NetworkBootstrap : MonoBehaviour
{
    [Tooltip("Optional: set the IP address clients should connect to. Leave as 127.0.0.1 for same-machine testing.")]
    [SerializeField] private string joinAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;

    /// <summary>Call from the "Host" button's OnClick().</summary>
    public void HostGame()
    {
        bool started = NetworkManager.Singleton.StartHost();
        if (!started)
            Debug.LogError("Failed to start host.");
    }

    /// <summary>Call from the "Join" button's OnClick().</summary>
    public void JoinGame()
    {
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(joinAddress, port);
        }

        bool started = NetworkManager.Singleton.StartClient();
        if (!started)
            Debug.LogError("Failed to start client.");
    }

    /// <summary>Optional: call from a "Server only" button if you ever need a dedicated server (no local player).</summary>
    public void StartDedicatedServer()
    {
        bool started = NetworkManager.Singleton.StartServer();
        if (!started)
            Debug.LogError("Failed to start server.");
    }

    /// <summary>Optional: wire to a "Disconnect"/"Leave" button.</summary>
    public void Disconnect()
    {
        NetworkManager.Singleton.Shutdown();
    }
}