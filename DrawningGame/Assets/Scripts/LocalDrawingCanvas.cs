using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Same toolset as DrawingCanvas (brush, eraser, fill, shapes, opacity, undo/redo,
/// color picker) but purely local — no networking. Used for:
///  - Solo Whiteboard mode
///  - Telephone/Gartic-Phone-style drawing tasks (each player draws privately,
///    then the finished image is submitted as a whole via GetPngBytes()).
/// </summary>
[RequireComponent(typeof(RawImage))]
public class LocalDrawingCanvas : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IDrawable
{
    public enum ToolType { Brush, Eraser, Fill, Rectangle, Ellipse, ColorPicker }

    [Header("Canvas Settings")]
    [SerializeField] private int textureWidth = 800;
    [SerializeField] private int textureHeight = 600;

    [Header("Tool Settings")]
    [SerializeField] private ToolType currentTool = ToolType.Brush;
    [SerializeField] private Color brushColor = Color.black;
    [SerializeField] private int brushSize = 4;
    [SerializeField, Range(0f, 1f)] private float opacity = 1f;
    [SerializeField] private bool filledShapes = true;
    [SerializeField] private int maxHistorySteps = 12;

    private Texture2D _texture;
    private RawImage _rawImage;
    private RectTransform _rectTransform;

    private Vector2? _lastLocalPoint;
    private Vector2 _shapeStartPoint;
    private byte[] _preActionSnapshot;
    private bool _actionInProgress;

    private readonly List<byte[]> _undoStack = new List<byte[]>();
    private readonly List<byte[]> _redoStack = new List<byte[]>();

    public static LocalDrawingCanvas Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        ActiveCanvas.Current = this;
        _rawImage = GetComponent<RawImage>();
        _rectTransform = GetComponent<RectTransform>();

        _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        ClearCanvas();
        _rawImage.texture = _texture;
    }

    // ---------- Public API ----------

    public void SetTool(ToolType tool) => currentTool = tool;
    public void SetToolBrush() => currentTool = ToolType.Brush;
    public void SetToolEraser() => currentTool = ToolType.Eraser;
    public void SetToolFill() => currentTool = ToolType.Fill;
    public void SetToolRectangle() => currentTool = ToolType.Rectangle;
    public void SetToolEllipse() => currentTool = ToolType.Ellipse;
    public void SetToolColorPicker() => currentTool = ToolType.ColorPicker;

    public void SetBrushColor(Color color) { color.a = 1f; brushColor = color; }
    public void SetBrushSize(float size) => brushSize = Mathf.Clamp(Mathf.RoundToInt(size), 1, 60);
    public void SetOpacity(float a) => opacity = Mathf.Clamp01(a);
    public void SetFilledShapes(bool filled) => filledShapes = filled;

    /// <summary>Clears the canvas back to blank white and resets undo/redo history.</summary>
    public void ClearCanvas()
    {
        Color[] fill = new Color[textureWidth * textureHeight];
        for (int i = 0; i < fill.Length; i++) fill[i] = Color.white;
        _texture.SetPixels(fill);
        _texture.Apply();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    /// <summary>Exports the current canvas as PNG bytes (e.g. to submit a Telephone drawing).</summary>
    public byte[] GetPngBytes() => _texture.EncodeToPNG();

    /// <summary>Loads a starting image onto the canvas (e.g. the previous frame to trace over, or a reference to caption). Resets undo/redo history.</summary>
    public void LoadFromPng(byte[] pngData)
    {
        _texture.LoadImage(pngData);
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private Color EffectiveColor => currentTool == ToolType.Eraser
        ? new Color(1f, 1f, 1f, 1f)
        : new Color(brushColor.r, brushColor.g, brushColor.b, opacity);

    // ---------- Input ----------

    public void OnPointerDown(PointerEventData eventData)
    {
        Vector2 point = ScreenToTextureCoords(eventData.position);

        switch (currentTool)
        {
            case ToolType.ColorPicker:
                PickColor(point);
                break;

            case ToolType.Fill:
                BeginAction();
                FloodFill(point, EffectiveColor);
                _texture.Apply();
                EndAction();
                break;

            case ToolType.Rectangle:
            case ToolType.Ellipse:
                BeginAction();
                _shapeStartPoint = point;
                break;

            default:
                BeginAction();
                _lastLocalPoint = point;
                break;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_actionInProgress) return;
        Vector2 point = ScreenToTextureCoords(eventData.position);

        switch (currentTool)
        {
            case ToolType.Brush:
            case ToolType.Eraser:
                Vector2 from = _lastLocalPoint ?? point;
                DrawLineOnTexture(from, point, EffectiveColor, brushSize);
                _lastLocalPoint = point;
                break;

            case ToolType.Rectangle:
            case ToolType.Ellipse:
                _texture.LoadRawTextureData(_preActionSnapshot);
                DrawShape(_shapeStartPoint, point);
                _texture.Apply();
                break;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_actionInProgress) return;
        _texture.Apply();
        EndAction();
        _lastLocalPoint = null;
    }

    private Vector2 ScreenToTextureCoords(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _rectTransform, screenPos, null, out Vector2 localPoint);

        Rect rect = _rectTransform.rect;
        float u = (localPoint.x - rect.x) / rect.width;
        float v = (localPoint.y - rect.y) / rect.height;

        return new Vector2(u * textureWidth, v * textureHeight);
    }

    // ---------- Undo / redo ----------

    private void BeginAction()
    {
        _preActionSnapshot = _texture.GetRawTextureData();
        _actionInProgress = true;
    }

    private void EndAction()
    {
        _undoStack.Add(_preActionSnapshot);
        if (_undoStack.Count > maxHistorySteps) _undoStack.RemoveAt(0);
        _redoStack.Clear();
        _preActionSnapshot = null;
        _actionInProgress = false;
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Add(_texture.GetRawTextureData());
        byte[] previous = _undoStack[_undoStack.Count - 1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _texture.LoadRawTextureData(previous);
        _texture.Apply();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Add(_texture.GetRawTextureData());
        byte[] next = _redoStack[_redoStack.Count - 1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _texture.LoadRawTextureData(next);
        _texture.Apply();
    }

    // ---------- Drawing helpers (identical approach to DrawingCanvas) ----------

    private void DrawLineOnTexture(Vector2 from, Vector2 to, Color color, int size)
    {
        float distance = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 point = Vector2.Lerp(from, to, i / (float)steps);
            DrawCircle(point, size, color);
        }
        _texture.Apply();
    }

    private void DrawCircle(Vector2 center, int radius, Color color)
    {
        int cx = Mathf.RoundToInt(center.x);
        int cy = Mathf.RoundToInt(center.y);
        for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y > radius * radius) continue;
                SetBlendedPixel(cx + x, cy + y, color);
            }
    }

    private void SetBlendedPixel(int x, int y, Color color)
    {
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;
        Color existing = _texture.GetPixel(x, y);
        Color blended = Color.Lerp(existing, new Color(color.r, color.g, color.b, 1f), color.a);
        _texture.SetPixel(x, y, blended);
    }

    private void DrawShape(Vector2 start, Vector2 end)
    {
        if (currentTool == ToolType.Rectangle) DrawRectangle(start, end, EffectiveColor, filledShapes, brushSize);
        else if (currentTool == ToolType.Ellipse) DrawEllipse(start, end, EffectiveColor, filledShapes, brushSize);
    }

    private void DrawRectangle(Vector2 a, Vector2 b, Color color, bool filled, int thickness)
    {
        int minX = Mathf.RoundToInt(Mathf.Min(a.x, b.x));
        int maxX = Mathf.RoundToInt(Mathf.Max(a.x, b.x));
        int minY = Mathf.RoundToInt(Mathf.Min(a.y, b.y));
        int maxY = Mathf.RoundToInt(Mathf.Max(a.y, b.y));

        if (filled)
        {
            for (int x = minX; x <= maxX; x++)
                for (int y = minY; y <= maxY; y++)
                    SetBlendedPixel(x, y, color);
        }
        else
        {
            DrawLineOnTexture(new Vector2(minX, minY), new Vector2(maxX, minY), color, thickness);
            DrawLineOnTexture(new Vector2(maxX, minY), new Vector2(maxX, maxY), color, thickness);
            DrawLineOnTexture(new Vector2(maxX, maxY), new Vector2(minX, maxY), color, thickness);
            DrawLineOnTexture(new Vector2(minX, maxY), new Vector2(minX, minY), color, thickness);
        }
    }

    private void DrawEllipse(Vector2 a, Vector2 b, Color color, bool filled, int thickness)
    {
        float cx = (a.x + b.x) / 2f;
        float cy = (a.y + b.y) / 2f;
        float rx = Mathf.Abs(a.x - b.x) / 2f;
        float ry = Mathf.Abs(a.y - b.y) / 2f;
        if (rx < 1f || ry < 1f) return;

        int minX = Mathf.FloorToInt(cx - rx);
        int maxX = Mathf.CeilToInt(cx + rx);
        int minY = Mathf.FloorToInt(cy - ry);
        int maxY = Mathf.CeilToInt(cy + ry);
        float thicknessNormalized = thickness / Mathf.Max(rx, ry);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                float nx = (x - cx) / rx;
                float ny = (y - cy) / ry;
                float dist = nx * nx + ny * ny;

                if (filled)
                {
                    if (dist <= 1f) SetBlendedPixel(x, y, color);
                }
                else if (dist <= 1f && dist >= (1f - thicknessNormalized) * (1f - thicknessNormalized))
                {
                    SetBlendedPixel(x, y, color);
                }
            }
        }
    }

    private void FloodFill(Vector2 startPoint, Color fillColor)
    {
        int startX = Mathf.RoundToInt(startPoint.x);
        int startY = Mathf.RoundToInt(startPoint.y);
        if (startX < 0 || startX >= textureWidth || startY < 0 || startY >= textureHeight) return;

        Color32[] pixels = _texture.GetPixels32();
        Color32 target = pixels[startY * textureWidth + startX];
        Color32 fill = fillColor;
        if (Color32ApproxEqual(target, fill)) return;

        bool[] visited = new bool[pixels.Length];
        var stack = new Stack<int>();
        stack.Push(startY * textureWidth + startX);

        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            if (visited[idx]) continue;
            visited[idx] = true;
            if (!Color32ApproxEqual(pixels[idx], target)) continue;

            pixels[idx] = fill;
            int x = idx % textureWidth;
            int y = idx / textureWidth;
            if (x > 0) stack.Push(idx - 1);
            if (x < textureWidth - 1) stack.Push(idx + 1);
            if (y > 0) stack.Push(idx - textureWidth);
            if (y < textureHeight - 1) stack.Push(idx + textureWidth);
        }
        _texture.SetPixels32(pixels);
    }

    private static bool Color32ApproxEqual(Color32 a, Color32 b, int tolerance = 12)
    {
        return Mathf.Abs(a.r - b.r) <= tolerance &&
               Mathf.Abs(a.g - b.g) <= tolerance &&
               Mathf.Abs(a.b - b.b) <= tolerance &&
               Mathf.Abs(a.a - b.a) <= tolerance;
    }

    private void PickColor(Vector2 point)
    {
        int x = Mathf.RoundToInt(point.x);
        int y = Mathf.RoundToInt(point.y);
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;

        Color picked = _texture.GetPixel(x, y);
        SetBrushColor(picked);
        currentTool = ToolType.Brush;
    }
}