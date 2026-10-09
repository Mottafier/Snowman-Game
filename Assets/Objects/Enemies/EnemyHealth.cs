using UnityEngine;
using System.Collections;

public class EnemyHealth : MonoBehaviour
{
    [SerializeField] public int hpMax = 100;

    public int hp;

    private Renderer[] renderers;
    private Color[][] originalColors;
    private Vector3 originalScale;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        hp = hpMax;
    }

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length][];

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;
            originalColors[i] = new Color[materials.Length];

            for (int j = 0; j < materials.Length; j++)
            {
                originalColors[i][j] = materials[j].color;
            }
        }
        originalScale = transform.localScale;
    }

    public void TakeDamage(int damage)
    {
        hp -= damage;
        if (hp <= 0)
        {
            Die();
        }

        StartCoroutine(DamageFlash());
        StartCoroutine(Squish());
    }
    IEnumerator DamageFlash()
    {

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;

            for (int j = 0; j < materials.Length; j++)
            {
                Color original = originalColors[i][j];
                materials[j].color = new Color(1f, 0f, 0f, original.a);
            }
        }

        yield return new WaitForSeconds(0.1f);

        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].materials;

            for (int j = 0; j < materials.Length; j++)
            {
                materials[j].color = originalColors[i][j];
            }
        }
    }
    IEnumerator Squish()
    {
        transform.localScale = new Vector3(
            originalScale.x * 1.15f,
            originalScale.y * 0.85f,
            originalScale.z * 1.15f);

        yield return new WaitForSeconds(0.08f);

        transform.localScale = originalScale;
    }


    // Raised just before the enemy is destroyed by running out of health
    public event System.Action Died;

    void Die()
    {
        // Add death logic here (e.g., play animation, drop loot, etc.)
        Died?.Invoke();
        Destroy(gameObject);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
