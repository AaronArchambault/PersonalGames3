using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// Tracks score, handles milestone notifications, and manages the run summary.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    [Header("Milestone Settings")]
    public int[] milestones = { 100, 250, 500, 1000, 2000, 5000 };
    public GameObject milestonePopup;
    public TextMeshProUGUI milestoneText;
    public float popupDuration = 2f;

    [Header("Score Multiplier")]
    public TextMeshProUGUI multiplierText;
    private int currentMultiplierIndex = 0;
    private int[] multiplierThresholds = { 0, 500, 1500, 3000 };
    private float[] multiplierValues = { 1f, 1.5f, 2f, 3f };

    private int nextMilestoneIndex = 0;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Update()
    {
        if (!GameManager.Instance.IsPlaying) return;

        float dist = GetCurrentDistance();

        // Check milestones
        if (nextMilestoneIndex < milestones.Length && dist >= milestones[nextMilestoneIndex])
        {
            TriggerMilestone(milestones[nextMilestoneIndex]);
            nextMilestoneIndex++;
        }

        // Update multiplier display
        UpdateMultiplier(dist);
    }

    void UpdateMultiplier(float distance)
    {
        int newIndex = 0;
        for (int i = 0; i < multiplierThresholds.Length; i++)
        {
            if (distance >= multiplierThresholds[i])
                newIndex = i;
        }

        if (newIndex != currentMultiplierIndex)
        {
            currentMultiplierIndex = newIndex;
            if (multiplierText)
                multiplierText.text = $"x{multiplierValues[currentMultiplierIndex]:F1}";
        }
    }

    public float GetMultiplier()
    {
        return multiplierValues[currentMultiplierIndex];
    }

    float GetCurrentDistance()
    {
        // Access distance from GameManager
        return GameManager.Instance != null ? Time.timeSinceLevelLoad * GameManager.Instance.CurrentSpeed : 0f;
    }

    void TriggerMilestone(int distance)
    {
        if (milestonePopup == null) return;
        StopCoroutine("ShowMilestoneRoutine");
        StartCoroutine(ShowMilestoneRoutine(distance));
    }

    IEnumerator ShowMilestoneRoutine(int distance)
    {
        if (milestonePopup) milestonePopup.SetActive(true);
        if (milestoneText) milestoneText.text = $"{distance}m!";

        // Animate in
        CanvasGroup cg = milestonePopup.GetComponent<CanvasGroup>();
        if (cg == null) cg = milestonePopup.AddComponent<CanvasGroup>();

        float t = 0f;
        while (t < 0.3f) { t += Time.deltaTime; cg.alpha = t / 0.3f; yield return null; }
        cg.alpha = 1f;

        yield return new WaitForSeconds(popupDuration);

        t = 0f;
        while (t < 0.3f) { t += Time.deltaTime; cg.alpha = 1f - t / 0.3f; yield return null; }

        if (milestonePopup) milestonePopup.SetActive(false);
    }

    public void ResetForNewRun()
    {
        nextMilestoneIndex = 0;
        currentMultiplierIndex = 0;
        if (multiplierText) multiplierText.text = "x1.0";
    }
}
