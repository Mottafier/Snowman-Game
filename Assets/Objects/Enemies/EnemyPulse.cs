using UnityEngine;

// A flat disc that quickly grows outward and disappears: shockwaves, heals and enrage bursts.
public class EnemyPulse : MonoBehaviour
{
    private float radius;
    private float seconds;
    private float age;

    public static void Spawn(Vector3 position, Color color, float radius, float seconds = 0.35f)
    {
        var go = new GameObject("Pulse");
        go.transform.position = position;
        go.transform.localScale = Vector3.zero;
        LowPolyBuilder.Part(go.transform, "Disc", LowPolyBuilder.Cylinder, color, Vector3.zero, new Vector3(1f, 0.04f, 1f));

        var pulse = go.AddComponent<EnemyPulse>();
        pulse.radius = radius;
        pulse.seconds = seconds;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= seconds)
        {
            Destroy(gameObject);
            return;
        }

        float diameter = radius * 2f * (age / seconds);
        transform.localScale = new Vector3(diameter, 1f, diameter);
    }
}
