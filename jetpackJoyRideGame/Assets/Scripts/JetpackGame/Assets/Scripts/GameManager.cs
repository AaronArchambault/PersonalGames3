using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("UI References")]
    public TextMeshProUGUI distanceText;
    public TextMeshProUGUI coinText;
    public TextMeshProUGUI bestDistanceText;
    public GameObject gameOverPanel;
    public TextMeshProUGUI finalDistanceText;
    public TextMeshProUGUI finalCoinsText;
    public GameObject startPanel;
    public TextMeshProUGUI countdownText;

    [Header("Game Settings")]
    public float startSpeed = 5f;
    public float maxSpeed = 15f;
    public float speedIncreaseRate = 0.05f;

    [Header("References")]
    public PlayerController player;
    public ObstacleSpawner obstacleSpawner;
    public BackgroundScroller backgroundScroller;
    public CoinSpawner coinSpawner;

    private float currentSpeed;
    private float distance;
    private int coins;
    private int totalCoins;
    private bool isPlaying;
    private bool isGameOver;

    public float CurrentSpeed => currentSpeed;
    public bool IsPlaying => isPlaying;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        totalCoins = PlayerPrefs.GetInt("TotalCoins", 0);
        UpdateBestDisplay();
    }

    void Start()
    {
        ShowStartPanel();
    }

    void Update()
    {
        if (!isPlaying) return;

        // Increase speed over time
        currentSpeed = Mathf.Min(currentSpeed + speedIncreaseRate * Time.deltaTime, maxSpeed);

        // Track distance
        distance += currentSpeed * Time.deltaTime;

        // Update UI
        if (distanceText) distanceText.text = $"{(int)distance}m";
        if (coinText) coinText.text = $"x{coins}";
    }

    public void StartGame()
    {
        if (startPanel) startPanel.SetActive(false);
        if (gameOverPanel) gameOverPanel.SetActive(false);
        StartCoroutine(CountdownRoutine());
    }

    IEnumerator CountdownRoutine()
    {
        if (countdownText)
        {
            countdownText.gameObject.SetActive(true);
            for (int i = 3; i >= 1; i--)
            {
                countdownText.text = i.ToString();
                yield return new WaitForSeconds(1f);
            }
            countdownText.text = "GO!";
            yield return new WaitForSeconds(0.5f);
            countdownText.gameObject.SetActive(false);
        }

        BeginPlay();
    }

    void BeginPlay()
    {
        isPlaying = true;
        isGameOver = false;
        currentSpeed = startSpeed;
        distance = 0;
        coins = 0;

        player.Revive();
        obstacleSpawner.StartSpawning();
        coinSpawner.StartSpawning();
        backgroundScroller.SetScrolling(true);

        UpdateCoinDisplay();
    }

    public void GameOver()
    {
        if (isGameOver) return;
        isGameOver = true;
        isPlaying = false;

        obstacleSpawner.StopSpawning();
        coinSpawner.StopSpawning();
        backgroundScroller.SetScrolling(false);

        // Save best distance
        int best = PlayerPrefs.GetInt("BestDistance", 0);
        if ((int)distance > best)
        {
            PlayerPrefs.SetInt("BestDistance", (int)distance);
            UpdateBestDisplay();
        }

        // Save coins
        totalCoins += coins;
        PlayerPrefs.SetInt("TotalCoins", totalCoins);
        PlayerPrefs.Save();

        StartCoroutine(ShowGameOverAfterDelay());
    }

    IEnumerator ShowGameOverAfterDelay()
    {
        yield return new WaitForSeconds(1.5f);
        ShowGameOverPanel();
    }

    void ShowGameOverPanel()
    {
        if (gameOverPanel) gameOverPanel.SetActive(true);
        if (finalDistanceText) finalDistanceText.text = $"{(int)distance}m";
        if (finalCoinsText) finalCoinsText.text = $"{coins} coins";
        UpdateBestDisplay();
    }

    void ShowStartPanel()
    {
        if (startPanel) startPanel.SetActive(true);
        if (gameOverPanel) gameOverPanel.SetActive(false);
    }

    public void CollectCoin()
    {
        coins++;
        UpdateCoinDisplay();
    }

    void UpdateCoinDisplay()
    {
        if (coinText) coinText.text = $"x{coins}";
    }

    void UpdateBestDisplay()
    {
        int best = PlayerPrefs.GetInt("BestDistance", 0);
        if (bestDistanceText) bestDistanceText.text = $"Best: {best}m";
    }

    public void RestartGame()
    {
        // Clear all spawned objects
        foreach (var o in GameObject.FindGameObjectsWithTag("Obstacle"))
            o.SetActive(false);
        foreach (var c in GameObject.FindGameObjectsWithTag("Coin"))
            c.SetActive(false);

        StartGame();
    }
}
