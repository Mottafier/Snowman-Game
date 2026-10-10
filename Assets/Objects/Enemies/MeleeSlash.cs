using System.Collections.Generic;
using UnityEngine;

// The swipe for melee enemies: a gust of air that whooshes across the front of the enemy from left to right,
// trailing wisps of air. A thin arc on the ground shows how far the hit will reach while the enemy winds up.
public class MeleeSlash : MonoBehaviour
{
    public const float HalfArc = 60f; // degrees either side of the aim direction

    private const float StepDegrees = 4f;
    private const float FadeSeconds = 0.2f;
    private const float TrailLength = 0.65f; // how much of the sweep stays visible behind the head
    private const float ParticlesPerSecond = 260f;

    // r = how far out, h = how tall, a = how solid. Overlapping layers make it look wispy.
    private static readonly Vector3[] Layers =
    {
        new Vector3(0.95f, 1.00f, 0.55f),
        new Vector3(0.80f, 0.70f, 0.45f),
        new Vector3(0.65f, 0.45f, 0.35f),
    };

    private static Material material;

    private float reach;
    private float midHeight;   // how high above the ground the gust travels
    private float halfHeight;  // how tall the gust is, from its middle
    private float warnSeconds;
    private float age;
    private float strikeStart = -1f;
    private float strikeSeconds;

    private Mesh warnMesh;
    private Mesh gustMesh;
    private MeshRenderer warnRenderer;
    private ParticleSystem air;

    private readonly List<Vector3> verts = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> tris = new List<int>();

    // `position` is on the ground under the enemy, `direction` is flat, `bodyHeight` is roughly how tall the enemy is
    public static MeleeSlash Show(Vector3 position, Vector3 direction, float reach, float warnSeconds, float bodyHeight)
    {
        var go = new GameObject("MeleeSlash");
        go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        var slash = go.AddComponent<MeleeSlash>();
        slash.reach = reach;
        slash.warnSeconds = Mathf.Max(warnSeconds, 0.01f);
        slash.midHeight = Mathf.Max(bodyHeight * 0.5f, 0.5f);
        slash.halfHeight = Mathf.Max(bodyHeight * 0.45f, 0.45f);
        slash.Build();
        return slash;
    }

    // Start the sweep; it should end right when the damage lands
    public void Strike(float seconds)
    {
        strikeStart = age;
        strikeSeconds = Mathf.Max(seconds, 0.01f);
    }

    // True if `toPlayer` (flat, from the enemy's centre) falls inside the swipe's angle
    public static bool InArc(Vector3 aim, Vector3 toPlayer)
    {
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f)
            return true;
        return Vector3.Angle(aim, toPlayer) <= HalfArc;
    }

    // ---------- Setup ----------

    private void Build()
    {
        if (material == null)
            material = new Material(Shader.Find("Sprites/Default")); // transparent, double-sided, reads vertex colours

        warnMesh = BuildWarning();
        warnRenderer = AddMesh("Warning", warnMesh);
        gustMesh = new Mesh { name = "Gust" };
        gustMesh.MarkDynamic();
        AddMesh("Gust", gustMesh);
        BuildParticles();
    }

    private MeshRenderer AddMesh(string name, Mesh mesh)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private void BuildParticles()
    {
        var go = new GameObject("Air");
        go.transform.SetParent(transform, false);
        air = go.AddComponent<ParticleSystem>();
        air.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = air.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = 0.4f;
        main.startSpeed = 0f;
        main.startSize = 0.07f;
        main.maxParticles = 300;

        var emission = air.emission;
        emission.enabled = false;
        var shape = air.shape;
        shape.enabled = false;

        // Fade out over their life
        var fade = air.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.95f, 1f), 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        // Thin streaks that point the way they're flying
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = material;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 5f;
        renderer.velocityScale = 0.04f;
        renderer.cameraVelocityScale = 0f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ---------- Each frame ----------

    void Update()
    {
        age += Time.deltaTime;

        if (strikeStart < 0f)
        {
            // Wind-up: the reach marker fades in
            SetWarnAlpha(Mathf.Clamp01(age / warnSeconds));
            return;
        }

        float sinceStrike = age - strikeStart;
        float progress = Mathf.Clamp01(sinceStrike / strikeSeconds);
        float afterglow = Mathf.Clamp01((sinceStrike - strikeSeconds) / FadeSeconds);
        SetWarnAlpha(1f - Mathf.Max(progress * 0.4f, afterglow));

        FillGust(progress, 1f - afterglow);
        if (progress < 1f)
            EmitAir(progress);

        // Stay around until the last streaks of air have faded
        if (sinceStrike > strikeSeconds + 0.55f)
            Destroy(gameObject);
    }

    // Sweep from the viewer's left to right: +yaw is the enemy's right, which the player sees as left
    private static float YawAt(float s) => Mathf.Lerp(HalfArc, -HalfArc, s);

    private static Vector3 Direction(float yaw)
    {
        float r = yaw * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
    }

    private void SetWarnAlpha(float amount)
    {
        warnRenderer.enabled = amount > 0.001f;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", new Color(1f, 1f, 1f, amount));
        warnRenderer.SetPropertyBlock(block);
    }

    // The gust: curved upright ribbons that taper to points at both ends. Only the part the sweep has reached
    // is drawn, bright at the head and fading behind it, and it leans so the sweep looks like a downward slice.
    private void FillGust(float progress, float alpha)
    {
        verts.Clear();
        colors.Clear();
        tris.Clear();

        int steps = Mathf.Max(2, Mathf.CeilToInt(2f * HalfArc * progress / StepDegrees));
        foreach (Vector3 layer in Layers)
        {
            int first = verts.Count;
            for (int i = 0; i <= steps; i++)
            {
                float s = progress * i / steps; // 0 = where it started, `progress` = the head
                float taper = Mathf.Pow(Mathf.Sin(Mathf.Clamp01(s) * Mathf.PI), 0.6f);
                float tail = Mathf.Clamp01(1f - (progress - s) / TrailLength);
                float a = tail * tail * layer.z * alpha * Mathf.Clamp01(taper * 1.5f);

                Vector3 dir = Direction(YawAt(s));
                float lean = (0.5f - s) * halfHeight * 0.6f; // starts high, ends low
                Vector3 centre = dir * (reach * layer.x) + Vector3.up * (midHeight + lean);
                // The head curls forward a little, like air being pushed ahead of it
                centre += dir * (0.15f * tail * tail);
                Vector3 up = Vector3.up * (halfHeight * layer.y * taper);

                verts.Add(centre - up);
                verts.Add(centre + up);
                Color edge = new Color(0.85f, 0.95f, 1f, a * 0.25f);
                colors.Add(edge);
                colors.Add(edge);
                // Brightest through the middle of the ribbon: a centre strip
                verts.Add(centre);
                colors.Add(new Color(1f, 1f, 1f, a));
            }

            for (int i = 0; i < steps; i++)
            {
                int v = first + i * 3;
                int n = v + 3;
                AddQuad(v, v + 2, n + 2, n);     // bottom half
                AddQuad(v + 2, v + 1, n + 1, n + 2); // top half
            }
        }

        gustMesh.Clear();
        gustMesh.SetVertices(verts);
        gustMesh.SetColors(colors);
        gustMesh.SetTriangles(tris, 0);
        gustMesh.bounds = new Bounds(new Vector3(0f, midHeight, reach * 0.5f), new Vector3(reach * 2.5f, halfHeight * 4f, reach * 2.5f));
    }

    private void AddQuad(int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(d);
    }

    // Streaks of air thrown off the head of the gust, flying the way it's swiping
    private void EmitAir(float progress)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(ParticlesPerSecond * Time.deltaTime));
        for (int i = 0; i < count; i++)
        {
            float s = Mathf.Clamp01(progress - Random.value * 0.12f);
            float yaw = YawAt(s);
            Vector3 dir = Direction(yaw);
            Vector3 along = new Vector3(-Mathf.Cos(yaw * Mathf.Deg2Rad), 0f, Mathf.Sin(yaw * Mathf.Deg2Rad)); // the way the sweep is heading

            float r = reach * Random.Range(0.55f, 1.05f);
            float h = midHeight + Random.Range(-1f, 1f) * halfHeight;
            Vector3 local = dir * r + Vector3.up * h;

            var emit = new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(local),
                velocity = transform.TransformDirection(along * Random.Range(5f, 9f) + dir * Random.Range(0f, 1.5f)),
                startLifetime = Random.Range(0.25f, 0.45f),
                startSize = Random.Range(0.04f, 0.09f),
            };
            air.Emit(emit, 1);
        }
    }

    // ---------- Reach marker ----------

    // A thin arc on the ground at the edge of the reach, with a line down each side
    private Mesh BuildWarning()
    {
        verts.Clear();
        colors.Clear();
        tris.Clear();

        Color edge = new Color(1f, 1f, 1f, 0.55f);
        Color faint = new Color(1f, 1f, 1f, 0f);
        const float width = 0.07f;

        int steps = Mathf.CeilToInt(2f * HalfArc / StepDegrees);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 dir = Direction(Mathf.Lerp(-HalfArc, HalfArc, (float)i / steps));
            verts.Add(dir * (reach - width)); colors.Add(edge);
            verts.Add(dir * reach); colors.Add(edge);
            if (i > 0)
            {
                int v = (i - 1) * 2;
                AddQuad(v, v + 1, v + 3, v + 2);
            }
        }

        foreach (float side in new[] { -HalfArc, HalfArc })
        {
            Vector3 dir = Direction(side);
            Vector3 across = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);
            int v = verts.Count;
            verts.Add(dir * reach * 0.35f - across); colors.Add(faint);
            verts.Add(dir * reach * 0.35f + across); colors.Add(faint);
            verts.Add(dir * reach + across); colors.Add(edge);
            verts.Add(dir * reach - across); colors.Add(edge);
            AddQuad(v, v + 1, v + 2, v + 3);
        }

        var mesh = new Mesh { name = "Reach" };
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        if (warnMesh != null) Destroy(warnMesh);
        if (gustMesh != null) Destroy(gustMesh);
    }
}
