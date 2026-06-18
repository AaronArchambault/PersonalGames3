using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class ObstacleSpawner : MonoBehaviour
{
    [System.Serializable]
    public class ObstacleData
    {
        public GameObject prefab;
        [Range(0f, 1f)] public float spawnWeight = 1f;
        public float minDistance = 0f; // Min game distance before this spawns
    }

    [Header("Obstacle Types")]
    public ObstacleData[] obstacles;

    [Header("Spawn Settings")]
    public float spawnX = 12f;
    public float minY = -2.5f;
    public float maxY = 2.5f;
    public float minSpawnInterval = 1.2f;
    public float maxSpawnInterval = 2.8f;

    [Header("Pooling")]
    public int poolSizePerType = 5;

    private Dictionary<GameObject, Queue<GameObject>> pools = new();
    private bool spawning;
    private Coroutine spawnCoroutine;

    void Awake()
    {
        // Initialize object pools
        foreach (var od in obstacles)
        {
            if (od.prefab == null) continue;
            var q = new Queue<GameObject>();
            for (int i = 0; i < poolSizePerType; i++)
            {
                var go = Instantiate(od.prefab);
                go.SetActive(false);
                q.Enqueue(go);
            }
            pools[od.prefab] = q;
        }
    }

    public void StartSpawning()
    {
        spawning = true;
        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
        spawnCoroutine = StartCoroutine(SpawnRoutine());
    }

    public void StopSpawning()
    {
        spawning = false;
        if (spawnCoroutine != null) StopCoroutine(spawnCoroutine);
    }

    IEnumerator SpawnRoutine()
    {
        yield return new WaitForSeconds(2f); // Initial delay

        while (spawning)
        {
            SpawnObstacle();
            float interval = Random.Range(minSpawnInterval, maxSpawnInterval);
            // Speed up spawning as game progresses
            interval /= (GameManager.Instance.CurrentSpeed / 5f);
            interval = Mathf.Max(0.8f, interval);
            yield return new WaitForSeconds(interval);
        }
    }

    void SpawnObstacle()
    {
        float totalWeight = 0f;
        foreach (var od in obstacles)
            if (od.prefab != null)
                totalWeight += od.spawnWeight;

        float roll = Random.Range(0f, totalWeight);
        float cumulative = 0f;
        ObstacleData chosen = null;

        foreach (var od in obstacles)
        {
            if (od.prefab == null) continue;
            cumulative += od.spawnWeight;
            if (roll <= cumulative)
            {
                chosen = od;
                break;
            }
        }

        if (chosen == null) return;

        GameObject obj = GetFromPool(chosen.prefab);
        if (obj == null) return;

        float yPos = Random.Range(minY, maxY);
        obj.transform.position = new Vector3(spawnX, yPos, 0f);
        obj.SetActive(true);

        // Give the obstacle a mover component reference
        ObstacleMover mover = obj.GetComponent<ObstacleMover>();
        if (mover) mover.ResetObstacle();
    }

    GameObject GetFromPool(GameObject prefab)
    {
        if (!pools.ContainsKey(prefab)) return null;
        var q = pools[prefab];

        // Try to find inactive pooled object
        if (q.Count > 0)
        {
            var obj = q.Dequeue();
            if (!obj.activeInHierarchy) return obj;
            q.Enqueue(obj); // Put it back if it's still active
        }

        // Expand pool
        var newObj = Instantiate(prefab);
        newObj.SetActive(false);
        return newObj;
    }

    public void ReturnToPool(GameObject prefab, GameObject obj)
    {
        obj.SetActive(false);
        if (pools.ContainsKey(prefab))
            pools[prefab].Enqueue(obj);
    }
}
