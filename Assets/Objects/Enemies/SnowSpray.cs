using UnityEngine;

// A chunk of snow kicked up from the ground (e.g. by a burrowing enemy): flies up, falls, shrinks away.
// Built from a small low poly box so it matches the rest of the art and needs no particle material.
public class SnowSpray : MonoBehaviour
{
    private const float Gravity = -9f;
    private static readonly Color Snow = new Color(0.93f, 0.96f, 1f);

    private Vector3 velocity;
    private Vector3 startScale;
    private float lifetime;
    private float age;

    // Throws `count` chunks up from a point on the ground; power scales how high and far they fly
    public static void Emit(Vector3 groundPoint, int count, float power = 1f)
    {
        for (int i = 0; i < count; i++)
        {
            var chunk = new GameObject("Snow chunk").transform;
            chunk.position = groundPoint + new Vector3(Random.Range(-0.2f, 0.2f), 0.05f, Random.Range(-0.2f, 0.2f));
            chunk.rotation = Random.rotation;
            float size = Random.Range(0.06f, 0.14f) * Mathf.Lerp(1f, 1.4f, power - 1f);
            LowPolyBuilder.Part(chunk, "Chunk", LowPolyBuilder.Box, Snow, Vector3.zero, Vector3.one * size);

            var spray = chunk.gameObject.AddComponent<SnowSpray>();
            Vector2 sideways = Random.insideUnitCircle * 1.5f * power;
            spray.velocity = new Vector3(sideways.x, Random.Range(2f, 4f) * power, sideways.y);
            spray.startScale = chunk.localScale;
            spray.lifetime = Random.Range(0.4f, 0.7f);
        }
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        velocity.y += Gravity * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        transform.localScale = startScale * (1f - age / lifetime);
    }
}
