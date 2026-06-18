using UnityEngine;
using System.Collections;

/// <summary>
/// Horizontal laser that flashes a warning before becoming lethal.
/// Can be static or oscillating vertically.
/// </summary>
public class LaserObstacle : MonoBehaviour
{
    [Header("Settings")]
    public float warningDuration = 1f;
    public bool oscillates = false;
    public float oscillateSpeed = 1.5f;
    public float oscillateRange = 1f;

    [Header("Visuals")]
    public SpriteRenderer[] laserSegments;
    public SpriteRenderer warningGlow;
    public Color warningColor = new Color(1f, 0.3f, 0f, 0.5f);
    public Color activeColor = new Color(1f, 0f, 0f, 1f);

    [Header("Audio")]
    public AudioClip chargeSound;
    public AudioClip activeLoop;

    private Collider2D[] colliders;
    private bool lethal = false;
    private float startY;
    private AudioSource audioSource;

    void Awake()
    {
        colliders = GetComponentsInChildren<Collider2D>();
        audioSource = GetComponent<AudioSource>();
    }

    void OnEnable()
    {
        lethal = false;
        startY = transform.position.y;
        SetCollidersActive(false);
        SetLaserColor(warningColor, true);
        StartCoroutine(ActivateRoutine());
    }

    IEnumerator ActivateRoutine()
    {
        if (audioSource && chargeSound)
            audioSource.PlayOneShot(chargeSound);

        // Flash warning
        float elapsed = 0f;
        float flashRate = 0.1f;
        bool visible = true;
        while (elapsed < warningDuration)
        {
            visible = !visible;
            SetLaserColor(warningColor, visible);
            yield return new WaitForSeconds(flashRate);
            elapsed += flashRate;
        }

        // Activate
        lethal = true;
        SetCollidersActive(true);
        SetLaserColor(activeColor, true);

        if (audioSource && activeLoop)
        {
            audioSource.clip = activeLoop;
            audioSource.loop = true;
            audioSource.Play();
        }
    }

    void Update()
    {
        if (!GameManager.Instance.IsPlaying) return;

        // Move left with game speed
        float speed = GameManager.Instance.CurrentSpeed;
        transform.position += Vector3.left * speed * Time.deltaTime;

        // Optional oscillation
        if (oscillates && lethal)
        {
            float newY = startY + Mathf.Sin(Time.time * oscillateSpeed) * oscillateRange;
            Vector3 pos = transform.position;
            pos.y = newY;
            transform.position = pos;
        }

        if (transform.position.x < -14f)
            gameObject.SetActive(false);
    }

    void OnDisable()
    {
        if (audioSource) audioSource.Stop();
    }

    void SetCollidersActive(bool state)
    {
        foreach (var col in colliders) col.enabled = state;
    }

    void SetLaserColor(Color color, bool visible)
    {
        foreach (var seg in laserSegments)
        {
            if (seg == null) continue;
            seg.color = visible ? color : Color.clear;
        }
        if (warningGlow) warningGlow.color = visible ? warningColor : Color.clear;
    }
}
