using System.Collections;
using UnityEngine;

// The warning every enemy gives before it attacks: it flashes white and swells up for a split second,
// shrinks back, holds still for a moment, and only then attacks or fires.
public static class AttackTelegraph
{
    public const float FlashSeconds = 0.15f; // white and swollen
    public const float PauseSeconds = 0.3f;  // back to normal, frozen, about to strike
    private const float Grow = 0.2f;         // 20% bigger at the peak of the flash

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static MaterialPropertyBlock whiteBlock;

    // Scales `grow` and whitens every mesh under `flashRoot`. Yield on this, then do the attack.
    public static IEnumerator Play(Transform grow, Transform flashRoot)
    {
        Vector3 restScale = grow.localScale;
        Renderer[] renderers = MeshRenderers(flashRoot);
        SetWhite(renderers, true);

        for (float t = 0f; t < FlashSeconds; t += Time.deltaTime)
        {
            if (grow == null)
                yield break;
            // Pop up fast, ease back down
            float swell = Mathf.Sin(Mathf.Clamp01(t / FlashSeconds) * Mathf.PI);
            grow.localScale = restScale * (1f + Grow * swell);
            yield return null;
        }

        if (grow == null)
            yield break;
        grow.localScale = restScale;
        SetWhite(renderers, false);

        yield return new WaitForSeconds(PauseSeconds);
    }

    // Only the model's meshes: skip health bars and other sprites/UI
    private static Renderer[] MeshRenderers(Transform root)
    {
        var all = root.GetComponentsInChildren<Renderer>();
        var meshes = new System.Collections.Generic.List<Renderer>(all.Length);
        foreach (Renderer r in all)
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                meshes.Add(r);
        return meshes.ToArray();
    }

    private static void SetWhite(Renderer[] renderers, bool white)
    {
        if (whiteBlock == null)
        {
            whiteBlock = new MaterialPropertyBlock();
            whiteBlock.SetColor(BaseColorId, Color.white); // URP Lit
            whiteBlock.SetColor(ColorId, Color.white);     // Standard and most others
        }

        foreach (Renderer r in renderers)
            if (r != null)
                r.SetPropertyBlock(white ? whiteBlock : null);
    }
}
