using UnityEngine;

/// <summary>
/// Moves an obstacle from right to left at the game's current speed.
/// Deactivates itself when it exits the left side of the screen.
/// </summary>
public class ObstacleMover : MonoBehaviour
{
    [Header("Settings")]
    public float despawnX = -14f;
    public bool rotates = false;
    public float rotationSpeed = 90f;

    // For laser/zapper: these have a fixed vertical position pattern
    [Header("Laser Settings")]
    public bool isLaser = false;
    public float laserWarningTime = 0.8f;
    public SpriteRenderer laserRenderer;
    public SpriteRenderer warningRenderer;

    private bool active = false;
    private float timer = 0f;

    public void ResetObstacle()
    {
        active = true;
        timer = 0f;

        if (isLaser && warningRenderer != null)
        {
            warningRenderer.enabled = true;
            if (laserRenderer != null) laserRenderer.enabled = false;
        }
    }

    void Update()
    {
        if (!active || !GameManager.Instance.IsPlaying) return;

        // Move left
        float speed = GameManager.Instance.CurrentSpeed;
        transform.position += Vector3.left * speed * Time.deltaTime;

        // Rotation for some obstacles
        if (rotates)
            transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);

        // Laser warning -> activate
        if (isLaser)
        {
            timer += Time.deltaTime;
            if (timer >= laserWarningTime)
            {
                if (warningRenderer != null) warningRenderer.enabled = false;
                if (laserRenderer != null) laserRenderer.enabled = true;
                // Enable collider after warning
                var col = GetComponent<Collider2D>();
                if (col != null) col.enabled = true;
            }
        }

        // Despawn check
        if (transform.position.x < despawnX)
        {
            active = false;
            gameObject.SetActive(false);
        }
    }
}
