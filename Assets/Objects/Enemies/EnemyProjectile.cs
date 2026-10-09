using UnityEngine;

// A shot fired by a ModelEnemy: flies toward where the player's eyes were (or curves gently toward them if homing)
// and hurts them if it gets close. Snowballs knock it out of the air, and snow walls stop it. The visible model is the "Visual" child, which spins according to the
// enemy's projectile design.
public class EnemyProjectile : MonoBehaviour
{
    private const float Speed = 5f;
    private const float HomingSpeed = 3.5f;
    private const float HomingTurnDegreesPerSecond = 35f; // a gentle curve you can still sidestep
    private const float HitboxRadius = 0.3f;               // for snowballs and walls to touch
    private const float HitDistance = 0.7f;
    private const float Lifetime = 4f;
    private const float HomingLifetime = 6f;

    private PlayerHealth target;
    private int damage;
    private bool homing;
    private bool slows;
    private Vector3 velocity;
    private float age;
    private Transform visual;
    private Vector3 spinAxis;
    private float spinSpeed;

    // yawOffset turns the shot left or right of the player (for spread attacks)
    public void Launch(PlayerHealth player, int damageAmount, string spin, float yawOffset, bool isHoming, bool slowsOnHit)
    {
        target = player;
        damage = damageAmount;
        homing = isHoming;
        slows = slowsOnHit;

        Vector3 aimPoint = player != null ? AimPointOf(player) : transform.position + transform.forward;
        Vector3 direction = Quaternion.AngleAxis(yawOffset, Vector3.up) * (aimPoint - transform.position).normalized;
        velocity = direction * (homing ? HomingSpeed : Speed);
        transform.rotation = Quaternion.LookRotation(velocity);

        // A trigger so snowballs and snow walls can touch it (it never pushes anything around)
        var hitbox = gameObject.AddComponent<SphereCollider>();
        hitbox.isTrigger = true;
        hitbox.radius = HitboxRadius;
        var body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;

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
        if (homing && target != null)
        {
            // Only steer left and right: it keeps the up/down angle it was fired at
            Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 toTarget = AimPointOf(target) - transform.position;
            Vector3 flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z);

            if (Vector3.Dot(flatToTarget, flatVelocity) <= 0f)
            {
                homing = false; // it's gone past the player: fly straight from here on
            }
            else
            {
                flatVelocity = Vector3.RotateTowards(flatVelocity, flatToTarget.normalized * flatVelocity.magnitude,
                    HomingTurnDegreesPerSecond * Mathf.Deg2Rad * Time.deltaTime, 0f);
                velocity = new Vector3(flatVelocity.x, velocity.y, flatVelocity.z);
                transform.rotation = Quaternion.LookRotation(velocity);
            }
        }

        transform.position += velocity * Time.deltaTime;
        age += Time.deltaTime;

        if (visual != null && spinSpeed != 0f)
            visual.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.Self);

        if (target != null && Vector3.Distance(transform.position, AimPointOf(target)) < HitDistance)
        {
            ModelEnemy.HitPlayer(target, damage, slows);
            Destroy(gameObject);
        }
        else if (age > (homing ? HomingLifetime : Lifetime))
        {
            Destroy(gameObject);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Shot down by a snowball: both burst into snow
        Snowball snowball = other.GetComponentInParent<Snowball>();
        if (snowball != null)
        {
            Destroy(snowball.gameObject);
            if (snowball.snowHitEffect != null)
                Instantiate(snowball.snowHitEffect, transform.position, Quaternion.LookRotation(-velocity));
            Burst();
            return;
        }

        // Stopped by a snow wall
        if (other.gameObject.layer == LayerMask.NameToLayer("WallLayer"))
            Burst();
    }

    private void Burst()
    {
        SnowSpray.Emit(transform.position, 8, 0.6f);
        Destroy(gameObject);
    }

    // Aim at the camera: the player shrinks as they use snow, so a fixed height above their feet misses
    private static Vector3 AimPointOf(PlayerHealth player)
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform.position + Vector3.down * 0.1f : player.transform.position + Vector3.up;
    }
}
