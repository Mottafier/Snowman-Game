using System.Collections;
using UnityEngine;

// Makes a model spawned from the terminal chase and attack the player.
// How it moves and attacks comes from the behavior Claude picked for it (see Tools/spawn_model.py).
public class ModelEnemy : MonoBehaviour
{
    [System.Serializable]
    public class BehaviorSpec
    {
        public string movement;      // walk, hop, fly, charge, zigzag
        public float speed;          // meters per second
        public string attack;        // melee, ranged, explode
        public int damage;
        public float attackRange;    // meters from the enemy's edge
        public float attackCooldown; // seconds between attacks
        public int health;
    }

    private const float WakeUpDelay = 1f;
    private const float HopHeight = 0.6f;
    private const float HoverHeight = 1.5f;

    private BehaviorSpec behavior;
    private Transform player;
    private PlayerHealth playerHealth;
    private Transform body;
    private Vector3 bodyRestPosition;
    private Color projectileColor = Color.white;

    private float groundY;
    private float radius;
    private float height;
    private float wakeTimer = WakeUpDelay;
    private float cooldown;
    private float age;
    private bool busy; // exploding or mid-lunge

    // Charge movement: creep closer, wind up, dash, rest
    private enum ChargePhase { Approach, WindUp, Dash, Rest }
    private ChargePhase chargePhase;
    private float chargeTimer = 1.5f;
    private Vector3 dashDirection;

    public void Configure(BehaviorSpec spec, Transform model, float footprintRadius, float modelHeight)
    {
        behavior = Sanitize(spec);
        body = model;
        bodyRestPosition = model.localPosition;
        radius = footprintRadius;
        height = modelHeight;
        groundY = transform.position.y;
        cooldown = behavior.attackCooldown * 0.5f;

        Renderer firstRenderer = model.GetComponentInChildren<Renderer>();
        if (firstRenderer != null)
            projectileColor = firstRenderer.sharedMaterial.color;

        var health = gameObject.AddComponent<EnemyHealth>();
        health.hpMax = behavior.health;
        ModelHealthBar.Attach(health, height, radius * 2f);
    }

    // Keep whatever Claude picked within playable limits
    private static BehaviorSpec Sanitize(BehaviorSpec spec)
    {
        var s = spec ?? new BehaviorSpec();
        s.movement = s.movement ?? "walk";
        s.attack = s.attack ?? "melee";
        s.speed = Mathf.Clamp(s.speed, 0.5f, 6f);
        s.damage = Mathf.Clamp(s.damage, 1, 50);
        s.attackCooldown = Mathf.Clamp(s.attackCooldown, 0.4f, 6f);
        s.health = Mathf.Clamp(s.health, 25, 400);
        switch (s.attack)
        {
            case "ranged": s.attackRange = Mathf.Clamp(s.attackRange, 3f, 15f); break;
            case "explode": s.attackRange = Mathf.Clamp(s.attackRange, 1f, 4f); break;
            default: s.attackRange = Mathf.Clamp(s.attackRange, 0.8f, 3f); break;
        }
        return s;
    }

    void Start()
    {
        playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (playerHealth != null)
            player = playerHealth.transform;
    }

    void Update()
    {
        if (behavior == null || player == null || busy)
            return;

        age += Time.deltaTime;
        if (wakeTimer > 0f)
        {
            wakeTimer -= Time.deltaTime;
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float centerDistance = toPlayer.magnitude;
        Vector3 direction = centerDistance > 0.01f ? toPlayer / centerDistance : transform.forward;
        float distance = Mathf.Max(0f, centerDistance - radius);

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 8f * Time.deltaTime);

        // Ranged enemies keep their distance; everyone else closes in
        float stopDistance = behavior.attack == "ranged" ? behavior.attackRange * 0.75f
                           : behavior.attack == "explode" ? 0.3f
                           : behavior.attackRange * 0.6f;
        Move(direction, distance > stopDistance);

        cooldown -= Time.deltaTime;
        if (distance <= behavior.attackRange && cooldown <= 0f)
        {
            cooldown = behavior.attackCooldown;
            Attack(direction);
        }
    }

    private void Move(Vector3 direction, bool advance)
    {
        Vector3 position = transform.position;
        float step = behavior.speed * Time.deltaTime;
        float height = 0f;

        switch (behavior.movement)
        {
            case "hop":
            {
                // Half-second hops with a short pause on the ground between them
                float cycle = age % 0.8f;
                bool airborne = cycle < 0.5f;
                height = airborne ? Mathf.Sin(cycle / 0.5f * Mathf.PI) * HopHeight : 0f;
                if (advance && airborne)
                    position += direction * step * 1.6f;
                break;
            }
            case "fly":
                height = HoverHeight + Mathf.Sin(age * 2f) * 0.25f;
                if (advance)
                    position += direction * step;
                break;
            case "zigzag":
                if (advance)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, direction);
                    position += (direction + side * Mathf.Sin(age * 3f) * 0.8f) * step;
                }
                break;
            case "charge":
                position = Charge(position, direction, advance);
                break;
            default: // walk
                if (advance)
                    position += direction * step;
                break;
        }

        position.y = groundY + height;
        transform.position = position;
    }

    private Vector3 Charge(Vector3 position, Vector3 direction, bool advance)
    {
        chargeTimer -= Time.deltaTime;
        switch (chargePhase)
        {
            case ChargePhase.Approach:
                if (advance)
                    position += direction * behavior.speed * 0.4f * Time.deltaTime;
                if (chargeTimer <= 0f)
                {
                    chargePhase = ChargePhase.WindUp;
                    chargeTimer = 0.6f;
                }
                break;
            case ChargePhase.WindUp:
                // Shake in place before dashing
                body.localPosition = bodyRestPosition + Random.insideUnitSphere * 0.05f;
                if (chargeTimer <= 0f)
                {
                    body.localPosition = bodyRestPosition;
                    dashDirection = direction;
                    chargePhase = ChargePhase.Dash;
                    chargeTimer = 0.7f;
                }
                break;
            case ChargePhase.Dash:
                position += dashDirection * behavior.speed * 3f * Time.deltaTime;
                if (chargeTimer <= 0f)
                {
                    chargePhase = ChargePhase.Rest;
                    chargeTimer = 1f;
                }
                break;
            case ChargePhase.Rest:
                if (chargeTimer <= 0f)
                {
                    chargePhase = ChargePhase.Approach;
                    chargeTimer = 1.5f;
                }
                break;
        }
        return position;
    }

    private void Attack(Vector3 direction)
    {
        switch (behavior.attack)
        {
            case "ranged":
                FireProjectile();
                break;
            case "explode":
                StartCoroutine(Explode());
                break;
            default:
                StartCoroutine(Lunge(direction));
                break;
        }
    }

    private IEnumerator Lunge(Vector3 direction)
    {
        busy = true;
        Vector3 localForward = transform.InverseTransformDirection(direction) * 0.4f / Mathf.Max(transform.localScale.x, 0.01f);
        for (float t = 0f; t < 0.2f; t += Time.deltaTime)
        {
            body.localPosition = bodyRestPosition + localForward * Mathf.Sin(t / 0.2f * Mathf.PI);
            yield return null;
        }
        body.localPosition = bodyRestPosition;
        if (playerHealth != null)
            playerHealth.TakeDamage(behavior.damage);
        busy = false;
    }

    private void FireProjectile()
    {
        var projectile = new GameObject($"{name} shot").transform;
        projectile.position = transform.position + Vector3.up * (height * 0.6f);
        LowPolyBuilder.Part(projectile, "Shape", LowPolyBuilder.Sphere, projectileColor, Vector3.zero, Vector3.one * 0.25f);
        projectile.gameObject.AddComponent<EnemyProjectile>().Launch(playerHealth, behavior.damage);
    }

    private IEnumerator Explode()
    {
        busy = true;
        Vector3 startScale = transform.localScale;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            // Swell up and flicker before going off
            transform.localScale = startScale * (1f + 0.4f * t / 0.6f) * (1f + 0.05f * Mathf.Sin(t * 60f));
            yield return null;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (playerHealth != null && toPlayer.magnitude - radius <= behavior.attackRange + 0.5f)
            playerHealth.TakeDamage(behavior.damage);
        Destroy(gameObject);
    }
}
