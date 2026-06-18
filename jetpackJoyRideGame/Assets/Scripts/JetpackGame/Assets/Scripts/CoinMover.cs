using UnityEngine;
using System.Collections.Generic;

public class CoinMover : MonoBehaviour
{
    public float despawnX = -14f;
    public float bobAmplitude = 0.1f;
    public float bobSpeed = 3f;

    [HideInInspector] public Queue<GameObject> poolRef;

    private float startY;
    private float timeOffset;

    void OnEnable()
    {
        startY = transform.position.y;
        timeOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        if (!GameManager.Instance.IsPlaying) return;

        float speed = GameManager.Instance.CurrentSpeed;
        transform.position += Vector3.left * speed * Time.deltaTime;

        // Gentle bobbing
        float y = startY + Mathf.Sin(Time.time * bobSpeed + timeOffset) * bobAmplitude;
        transform.position = new Vector3(transform.position.x, y, transform.position.z);

        // Rotate (spinning coin effect)
        transform.Rotate(0, 180f * Time.deltaTime, 0);

        if (transform.position.x < despawnX)
        {
            gameObject.SetActive(false);
            if (poolRef != null) poolRef.Enqueue(gameObject);
        }
    }
}
