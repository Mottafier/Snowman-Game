using UnityEngine;

public class Snowball : MonoBehaviour
{
    private Vector3 previousVelocity;
    [SerializeField] private AudioClip snowHitClip;

    [SerializeField] private int damage = 25;
    [SerializeField] public GameObject snowHitEffect;
    private void OnCollisionEnter(Collision collision)
    {
        EnemyHealth enemyHealth = collision.gameObject.GetComponent<EnemyHealth>();
        ContactPoint contact = collision.contacts[0];
        Vector3 hitDirection = previousVelocity.normalized;

        if (enemyHealth != null)
        {
            enemyHealth.TakeDamage(damage);
            Destroy(gameObject);
            Instantiate(snowHitEffect,contact.point,Quaternion.LookRotation(hitDirection));
            AudioSource.PlayClipAtPoint(snowHitClip, contact.point);
        }
       
    }

    void FixedUpdate()
    {
        previousVelocity = GetComponent<Rigidbody>().linearVelocity;
    }
}
