using UnityEngine;
using System.Collections;

/// <summary>
/// Guided missile that tracks the player's Y position.
/// Plays a warning sound, then launches toward the player.
/// </summary>
public class GuidedMissile : MonoBehaviour
{
    [Header("Missile Settings")]
    public float warningDuration = 1.5f;
    public float moveSpeed = 10f;
    public float trackingSpeed = 3f;   // How fast it adjusts Y
    public float maxTrackAngle = 35f;

    [Header("References")]
    public SpriteRenderer warningIndicator;   // Flashing "!" or arrow
    public AudioClip warningSound;
    public AudioClip launchSound;
    public ParticleSystem exhaustParticles;

    private Transform player;
    private bool launched = false;
    private float timer = 0f;
    private Vector3 velocity;
    private AudioSource audioSource;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    void OnEnable()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        launched = false;
        timer = 0f;
        velocity = Vector3.zero;

        // Start flashing warning
        if (warningIndicator) warningIndicator.enabled = true;
        var col = GetComponent<Collider2D>();
        if (col) col.enabled = false;

        if (audioSource && warningSound)
            audioSource.PlayOneShot(warningSound);

        StartCoroutine(LaunchRoutine());
    }

    IEnumerator LaunchRoutine()
    {
        // Flash warning indicator
        float elapsed = 0f;
        float flashInterval = 0.15f;
        while (elapsed < warningDuration)
        {
            if (warningIndicator)
                warningIndicator.enabled = !warningIndicator.enabled;
            yield return new WaitForSeconds(flashInterval);
            elapsed += flashInterval;
        }

        // Launch!
        if (warningIndicator) warningIndicator.enabled = false;
        launched = true;

        var col = GetComponent<Collider2D>();
        if (col) col.enabled = true;

        if (audioSource && launchSound)
            audioSource.PlayOneShot(launchSound);

        if (exhaustParticles) exhaustParticles.Play();

        // Set initial velocity toward player's current Y
        if (player != null)
        {
            Vector3 dir = (player.position - transform.position).normalized;
            velocity = dir * moveSpeed;
        }
        else
        {
            velocity = Vector3.left * moveSpeed;
        }
    }

    void Update()
    {
        if (!launched || !GameManager.Instance.IsPlaying) return;

        // Gradually track player's Y
        if (player != null)
        {
            float targetY = player.position.y;
            float dy = targetY - transform.position.y;
            velocity.y += Mathf.Sign(dy) * trackingSpeed * Time.deltaTime;
            // Clamp vertical speed
            velocity.y = Mathf.Clamp(velocity.y, -moveSpeed * 0.6f, moveSpeed * 0.6f);
        }

        velocity.x -= 0.5f * Time.deltaTime; // Slight acceleration left
        velocity.x = Mathf.Min(velocity.x, -moveSpeed);

        transform.position += velocity * Time.deltaTime;

        // Rotate to face direction of travel
        float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0, 0, angle), 10f * Time.deltaTime);

        // Despawn
        if (transform.position.x < -14f || transform.position.y < -6f || transform.position.y > 6f)
            gameObject.SetActive(false);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            other.GetComponent<PlayerController>()?.Die();
            gameObject.SetActive(false);
        }
    }
}
