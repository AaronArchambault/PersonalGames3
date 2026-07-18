using UnityEngine;

/// <summary>
/// Attach this to each color swatch button. Set "Swatch Color" in the
/// Inspector to whatever color that button represents, then wire the
/// button's OnClick() to this script's SelectColor() method.
///
/// This exists because UnityEvent (used by Button.OnClick) only supports
/// dynamic arguments of type bool/float/int/string/Object in the Inspector —
/// Color isn't one of them, so DrawingCanvas.SetBrushColor(Color) can't be
/// wired directly.
/// </summary>
public class ColorSwatchButton : MonoBehaviour
{
    [SerializeField] private Color swatchColor = Color.black;

    /// <summary>Wire this to the button's OnClick().</summary>
    public void SelectColor()
    {
        ActiveCanvas.Current?.SetBrushColor(swatchColor);
    }
}