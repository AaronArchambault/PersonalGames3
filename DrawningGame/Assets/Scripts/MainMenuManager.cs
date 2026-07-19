using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wire this up in your Main Menu scene. Each mode button calls one of the
/// public methods below. Networked modes start hosting/joining and then load
/// the target scene for everyone. Solo Whiteboard just loads a scene directly
/// with no networking involved.
///
/// SCENE SETUP: add every scene listed below to
/// File -> Build Settings -> Scenes In Build. Exact names must match your
/// actual scene names.
///
/// Note: Animation, Exquisite Corpse, Complement, Story, Missing Piece, and
/// Knock-Off are all the SAME underlying scripts as Telephone (TelephoneGameManager
/// + TelephoneUIManager) just with different Inspector settings per scene —
/// see SETUP_GUIDE.md section 14 for exactly what to change in each duplicated scene.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Core scenes")]
    [SerializeField] private string skribblSceneName = "SkribblGame";
    [SerializeField] private string whiteboardSoloSceneName = "WhiteboardSolo";
    [SerializeField] private string whiteboardMultiplayerSceneName = "WhiteboardMultiplayer";

    [Header("Telephone family scenes (all use TelephoneGameManager, different FlowMode per scene)")]
    [SerializeField] private string classicTelephoneSceneName = "TelephoneClassic";
    [SerializeField] private string knockOffSceneName = "TelephoneKnockOff";
    [SerializeField] private string animationSceneName = "TelephoneAnimation";
    [SerializeField] private string exquisiteCorpseSceneName = "TelephoneExquisiteCorpse";
    [SerializeField] private string complementSceneName = "TelephoneComplement";
    [SerializeField] private string storySceneName = "TelephoneStory";
    [SerializeField] private string missingPieceSceneName = "TelephoneMissingPiece";

    [Header("Join settings")]
    [SerializeField] private string joinAddress = "127.0.0.1";
    [SerializeField] private ushort port = 7777;

    private string _pendingSceneName;

    // ---------- Mode buttons ----------

    public void HostSkribbl() => HostAndLoad(skribblSceneName);
    public void HostWhiteboardMultiplayer() => HostAndLoad(whiteboardMultiplayerSceneName);

    public void HostTelephoneClassic() => HostAndLoad(classicTelephoneSceneName);
    public void HostTelephoneKnockOff() => HostAndLoad(knockOffSceneName);
    public void HostTelephoneAnimation() => HostAndLoad(animationSceneName);
    public void HostTelephoneExquisiteCorpse() => HostAndLoad(exquisiteCorpseSceneName);
    public void HostTelephoneComplement() => HostAndLoad(complementSceneName);
    public void HostTelephoneStory() => HostAndLoad(storySceneName);
    public void HostTelephoneMissingPiece() => HostAndLoad(missingPieceSceneName);

    /// <summary>Wire to "Join" button — joins whatever scene the host already started.</summary>
    public void JoinGame()
    {
        var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
        if (transport != null) transport.SetConnectionData(joinAddress, port);

        NetworkManager.Singleton.StartClient();
        // No manual scene load needed — Netcode's scene management automatically
        // syncs the client to whatever scene the host is currently in.
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
        Invoke(nameof(LoadPendingScene), 0.1f);
    }

    private void LoadPendingScene()
    {
        NetworkManager.Singleton.SceneManager.LoadScene(_pendingSceneName, LoadSceneMode.Single);
    }
}