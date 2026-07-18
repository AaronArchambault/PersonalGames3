using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A circular hue/saturation color wheel + brightness slider, generated at runtime.
/// Drag anywhere on the wheel to pick hue (angle) and saturation (distance from center);
/// use the paired brightness slider for value (V in HSV).
///
/// SETUP:
/// 1. Create a square RawImage (e.g. 200x200) named "ColorWheel".
/// 2. Add this script to that same GameObject.
/// 3. Assign "Wheel Image" and "Wheel Rect" to that RawImage/RectTransform.
/// 4. Add a horizontal Slider (range 0-1) nearby for brightness, assign it to "Brightness Slider".
/// 5. (Optional) Add a small non-interactive Image as a child "SelectorHandle" to show
///    the current pick location; assign its RectTransform to "Selector Handle".
/// 6. (Optional) Assign a "Preview Swatch" Image to show the currently selected color.
/// </summary>
public class ColorWheelPicker : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    [Header("Required")]
    [SerializeField] private RawImage wheelImage;
    [SerializeField] private RectTransform wheelRect;

    [Header("Optional")]
    [SerializeField] private Slider brightnessSlider;
    [SerializeField] private RectTransform selectorHandle;
    [SerializeField] private Image previewSwatch;

    [SerializeField] private int wheelResolution = 256;

    private Texture2D _wheelTexture;
    private float _hue;
    private float _saturation = 1f;
    private float _value = 1f;

    private void Awake()
    {
        GenerateWheelTexture(wheelResolution);
        wheelImage.texture = _wheelTexture;

        if (brightnessSlider != null)
        {
            _value = brightnessSlider.value <= 0f ? 1f : brightnessSlider.value;
            brightnessSlider.onValueChanged.AddListener(OnBrightnessChanged);
        }

        UpdateColor();
    }

    private void GenerateWheelTexture(int size)
    {
        _wheelTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _wheelTexture.wrapMode = TextureWrapMode.Clamp;

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 pos = new Vector2(x, y) - center;
                float dist = pos.magnitude / radius;

                if (dist > 1f)
                {
                    _wheelTexture.SetPixel(x, y, new Color(0, 0, 0, 0)); // transparent outside the circle
                    continue;
                }

                float angle = Mathf.Atan2(pos.y, pos.x);
                float hue01 = angle / (2f * Mathf.PI);
                if (hue01 < 0f) hue01 += 1f;

                Color c = Color.HSVToRGB(hue01, dist, 1f);
                _wheelTexture.SetPixel(x, y, c);
            }
        }
        _wheelTexture.Apply();
    }

    public void OnPointerDown(PointerEventData eventData) => HandlePointer(eventData);
    public void OnDrag(PointerEventData eventData) => HandlePointer(eventData);

    private void HandlePointer(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            wheelRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        Rect rect = wheelRect.rect;
        Vector2 center = rect.center;
        Vector2 pos = localPoint - center;

        float radius = Mathf.Min(rect.width, rect.height) / 2f;
        float dist = Mathf.Clamp01(pos.magnitude / radius);

        float angle = Mathf.Atan2(pos.y, pos.x);
        float hue01 = angle / (2f * Mathf.PI);
        if (hue01 < 0f) hue01 += 1f;

        _hue = hue01;
        _saturation = dist;
        UpdateColor();

        if (selectorHandle != null)
        {
            Vector2 clampedPos = (dist > 0f ? pos.normalized : Vector2.zero) * dist * radius;
            selectorHandle.anchoredPosition = clampedPos;
        }
    }

    private void OnBrightnessChanged(float v)
    {
        _value = v;
        UpdateColor();
    }

    private void UpdateColor()
    {
        Color color = Color.HSVToRGB(_hue, _saturation, _value);
        if (previewSwatch != null) previewSwatch.color = color;
        ActiveCanvas.Current?.SetBrushColor(color);
    }
}