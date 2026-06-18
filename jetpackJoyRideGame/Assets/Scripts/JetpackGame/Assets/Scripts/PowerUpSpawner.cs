using UnityEngine;
using System.Collections;

/// <summary>
/// Spawns power-up tokens that float across the screen.
/// Players fly into them to activate the effect.
/// </summary>
public class PowerUpSpawner : MonoBehaviour
{
    [System.Serializable]
    public class PowerUpEntry
    {
        public GameObject prefab;
        public float weight = 1f;
        public string label;
    }

    public PowerUpEntry[] powerUps;

    [Header("Spawn Settings")]
    public float spawnX = 13f;
    public float minY = -2f;
    public float maxY = 2f;
    public float minInterval = 8f;
    public float maxInterval = 20f;

    private Coroutine spawnCoroutine;

    void Start()
    {
        StartCoroutine(SpawnRoutine());
    }

    IEnumerator SpawnRoutine()
    {
        yield return new WaitForSeconds(5f); // Don't spawn immediately

        while (true)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsPlaying)
            {
                SpawnPowerUp();
            }
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
        }
    }

    void SpawnPowerUp()
    {
        if (powerUps == null || powerUps.Length == 0) return;

        float totalWeight = 0;
        foreach (var p in powerUps) totalWeight += p.weight;

        float roll = Random.Range(0f, totalWeight);
        float cum = 0f;
        PowerUpEntry chosen = null;

        foreach (var p in powerUps)
        {
            cum += p.weight;
            if (roll <= cum) { chosen = p; break; }
        }

        if (chosen == null || chosen.prefab == null) return;

        float y = Random.Range(minY, maxY);
        var go = Instantiate(chosen.prefab, new Vector3(spawnX, y, 0), Quaternion.identity);

        // Add a simple mover if not present
        if (go.GetComponent<ObstacleMover>() == null)
        {
            go.AddComponent<ObstacleMover>();
        }
    }
}
