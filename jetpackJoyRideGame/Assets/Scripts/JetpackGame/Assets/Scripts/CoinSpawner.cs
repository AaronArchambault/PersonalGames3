using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Spawns coins in fun patterns (lines, arcs, clusters) as the player runs.
/// </summary>
public class CoinSpawner : MonoBehaviour
{
    public GameObject coinPrefab;
    public int poolSize = 60;

    [Header("Spawn Settings")]
    public float spawnX = 13f;
    public float minY = -2.5f;
    public float maxY = 2.5f;
    public float minInterval = 1.5f;
    public float maxInterval = 3.5f;
    public float coinSpacing = 0.6f;

    private Queue<GameObject> pool = new();
    private bool spawning;
    private Coroutine spawnCoroutine;

    void Awake()
    {
        for (int i = 0; i < poolSize; i++)
        {
            var c = Instantiate(coinPrefab);
            c.SetActive(false);
            pool.Enqueue(c);
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
        yield return new WaitForSeconds(1f);

        while (spawning)
        {
            SpawnPattern();
            float interval = Random.Range(minInterval, maxInterval);
            yield return new WaitForSeconds(interval);
        }
    }

    enum CoinPattern { Line, Arc, Cluster, DiagonalLine, ZigZag }

    void SpawnPattern()
    {
        CoinPattern pattern = (CoinPattern)Random.Range(0, System.Enum.GetValues(typeof(CoinPattern)).Length);
        float baseY = Random.Range(minY, maxY);
        float startX = spawnX;

        switch (pattern)
        {
            case CoinPattern.Line:
                // Horizontal line of 6-10 coins
                int lineCount = Random.Range(6, 11);
                for (int i = 0; i < lineCount; i++)
                    SpawnCoin(new Vector3(startX + i * coinSpacing, baseY, 0));
                break;

            case CoinPattern.Arc:
                // Arc of coins going up then down
                int arcCount = Random.Range(8, 13);
                for (int i = 0; i < arcCount; i++)
                {
                    float t = (float)i / (arcCount - 1);
                    float arcY = baseY + Mathf.Sin(t * Mathf.PI) * 1.5f;
                    SpawnCoin(new Vector3(startX + i * coinSpacing, arcY, 0));
                }
                break;

            case CoinPattern.Cluster:
                // 3x3 grid cluster
                for (int x = 0; x < 3; x++)
                    for (int y = 0; y < 3; y++)
                        SpawnCoin(new Vector3(startX + x * coinSpacing, baseY + (y - 1) * coinSpacing, 0));
                break;

            case CoinPattern.DiagonalLine:
                int diagCount = Random.Range(5, 9);
                float dir = Random.value > 0.5f ? 1f : -1f;
                for (int i = 0; i < diagCount; i++)
                    SpawnCoin(new Vector3(startX + i * coinSpacing, baseY + i * coinSpacing * 0.5f * dir, 0));
                break;

            case CoinPattern.ZigZag:
                int zigCount = Random.Range(8, 14);
                for (int i = 0; i < zigCount; i++)
                {
                    float zigY = baseY + (i % 2 == 0 ? 0.6f : -0.6f);
                    SpawnCoin(new Vector3(startX + i * coinSpacing, zigY, 0));
                }
                break;
        }
    }

    void SpawnCoin(Vector3 position)
    {
        if (pool.Count == 0) return;
        // Clamp Y
        position.y = Mathf.Clamp(position.y, minY, maxY);

        var coin = pool.Dequeue();
        coin.transform.position = position;
        coin.SetActive(true);

        var mover = coin.GetComponent<CoinMover>();
        if (mover) mover.poolRef = pool;
    }
}
