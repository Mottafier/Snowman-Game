using UnityEngine;

// A shot fired by a ModelEnemy: flies toward where the player was and hurts them if it gets close.
public class EnemyProjectile : MonoBehaviour
{
    private const float Speed = 8f;
    private const float HitDistance = 0.7f;
    private const float Lifetime = 4f;

    private PlayerHealth target;
    private int damage;
    private Vector3 velocity;
    private float age;

    public void Launch(PlayerHealth player, int damageAmount)
    {
        target = player;
        damage = damageAmount;
        Vector3 aimPoint = player != null ? ChestOf(player) : transform.position + transform.forward;
        velocity = (aimPoint - transform.position).normalized * Speed;
    }

    void Update()
    {
        transform.position += velocity * Time.deltaTime;
        age += Time.deltaTime;

        if (target != null && Vector3.Distance(transform.position, ChestOf(target)) < HitDistance)
        {
            target.TakeDamage(damage);
            Destroy(gameObject);
        }
        else if (age > Lifetime)
        {
            Destroy(gameObject);
        }
    }

    private static Vector3 ChestOf(PlayerHealth player) => player.transform.position + Vector3.up * 1.2f;
}
