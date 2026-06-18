using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float jetpackForce = 15f;
    public float maxUpSpeed = 8f;
    public float gravity = -20f;

    [Header("Bounds")]
    public float floorY = -3.5f;
    public float ceilingY = 3.5f;

    [Header("Effects")]
    public ParticleSystem jetpackParticles;
    public AudioClip jetpackSound;
    public AudioClip deathSound;

    private Rigidbody2D rb;
    private AudioSource audioSource;
    private bool isFiring = false;
    private bool isAlive = true;
    private Animator animator;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        audioSource = GetComponent<AudioSource>();
        animator = GetComponent<Animator>();
        rb.gravityScale = 0f; // We handle gravity manually
    }

    void Update()
    {
        if (!isAlive) return;

        // Input detection
        isFiring = Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space) ||
                   (Input.touchCount > 0 && Input.GetTouch(0).phase != TouchPhase.Ended);

        // Clamp position
        Vector3 pos = transform.position;
        if (pos.y <= floorY)
        {
            pos.y = floorY;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Max(0, rb.linearVelocity.y));
        }
        if (pos.y >= ceilingY)
        {
            pos.y = ceilingY;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, Mathf.Min(0, rb.linearVelocity.y));
        }
        transform.position = pos;

        // Particle effects
        if (jetpackParticles != null)
        {
            if (isFiring && !jetpackParticles.isPlaying) jetpackParticles.Play();
            else if (!isFiring && jetpackParticles.isPlaying) jetpackParticles.Stop();
        }

        // Animator
        if (animator != null)
        {
            animator.SetBool("IsFiring", isFiring);
            animator.SetFloat("VelocityY", rb.linearVelocity.y);
        }

        // Audio
        if (audioSource != null && jetpackSound != null)
        {
            if (isFiring && !audioSource.isPlaying)
                audioSource.PlayOneShot(jetpackSound);
        }
    }

    void FixedUpdate()
    {
        if (!isAlive) return;

        float yVelocity = rb.linearVelocity.y;

        if (isFiring)
        {
            yVelocity += jetpackForce * Time.fixedDeltaTime;
            yVelocity = Mathf.Min(yVelocity, maxUpSpeed);
        }
        else
        {
            yVelocity += gravity * Time.fixedDeltaTime;
        }

        rb.linearVelocity = new Vector2(0, yVelocity);
    }

    public void Die()
    {
        if (!isAlive) return;
        isAlive = false;
        rb.linearVelocity = Vector2.zero;
        if (audioSource != null && deathSound != null)
            audioSource.PlayOneShot(deathSound);
        if (jetpackParticles != null) jetpackParticles.Stop();
        if (animator != null) animator.SetTrigger("Die");
        GameManager.Instance.GameOver();
    }

    public void Revive()
    {
        isAlive = true;
        transform.position = new Vector3(-3f, 0f, 0f);
        rb.linearVelocity = Vector2.zero;
        if (animator != null) animator.SetTrigger("Revive");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!isAlive) return;

        if (other.CompareTag("Obstacle"))
        {
            Die();
        }
        else if (other.CompareTag("Coin"))
        {
            GameManager.Instance.CollectCoin();
            other.gameObject.SetActive(false);
        }
        else if (other.CompareTag("PowerUp"))
        {
            PowerUp pu = other.GetComponent<PowerUp>();
            if (pu != null) pu.Activate();
            other.gameObject.SetActive(false);
        }
    }
}
