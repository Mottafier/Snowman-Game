using System.Collections.Generic;
using UnityEngine;

// Snow Kingdom, at the end of the trail. For now it's a flat 2D castle cutout facing back down the trail,
// built from thin extruded shapes: walls, towers with pointed roofs, a gate, windows and flags.
public static class SnowKingdom
{
    private const float Scale = 1.3f;      // big enough to stand out on the horizon from the start of the trail
    private const float BuriedDepth = 12f; // the panels reach down this far so they meet the lower tiers on either side

    private static readonly Color Wall = new Color(0.72f, 0.84f, 0.96f);
    private static readonly Color Tower = new Color(0.82f, 0.9f, 0.99f);
    private static readonly Color Keep = new Color(0.9f, 0.95f, 1f);
    private static readonly Color Roof = new Color(0.26f, 0.42f, 0.8f);
    private static readonly Color Dark = new Color(0.15f, 0.2f, 0.36f);
    private static readonly Color Snow = new Color(0.98f, 0.99f, 1f);
    private static readonly Color Flag = new Color(0.98f, 0.78f, 0.22f);

    // `basePoint` is the middle of the castle's front at ground level; the castle faces -z
    public static Transform Build(Transform parent, Vector3 basePoint)
    {
        var castle = new GameObject("Snow Kingdom").transform;
        castle.SetParent(parent, false);
        castle.position = basePoint;
        castle.localScale = Vector3.one * Scale;

        // Back to front: each layer sits a little further forward so overlapping shapes don't flicker
        const float wallZ = 0f, towerZ = -0.15f, keepZ = -0.3f, detailZ = -0.5f;

        // Curtain wall with battlements and a line of snow along the top
        Rect(castle, Wall, -10f, -BuriedDepth, 10f, 9f, wallZ, 0.4f);
        Battlements(castle, Wall, -9.6f, 9.6f, 9f, wallZ);
        Rect(castle, Snow, -10f, 8.75f, 10f, 9.05f, wallZ - 0.08f, 0.3f);

        // Outer towers, inner towers, then the tall central keep
        foreach (float side in new[] { -1f, 1f })
        {
            TowerWithRoof(castle, Tower, side * 11.5f, 2.6f, 15f, 6.5f, towerZ);
            TowerWithRoof(castle, Tower, side * 6f, 1.9f, 17f, 5.5f, towerZ - 0.05f);
        }
        TowerWithRoof(castle, Keep, 0f, 3.3f, 20f, 7.5f, keepZ);

        // Gate: a tall pointed arch
        Shape(castle, Dark, detailZ, 0.2f, new Vector2(-2.2f, 0f), new Vector2(2.2f, 0f), new Vector2(2.2f, 4.2f),
            new Vector2(0f, 6.3f), new Vector2(-2.2f, 4.2f));

        // Windows
        foreach (float side in new[] { -1f, 1f })
        {
            Window(castle, side * 11.5f, 10.5f, detailZ);
            Window(castle, side * 11.5f, 5f, detailZ);
            Window(castle, side * 6f, 12.5f, detailZ);
            Window(castle, side * 1.6f, 15f, detailZ);
        }
        Window(castle, 0f, 10f, detailZ, 1f, 1.8f);

        // Something solid to bump into, so nobody walks through the cutout
        var block = castle.gameObject.AddComponent<BoxCollider>();
        block.center = new Vector3(0f, 8f, 0f);
        block.size = new Vector3(30f, 40f, 1f);

        return castle;
    }

    private static void TowerWithRoof(Transform castle, Color color, float x, float halfWidth, float height, float roofHeight, float z)
    {
        Rect(castle, color, x - halfWidth, -BuriedDepth, x + halfWidth, height, z, 0.4f);

        float eave = halfWidth + 0.5f;
        Shape(castle, Roof, z - 0.05f, 0.4f, new Vector2(x - eave, height), new Vector2(x + eave, height),
            new Vector2(x, height + roofHeight));
        Rect(castle, Snow, x - eave, height - 0.15f, x + eave, height + 0.2f, z - 0.12f, 0.3f);

        // Flagpole and a pennant blowing to the right
        float top = height + roofHeight;
        Rect(castle, Dark, x - 0.07f, top - 0.2f, x + 0.07f, top + 2.6f, z, 0.14f);
        Shape(castle, Flag, z, 0.12f, new Vector2(x + 0.07f, top + 2.55f), new Vector2(x + 0.07f, top + 1.5f),
            new Vector2(x + 2f, top + 2.1f));
    }

    private static void Battlements(Transform castle, Color color, float from, float to, float top, float z)
    {
        for (float x = from; x <= to - 1.1f; x += 2.2f)
            Rect(castle, color, x, top, x + 1.1f, top + 1.1f, z, 0.4f);
    }

    private static void Window(Transform castle, float x, float y, float z, float width = 0.7f, float height = 1.5f)
    {
        float half = width / 2f;
        Shape(castle, Dark, z, 0.15f, new Vector2(x - half, y), new Vector2(x + half, y), new Vector2(x + half, y + height),
            new Vector2(x, y + height + half), new Vector2(x - half, y + height));
    }

    private static void Rect(Transform castle, Color color, float x0, float y0, float x1, float y1, float z, float depth)
    {
        Shape(castle, color, z, depth, new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1));
    }

    // A flat convex outline (x right, y up) pushed out `depth` thick, with its front face at `z`
    private static void Shape(Transform castle, Color color, float z, float depth, params Vector2[] outline)
    {
        Vector2 middle = Vector2.zero;
        foreach (Vector2 point in outline)
            middle += point;
        middle /= outline.Length;
        var center = new Vector3(middle.x, middle.y, z + depth / 2f);

        var tris = new List<Vector3>();
        for (int i = 0; i < outline.Length; i++)
        {
            Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
            var frontA = new Vector3(a.x, a.y, z);
            var frontB = new Vector3(b.x, b.y, z);
            var backA = new Vector3(a.x, a.y, z + depth);
            var backB = new Vector3(b.x, b.y, z + depth);
            var frontMid = new Vector3(middle.x, middle.y, z);
            var backMid = new Vector3(middle.x, middle.y, z + depth);

            AddTri(tris, center, frontMid, frontA, frontB);
            AddTri(tris, center, backMid, backA, backB);
            AddTri(tris, center, frontA, frontB, backB);
            AddTri(tris, center, frontA, backB, backA);
        }

        var indices = new int[tris.Count];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;
        var mesh = new Mesh { name = "Castle piece" };
        mesh.SetVertices(tris);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var piece = new GameObject("Castle piece");
        piece.transform.SetParent(castle, false);
        piece.AddComponent<MeshFilter>().sharedMesh = mesh;
        piece.AddComponent<MeshRenderer>().sharedMaterial = LowPolyBuilder.GetMaterial(color);
    }

    // The shapes are convex, so face each triangle away from the middle
    private static void AddTri(List<Vector3> tris, Vector3 center, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.sqrMagnitude < 1e-8f)
            return;
        if (Vector3.Dot(normal, (a + b + c) / 3f - center) < 0f)
            (b, c) = (c, b);
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
    }
}
