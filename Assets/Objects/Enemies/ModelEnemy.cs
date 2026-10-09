using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Makes a model spawned from the terminal chase and attack the player.
// How it moves, attacks and any special ability come from the behavior Claude picked for it (see Tools/spawn_model.py).
public class ModelEnemy : MonoBehaviour
{
    [System.Serializable]
    public class BehaviorSpec
    {
        public string movement;      // walk, hop, fly, charge, zigzag, teleport, burrow, orbit
        public float speed;          // meters per second
        public string attack;        // melee, ranged, burst, spread, homing, slam, explode
        public int damage;           // per hit
        public float attackRange;    // meters from the enemy's edge
        public float attackCooldown; // seconds between attacks
        public int health;
        public string special;       // none, splits, heals, slows, enrages

        public BehaviorSpec Clone() => (BehaviorSpec)MemberwiseClone();
    }

    private const float WakeUpDelay = 2f;
    private const float HopHeight = 0.6f;
    private const float HoverHeight = 1.5f;
    private const float ProjectileSize = 0.45f;
    private const float MaxEnragedSpeed = 2.5f;
    private const float SlowMultiplier = 0.5f;
    private const float SlowSeconds = 2f;
    private const float HealRadius = 6f;
    private const float HealInterval = 3f;
    private const float BurrowSeconds = 0.4f;     // time to dig down or pop up
    private const float ZipSpeed = 8f;            // underground speed: far faster than the player
    private const float ZipTurnDegreesPerSecond = 200f;
    private const float SprayPerSecond = 30f;     // snow chunks kicked up while zipping

    private static readonly Color HealGreen = new Color(0.3f, 0.95f, 0.4f);
    private static readonly Color EnrageRed = new Color(0.95f, 0.15f, 0.1f);
    private static readonly Color SlowBlue = new Color(0.3f, 0.6f, 1f);

    // Every live enemy, so healers can find their friends
    private static readonly List<ModelEnemy> active = new List<ModelEnemy>();

    private BehaviorSpec behavior;
    private ModelRecipe.ProjectileSpec projectileSpec;
    private Transform player;
    private PlayerHealth playerHealth;
    private EnemyHealth health;
    private ModelHealthBar healthBar;
    private Transform body;
    private Vector3 bodyRestPosition;
    private Vector3 baseScale;
    private GameObject projectileTemplate; // hidden copy that each shot is cloned from
    private string projectileSpin;

    private float groundY;
    private float radius;
    private float height;
    private float wakeTimer = WakeUpDelay;
    private float cooldown;
    private float age;
    private float healTimer = HealInterval;
    private bool busy; // mid-attack or mid-teleport, so movement pauses
    private bool enraged;
    private bool isSplitPiece; // pieces from a split don't split again
    private bool hasSplit;

    // Charge movement: creep closer, wind up, dash, rest
    private enum ChargePhase { Approach, WindUp, Dash, Rest }
    private ChargePhase chargePhase;
    private float chargeTimer = 1.5f;
    private Vector3 dashDirection;

    // Teleport movement: blink to a new spot near the player every few seconds
    private float teleportTimer = 3f;

    // Burrow movement: dive, zip around underground for 5-10 s (swooping past and looping around the player),
    // then pop up at attack distance, attack for a few seconds, and dive again
    private enum BurrowPhase { Surfaced, Diving, Zipping, Surfacing }
    private BurrowPhase burrowPhase = BurrowPhase.Surfaced;
    private float burrowOffset;
    private float surfacedTimer = 0.5f;
    private float undergroundTimer;
    private Vector3 zipHeading;
    private Vector3 zipWaypoint;
    private float waypointTimer;
    private Vector3 popUpSpot;
    private bool popUpChosen;
    private float sprayBuildup;

    // Orbit movement: circle the player, clockwise or counterclockwise
    private float orbitDirection = 1f;

    private bool IsShooter => behavior.attack == "ranged" || behavior.attack == "burst"
                           || behavior.attack == "spread" || behavior.attack == "homing";

    private float Speed => enraged ? Mathf.Min(behavior.speed * 1.5f, MaxEnragedSpeed) : behavior.speed;

    private bool Slows => behavior.special == "slows";

    public void Configure(BehaviorSpec spec, ModelRecipe.ProjectileSpec projectile, Transform model,
        float footprintRadius, float modelHeight, bool splitPiece = false)
    {
        behavior = Sanitize(spec);
        projectileSpec = projectile;
        body = model;
        bodyRestPosition = model.localPosition;
        baseScale = transform.localScale;
        radius = footprintRadius;
        height = modelHeight;
        groundY = transform.position.y;
        cooldown = behavior.attackCooldown * 0.5f;
        orbitDirection = Random.value < 0.5f ? -1f : 1f;
        isSplitPiece = splitPiece;
        if (splitPiece)
            wakeTimer = 0.5f;

        if (IsShooter)
            BuildProjectileTemplate(projectile, model);

        health = gameObject.AddComponent<EnemyHealth>();
        health.hpMax = behavior.health;
        health.Died += OnDied;
        healthBar = ModelHealthBar.Attach(health, height, radius * 2f);
    }

    // Keep whatever Claude picked within playable limits
    private static BehaviorSpec Sanitize(BehaviorSpec spec)
    {
        var s = spec ?? new BehaviorSpec();
        s.movement = s.movement ?? "walk";
        s.attack = s.attack ?? "melee";
        s.special = s.special ?? "none";
        // The player only manages 1-3.5 m/s depending on how much snow they carry, so keep enemies slower
        s.speed = Mathf.Clamp(s.speed, 0.3f, 2f);
        s.damage = Mathf.Clamp(s.damage, 1, 50);
        s.attackCooldown = Mathf.Clamp(s.attackCooldown, 0.4f, 6f);
        s.health = Mathf.Clamp(s.health, 25, 400);
        switch (s.attack)
        {
            case "ranged":
            case "burst":
            case "spread":
            case "homing": s.attackRange = Mathf.Clamp(s.attackRange, 3f, 15f); break;
            case "explode": s.attackRange = Mathf.Clamp(s.attackRange, 1f, 4f); break;
            case "slam": s.attackRange = Mathf.Clamp(s.attackRange, 1.5f, 4f); break;
            default: s.attackRange = Mathf.Clamp(s.attackRange, 0.8f, 3f); break;
        }
        return s;
    }

    void OnEnable() => active.Add(this);
    void OnDisable() => active.Remove(this);

    void Start()
    {
        playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (playerHealth != null)
            player = playerHealth.transform;
    }

    void Update()
    {
        if (behavior == null || player == null)
            return;

        age += Time.deltaTime;
        if (wakeTimer > 0f)
        {
            wakeTimer -= Time.deltaTime;
            return;
        }

        if (behavior.special == "heals")
            HealNearby();
        if (behavior.special == "enrages" && !enraged && health.hp > 0 && health.hp <= health.hpMax / 2)
            Enrage();

        if (busy)
            return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float centerDistance = toPlayer.magnitude;
        Vector3 direction = centerDistance > 0.01f ? toPlayer / centerDistance : transform.forward;
        float distance = Mathf.Max(0f, centerDistance - radius);

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 8f * Time.deltaTime);

        // Shooters keep their distance; everyone else closes in
        float stopDistance = IsShooter ? behavior.attackRange * 0.75f
                           : behavior.attack == "explode" ? 0.3f
                           : behavior.attackRange * 0.6f;
        Move(direction, distance, distance > stopDistance);

        // Burrowers can only attack once they've fully surfaced
        bool canAttack = behavior.movement != "burrow" || burrowPhase == BurrowPhase.Surfaced;

        cooldown -= Time.deltaTime;
        if (canAttack && distance <= behavior.attackRange && cooldown <= 0f)
        {
            cooldown = behavior.attackCooldown * (enraged ? 0.6f : 1f);
            Attack(direction);
        }
    }

    // ---------- Movement ----------

    private void Move(Vector3 direction, float distance, bool advance)
    {
        Vector3 position = transform.position;
        float step = Speed * Time.deltaTime;
        float lift = 0f;

        switch (behavior.movement)
        {
            case "hop":
            {
                // Half-second hops with a short pause on the ground between them
                float cycle = age % 0.8f;
                bool airborne = cycle < 0.5f;
                lift = airborne ? Mathf.Sin(cycle / 0.5f * Mathf.PI) * HopHeight : 0f;
                if (advance && airborne)
                    position += direction * step * 1.6f;
                break;
            }
            case "fly":
                lift = HoverHeight + Mathf.Sin(age * 2f) * 0.25f;
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
            case "teleport":
                if (advance)
                    position += direction * step * 0.5f;
                teleportTimer -= Time.deltaTime;
                if (teleportTimer <= 0f && distance > 2f)
                {
                    teleportTimer = Random.Range(3f, 5f);
                    StartCoroutine(Teleport());
                }
                break;
            case "burrow":
                position = Burrow(position);
                lift = burrowOffset;
                break;
            case "orbit":
            {
                // Circle at attack distance, drifting in or out to hold the ring
                float preferred = IsShooter ? behavior.attackRange * 0.75f : behavior.attackRange * 0.6f;
                Vector3 around = Vector3.Cross(Vector3.up, direction) * orbitDirection;
                float inOut = Mathf.Clamp(distance - preferred, -1f, 1f);
                position += (around + direction * inOut).normalized * step;
                break;
            }
            default: // walk
                if (advance)
                    position += direction * step;
                break;
        }

        position.y = groundY + lift;
        transform.position = position;
    }

    private Vector3 Charge(Vector3 position, Vector3 direction, bool advance)
    {
        chargeTimer -= Time.deltaTime;
        switch (chargePhase)
        {
            case ChargePhase.Approach:
                if (advance)
                    position += direction * Speed * 0.4f * Time.deltaTime;
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
                position += dashDirection * Speed * 2f * Time.deltaTime;
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

    // Shrink away, reappear somewhere around the player, and grow back
    private IEnumerator Teleport()
    {
        busy = true;
        for (float t = 0f; t < 0.15f; t += Time.deltaTime)
        {
            transform.localScale = baseScale * (1f - t / 0.15f);
            yield return null;
        }

        float angle = Random.value * Mathf.PI * 2f;
        float distance = (IsShooter ? behavior.attackRange * 0.7f : 2.5f) + radius;
        transform.position = new Vector3(player.position.x + Mathf.Cos(angle) * distance, transform.position.y,
                                         player.position.z + Mathf.Sin(angle) * distance);

        for (float t = 0f; t < 0.15f; t += Time.deltaTime)
        {
            transform.localScale = baseScale * (t / 0.15f);
            yield return null;
        }
        transform.localScale = baseScale;
        busy = false;
    }

    // Underground it can't be hit, its health bar is hidden, and it zips around far faster than the player,
    // leaving a trail of snow spraying up from the ground. Every 5-10 s it pops up at attack distance,
    // attacks for a few seconds, then dives again.
    private Vector3 Burrow(Vector3 position)
    {
        float depth = height + 0.2f;
        float digSpeed = depth / BurrowSeconds * Time.deltaTime;
        Vector3 groundPoint = new Vector3(position.x, groundY, position.z);

        switch (burrowPhase)
        {
            case BurrowPhase.Surfaced:
                surfacedTimer -= Time.deltaTime;
                if (surfacedTimer <= 0f)
                {
                    burrowPhase = BurrowPhase.Diving;
                    healthBar.SetVisible(false);
                    SnowSpray.Emit(groundPoint, 15);
                }
                break;

            case BurrowPhase.Diving:
                burrowOffset = Mathf.MoveTowards(burrowOffset, -depth, digSpeed);
                if (burrowOffset <= -depth)
                {
                    burrowPhase = BurrowPhase.Zipping;
                    undergroundTimer = Random.Range(5f, 10f);
                    popUpChosen = false;
                    zipHeading = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                    PickZipWaypoint();
                }
                break;

            case BurrowPhase.Zipping:
            {
                undergroundTimer -= Time.deltaTime;
                waypointTimer -= Time.deltaTime;

                // Time's up: head for a spot at attack distance from the player and pop up there
                if (undergroundTimer <= 0f && !popUpChosen)
                {
                    float angle = Random.value * Mathf.PI * 2f;
                    float distance = (IsShooter ? Mathf.Max(3f, behavior.attackRange * 0.6f) : behavior.attackRange * 0.5f) + radius;
                    popUpSpot = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                    popUpChosen = true;
                }

                Vector3 target;
                if (popUpChosen)
                {
                    target = player.position + popUpSpot;
                }
                else
                {
                    if (waypointTimer <= 0f || Vector3.Distance(Flat(position), Flat(zipWaypoint)) < 1.5f)
                        PickZipWaypoint();
                    target = zipWaypoint;
                }

                // Steer with a limited turn rate so it swoops and loops instead of moving in straight lines
                Vector3 desired = Flat(target - position);
                if (desired.sqrMagnitude > 0.01f)
                    zipHeading = Vector3.RotateTowards(zipHeading, desired.normalized, ZipTurnDegreesPerSecond * Mathf.Deg2Rad * Time.deltaTime, 0f);
                position += zipHeading * ZipSpeed * Time.deltaTime;

                sprayBuildup += SprayPerSecond * Time.deltaTime;
                if (sprayBuildup >= 1f)
                {
                    SnowSpray.Emit(new Vector3(position.x, groundY, position.z), (int)sprayBuildup, 0.7f);
                    sprayBuildup -= (int)sprayBuildup;
                }

                bool arrived = popUpChosen && Vector3.Distance(Flat(position), Flat(target)) < 1.2f;
                if (arrived || undergroundTimer < -4f)
                {
                    burrowPhase = BurrowPhase.Surfacing;
                    transform.rotation = Quaternion.LookRotation(Flat(player.position - position).normalized);
                    healthBar.SetVisible(true);
                    SnowSpray.Emit(new Vector3(position.x, groundY, position.z), 25, 1.4f);
                }
                break;
            }

            case BurrowPhase.Surfacing:
                burrowOffset = Mathf.MoveTowards(burrowOffset, 0f, digSpeed);
                if (burrowOffset >= 0f)
                {
                    burrowPhase = BurrowPhase.Surfaced;
                    surfacedTimer = Random.Range(2.5f, 3.5f);
                    cooldown = Mathf.Min(cooldown, 0.3f); // attack right after popping up
                }
                break;
        }
        return position;
    }

    // A random spot 4-14 m from the player to swoop toward while underground
    private void PickZipWaypoint()
    {
        float angle = Random.value * Mathf.PI * 2f;
        zipWaypoint = player.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(4f, 14f);
        waypointTimer = 2f;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ---------- Attacks ----------

    private void Attack(Vector3 direction)
    {
        switch (behavior.attack)
        {
            case "ranged":
                Fire(0f, false);
                break;
            case "burst":
                StartCoroutine(Burst());
                break;
            case "spread":
                for (int i = -2; i <= 2; i++)
                    Fire(i * 12f, false);
                break;
            case "homing":
                Fire(0f, true);
                break;
            case "slam":
                StartCoroutine(Slam());
                break;
            case "explode":
                StartCoroutine(Explode());
                break;
            default:
                StartCoroutine(Lunge(direction));
                break;
        }
    }

    // Damages the player, and slows them too if the attacker has the "slows" ability
    public static void HitPlayer(PlayerHealth target, int damage, bool slows)
    {
        if (target == null)
            return;

        target.TakeDamage(damage);
        if (!slows)
            return;

        var mover = target.GetComponentInParent<SimpleFirstPerson>();
        if (mover == null)
            mover = FindFirstObjectByType<SimpleFirstPerson>();
        if (mover != null)
            mover.ApplySlow(SlowMultiplier, SlowSeconds);
        DamageFlash.Play(0.8f, SlowBlue);
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
        HitPlayer(playerHealth, behavior.damage, Slows);
        busy = false;
    }

    // Three quick shots in a row
    private IEnumerator Burst()
    {
        for (int i = 0; i < 3; i++)
        {
            Fire(0f, false);
            yield return new WaitForSeconds(0.15f);
        }
    }

    // Jump up and crash down, hurting the player if they're within range of the shockwave
    private IEnumerator Slam()
    {
        busy = true;
        float rise = 0.8f / Mathf.Max(transform.localScale.y, 0.01f);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            body.localPosition = bodyRestPosition + Vector3.up * (Mathf.Sin(t / 0.4f * Mathf.PI) * rise);
            yield return null;
        }
        body.localPosition = bodyRestPosition;

        EnemyPulse.Spawn(new Vector3(transform.position.x, groundY + 0.05f, transform.position.z), Color.white, behavior.attackRange + radius);

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.magnitude - radius <= behavior.attackRange)
            HitPlayer(playerHealth, behavior.damage, Slows);
        busy = false;
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
        if (toPlayer.magnitude - radius <= behavior.attackRange + 0.5f)
            HitPlayer(playerHealth, behavior.damage, Slows);
        Destroy(gameObject);
    }

    private void Fire(float yawOffset, bool homing)
    {
        if (projectileTemplate == null)
            return;

        Vector3 origin = transform.position + Vector3.up * (height * 0.6f);
        GameObject shot = Instantiate(projectileTemplate, origin, Quaternion.identity);
        shot.name = $"{name} shot";
        shot.SetActive(true);
        shot.AddComponent<EnemyProjectile>().Launch(playerHealth, behavior.damage, projectileSpin, yawOffset, homing, Slows);
    }

    // Builds the enemy's own projectile from Claude's design (or a ball in its main color if there isn't one),
    // centered and sized to ProjectileSize, with its front facing the direction of flight
    private void BuildProjectileTemplate(ModelRecipe.ProjectileSpec spec, Transform enemyModel)
    {
        projectileTemplate = new GameObject($"{name} projectile");
        var visual = new GameObject("Visual").transform;
        visual.SetParent(projectileTemplate.transform, false);

        Transform shape;
        if (spec != null && spec.parts != null && spec.parts.Length > 0)
        {
            shape = ModelRecipe.Build(spec.parts);
            projectileSpin = spec.spin;
        }
        else
        {
            shape = new GameObject("Model").transform;
            Renderer firstRenderer = enemyModel.GetComponentInChildren<Renderer>();
            Color color = firstRenderer != null ? firstRenderer.sharedMaterial.color : Color.white;
            LowPolyBuilder.Part(shape, "Ball", LowPolyBuilder.Sphere, color, Vector3.zero, Vector3.one);
            projectileSpin = "none";
        }

        Bounds bounds = ModelRecipe.MeasureBounds(shape);
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
        shape.SetParent(visual, false);
        shape.localPosition = -bounds.center;
        visual.localScale = Vector3.one * (ProjectileSize / largest);

        projectileTemplate.SetActive(false);
    }

    // ---------- Specials ----------

    // Every few seconds, patch up other hurt enemies nearby
    private void HealNearby()
    {
        healTimer -= Time.deltaTime;
        if (healTimer > 0f)
            return;
        healTimer = HealInterval;

        bool healedAny = false;
        foreach (ModelEnemy other in active)
        {
            if (other == this || other.health == null || other.health.hp <= 0 || other.health.hp >= other.health.hpMax)
                continue;
            if (Vector3.Distance(other.transform.position, transform.position) > HealRadius)
                continue;

            other.health.hp = Mathf.Min(other.health.hpMax, other.health.hp + Mathf.Max(5, other.health.hpMax / 5));
            healedAny = true;
        }

        if (healedAny)
            EnemyPulse.Spawn(new Vector3(transform.position.x, groundY + 0.05f, transform.position.z), HealGreen, 1.5f, 0.5f);
    }

    // Below half health: faster and attacks more often
    private void Enrage()
    {
        enraged = true;
        EnemyPulse.Spawn(new Vector3(transform.position.x, groundY + 0.05f, transform.position.z), EnrageRed, radius + 1f);
    }

    // "splits" enemies break into two smaller, weaker copies when killed
    private void OnDied()
    {
        if (behavior.special != "splits" || isSplitPiece || hasSplit)
            return;
        hasSplit = true;

        SpawnSplitPiece(-1f);
        SpawnSplitPiece(1f);
    }

    private void SpawnSplitPiece(float side)
    {
        var piece = new GameObject(name).transform;
        Vector3 ground = new Vector3(transform.position.x, groundY, transform.position.z);
        piece.SetPositionAndRotation(ground + transform.right * side * (radius * 0.6f + 0.2f), transform.rotation);
        piece.localScale = baseScale * 0.6f;

        Transform model = Instantiate(body, piece, false);
        model.name = "Model";
        model.localPosition = bodyRestPosition;

        var sourceBox = GetComponent<BoxCollider>();
        var box = piece.gameObject.AddComponent<BoxCollider>();
        box.center = sourceBox.center;
        box.size = sourceBox.size;

        BehaviorSpec pieceBehavior = behavior.Clone();
        pieceBehavior.health = behavior.health / 2;
        pieceBehavior.special = "none";
        piece.gameObject.AddComponent<ModelEnemy>().Configure(pieceBehavior, projectileSpec, model, radius * 0.6f, height * 0.6f, true);
    }

    void OnDestroy()
    {
        if (projectileTemplate != null)
            Destroy(projectileTemplate);
    }
}
