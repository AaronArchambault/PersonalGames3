using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Handles the drawable texture and syncs strokes over the network.
/// Only the current drawer (set by GameManager) is allowed to draw.
/// Attach to a GameObject with a RawImage covering the drawing area.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class DrawingCanvas : NetworkBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("Canvas Settings")]
    [SerializeField] private int textureWidth = 800;
    [SerializeField] private int textureHeight = 600;
    [SerializeField] private int brushSize = 4;
    [SerializeField] private Color brushColor = Color.black;

    private Texture2D _texture;
    private RawImage _rawImage;
    private RectTransform _rectTransform;
    private Vector2? _lastLocalPoint;

    public static DrawingCanvas Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        _rawImage = GetComponent<RawImage>();
        _rectTransform = GetComponent<RectTransform>();

        _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        ClearLocalTexture();
        _rawImage.texture = _texture;
    }

    // ---------- Public API used by UI (color/size pickers) ----------

    public void SetBrushColor(Color color) => brushColor = color;
    public void SetBrushSize(int size) => brushSize = Mathf.Clamp(size, 1, 40);

    /// <summary>Returns true if the local player is currently allowed to draw.</summary>
    private bool CanDraw()
    {
        if (GameManager.Instance == null) return false;
        return GameManager.Instance.CurrentDrawerClientId.Value == NetworkManager.Singleton.LocalClientId;
    }

    // ---------- Input handling ----------

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanDraw()) return;
        _lastLocalPoint = ScreenToTextureCoords(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!CanDraw()) return;

        Vector2 current = ScreenToTextureCoords(eventData.position);
        Vector2 from = _lastLocalPoint ?? current;

        // Draw locally right away for zero-latency feedback
        DrawLineOnTexture(from, current, brushColor, brushSize);

        // Tell the server, which relays to everyone else
        SendStrokeServerRpc(from, current, brushColor, brushSize);

        _lastLocalPoint = current;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
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

    // ---------- Networking ----------

    [ServerRpc(RequireOwnership = false)]
    private void SendStrokeServerRpc(Vector2 from, Vector2 to, Color color, int size, ServerRpcParams rpcParams = default)
    {
        // Validate: only the designated drawer's strokes are relayed
        if (rpcParams.Receive.SenderClientId != GameManager.Instance.CurrentDrawerClientId.Value)
            return;

        ReceiveStrokeClientRpc(from, to, color, size, rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ReceiveStrokeClientRpc(Vector2 from, Vector2 to, Color color, int size, ulong senderClientId)
    {
        // Don't redraw for the sender, they already drew it locally
        if (senderClientId == NetworkManager.Singleton.LocalClientId) return;
        DrawLineOnTexture(from, to, color, size);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestClearServerRpc()
    {
        ClearClientRpc();
    }

    [ClientRpc]
    private void ClearClientRpc()
    {
        ClearLocalTexture();
    }

    // ---------- Drawing helpers ----------

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
                int px = cx + x;
                int py = cy + y;
                if (px < 0 || px >= textureWidth || py < 0 || py >= textureHeight) continue;
                _texture.SetPixel(px, py, color);
            }
        }
    }
}