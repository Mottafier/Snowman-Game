using UnityEngine;

// Floating health bar above an enemy spawned from the terminal. Always faces the camera,
// shrinks from the right and shifts from green to red as the enemy loses health.
// Its size follows the enemy's size (not its health): giants get a big bar, tiny critters a small one.
public class ModelHealthBar : MonoBehaviour
{
    private const float MinWidth = 0.3f;
    private const float MaxWidth = 6f;

    private EnemyHealth target;
    private float heightAboveTarget;
    private float width;
    private float thickness;
    private float border;
    private Transform fill;
    private Material fillMaterial;
    private Camera cam;

    public static ModelHealthBar Attach(EnemyHealth target, float modelHeight, float modelWidth)
    {
        var bar = new GameObject($"{target.name} health bar").AddComponent<ModelHealthBar>();
        bar.target = target;
        // Size from how big the enemy looks: mostly its width, but tall thin enemies count too
        float size = Mathf.Max(modelWidth, modelHeight * 0.6f);
        bar.width = Mathf.Clamp(size, MinWidth, MaxWidth);
        bar.thickness = Mathf.Clamp(bar.width * 0.1f, 0.04f, 0.4f);
        bar.border = bar.thickness * 0.25f;
        bar.heightAboveTarget = modelHeight + Mathf.Clamp(bar.width * 0.25f, 0.15f, 1f);
        bar.Build();
        return bar;
    }

    // Hide the bar while its enemy can't be seen (e.g. burrowed underground)
    public void SetVisible(bool visible)
    {
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
            r.enabled = visible;
    }

    private void Build()
    {
        Transform back = LowPolyBuilder.Part(transform, "Background", LowPolyBuilder.Box, Color.black,
            Vector3.zero, new Vector3(width + border * 2f, thickness + border * 2f, 0.01f));
        back.GetComponent<Renderer>().sharedMaterial = UnlitMaterial(new Color(0.1f, 0.1f, 0.1f));

        // Sits slightly toward the camera (the bar's -Z side) so it draws on top of the background
        fill = LowPolyBuilder.Part(transform, "Fill", LowPolyBuilder.Box, Color.green,
            new Vector3(0f, 0f, -0.01f), new Vector3(width, thickness, 0.01f));
        fillMaterial = UnlitMaterial(Color.green);
        fill.GetComponent<Renderer>().sharedMaterial = fillMaterial;
    }

    void LateUpdate()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = target.transform.position + Vector3.up * heightAboveTarget;

        if (cam == null)
            cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

        // hp is only set once EnemyHealth starts; until then keep showing a full bar
        if (target.hp <= 0 || target.hpMax <= 0)
            return;

        float fraction = Mathf.Clamp01((float)target.hp / target.hpMax);
        fill.localScale = new Vector3(width * fraction, thickness, 0.01f);
        fill.localPosition = new Vector3(-width * (1f - fraction) / 2f, 0f, -0.01f);
        fillMaterial.color = Color.Lerp(new Color(0.9f, 0.15f, 0.1f), new Color(0.2f, 0.85f, 0.25f), fraction);
    }

    void OnDestroy()
    {
        if (fillMaterial != null)
            Destroy(fillMaterial);
    }

    private static Material UnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        return new Material(shader) { color = color };
    }
}
