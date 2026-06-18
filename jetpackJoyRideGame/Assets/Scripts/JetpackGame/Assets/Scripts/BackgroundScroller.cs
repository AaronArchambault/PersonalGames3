using UnityEngine;

/// <summary>
/// Handles infinite parallax background scrolling.
/// Attach one of these to each background layer.
/// </summary>
public class BackgroundScroller : MonoBehaviour
{
    [System.Serializable]
    public class ScrollLayer
    {
        public Transform[] tiles;        // 2-3 copies of the background tile
        public float parallaxFactor;     // 0 = stationary, 1 = full speed
        public float tileWidth;          // Width of one tile in world units
    }

    [Header("Layers (back to front)")]
    public ScrollLayer[] layers;

    [Header("Settings")]
    public float resetX = -20f;   // When a tile goes past this, wrap it
    public float wrapX = 20f;     // Wrap to this position

    private bool scrolling = false;

    public void SetScrolling(bool value) => scrolling = value;

    void Update()
    {
        if (!scrolling || !GameManager.Instance.IsPlaying) return;

        float speed = GameManager.Instance.CurrentSpeed;

        foreach (var layer in layers)
        {
            if (layer.tiles == null) continue;
            float scrollAmount = speed * layer.parallaxFactor * Time.deltaTime;

            foreach (var tile in layer.tiles)
            {
                if (tile == null) continue;
                tile.position += Vector3.left * scrollAmount;

                // Wrap tile when it exits left
                if (tile.position.x < resetX)
                {
                    // Find rightmost tile in this layer
                    float maxX = float.MinValue;
                    foreach (var t in layer.tiles)
                        if (t != null && t.position.x > maxX)
                            maxX = t.position.x;

                    tile.position = new Vector3(maxX + layer.tileWidth, tile.position.y, tile.position.z);
                }
            }
        }
    }
}
