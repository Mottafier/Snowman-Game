using UnityEngine;

// Thrown snowball projectile: damages enemies on impact and plays a hit effect/sound.
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
        else
        {
            // Hit the ground or scenery: burst into snow, spraying away from the surface
            Destroy(gameObject);
            Instantiate(snowHitEffect, contact.point, Quaternion.LookRotation(contact.normal));
            AudioSource.PlayClipAtPoint(snowHitClip, contact.point, 0.5f);
        }
    }

    void FixedUpdate()
    {
        previousVelocity = GetComponent<Rigidbody>().linearVelocity;
    }
}
