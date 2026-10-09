using UnityEngine;

public class Snowpile : MonoBehaviour
{
    public float snowAmount = 4;

    public bool Shrink()
    {
        if (snowAmount <= 0)
            return false;

        snowAmount--;

        UpdateVisuals();

        return true;
    }
    private void UpdateVisuals()
    {
        float size = snowAmount / 4f;

        transform.localScale = new Vector3(1f, size, 1f);

        Renderer renderer = GetComponent<Renderer>();

        Color color = renderer.material.color;
        color.a = Mathf.Clamp(size + 0.3f, 0f, 1f);
        renderer.material.color = color;

        renderer.enabled = snowAmount > 0;
    }
}
