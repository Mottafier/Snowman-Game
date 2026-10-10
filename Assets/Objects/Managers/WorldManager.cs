using System.Collections.Generic;
using UnityEngine;

// Builds the level: a long, narrow trail of snow tiles running north (+z) to Snow Kingdom.
// The stretches, in order: an open snowfield with a few trees, a thick forest with a winding path through it,
// a frozen lake (bare ice, so there's no snow to collect), and a tiered hill climbed by ramps, with Snow Kingdom on top.
// Snow tile (x, z) sits on whole-number spot (x, z) and covers x-0.5..x+0.5, z-0.5..z+0.5.
public class WorldManager : MonoBehaviour
{
    public const int TrailWidth = 32;
    public const int ForestStart = 90;
    public const int IceStart = 200;
    public const int HillStart = 270;
    public const int TrailLength = 372;
    public const float TierHeight = 2.5f;

    [SerializeField] private Snowpile snowTilePrefab;
    [SerializeField] private GameObject treePrefab;
    [SerializeField] private Material groundMaterial;
    [SerializeField] private int seed = 7;

    private static readonly Color IceColor = new Color(0.93f, 1f, 0.996f);
    private static readonly Color LakeColor = new Color(0.6f, 0.83f, 0.97f);
    private static readonly Color RampColor = new Color(0.8f, 0.86f, 0.94f);
    private static readonly Color RockColor = new Color(0.5f, 0.55f, 0.64f);
    private static readonly Color FarSnowColor = new Color(0.94f, 0.96f, 1f);
    private static readonly Color MountainSnow = new Color(0.97f, 0.98f, 1f);

    // A raised flat tier, or a ramp that climbs `rise` meters toward +z, covering tiles x0..x1-1 and z0..z1-1
    private struct Block
    {
        public int x0, x1, z0, z1;
        public float height, rise;

        public bool IsRamp => rise > 0f;
        public float Top => height + rise;
        public bool Contains(float x, float z) => x >= x0 - 0.5f && x < x1 - 0.5f && z >= z0 - 0.5f && z < z1 - 0.5f;
        public float HeightAt(float z) => height + rise * Mathf.Clamp01((z - (z0 - 0.5f)) / (z1 - z0));
    }

    private static Block Tier(int x0, int x1, int z0, float height) =>
        new Block { x0 = x0, x1 = x1, z0 = z0, z1 = TrailLength, height = height };

    private static Block Ramp(int x0, int x1, int z0, int z1, float from) =>
        new Block { x0 = x0, x1 = x1, z0 = z0, z1 = z1, height = from, rise = TierHeight };

    // The hill: four tiers that get narrower as they go up, each joined to the next by a ramp on the opposite
    // side from the last one, so the climb zigzags back and forth up to the summit
    private static readonly Block[] hill =
    {
        Ramp(12, 20, 274, 282, 0f),
        Tier(1, 31, 282, TierHeight),
        Ramp(5, 11, 290, 298, TierHeight),
        Tier(4, 28, 298, TierHeight * 2f),
        Ramp(19, 24, 306, 314, TierHeight * 2f),
        Tier(8, 24, 314, TierHeight * 3f),
        Ramp(12, 17, 322, 330, TierHeight * 3f),
        Tier(11, 21, 330, TierHeight * 4f),
    };

    public static WorldManager Instance { get; private set; }

    private Snowpile[,] snowpiles;
    private System.Random rng;

    void Awake()
    {
        Instance = this;
        rng = new System.Random(seed);
        BuildGround(); // in Awake so the floor is there before anything starts falling
    }

    void Start()
    {
        SpawnSnow();
        PlantTrees();
        BuildMountains();
        SnowKingdom.Build(transform, new Vector3(16f, TierHeight * 4f, 364f));
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public int Collect(int x, int z)
    {
        if (snowpiles == null || x < 0 || z < 0 || x >= TrailWidth || z >= TrailLength)
            return 0;

        Snowpile snowpile = snowpiles[x, z];

        if (snowpile == null)
            return 0;

        return snowpile.Shrink() ? 1 : 0;
    }

    // ---------- Ground height ----------

    // How high the walkable ground is at a point: 0 everywhere except on the hill
    public static float HeightAt(float x, float z)
    {
        float height = 0f;
        foreach (Block block in hill)
        {
            if (block.Contains(x, z))
                height = Mathf.Max(height, block.HeightAt(z));
        }
        return height;
    }

    // Eases a height toward the ground under `point`: drops fast off a ledge, scrambles up a bit slower.
    // Leaves it alone in scenes without a trail.
    public static float FollowGround(float current, Vector3 point)
    {
        if (Instance == null)
            return current;

        float target = HeightAt(point.x, point.z);
        return Mathf.MoveTowards(current, target, (target > current ? 6f : 12f) * Time.deltaTime);
    }

    private static bool OnRamp(float x, float z)
    {
        Block? top = null;
        foreach (Block block in hill)
        {
            if (block.Contains(x, z) && (top == null || block.HeightAt(z) >= top.Value.HeightAt(z)))
                top = block;
        }
        return top != null && top.Value.IsRamp;
    }

    // ---------- Ground ----------

    private void BuildGround()
    {
        var ground = new GameObject("Ground").transform;
        ground.SetParent(transform, false);

        Material ice = groundMaterial != null ? groundMaterial : LowPolyBuilder.GetMaterial(IceColor);
        Material lake = Tinted(ice, LakeColor, 0.9f);
        Material ramp = Tinted(ice, RampColor, 0.3f);
        Material rock = LowPolyBuilder.GetMaterial(RockColor);

        const float left = -0.5f, right = TrailWidth - 0.5f;

        // The trail floor, in three slabs so the frozen lake can be a deeper, glassier blue
        Slab(ground, "Trail floor", left, right, -0.5f, IceStart - 0.5f, -1f, 0f, ice);
        Slab(ground, "Frozen lake", left, right, IceStart - 0.5f, HillStart - 0.5f, -1f, 0f, lake);
        Slab(ground, "Hill foot", left, right, HillStart - 0.5f, TrailLength - 0.5f, -1f, 0f, ice);

        // Snowy land all around the trail, just below it, stretching off to the horizon
        Slab(ground, "Far snow", -500f, 532f, -300f, 900f, -1.02f, -0.02f, Tinted(ice, FarSnowColor, 0.1f));

        foreach (Block block in hill)
        {
            if (block.IsRamp)
            {
                BuildRamp(ground, block, ramp);
                continue;
            }

            // Rock cliffs with a thin icy cap on top for the snow tiles to sit on
            Slab(ground, "Tier", block.x0 - 0.5f, block.x1 - 0.5f, block.z0 - 0.5f, block.z1 - 0.5f, 0f, block.height - 0.1f, rock);
            Slab(ground, "Tier top", block.x0 - 0.5f, block.x1 - 0.5f, block.z0 - 0.5f, block.z1 - 0.5f, block.height - 0.1f, block.height, ice);
        }

        // Invisible walls down both sides and across the ends so nobody wanders off the trail
        int wallLayer = LayerMask.NameToLayer("WallLayer");
        Wall(ground, left - 1f, left, -1.5f, TrailLength + 0.5f, wallLayer);
        Wall(ground, right, right + 1f, -1.5f, TrailLength + 0.5f, wallLayer);
        Wall(ground, left, right, -1.5f, -0.5f, wallLayer);
        Wall(ground, left, right, TrailLength - 0.5f, TrailLength + 0.5f, wallLayer);
    }

    private static Material Tinted(Material source, Color color, float smoothness)
    {
        var material = new Material(source) { color = color };
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    private static GameObject Slab(Transform parent, string name, float x0, float x1, float z0, float z1, float y0, float y1, Material material)
    {
        GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = name;
        slab.layer = LayerMask.NameToLayer("Ground");
        slab.transform.SetParent(parent, false);
        slab.transform.position = new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, (z0 + z1) / 2f);
        slab.transform.localScale = new Vector3(x1 - x0, y1 - y0, z1 - z0);
        slab.GetComponent<MeshRenderer>().sharedMaterial = material;
        return slab;
    }

    private static void Wall(Transform parent, float x0, float x1, float z0, float z1, int layer)
    {
        var wall = new GameObject("Edge wall");
        wall.layer = layer >= 0 ? layer : 0;
        wall.transform.SetParent(parent, false);
        wall.transform.position = new Vector3((x0 + x1) / 2f, 25f, (z0 + z1) / 2f);
        wall.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, 60f, z1 - z0);
    }

    // A wedge sitting on the tier below, sloping up to the tier above
    private static void BuildRamp(Transform parent, Block block, Material material)
    {
        float x0 = block.x0 - 0.5f, x1 = block.x1 - 0.5f, z0 = block.z0 - 0.5f, z1 = block.z1 - 0.5f;
        float low = block.height, high = block.Top;
        var a = new Vector3(x0, low, z0);
        var b = new Vector3(x1, low, z0);
        var c = new Vector3(x1, high, z1);
        var d = new Vector3(x0, high, z1);
        var e = new Vector3(x0, low, z1);
        var f = new Vector3(x1, low, z1);

        var tris = new List<Vector3>();
        Face(tris, Vector3.up, a, b, c, d);         // slope
        Face(tris, Vector3.left, a, e, d);          // left side
        Face(tris, Vector3.right, b, f, c);         // right side
        Face(tris, Vector3.forward, e, f, c, d);    // back, against the cliff

        var indices = new int[tris.Count];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;
        var mesh = new Mesh { name = "Ramp" };
        mesh.SetVertices(tris);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var ramp = new GameObject("Ramp");
        ramp.layer = LayerMask.NameToLayer("Ground");
        ramp.transform.SetParent(parent, false);
        ramp.AddComponent<MeshFilter>().sharedMesh = mesh;
        ramp.AddComponent<MeshRenderer>().sharedMaterial = material;
        ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    // Adds a flat face (triangle or quad), wound so it faces `outward`
    private static void Face(List<Vector3> tris, Vector3 outward, params Vector3[] corners)
    {
        for (int i = 1; i + 1 < corners.Length; i++)
        {
            Vector3 p = corners[0], q = corners[i], r = corners[i + 1];
            if (Vector3.Dot(Vector3.Cross(q - p, r - p), outward) < 0f)
                (q, r) = (r, q);
            tris.Add(p);
            tris.Add(q);
            tris.Add(r);
        }
    }

    // ---------- Snow ----------

    private void SpawnSnow()
    {
        snowpiles = new Snowpile[TrailWidth, TrailLength];
        var snow = new GameObject("Snow").transform;
        snow.SetParent(transform, false);

        for (int x = 0; x < TrailWidth; x++)
        {
            // The lake's shores wobble a little instead of being dead straight
            int nearShore = IceStart + Mathf.FloorToInt(Mathf.PerlinNoise(x * 0.23f, 3.1f) * 4f);
            int farShore = HillStart - Mathf.FloorToInt(Mathf.PerlinNoise(x * 0.23f, 8.7f) * 4f);

            for (int z = 0; z < TrailLength; z++)
            {
                if (z >= nearShore && z < farShore)
                    continue; // frozen lake: bare ice
                if (OnRamp(x, z))
                    continue; // ramps are packed snow you can't scoop up

                Vector3 position = new Vector3(x, HeightAt(x, z), z);
                snowpiles[x, z] = Instantiate(snowTilePrefab, position, Quaternion.identity, snow);
            }
        }
    }

    // ---------- Trees ----------

    // Where the path through the forest runs (x), at a given distance along the trail
    private static float ForestPath(float z)
    {
        float t = z - ForestStart;
        float x = TrailWidth / 2f + 8f * Mathf.Sin(t * 0.05f) + 3f * Mathf.Sin(t * 0.13f + 1f);
        return Mathf.Clamp(x, 4f, TrailWidth - 5f);
    }

    private void PlantTrees()
    {
        if (treePrefab == null)
            return;

        var spots = new List<Vector2>();

        // Snowfield: a scattering of lone trees, keeping the start clear
        for (int tries = 0; tries < 400 && spots.Count < 28; tries++)
            TryAdd(spots, Range(1f, TrailWidth - 2f), Range(14f, ForestStart - 8f), 5f);

        // Forest: thick on both sides of a winding path, thinning out at each end
        for (float z = ForestStart - 6f; z < IceStart - 3f; z += 2.3f)
        {
            float thickness = Mathf.Min(Mathf.InverseLerp(ForestStart - 6f, ForestStart + 8f, z),
                                        Mathf.InverseLerp(IceStart - 3f, IceStart - 14f, z));
            for (float x = 0.6f; x < TrailWidth - 1f; x += 2.3f)
            {
                float px = x + Range(-0.7f, 0.7f), pz = z + Range(-0.7f, 0.7f);
                if (Mathf.Abs(px - ForestPath(pz)) < 2.8f || Range(0f, 1f) > 0.85f * thickness)
                    continue;
                TryAdd(spots, Mathf.Clamp(px, 0.3f, TrailWidth - 1.3f), pz, 1.4f);
            }
        }

        // Hill: a few hardy pines along the tiers, away from the cliff edges and ramps
        for (int tries = 0; tries < 600 && spots.Count < 1000; tries++)
        {
            float x = Range(1f, TrailWidth - 2f), z = Range(HillStart + 14f, TrailLength - 12f);
            if (OnLevelGround(x, z, 1.5f) && !NearRamp(x, z, 3f) && Range(0f, 1f) < 0.04f)
                TryAdd(spots, x, z, 4f);
        }

        var trees = new GameObject("Trees").transform;
        trees.SetParent(transform, false);
        foreach (Vector2 spot in spots)
            TrailTree.Plant(treePrefab, new Vector3(spot.x, HeightAt(spot.x, spot.y), spot.y), Range(0f, 360f), Range(1.3f, 2.6f), trees);

        PlantBackdropTrees();
    }

    // Woods beyond the walls on both sides, just for looks, so no colliders
    private void PlantBackdropTrees()
    {
        var backdrop = new GameObject("Backdrop trees");
        backdrop.transform.SetParent(transform, false);

        for (float z = -30f; z < HillStart + 6f; z += 3.6f)
        {
            float density = z < 0f ? 0.5f : z < ForestStart ? 0.22f : z < IceStart ? 0.85f : 0.07f;
            for (float x = -40f; x < TrailWidth + 40f; x += 3.6f)
            {
                if (x > -2.5f && x < TrailWidth + 1.5f)
                    continue; // that's the trail
                if (Range(0f, 1f) > density)
                    continue;

                var position = new Vector3(x + Range(-1.2f, 1.2f), -0.02f, z + Range(-1.2f, 1.2f));
                GameObject tree = Instantiate(treePrefab, position, Quaternion.Euler(0f, Range(0f, 360f), 0f), backdrop.transform);
                tree.transform.localScale = Vector3.one * Range(1.4f, 3f);
                foreach (Collider collider in tree.GetComponentsInChildren<Collider>())
                    Destroy(collider);
            }
        }
    }

    private static void TryAdd(List<Vector2> spots, float x, float z, float spacing)
    {
        var spot = new Vector2(x, z);
        foreach (Vector2 other in spots)
        {
            if ((other - spot).sqrMagnitude < spacing * spacing)
                return;
        }
        spots.Add(spot);
    }

    private static bool OnLevelGround(float x, float z, float margin)
    {
        float height = HeightAt(x, z);
        return height > 0f && !OnRamp(x, z)
            && HeightAt(x - margin, z) == height && HeightAt(x + margin, z) == height
            && HeightAt(x, z - margin) == height && HeightAt(x, z + margin) == height;
    }

    private static bool NearRamp(float x, float z, float distance)
    {
        foreach (Block block in hill)
        {
            if (block.IsRamp && x > block.x0 - 0.5f - distance && x < block.x1 - 0.5f + distance
                && z > block.z0 - 0.5f - distance && z < block.z1 - 0.5f + distance)
                return true;
        }
        return false;
    }

    private float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

    // ---------- Mountains ----------

    // Snow-capped peaks hemming in the hill and the lake, a big range behind Snow Kingdom, and far-off ones along the way
    private void BuildMountains()
    {
        var mountains = new GameObject("Mountains").transform;
        mountains.SetParent(transform, false);

        for (float z = IceStart + 20f; z < TrailLength + 50f; z += Range(18f, 26f))
        {
            float r = Range(18f, 28f);
            Mountain(mountains, new Vector3(-r - 2f, 0f, z), r, Range(25f, 48f));
            r = Range(18f, 28f);
            Mountain(mountains, new Vector3(TrailWidth + r + 1f, 0f, z + Range(-8f, 8f)), r, Range(25f, 48f));
        }

        for (float x = -60f; x < TrailWidth + 70f; x += Range(28f, 40f))
            Mountain(mountains, new Vector3(x, 0f, TrailLength + Range(45f, 80f)), Range(35f, 50f), Range(55f, 85f));

        for (float z = -60f; z < IceStart + 20f; z += Range(40f, 55f))
        {
            Mountain(mountains, new Vector3(Range(-150f, -110f), 0f, z), Range(30f, 45f), Range(40f, 80f));
            Mountain(mountains, new Vector3(TrailWidth + Range(110f, 150f), 0f, z), Range(30f, 45f), Range(40f, 80f));
        }
    }

    private void Mountain(Transform parent, Vector3 basePoint, float radius, float height)
    {
        var mountain = new GameObject("Mountain").transform;
        mountain.SetParent(parent, false);
        mountain.position = basePoint + Vector3.down * 2f;
        mountain.rotation = Quaternion.Euler(0f, Range(0f, 360f), 0f);

        const float cap = 0.33f; // top third is snow
        LowPolyBuilder.Part(mountain, "Rock", LowPolyBuilder.Cone, RockColor,
            new Vector3(0f, height / 2f, 0f), new Vector3(radius * 2f, height, radius * 2f));
        LowPolyBuilder.Part(mountain, "Snowcap", LowPolyBuilder.Cone, MountainSnow,
            new Vector3(0f, height * (1f - cap / 2f), 0f), new Vector3(radius * 2f * cap * 1.04f, height * cap, radius * 2f * cap * 1.04f));
    }
}
