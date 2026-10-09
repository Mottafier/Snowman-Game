using System;
using UnityEngine;

// The low poly model recipe Claude designs in Tools/spawn_model.py, and helpers to build it.
// Fields are filled in by JsonUtility.
#pragma warning disable 0649
[Serializable]
public class ModelRecipe
{
    public string name;
    public PartSpec[] parts;
    public ModelEnemy.BehaviorSpec behavior;
    public ProjectileSpec projectile;

    [Serializable]
    public class PartSpec
    {
        public string shape;
        public Rgb color;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
    }

    [Serializable]
    public class Rgb
    {
        public float r, g, b;
    }

    // What a ranged enemy throws
    [Serializable]
    public class ProjectileSpec
    {
        public PartSpec[] parts;
        public string spin; // none, spin, roll, tumble
    }
#pragma warning restore 0649

    // Builds the parts under a new "Model" object at the origin
    public static Transform Build(PartSpec[] parts)
    {
        var root = new GameObject("Model").transform;
        if (parts == null)
            return root;

        foreach (PartSpec part in parts)
        {
            Mesh mesh = MeshFor(part.shape);
            if (mesh == null)
                continue;

            var color = part.color != null ? new Color(part.color.r, part.color.g, part.color.b) : Color.white;
            LowPolyBuilder.Part(root, part.shape, mesh, color, part.position, part.scale, part.rotation);
        }
        return root;
    }

    public static Bounds MeasureBounds(Transform model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(model.position, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private static Mesh MeshFor(string shape)
    {
        switch (shape)
        {
            case "sphere": return LowPolyBuilder.Sphere;
            case "cube": return LowPolyBuilder.Box;
            case "cylinder": return LowPolyBuilder.Cylinder;
            case "cone": return LowPolyBuilder.Cone;
            default: return null;
        }
    }
}
