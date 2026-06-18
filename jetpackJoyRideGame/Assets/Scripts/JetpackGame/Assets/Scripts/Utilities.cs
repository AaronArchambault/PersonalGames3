using UnityEngine;
using System.Collections;

/// <summary>
/// Camera shake effect — call CameraShake.Instance.Shake() from any script.
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance;

    private Vector3 originalPos;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        originalPos = transform.localPosition;
    }

    public void Shake(float duration = 0.3f, float magnitude = 0.2f)
    {
        StopAllCoroutines();
        StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            transform.localPosition = originalPos + new Vector3(x, y, 0f);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        transform.localPosition = originalPos;
    }
}

// ─────────────────────────────────────────────────
// Floating text that shows "+1 COIN!" etc.
// ─────────────────────────────────────────────────
public class FloatingText : MonoBehaviour
{
    public static void Spawn(GameObject prefab, Vector3 worldPos, string message, Color color)
    {
        if (prefab == null) return;
        var go = Instantiate(prefab, worldPos, Quaternion.identity);
        var ft = go.GetComponent<FloatingText>();
        if (ft) ft.Init(message, color);
    }

    public TMPro.TextMeshPro textMesh;
    public float riseSpeed = 1.5f;
    public float lifetime = 1f;

    public void Init(string message, Color color)
    {
        if (textMesh)
        {
            textMesh.text = message;
            textMesh.color = color;
        }
        Destroy(gameObject, lifetime);
    }

    void Update()
    {
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;
        // Fade out
        if (textMesh)
        {
            Color c = textMesh.color;
            c.a -= Time.deltaTime / lifetime;
            textMesh.color = c;
        }
    }
}

// ─────────────────────────────────────────────────
// Floor & ceiling trigger — kills player on touch
// ─────────────────────────────────────────────────
public class BoundaryKill : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            other.GetComponent<PlayerController>()?.Die();
    }
}
