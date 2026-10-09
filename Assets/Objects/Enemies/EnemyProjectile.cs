using UnityEngine;

// A shot fired by a ModelEnemy: flies toward where the player's eyes were and hurts them if it gets close.
// The visible model is the "Visual" child, which spins according to the enemy's projectile design.
public class EnemyProjectile : MonoBehaviour
{
    private const float Speed = 5f;
    private const float HitDistance = 0.7f;
    private const float Lifetime = 4f;

    private PlayerHealth target;
    private int damage;
    private Vector3 velocity;
    private float age;
    private Transform visual;
    private Vector3 spinAxis;
    private float spinSpeed;

    public void Launch(PlayerHealth player, int damageAmount, string spin)
    {
        target = player;
        damage = damageAmount;
        Vector3 aimPoint = player != null ? AimPointOf(player) : transform.position + transform.forward;
        velocity = (aimPoint - transform.position).normalized * Speed;
        transform.rotation = Quaternion.LookRotation(velocity);

        visual = transform.Find("Visual");
        switch (spin)
        {
            case "spin": spinAxis = Vector3.up; spinSpeed = 720f; break;        // flat, like a frisbee or thrown card
            case "roll": spinAxis = Vector3.forward; spinSpeed = 900f; break;  // around its flight path, like a bullet
            case "tumble": spinAxis = Vector3.right; spinSpeed = 540f; break;  // end over end, like a thrown axe
            default: spinSpeed = 0f; break;
        }
    }

    void Update()
    {
        transform.position += velocity * Time.deltaTime;
        age += Time.deltaTime;

        if (visual != null && spinSpeed != 0f)
            visual.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.Self);

        if (target != null && Vector3.Distance(transform.position, AimPointOf(target)) < HitDistance)
        {
            target.TakeDamage(damage);
            Destroy(gameObject);
        }
        else if (age > Lifetime)
        {
            Destroy(gameObject);
        }
    }

    // Aim at the camera: the player shrinks as they use snow, so a fixed height above their feet misses
    private static Vector3 AimPointOf(PlayerHealth player)
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform.position + Vector3.down * 0.1f : player.transform.position + Vector3.up;
    }
}
