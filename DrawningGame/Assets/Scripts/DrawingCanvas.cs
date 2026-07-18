
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles the drawable texture, all drawing tools, and syncs actions over the network.
/// Only the current drawer (set by GameManager) is allowed to draw.
/// Attach to a GameObject with a RawImage covering the drawing area.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class DrawingCanvas : NetworkBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IDrawable
{
    public enum ToolType { Brush, Eraser, Fill, Rectangle, Ellipse, ColorPicker }

    /// <summary>TurnBased = only GameManager's current drawer can draw (Skribbl mode).
    /// FreeForAll = anyone connected can draw at the same time (multiplayer Whiteboard mode).</summary>
    public enum DrawPermission { TurnBased, FreeForAll }

    [Header("Canvas Settings")]
    [SerializeField] private int textureWidth = 800;
    [SerializeField] private int textureHeight = 600;
    [SerializeField] private DrawPermission drawPermission = DrawPermission.TurnBased;

    [Header("Tool Settings")]
    [SerializeField] private ToolType currentTool = ToolType.Brush;
    [SerializeField] private Color brushColor = Color.black;
    [SerializeField] private int brushSize = 4;
    [SerializeField, Range(0f, 1f)] private float opacity = 1f;
    [SerializeField] private bool filledShapes = true;

    [Header("Undo/Redo")]
    [Tooltip("Each snapshot is a full copy of the canvas (~1.8MB at 800x600). Keep this modest.")]
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

    public static DrawingCanvas Instance { get; private set; }
    public ToolType CurrentTool => currentTool;

    private void Awake()
    {
        Instance = this;
        ActiveCanvas.Current = this;
        _rawImage = GetComponent<RawImage>();
        _rectTransform = GetComponent<RectTransform>();

        _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        ClearLocalTexture();
        _rawImage.texture = _texture;
    }

    // ---------- Public API used by UI buttons/sliders ----------

    public void SetTool(ToolType tool) => currentTool = tool;
    public void SetToolBrush() => currentTool = ToolType.Brush;
    public void SetToolEraser() => currentTool = ToolType.Eraser;
    public void SetToolFill() => currentTool = ToolType.Fill;
    public void SetToolRectangle() => currentTool = ToolType.Rectangle;
    public void SetToolEllipse() => currentTool = ToolType.Ellipse;
    public void SetToolColorPicker() => currentTool = ToolType.ColorPicker;

    /// <summary>Wire directly to color swatch buttons' OnClick (fixed Color argument).</summary>
    public void SetBrushColor(Color color)
    {
        color.a = 1f; // opacity is controlled separately via the opacity slider
        brushColor = color;
    }

    /// <summary>Wire directly to a Slider's OnValueChanged (float).</summary>
    public void SetBrushSize(float size) => brushSize = Mathf.Clamp(Mathf.RoundToInt(size), 1, 60);

    /// <summary>Wire directly to a Slider's OnValueChanged (float), range 0-1.</summary>
    public void SetOpacity(float a) => opacity = Mathf.Clamp01(a);

    public void SetFilledShapes(bool filled) => filledShapes = filled;

    /// <summary>Returns true if the local player is currently allowed to draw.</summary>
    private bool CanDraw()
    {
        if (drawPermission == DrawPermission.FreeForAll) return true;
        if (GameManager.Instance == null) return false;
        return GameManager.Instance.CurrentDrawerClientId.Value == NetworkManager.Singleton.LocalClientId;
    }

    private Color EffectiveColor => currentTool == ToolType.Eraser
        ? new Color(1f, 1f, 1f, 1f) // eraser always paints opaque white (canvas background)
        : new Color(brushColor.r, brushColor.g, brushColor.b, opacity);

    // ---------- Input handling ----------

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanDraw()) return;
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
                EndAction(broadcastFullCanvas: true);
                break;

            case ToolType.Rectangle:
            case ToolType.Ellipse:
                BeginAction();
                _shapeStartPoint = point;
                break;

            default: // Brush / Eraser
                BeginAction();
                _lastLocalPoint = point;
                break;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!CanDraw() || !_actionInProgress) return;
        Vector2 point = ScreenToTextureCoords(eventData.position);

        switch (currentTool)
        {
            case ToolType.Brush:
            case ToolType.Eraser:
                Vector2 from = _lastLocalPoint ?? point;
                Color c = EffectiveColor;
                DrawLineOnTexture(from, point, c, brushSize);
                SendStrokeServerRpc(from, point, c, brushSize);
                _lastLocalPoint = point;
                break;

            case ToolType.Rectangle:
            case ToolType.Ellipse:
                // Live preview: restore the pre-action state, then redraw the shape at the current size
                _texture.LoadRawTextureData(_preActionSnapshot);
                DrawShape(_shapeStartPoint, point);
                _texture.Apply();
                break;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_actionInProgress) return;

        switch (currentTool)
        {
            case ToolType.Rectangle:
            case ToolType.Ellipse:
                EndAction(broadcastFullCanvas: true);
                break;

            default: // Brush / Eraser — points already streamed live, no extra broadcast needed
                EndAction(broadcastFullCanvas: false);
                break;
        }

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

    // ---------- Action lifecycle (for undo/redo) ----------

    private void BeginAction()
    {
        _preActionSnapshot = _texture.GetRawTextureData();
        _actionInProgress = true;
    }

    private void EndAction(bool broadcastFullCanvas)
    {
        PushUndo(_preActionSnapshot);
        _preActionSnapshot = null;
        _actionInProgress = false;

        if (broadcastFullCanvas)
            BroadcastFullCanvas();
    }

    private void PushUndo(byte[] snapshot)
    {
        _undoStack.Add(snapshot);
        if (_undoStack.Count > maxHistorySteps)
            _undoStack.RemoveAt(0);
        _redoStack.Clear();
    }

    /// <summary>Wire directly to an Undo button's OnClick.</summary>
    public void Undo()
    {
        if (!CanDraw() || _undoStack.Count == 0) return;

        byte[] current = _texture.GetRawTextureData();
        _redoStack.Add(current);

        byte[] previous = _undoStack[_undoStack.Count - 1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _texture.LoadRawTextureData(previous);
        _texture.Apply();
        BroadcastFullCanvas();
    }

    /// <summary>Wire directly to a Redo button's OnClick.</summary>
    public void Redo()
    {
        if (!CanDraw() || _redoStack.Count == 0) return;

        byte[] current = _texture.GetRawTextureData();
        _undoStack.Add(current);

        byte[] next = _redoStack[_redoStack.Count - 1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        _texture.LoadRawTextureData(next);
        _texture.Apply();
        BroadcastFullCanvas();
    }

    // ---------- Networking: continuous strokes (brush/eraser) ----------

    [ServerRpc(RequireOwnership = false)]
    private void SendStrokeServerRpc(Vector2 from, Vector2 to, Color color, int size, ServerRpcParams rpcParams = default)
    {
        if (drawPermission == DrawPermission.TurnBased &&
            rpcParams.Receive.SenderClientId != GameManager.Instance.CurrentDrawerClientId.Value)
            return;

        ReceiveStrokeClientRpc(from, to, color, size, rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ReceiveStrokeClientRpc(Vector2 from, Vector2 to, Color color, int size, ulong senderClientId)
    {
        if (senderClientId == NetworkManager.Singleton.LocalClientId) return;
        DrawLineOnTexture(from, to, color, size);
    }

    // ---------- Networking: full-canvas sync (fill, shapes, undo, redo) ----------

    private void BroadcastFullCanvas()
    {
        byte[] png = _texture.EncodeToPNG();
        SendFullCanvasServerRpc(png);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendFullCanvasServerRpc(byte[] pngData, ServerRpcParams rpcParams = default)
    {
        if (drawPermission == DrawPermission.TurnBased &&
            rpcParams.Receive.SenderClientId != GameManager.Instance.CurrentDrawerClientId.Value)
            return;

        ReceiveFullCanvasClientRpc(pngData, rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ReceiveFullCanvasClientRpc(byte[] pngData, ulong senderClientId)
    {
        if (senderClientId == NetworkManager.Singleton.LocalClientId) return;
        _texture.LoadImage(pngData);
    }

    // ---------- Networking: clear canvas (round reset) ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestClearServerRpc()
    {
        ClearClientRpc();
    }

    [ClientRpc]
    private void ClearClientRpc()
    {
        ClearLocalTexture();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    // ---------- Core drawing helpers ----------

    private void ClearLocalTexture()
    {
        Color[] fill = new Color[textureWidth * textureHeight];
        for (int i = 0; i < fill.Length; i++) fill[i] = Color.white;
        _texture.SetPixels(fill);
        _texture.Apply();
    }

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
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y > radius * radius) continue;
                SetBlendedPixel(cx + x, cy + y, color);
            }
        }
    }

    private void SetBlendedPixel(int x, int y, Color color)
    {
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;
        Color existing = _texture.GetPixel(x, y);
        Color blended = Color.Lerp(existing, new Color(color.r, color.g, color.b, 1f), color.a);
        _texture.SetPixel(x, y, blended);
    }

    // ---------- Shapes (rectangle / ellipse) ----------

    private void DrawShape(Vector2 start, Vector2 end)
    {
        if (currentTool == ToolType.Rectangle)
            DrawRectangle(start, end, EffectiveColor, filledShapes, brushSize);
        else if (currentTool == ToolType.Ellipse)
            DrawEllipse(start, end, EffectiveColor, filledShapes, brushSize);
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
                else
                {
                    if (dist <= 1f && dist >= (1f - thicknessNormalized) * (1f - thicknessNormalized))
                        SetBlendedPixel(x, y, color);
                }
            }
        }
    }

    // ---------- Fill bucket ----------

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

    // ---------- Color picker (eyedropper) ----------

    private void PickColor(Vector2 point)
    {
        int x = Mathf.RoundToInt(point.x);
        int y = Mathf.RoundToInt(point.y);
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;

        Color picked = _texture.GetPixel(x, y);
        SetBrushColor(picked);
        currentTool = ToolType.Brush; // convenience: switch back to brush after picking
        UIManager.Instance?.OnColorPicked(picked);
    }
}


























/*using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles the drawable texture, all drawing tools, and syncs actions over the network.
/// Only the current drawer (set by GameManager) is allowed to draw.
/// Attach to a GameObject with a RawImage covering the drawing area.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class DrawingCanvas : NetworkBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
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

    [Header("Undo/Redo")]
    [Tooltip("Each snapshot is a full copy of the canvas (~1.8MB at 800x600). Keep this modest.")]
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

    public static DrawingCanvas Instance { get; private set; }
    public ToolType CurrentTool => currentTool;

    private void Awake()
    {
        Instance = this;
        _rawImage = GetComponent<RawImage>();
        _rectTransform = GetComponent<RectTransform>();

        _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        ClearLocalTexture();
        _rawImage.texture = _texture;
    }

    // ---------- Public API used by UI buttons/sliders ----------

    public void SetTool(ToolType tool) => currentTool = tool;
    public void SetToolBrush() => currentTool = ToolType.Brush;
    public void SetToolEraser() => currentTool = ToolType.Eraser;
    public void SetToolFill() => currentTool = ToolType.Fill;
    public void SetToolRectangle() => currentTool = ToolType.Rectangle;
    public void SetToolEllipse() => currentTool = ToolType.Ellipse;
    public void SetToolColorPicker() => currentTool = ToolType.ColorPicker;

    /// <summary>Wire directly to color swatch buttons' OnClick (fixed Color argument).</summary>
    public void SetBrushColor(Color color)
    {
        color.a = 1f; // opacity is controlled separately via the opacity slider
        brushColor = color;
    }

    /// <summary>Wire directly to a Slider's OnValueChanged (float).</summary>
    public void SetBrushSize(float size) => brushSize = Mathf.Clamp(Mathf.RoundToInt(size), 1, 60);

    /// <summary>Wire directly to a Slider's OnValueChanged (float), range 0-1.</summary>
    public void SetOpacity(float a) => opacity = Mathf.Clamp01(a);

    public void SetFilledShapes(bool filled) => filledShapes = filled;

    /// <summary>Returns true if the local player is currently allowed to draw.</summary>
    private bool CanDraw()
    {
        if (GameManager.Instance == null) return false;
        return GameManager.Instance.CurrentDrawerClientId.Value == NetworkManager.Singleton.LocalClientId;
    }

    private Color EffectiveColor => currentTool == ToolType.Eraser
        ? new Color(1f, 1f, 1f, 1f) // eraser always paints opaque white (canvas background)
        : new Color(brushColor.r, brushColor.g, brushColor.b, opacity);

    // ---------- Input handling ----------

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanDraw()) return;
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
                EndAction(broadcastFullCanvas: true);
                break;

            case ToolType.Rectangle:
            case ToolType.Ellipse:
                BeginAction();
                _shapeStartPoint = point;
                break;

            default: // Brush / Eraser
                BeginAction();
                _lastLocalPoint = point;
                break;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!CanDraw() || !_actionInProgress) return;
        Vector2 point = ScreenToTextureCoords(eventData.position);

        switch (currentTool)
        {
            case ToolType.Brush:
            case ToolType.Eraser:
                Vector2 from = _lastLocalPoint ?? point;
                Color c = EffectiveColor;
                DrawLineOnTexture(from, point, c, brushSize);
                SendStrokeServerRpc(from, point, c, brushSize);
                _lastLocalPoint = point;
                break;

            case ToolType.Rectangle:
            case ToolType.Ellipse:
                // Live preview: restore the pre-action state, then redraw the shape at the current size
                _texture.LoadRawTextureData(_preActionSnapshot);
                DrawShape(_shapeStartPoint, point);
                _texture.Apply();
                break;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (!_actionInProgress) return;

        switch (currentTool)
        {
            case ToolType.Rectangle:
            case ToolType.Ellipse:
                EndAction(broadcastFullCanvas: true);
                break;

            default: // Brush / Eraser — points already streamed live, no extra broadcast needed
                EndAction(broadcastFullCanvas: false);
                break;
        }

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

    // ---------- Action lifecycle (for undo/redo) ----------

    private void BeginAction()
    {
        _preActionSnapshot = _texture.GetRawTextureData();
        _actionInProgress = true;
    }

    private void EndAction(bool broadcastFullCanvas)
    {
        PushUndo(_preActionSnapshot);
        _preActionSnapshot = null;
        _actionInProgress = false;

        if (broadcastFullCanvas)
            BroadcastFullCanvas();
    }

    private void PushUndo(byte[] snapshot)
    {
        _undoStack.Add(snapshot);
        if (_undoStack.Count > maxHistorySteps)
            _undoStack.RemoveAt(0);
        _redoStack.Clear();
    }

    /// <summary>Wire directly to an Undo button's OnClick.</summary>
    public void Undo()
    {
        if (!CanDraw() || _undoStack.Count == 0) return;

        byte[] current = _texture.GetRawTextureData();
        _redoStack.Add(current);

        byte[] previous = _undoStack[_undoStack.Count - 1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        _texture.LoadRawTextureData(previous);
        _texture.Apply();
        BroadcastFullCanvas();
    }

    /// <summary>Wire directly to a Redo button's OnClick.</summary>
    public void Redo()
    {
        if (!CanDraw() || _redoStack.Count == 0) return;

        byte[] current = _texture.GetRawTextureData();
        _undoStack.Add(current);

        byte[] next = _redoStack[_redoStack.Count - 1];
        _redoStack.RemoveAt(_redoStack.Count - 1);

        _texture.LoadRawTextureData(next);
        _texture.Apply();
        BroadcastFullCanvas();
    }

    // ---------- Networking: continuous strokes (brush/eraser) ----------

    [ServerRpc(RequireOwnership = false)]
    private void SendStrokeServerRpc(Vector2 from, Vector2 to, Color color, int size, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != GameManager.Instance.CurrentDrawerClientId.Value)
            return;

        ReceiveStrokeClientRpc(from, to, color, size, rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ReceiveStrokeClientRpc(Vector2 from, Vector2 to, Color color, int size, ulong senderClientId)
    {
        if (senderClientId == NetworkManager.Singleton.LocalClientId) return;
        DrawLineOnTexture(from, to, color, size);
    }

    // ---------- Networking: full-canvas sync (fill, shapes, undo, redo) ----------

    private void BroadcastFullCanvas()
    {
        byte[] png = _texture.EncodeToPNG();
        SendFullCanvasServerRpc(png);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendFullCanvasServerRpc(byte[] pngData, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != GameManager.Instance.CurrentDrawerClientId.Value)
            return;

        ReceiveFullCanvasClientRpc(pngData, rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ReceiveFullCanvasClientRpc(byte[] pngData, ulong senderClientId)
    {
        if (senderClientId == NetworkManager.Singleton.LocalClientId) return;
        _texture.LoadImage(pngData);
    }

    // ---------- Networking: clear canvas (round reset) ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestClearServerRpc()
    {
        ClearClientRpc();
    }

    [ClientRpc]
    private void ClearClientRpc()
    {
        ClearLocalTexture();
        _undoStack.Clear();
        _redoStack.Clear();
    }

    // ---------- Core drawing helpers ----------

    private void ClearLocalTexture()
    {
        Color[] fill = new Color[textureWidth * textureHeight];
        for (int i = 0; i < fill.Length; i++) fill[i] = Color.white;
        _texture.SetPixels(fill);
        _texture.Apply();
    }

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
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y > radius * radius) continue;
                SetBlendedPixel(cx + x, cy + y, color);
            }
        }
    }

    private void SetBlendedPixel(int x, int y, Color color)
    {
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;
        Color existing = _texture.GetPixel(x, y);
        Color blended = Color.Lerp(existing, new Color(color.r, color.g, color.b, 1f), color.a);
        _texture.SetPixel(x, y, blended);
    }

    // ---------- Shapes (rectangle / ellipse) ----------

    private void DrawShape(Vector2 start, Vector2 end)
    {
        if (currentTool == ToolType.Rectangle)
            DrawRectangle(start, end, EffectiveColor, filledShapes, brushSize);
        else if (currentTool == ToolType.Ellipse)
            DrawEllipse(start, end, EffectiveColor, filledShapes, brushSize);
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
                else
                {
                    if (dist <= 1f && dist >= (1f - thicknessNormalized) * (1f - thicknessNormalized))
                        SetBlendedPixel(x, y, color);
                }
            }
        }
    }

    // ---------- Fill bucket ----------

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

    // ---------- Color picker (eyedropper) ----------

    private void PickColor(Vector2 point)
    {
        int x = Mathf.RoundToInt(point.x);
        int y = Mathf.RoundToInt(point.y);
        if (x < 0 || x >= textureWidth || y < 0 || y >= textureHeight) return;

        Color picked = _texture.GetPixel(x, y);
        SetBrushColor(picked);
        currentTool = ToolType.Brush; // convenience: switch back to brush after picking
        UIManager.Instance?.OnColorPicked(picked);
    }
}*/