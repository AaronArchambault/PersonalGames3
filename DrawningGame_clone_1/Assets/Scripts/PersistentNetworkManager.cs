using UnityEngine;

/// <summary>
/// Attach this to your NetworkManager GameObject in the Main Menu scene
/// (the very first scene that loads). Without this, the NetworkManager
/// gets destroyed when MainMenuManager loads a new scene with LoadSceneMode.Single.
/// </summary>
public class PersistentNetworkManager : MonoBehaviour
{
    private static bool _created;

    private void Awake()
    {
        if (_created)
        {
            Destroy(gameObject); // a duplicate NetworkManager snuck in (e.g. scene reloaded) — remove it
            return;
        }
        _created = true;
        DontDestroyOnLoad(gameObject);
    }
}