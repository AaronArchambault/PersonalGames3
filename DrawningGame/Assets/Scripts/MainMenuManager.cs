using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wire this up in your Main Menu scene. Each mode button calls one of the
/// public methods below. Networked modes (Skribbl, Telephone, Whiteboard-Multiplayer)
/// start hosting/joining and then load the target scene for everyone. Solo Whiteboard
/// just loads a scene directly with no networking involved.
///
/// SCENE SETUP: add MainMenu, Skribbl, Telephone, WhiteboardSolo, and
/// WhiteboardMultiplayer scenes to File -> Build Settings -> Scenes In Build.
/// Exact names below must match your actual scene names.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Scene names (must match Build Settings exactly)")]
    [SerializeField] private string skribblSceneName = "SkribblGame";
    [SerializeField] private string telephoneSceneName = "TelephoneGame";
    [SerializeField] private string whiteboardSoloSceneName = "WhiteboardSolo";
    [SerializeField] private string whiteboardMultiplayerSceneName = "WhiteboardMultiplayer";

    [Header("Join settings")]
    [SerializeField] private string joinAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;

    private string _pendingSceneName;

    // ---------- Mode buttons ----------

    /// <summary>Wire to "Host Skribbl" button.</summary>
    public void HostSkribbl() => HostAndLoad(skribblSceneName);

    /// <summary>Wire to "Host Telephone" button.</summary>
    public void HostTelephone() => HostAndLoad(telephoneSceneName);

    /// <summary>Wire to "Host Multiplayer Whiteboard" button.</summary>
    public void HostWhiteboardMultiplayer() => HostAndLoad(whiteboardMultiplayerSceneName);

    /// <summary>Wire to "Join" button — joins whatever scene the host already started.</summary>
    public void JoinGame()
    {
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null) transport.SetConnectionData(joinAddress, port);

        NetworkManager.Singleton.StartClient();
        // No manual scene load needed here — Netcode's scene management (enabled on
        // your NetworkManager) automatically syncs the client to whatever scene the host is in.
    }

    /// <summary>Wire to "Solo Whiteboard" button — no networking at all.</summary>
    public void PlayWhiteboardSolo()
    {
        SceneManager.LoadScene(whiteboardSoloSceneName, LoadSceneMode.Single);
    }

    // ---------- Shared host+load flow ----------

    private void HostAndLoad(string sceneName)
    {
        _pendingSceneName = sceneName;
        bool started = NetworkManager.Singleton.StartHost();
        if (!started)
        {
            Debug.LogError("Failed to start host.");
            return;
        }
        // Wait one frame so the NetworkManager is fully initialized before loading a scene.
        Invoke(nameof(LoadPendingScene), 0.1f);
    }

    private void LoadPendingScene()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(_pendingSceneName, LoadSceneMode.Single);
    }
}