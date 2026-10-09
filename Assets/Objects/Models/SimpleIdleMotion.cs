using UnityEngine;

// Small looping motion for decorative models: bob up and down and/or swing back and forth.
public class SimpleIdleMotion : MonoBehaviour
{
    public float bobHeight = 0f;
    public float bobSpeed = 0.5f;

    public Vector3 swingAxis = Vector3.up;
    public float swingAngle = 0f;
    public float swingSpeed = 1f;

    private Vector3 startPosition;
    private Quaternion startRotation;
    private float timeOffset;

    void Start()
    {
        startPosition = transform.localPosition;
        startRotation = transform.localRotation;
        timeOffset = Random.value * 10f;
    }

    void Update()
    {
        float t = Time.time + timeOffset;

        float bob = Mathf.Sin(t * bobSpeed * 2f * Mathf.PI) * bobHeight;
        transform.localPosition = startPosition + Vector3.up * bob;

        float swing = Mathf.Sin(t * swingSpeed * 2f * Mathf.PI) * swingAngle;
        transform.localRotation = startRotation * Quaternion.AngleAxis(swing, swingAxis);
    }
}
