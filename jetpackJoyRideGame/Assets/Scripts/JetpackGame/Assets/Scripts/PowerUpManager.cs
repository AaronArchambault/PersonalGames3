using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class PowerUpManager : MonoBehaviour
{
    public static PowerUpManager Instance;

    [Header("References")]
    public PlayerController player;
    public GameObject shieldVisual;
    public TextMeshProUGUI activePowerUpText;
    public Image powerUpTimerBar;

    [Header("Magnet Settings")]
    public float magnetRadius = 4f;
    public float magnetForce = 10f;

    // Active states
    private bool shieldActive;
    private bool magnetActive;
    private bool slowMoActive;
    private int coinMultiplier = 1;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // ─── Shield ───────────────────────────────────
    public void ActivateShield(float duration)
    {
        if (shieldActive) StopCoroutine("ShieldRoutine");
        StartCoroutine(ShieldRoutine(duration));
    }

    IEnumerator ShieldRoutine(float duration)
    {
        shieldActive = true;
        if (shieldVisual) shieldVisual.SetActive(true);
        ShowPowerUpUI("SHIELD", duration);

        // Make obstacles non-lethal for duration
        Physics2D.IgnoreLayerCollision(LayerMask.NameToLayer("Player"), LayerMask.NameToLayer("Obstacle"), true);

        yield return new WaitForSeconds(duration);

        shieldActive = false;
        if (shieldVisual) shieldVisual.SetActive(false);
        Physics2D.IgnoreLayerCollision(LayerMask.NameToLayer("Player"), LayerMask.NameToLayer("Obstacle"), false);
    }

    // ─── Magnet ───────────────────────────────────
    public void ActivateMagnet(float duration)
    {
        if (magnetActive) StopCoroutine("MagnetRoutine");
        StartCoroutine(MagnetRoutine(duration));
    }

    IEnumerator MagnetRoutine(float duration)
    {
        magnetActive = true;
        ShowPowerUpUI("MAGNET", duration);
        yield return new WaitForSeconds(duration);
        magnetActive = false;
    }

    void Update()
    {
        if (!magnetActive || player == null) return;

        // Attract all coins within radius
        Collider2D[] coins = Physics2D.OverlapCircleAll(player.transform.position, magnetRadius,
            LayerMask.GetMask("Coin"));
        foreach (var coin in coins)
        {
            Vector3 dir = (player.transform.position - coin.transform.position).normalized;
            coin.transform.position += dir * magnetForce * Time.deltaTime;
        }
    }

    // ─── Slow Mo ──────────────────────────────────
    public void ActivateSlowMo(float duration)
    {
        if (slowMoActive) StopCoroutine("SlowMoRoutine");
        StartCoroutine(SlowMoRoutine(duration));
    }

    IEnumerator SlowMoRoutine(float duration)
    {
        slowMoActive = true;
        Time.timeScale = 0.5f;
        Time.fixedDeltaTime = 0.02f * Time.timeScale;
        ShowPowerUpUI("SLOW MO", duration);

        yield return new WaitForSecondsRealtime(duration);

        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
        slowMoActive = false;
    }

    // ─── Coin Multiplier ──────────────────────────
    public void ActivateCoinMultiplier(int multiplier, float duration)
    {
        StartCoroutine(CoinMultiplierRoutine(multiplier, duration));
    }

    IEnumerator CoinMultiplierRoutine(int multiplier, float duration)
    {
        coinMultiplier = multiplier;
        ShowPowerUpUI($"x{multiplier} COINS", duration);
        yield return new WaitForSeconds(duration);
        coinMultiplier = 1;
    }

    public int GetCoinMultiplier() => coinMultiplier;

    // ─── UI Helper ────────────────────────────────
    void ShowPowerUpUI(string name, float duration)
    {
        StopCoroutine("PowerUpUIRoutine");
        StartCoroutine(PowerUpUIRoutine(name, duration));
    }

    IEnumerator PowerUpUIRoutine(string name, float duration)
    {
        if (activePowerUpText) activePowerUpText.text = name;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            if (powerUpTimerBar)
                powerUpTimerBar.fillAmount = 1f - (elapsed / duration);
            yield return null;
        }
        if (activePowerUpText) activePowerUpText.text = "";
        if (powerUpTimerBar) powerUpTimerBar.fillAmount = 0f;
    }
}
