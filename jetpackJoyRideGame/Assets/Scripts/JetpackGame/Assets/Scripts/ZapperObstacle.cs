using UnityEngine;
using System.Collections;

/// <summary>
/// Zapper: electric hazard that flickers on and off.
/// Can be rotated at various angles.
/// </summary>
public class ZapperObstacle : MonoBehaviour
{
    [Header("Zapper Settings")]
    public float onDuration = 0.8f;
    public float offDuration = 0.4f;
    public bool startOn = true;

    [Header("Visuals")]
    public SpriteRenderer zapperRenderer;
    public ParticleSystem sparkParticles;
    public Color onColor = new Color(0.2f, 0.8f, 1f);
    public Color offColor = new Color(0.1f, 0.3f, 0.5f, 0.4f);

    [Header("Audio")]
    public AudioClip zapLoop;

    private Collider2D zapCollider;
    private AudioSource audioSource;
    private bool isOn;

    void Awake()
    {
        zapCollider = GetComponent<Collider2D>();
        audioSource = GetComponent<AudioSource>();
    }

    void OnEnable()
    {
        isOn = startOn;
        ApplyState();
        StartCoroutine(FlickerRoutine());
    }

    void OnDisable()
    {
        StopAllCoroutines();
        if (audioSource) audioSource.Stop();
    }

    IEnumerator FlickerRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(isOn ? onDuration : offDuration);
            isOn = !isOn;
            ApplyState();
        }
    }

    void ApplyState()
    {
        if (zapperRenderer) zapperRenderer.color = isOn ? onColor : offColor;
        if (zapCollider) zapCollider.enabled = isOn;
        if (sparkParticles)
        {
            if (isOn && !sparkParticles.isPlaying) sparkParticles.Play();
            else if (!isOn && sparkParticles.isPlaying) sparkParticles.Stop();
        }
        if (audioSource && zapLoop)
        {
            if (isOn) { audioSource.clip = zapLoop; audioSource.loop = true; audioSource.Play(); }
            else audioSource.Stop();
        }
    }

    void Update()
    {
        if (!GameManager.Instance.IsPlaying) return;
        transform.position += Vector3.left * GameManager.Instance.CurrentSpeed * Time.deltaTime;
        if (transform.position.x < -14f) gameObject.SetActive(false);
    }
}
