using System.Collections.Generic;
using UnityEngine;

// Helpers for building flat-shaded low poly models out of simple shapes at runtime.
// All meshes are unit sized (fit in a 1x1x1 box) and centered on the origin.
public static class LowPolyBuilder
{
    private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    private static Mesh sphere, cone, cylinder, box;

    public static Mesh Sphere
    {
        get
        {
            if (sphere == null) sphere = BuildSphere(10, 6);
            return sphere;
        }
    }

    // Tip points along +Y
    public static Mesh Cone
    {
        get
        {
            if (cone == null) cone = BuildCone(6);
            return cone;
        }
    }

    public static Mesh Cylinder
    {
        get
        {
            if (cylinder == null) cylinder = BuildCylinder(8);
            return cylinder;
        }
    }

    public static Mesh Box
    {
        get
        {
            if (box == null) box = BuildBox();
            return box;
        }
    }

    // Adds a colored shape as a child of parent
    public static Transform Part(Transform parent, string name, Mesh mesh, Color color,
        Vector3 position, Vector3 scale, Vector3 eulerAngles = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localEulerAngles = eulerAngles;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(color);
        return go.transform;
    }

    // Adds an empty child to rotate a group of parts around (e.g. a shoulder or tail joint)
    public static Transform Pivot(Transform parent, string name, Vector3 position, Vector3 eulerAngles = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localEulerAngles = eulerAngles;
        return go.transform;
    }

    public static Material GetMaterial(Color color)
    {
        if (materials.TryGetValue(color, out Material material) && material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        material = new Material(shader) { color = color };
        material.SetFloat("_Smoothness", 0.15f);
        materials[color] = material;
        return material;
    }

    private static Mesh BuildSphere(int segments, int rings)
    {
        var points = new Vector3[rings + 1, segments];
        for (int i = 0; i <= rings; i++)
        {
            float phi = Mathf.PI * i / rings;
            for (int j = 0; j < segments; j++)
            {
                float theta = 2f * Mathf.PI * j / segments;
                points[i, j] = 0.5f * new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
            }
        }

        var tris = new List<Vector3>();
        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                int next = (j + 1) % segments;
                AddQuad(tris, points[i, j], points[i, next], points[i + 1, next], points[i + 1, j]);
            }
        }
        return ToMesh("LowPolySphere", tris);
    }

    private static Mesh BuildCone(int sides)
    {
        var tip = new Vector3(0f, 0.5f, 0f);
        var baseCenter = new Vector3(0f, -0.5f, 0f);
        var tris = new List<Vector3>();
        for (int j = 0; j < sides; j++)
        {
            Vector3 a = Ring(j, sides, -0.5f);
            Vector3 b = Ring(j + 1, sides, -0.5f);
            AddTri(tris, tip, a, b);
            AddTri(tris, baseCenter, a, b);
        }
        return ToMesh("LowPolyCone", tris);
    }

    private static Mesh BuildCylinder(int sides)
    {
        var top = new Vector3(0f, 0.5f, 0f);
        var bottom = new Vector3(0f, -0.5f, 0f);
        var tris = new List<Vector3>();
        for (int j = 0; j < sides; j++)
        {
            Vector3 a = Ring(j, sides, -0.5f);
            Vector3 b = Ring(j + 1, sides, -0.5f);
            Vector3 c = Ring(j + 1, sides, 0.5f);
            Vector3 d = Ring(j, sides, 0.5f);
            AddQuad(tris, a, b, c, d);
            AddTri(tris, bottom, a, b);
            AddTri(tris, top, d, c);
        }
        return ToMesh("LowPolyCylinder", tris);
    }

    private static Mesh BuildBox()
    {
        var c = new Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);

        var tris = new List<Vector3>();
        AddQuad(tris, c[0], c[1], c[3], c[2]); // back
        AddQuad(tris, c[4], c[5], c[7], c[6]); // front
        AddQuad(tris, c[0], c[2], c[6], c[4]); // left
        AddQuad(tris, c[1], c[3], c[7], c[5]); // right
        AddQuad(tris, c[0], c[1], c[5], c[4]); // bottom
        AddQuad(tris, c[2], c[3], c[7], c[6]); // top
        return ToMesh("LowPolyBox", tris);
    }

    private static Vector3 Ring(int index, int sides, float y)
    {
        float theta = 2f * Mathf.PI * index / sides;
        return new Vector3(0.5f * Mathf.Cos(theta), y, 0.5f * Mathf.Sin(theta));
    }

    private static void AddQuad(List<Vector3> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        AddTri(tris, a, b, c);
        AddTri(tris, a, c, d);
    }

    // Shapes are convex and centered on the origin, so face each triangle away from the center
    private static void AddTri(List<Vector3> tris, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);
        if (normal.sqrMagnitude < 1e-8f)
            return; // degenerate (sphere poles)

        if (Vector3.Dot(normal, a + b + c) < 0f)
            (b, c) = (c, b);

        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
    }

    // No shared vertices, so normals come out per-face for the faceted low poly look
    private static Mesh ToMesh(string name, List<Vector3> tris)
    {
        var indices = new int[tris.Count];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        var mesh = new Mesh { name = name };
        mesh.SetVertices(tris);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
