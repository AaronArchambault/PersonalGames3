using System.Collections;
using UnityEngine;

/// <summary>
/// Drop this on any panel GameObject to fade + slightly scale it in/out
/// instead of an abrupt SetActive(true/false). Call Show()/Hide() instead
/// of directly toggling the GameObject's active state.
///
/// Requires a CanvasGroup on the same GameObject (Add Component -> Canvas Group).
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class PanelFader : MonoBehaviour
{
    [SerializeField] private float fadeDuration = 0.2f;
    [SerializeField] private float startScale = 0.9f;
    [SerializeField] private bool startHidden = true;

    private CanvasGroup _canvasGroup;
    private RectTransform _rect;
    private Coroutine _routine;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rect = GetComponent<RectTransform>();

        if (startHidden)
        {
            _canvasGroup.alpha = 0f;
            _rect.localScale = Vector3.one * startScale;
            gameObject.SetActive(false);
        }
    }

    public void Show()
    {
        gameObject.SetActive(true);
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Animate(targetAlpha: 1f, targetScale: 1f, disableAtEnd: false));
    }

    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Animate(targetAlpha: 0f, targetScale: startScale, disableAtEnd: true));
    }

    private IEnumerator Animate(float targetAlpha, float targetScale, bool disableAtEnd)
    {
        float startAlpha = _canvasGroup.alpha;
        float fromScale = _rect.localScale.x;
        float t = 0f;

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / fadeDuration);
            _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, p);
            float s = Mathf.Lerp(fromScale, targetScale, p);
            _rect.localScale = Vector3.one * s;
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;
        _rect.localScale = Vector3.one * targetScale;

        if (disableAtEnd) gameObject.SetActive(false);
    }
}