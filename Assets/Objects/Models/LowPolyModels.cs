using UnityEngine;
using static LowPolyBuilder;

// Hand-built low poly models. Each faces +Z; the fish is centered on its origin,
// people stand with their feet at the origin.
public static class LowPolyModels
{
    public static Transform BuildFish()
    {
        var orange = new Color(1f, 0.5f, 0.1f);
        var darkOrange = new Color(0.9f, 0.32f, 0.05f);
        var cream = new Color(1f, 0.85f, 0.6f);
        var white = new Color(0.95f, 0.95f, 0.95f);
        var black = new Color(0.05f, 0.05f, 0.05f);

        var root = new GameObject("Fish").transform;

        Part(root, "Body", Sphere, orange, new Vector3(0f, 0f, 0f), new Vector3(0.45f, 0.6f, 1.2f));
        Part(root, "Belly", Sphere, cream, new Vector3(0f, -0.1f, 0.05f), new Vector3(0.4f, 0.45f, 1f));

        // Tail wags from a joint at the back of the body
        Transform tailJoint = Pivot(root, "TailJoint", new Vector3(0f, 0f, -0.55f));
        Part(tailJoint, "Tail", Cone, darkOrange, new Vector3(0f, 0f, -0.22f), new Vector3(0.08f, 0.45f, 0.7f), new Vector3(90f, 0f, 0f));
        var tailWag = tailJoint.gameObject.AddComponent<SimpleIdleMotion>();
        tailWag.swingAngle = 25f;
        tailWag.swingSpeed = 1.5f;

        Part(root, "DorsalFin", Cone, darkOrange, new Vector3(0f, 0.3f, -0.05f), new Vector3(0.06f, 0.3f, 0.45f), new Vector3(-25f, 0f, 0f));
        Part(root, "FinLeft", Sphere, darkOrange, new Vector3(-0.21f, -0.08f, 0.12f), new Vector3(0.03f, 0.14f, 0.26f), new Vector3(0f, 0f, -35f));
        Part(root, "FinRight", Sphere, darkOrange, new Vector3(0.21f, -0.08f, 0.12f), new Vector3(0.03f, 0.14f, 0.26f), new Vector3(0f, 0f, 35f));

        foreach (float side in new[] { -1f, 1f })
        {
            Part(root, "Eye", Sphere, white, new Vector3(0.17f * side, 0.08f, 0.38f), new Vector3(0.13f, 0.13f, 0.13f));
            Part(root, "Pupil", Sphere, black, new Vector3(0.2f * side, 0.08f, 0.41f), new Vector3(0.07f, 0.07f, 0.07f));
        }
        Part(root, "Mouth", Sphere, black, new Vector3(0f, -0.04f, 0.59f), new Vector3(0.08f, 0.04f, 0.04f));

        // Gentle floating and side-to-side swimming
        var swim = root.gameObject.AddComponent<SimpleIdleMotion>();
        swim.bobHeight = 0.1f;
        swim.bobSpeed = 0.4f;
        swim.swingAngle = 8f;
        swim.swingSpeed = 0.75f;

        return root;
    }

    public static Transform BuildObama()
    {
        var skin = new Color(0.48f, 0.32f, 0.23f);
        var hair = new Color(0.25f, 0.24f, 0.23f);
        var suit = new Color(0.16f, 0.17f, 0.2f);
        var white = new Color(0.95f, 0.95f, 0.95f);
        var tie = new Color(0.65f, 0.08f, 0.1f);
        var flagBlue = new Color(0.1f, 0.2f, 0.55f);
        var black = new Color(0.05f, 0.05f, 0.05f);
        var eyeColor = new Color(0.1f, 0.07f, 0.05f);
        var lips = new Color(0.3f, 0.15f, 0.12f);

        var root = new GameObject("Barack Obama").transform;

        // Legs and shoes
        foreach (float side in new[] { -1f, 1f })
        {
            Part(root, "Leg", Cylinder, suit, new Vector3(0.11f * side, 0.48f, 0f), new Vector3(0.17f, 0.86f, 0.17f));
            Part(root, "Shoe", Box, black, new Vector3(0.11f * side, 0.05f, 0.04f), new Vector3(0.13f, 0.1f, 0.29f));
        }

        // Suit jacket, shirt, tie and flag pin
        Part(root, "Jacket", Box, suit, new Vector3(0f, 1.2f, 0f), new Vector3(0.46f, 0.64f, 0.26f));
        Part(root, "ShoulderLeft", Sphere, suit, new Vector3(-0.22f, 1.47f, 0f), new Vector3(0.2f, 0.14f, 0.26f));
        Part(root, "ShoulderRight", Sphere, suit, new Vector3(0.22f, 1.47f, 0f), new Vector3(0.2f, 0.14f, 0.26f));
        Part(root, "Shirt", Box, white, new Vector3(0f, 1.4f, 0.131f), new Vector3(0.14f, 0.22f, 0.01f));
        Part(root, "Tie", Box, tie, new Vector3(0f, 1.33f, 0.137f), new Vector3(0.06f, 0.32f, 0.012f));
        Part(root, "FlagPin", Box, tie, new Vector3(-0.13f, 1.42f, 0.135f), new Vector3(0.035f, 0.025f, 0.01f));
        Part(root, "FlagPinStars", Box, flagBlue, new Vector3(-0.142f, 1.426f, 0.141f), new Vector3(0.012f, 0.012f, 0.004f));

        // Left arm hangs at his side
        Transform leftShoulder = Pivot(root, "LeftShoulder", new Vector3(-0.27f, 1.46f, 0f), new Vector3(0f, 0f, -6f));
        Part(leftShoulder, "Arm", Cylinder, suit, new Vector3(0f, -0.3f, 0f), new Vector3(0.13f, 0.6f, 0.13f));
        Part(leftShoulder, "Hand", Sphere, skin, new Vector3(0f, -0.66f, 0f), new Vector3(0.1f, 0.13f, 0.1f));

        // Right arm raised in a wave
        Transform rightShoulder = Pivot(root, "RightShoulder", new Vector3(0.27f, 1.46f, 0f), new Vector3(0f, 0f, 150f));
        Part(rightShoulder, "Arm", Cylinder, suit, new Vector3(0f, -0.3f, 0f), new Vector3(0.13f, 0.6f, 0.13f));
        Part(rightShoulder, "Hand", Sphere, skin, new Vector3(0f, -0.66f, 0f), new Vector3(0.1f, 0.13f, 0.05f));
        var wave = rightShoulder.gameObject.AddComponent<SimpleIdleMotion>();
        wave.swingAxis = Vector3.forward;
        wave.swingAngle = 12f;
        wave.swingSpeed = 1.2f;

        // Neck, collar and head
        Part(root, "Collar", Cylinder, white, new Vector3(0f, 1.53f, 0f), new Vector3(0.14f, 0.05f, 0.14f));
        Part(root, "Neck", Cylinder, skin, new Vector3(0f, 1.56f, 0f), new Vector3(0.11f, 0.1f, 0.11f));
        Part(root, "Head", Sphere, skin, new Vector3(0f, 1.72f, 0f), new Vector3(0.2f, 0.26f, 0.23f));
        Part(root, "EarLeft", Sphere, skin, new Vector3(-0.105f, 1.71f, 0f), new Vector3(0.05f, 0.085f, 0.05f));
        Part(root, "EarRight", Sphere, skin, new Vector3(0.105f, 1.71f, 0f), new Vector3(0.05f, 0.085f, 0.05f));

        // Short, greying hair
        Part(root, "HairTop", Sphere, hair, new Vector3(0f, 1.8f, -0.015f), new Vector3(0.21f, 0.15f, 0.24f));
        Part(root, "HairBack", Sphere, hair, new Vector3(0f, 1.74f, -0.03f), new Vector3(0.205f, 0.2f, 0.2f));

        // Face
        foreach (float side in new[] { -1f, 1f })
        {
            Part(root, "Eye", Sphere, eyeColor, new Vector3(0.042f * side, 1.725f, 0.108f), new Vector3(0.03f, 0.022f, 0.02f));
            Part(root, "Eyebrow", Box, hair, new Vector3(0.045f * side, 1.755f, 0.102f), new Vector3(0.05f, 0.012f, 0.014f));
        }
        Part(root, "Nose", Cone, skin, new Vector3(0f, 1.69f, 0.118f), new Vector3(0.04f, 0.05f, 0.04f), new Vector3(90f, 0f, 0f));
        Part(root, "Mouth", Box, lips, new Vector3(0f, 1.645f, 0.1f), new Vector3(0.07f, 0.026f, 0.01f));
        Part(root, "Smile", Box, white, new Vector3(0f, 1.645f, 0.104f), new Vector3(0.06f, 0.016f, 0.012f));

        return root;
    }
}
