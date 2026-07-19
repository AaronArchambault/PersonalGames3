using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drop this on any Button (or any UI element with a RectTransform) to get
/// smooth hover-grow, press-shrink, color tinting, and an optional "punch"
/// bounce on click release. No external tweening library needed.
///
/// Works alongside Unity's built-in Button "Color Tint" transition, but if
/// you enable Animate Color here, set the Button's own Transition to "None"
/// first so they don't fight each other over the same color.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Header("Scale")]
    [SerializeField] private float normalScale = 1f;
    [SerializeField] private float hoverScale = 1.1f;
    [SerializeField] private float pressedScale = 0.95f;
    [Tooltip("Higher = snappier transitions.")]
    [SerializeField] private float animationSpeed = 12f;

    [Header("Color (optional)")]
    [SerializeField] private bool animateColor = true;
    [SerializeField] private Graphic targetGraphic;
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color hoverColor = new Color(0.92f, 0.92f, 0.92f);
    [SerializeField] private Color pressedColor = new Color(0.78f, 0.78f, 0.78f);

    [Header("Click punch (optional)")]
    [SerializeField] private bool punchOnClick = true;
    [SerializeField] private float punchScale = 1.2f;
    [SerializeField] private float punchDuration = 0.15f;

    private RectTransform _rect;
    private Vector3 _targetScale;
    private Color _targetColor;
    private bool _isHovering;
    private bool _isPressed;
    private bool _isPunching;
    private Coroutine _punchCoroutine;

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _targetScale = Vector3.one * normalScale;

        if (animateColor)
        {
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
            _targetColor = normalColor;
        }
    }

    private void Update()
    {
        if (!_isPunching)
            _rect.localScale = Vector3.Lerp(_rect.localScale, _targetScale, Time.unscaledDeltaTime * animationSpeed);

        if (animateColor && targetGraphic != null)
            targetGraphic.color = Color.Lerp(targetGraphic.color, _targetColor, Time.unscaledDeltaTime * animationSpeed);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _isHovering = true;
        RefreshTargets();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _isHovering = false;
        _isPressed = false;
        RefreshTargets();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;
        RefreshTargets();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;
        RefreshTargets();

        if (punchOnClick && _isHovering)
        {
            if (_punchCoroutine != null) StopCoroutine(_punchCoroutine);
            _punchCoroutine = StartCoroutine(PunchRoutine());
        }
    }

    private void RefreshTargets()
    {
        if (_isPressed)
        {
            _targetScale = Vector3.one * pressedScale;
            _targetColor = pressedColor;
        }
        else if (_isHovering)
        {
            _targetScale = Vector3.one * hoverScale;
            _targetColor = hoverColor;
        }
        else
        {
            _targetScale = Vector3.one * normalScale;
            _targetColor = normalColor;
        }
    }

    private IEnumerator PunchRoutine()
    {
        _isPunching = true;
        Vector3 start = _rect.localScale;
        Vector3 peak = Vector3.one * punchScale;

        float t = 0f;
        while (t < punchDuration)
        {
            t += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(t / punchDuration);
            float eased = Mathf.Sin(p * Mathf.PI); // rises to the peak, then eases back down to 0
            _rect.localScale = Vector3.LerpUnclamped(start, peak, eased);
            yield return null;
        }

        _isPunching = false; // hand control back to Update(), which smoothly settles to the current target
    }
}