using UnityEngine;
using UnityEngine.UI;

public class HealthBar : MonoBehaviour
{
    [Header("References")]
    public EnemyHealth target;            // Assign at runtime or via prefab variant
    public Image fillImage;               // The Fill Image

    [Header("Positioning")]
    public Transform followPoint;         // Where to hover (e.g., penguin head bone)
    public Vector3 worldOffset = new Vector3(0, 0.65f, 0);

    [Header("Behavior")]
    public bool hideWhenFull = true;
    public float faceCameraLerp = 20f;    // Smoothing for billboard

    Camera _cam;

    void Awake()
    {
        _cam = Camera.main;
    }

    void LateUpdate()
    {
        if (followPoint != null)
            transform.position = followPoint.position + worldOffset;
        else if (target != null)
            transform.position = target.transform.position + worldOffset;

        if (_cam != null)
        {
            // Billboard to camera
            var toCam = _cam.transform.rotation;
            transform.rotation = toCam;
        }

        if (fillImage) fillImage.fillAmount = (float)target.hp/target.hpMax;

    }

    void HandleHealthChanged(float current, float max)
    {
        float t = (max <= 0f) ? 0f : current / max;
        if (fillImage) fillImage.fillAmount = t;

        if (hideWhenFull)
            gameObject.SetActive(t < 0.999f);
        else
            gameObject.SetActive(true);
    }
}