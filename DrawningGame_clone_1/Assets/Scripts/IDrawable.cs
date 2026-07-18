using UnityEngine;

/// <summary>
/// Implemented by any drawing canvas (networked or local) so color tools
/// (ColorSwatchButton, ColorWheelPicker) can work regardless of which
/// game mode / scene is currently active.
/// </summary>
public interface IDrawable
{
    void SetBrushColor(Color color);
    void SetBrushSize(float size);
    void SetOpacity(float opacity);
}

/// <summary>
/// Points to whichever drawing canvas is active in the currently loaded scene.
/// Each canvas type sets this in its own Awake().
/// </summary>
public static class ActiveCanvas
{
    public static IDrawable Current { get; set; }
}