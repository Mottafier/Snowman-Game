using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// A tree on the trail. Its trunk is solid: the player bumps into it and small enemies have to walk around it.
// Big enemies smash trees out of their way, sending them spinning off into the distance, shrinking and fading as they fly.
public class TrailTree : MonoBehaviour
{
    private const float TrunkRadius = 0.3f;  // before the tree's scale
    private const float CellSize = 4f;       // trees are sorted into a grid so enemies only check the ones nearby
    private const float MaxRadius = 1f;
    private const float FlySeconds = 2.2f;
    private const float FlyGravity = 5f;     // light, so it soars off instead of dropping

    private static readonly Dictionary<Vector2Int, List<TrailTree>> cells = new Dictionary<Vector2Int, List<TrailTree>>();
    private static readonly List<TrailTree> found = new List<TrailTree>();

    private float radius;
    private Vector2Int cell;
    private bool flying;
    private float age;
    private Vector3 velocity;
    private Vector3 spinAxis;
    private float spinSpeed;
    private Vector3 startScale;
    private readonly List<Material> fading = new List<Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => cells.Clear();

    public static TrailTree Plant(GameObject prefab, Vector3 position, float yaw, float scale, Transform parent)
    {
        GameObject go = Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f), parent);
        go.transform.localScale = Vector3.one * scale;

        var trunk = go.GetComponent<CapsuleCollider>();
        if (trunk == null)
        {
            trunk = go.AddComponent<CapsuleCollider>();
            trunk.height = 2f;
            trunk.center = Vector3.up;
        }
        trunk.radius = TrunkRadius;

        var tree = go.AddComponent<TrailTree>();
        tree.radius = Mathf.Min(TrunkRadius * scale, MaxRadius);
        tree.enabled = false; // nothing to do each frame until it's smashed
        tree.Register();
        return tree;
    }

    // ---------- Finding trees ----------

    // Standing trees whose trunks come within `reach` of `point`. The list is reused, so use it right away.
    public static List<TrailTree> Near(Vector3 point, float reach)
    {
        found.Clear();
        Vector2Int center = CellOf(point);
        int span = Mathf.CeilToInt((reach + MaxRadius) / CellSize);
        for (int dx = -span; dx <= span; dx++)
        {
            for (int dz = -span; dz <= span; dz++)
            {
                if (!cells.TryGetValue(new Vector2Int(center.x + dx, center.y + dz), out List<TrailTree> list))
                    continue;
                foreach (TrailTree tree in list)
                {
                    if (Flat(tree.transform.position - point).magnitude - tree.radius <= reach)
                        found.Add(tree);
                }
            }
        }
        return found;
    }

    // The first tree trunk overlapping a circle of `radius` around `position`, if any
    public static TrailTree Touching(Vector3 position, float radius)
    {
        List<TrailTree> trees = Near(position, radius + 0.05f);
        return trees.Count > 0 ? trees[0] : null;
    }

    // Slides a circle of `radius` at `position` out of any trunks it overlaps
    public static Vector3 PushOut(Vector3 position, float radius)
    {
        foreach (TrailTree tree in Near(position, radius))
        {
            Vector3 away = Flat(position - tree.transform.position);
            float distance = away.magnitude;
            float needed = radius + tree.radius;
            if (distance >= needed)
                continue;
            away = distance > 0.001f ? away / distance : Vector3.right;
            position += away * (needed - distance);
        }
        return position;
    }

    // ---------- Smashing ----------

    // Every tree within `reach` of `center` and within `halfArc` degrees of `aim`
    public static void SmashInArc(Vector3 center, Vector3 aim, float reach, float halfArc)
    {
        foreach (TrailTree tree in Near(center, reach).ToArray())
        {
            if (Vector3.Angle(Flat(aim), Flat(tree.transform.position - center)) <= halfArc)
                tree.Smash(center);
        }
    }

    public static void SmashAround(Vector3 center, float reach)
    {
        foreach (TrailTree tree in Near(center, reach).ToArray())
            tree.Smash(center);
    }

    // Knock the tree flying away from `from`
    public void Smash(Vector3 from)
    {
        if (flying)
            return;
        flying = true;
        enabled = true;
        Unregister();
        foreach (Collider part in GetComponentsInChildren<Collider>())
            part.enabled = false;

        Vector3 away = Flat(transform.position - from);
        if (away.sqrMagnitude < 0.0001f)
            away = Flat(Random.onUnitSphere);
        away.Normalize();

        velocity = away * Random.Range(16f, 24f) + Vector3.up * Random.Range(9f, 13f);
        spinAxis = Vector3.Cross(Vector3.up, away); // topples away from the hit and tumbles end over end
        spinSpeed = Random.Range(420f, 720f);
        startScale = transform.localScale;

        SnowSpray.Emit(transform.position, 20, 1.3f);
        MakeFadeable();
    }

    void Update()
    {
        if (!flying)
            return;

        age += Time.deltaTime;
        float t = age / FlySeconds;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        velocity.y -= FlyGravity * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
        transform.localScale = startScale * Mathf.Lerp(1f, 0.1f, t);

        float alpha = 1f - t * t;
        foreach (Material material in fading)
        {
            Color color = material.color;
            color.a = alpha;
            material.color = color;
        }
    }

    // Swap the tree's materials for see-through copies (URP Lit) so it can fade out
    private void MakeFadeable()
    {
        foreach (Renderer part in GetComponentsInChildren<Renderer>())
        {
            Material[] materials = part.materials; // per-tree copies
            foreach (Material material in materials)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
                fading.Add(material);
            }
            part.materials = materials;
        }
    }

    void OnDestroy()
    {
        Unregister();
        foreach (Material material in fading)
            Destroy(material);
    }

    // ---------- Grid ----------

    private void Register()
    {
        cell = CellOf(transform.position);
        if (!cells.TryGetValue(cell, out List<TrailTree> list))
            cells[cell] = list = new List<TrailTree>();
        list.Add(this);
    }

    private void Unregister()
    {
        if (cells.TryGetValue(cell, out List<TrailTree> list))
            list.Remove(this);
    }

    private static Vector2Int CellOf(Vector3 point) =>
        new Vector2Int(Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.z / CellSize));

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
