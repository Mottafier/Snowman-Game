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
    private const float ClimbSpeed = 3f;          // how fast an enemy scrambles up onto another's head
    private const float FallGravity = 20f;
    private const float SeparateSpeed = 3f;       // how hard side-by-side enemies push apart
    private const float MaxStackHeight = 6f;      // don't build towers taller than this
    private const float ClimbPatience = 0.5f;     // seconds stuck behind another enemy before climbing it
    private const float SpreadDegrees = 45f;      // enemies try to keep this far apart around the player
    private const float WobbleDegrees = 20f;      // how much they weave left and right while approaching

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
    private Vector3 bodyRestScale;
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
    private const float MeleeLungeMeters = 0.4f;  // how far past attackRange a melee hit still reaches
    private const float LungeSeconds = 0.36f;     // dash out, hit, snap back
    private const float LungeOutFraction = 0.45f; // the dash out is the quick part
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

    // Crowding: how high it's standing on top of other enemies, and what it's doing this frame
    private float standHeight;
    private float fallSpeed;
    private float lift; // hop/hover/burrow offset on top of standHeight
    private Vector3 moveDirection;
    private bool advancing;
    private bool climbing;
    private float blockedTimer;
    private float rideTimer;       // how long it's been riding on someone's head
    private float rideLimit;       // after this long it stops holding on and may wander off

    // Measured from the actual mesh (not the bounding box, which rotated parts make too big):
    // the very top of the model, so others stand right on it, and how far the lowest point sits above the feet
    private Vector3 headLocal;
    private float footGap;

    // Natural variation, so a crowd surrounds the player instead of funnelling into one spot
    private float surroundAngle;   // where around the player (degrees) this enemy wants to stand
    private bool hasSurroundAngle;
    private float speedScale = 1f;
    private float wanderSeed;

    private bool IsShooter => behavior.attack == "ranged" || behavior.attack == "burst"
                           || behavior.attack == "spread" || behavior.attack == "homing";

    private float Speed => (enraged ? Mathf.Min(behavior.speed * 1.5f, MaxEnragedSpeed) : behavior.speed) * speedScale;

    private bool Slows => behavior.special == "slows";

    // Who crowds with whom: walkers stack on each other, flyers only push apart from other flyers,
    // and burrowers underground don't touch anyone
    private enum CrowdLayer { None, Ground, Air }
    private CrowdLayer Layer => behavior.movement == "fly" ? CrowdLayer.Air
                              : behavior.movement == "burrow" && burrowPhase != BurrowPhase.Surfaced ? CrowdLayer.None
                              : CrowdLayer.Ground;

    public void Configure(BehaviorSpec spec, ModelRecipe.ProjectileSpec projectile, Transform model,
        float footprintRadius, float modelHeight, bool splitPiece = false)
    {
        behavior = Sanitize(spec);
        projectileSpec = projectile;
        body = model;
        bodyRestPosition = model.localPosition;
        bodyRestScale = model.localScale;
        WalkBob.Add(gameObject, model);
        baseScale = transform.localScale;
        radius = footprintRadius;
        height = modelHeight;
        groundY = transform.position.y;
        cooldown = behavior.attackCooldown * 0.5f;
        orbitDirection = Random.value < 0.5f ? -1f : 1f;
        speedScale = Random.Range(0.85f, 1.15f);
        wanderSeed = Random.Range(0f, 100f);
        isSplitPiece = splitPiece;
        if (splitPiece)
            wakeTimer = 0.5f;

        if (IsShooter)
            BuildProjectileTemplate(projectile, model);

        health = gameObject.AddComponent<EnemyHealth>();
        health.hpMax = behavior.health;
        health.Died += OnDied;
        healthBar = ModelHealthBar.Attach(health, height, radius * 2f, name.Replace("(Clone)", ""));
        MeasureHeadAndFeet();
        rideLimit = Random.Range(2f, 5f);
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
        {
            // Still get shoved and fall while attacking or teleporting
            Vector3 settled = Crowd(transform.position, transform.forward, false);
            settled.y = groundY + standHeight + lift;
            transform.position = settled;
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float centerDistance = toPlayer.magnitude;
        Vector3 direction = centerDistance > 0.01f ? toPlayer / centerDistance : transform.forward;
        float distance = Mathf.Max(0f, centerDistance - radius);

        // Shooters keep their distance; everyone else closes in
        float stopDistance = IsShooter ? behavior.attackRange * 0.75f
                           : behavior.attack == "explode" ? 0.3f
                           : behavior.attackRange * 0.6f;

        bool advance = distance > stopDistance;
        Vector3 travel = direction;
        if (behavior.movement != "orbit")
            travel = Steer(direction, centerDistance, stopDistance, out advance);

        // Face where it's walking while it's still on its way, and the player once it's close
        Vector3 facing = advance && distance > stopDistance + 1f ? travel : direction;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(facing), 8f * Time.deltaTime);

        Move(travel, distance, advance);

        // Burrowers can only attack once they've fully surfaced
        bool canAttack = behavior.movement != "burrow" || burrowPhase == BurrowPhase.Surfaced;

        cooldown -= Time.deltaTime;
        if (canAttack && distance <= behavior.attackRange && cooldown <= 0f)
        {
            cooldown = behavior.attackCooldown * (enraged ? 0.6f : 1f);
            StartCoroutine(TelegraphThenAttack());
        }
    }

    // ---------- Movement ----------

    private void MeasureHeadAndFeet()
    {
        float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
        foreach (MeshFilter filter in body.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null)
                continue;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 local = transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                if (local.y > top)
                {
                    top = local.y;
                    headLocal = local;
                }
                bottom = Mathf.Min(bottom, local.y);
            }
        }

        if (float.IsInfinity(top))
        {
            headLocal = Vector3.up * (height / Mathf.Max(transform.localScale.y, 0.01f));
            bottom = 0f;
        }
        footGap = Mathf.Max(0f, bottom * transform.localScale.y);
    }

    // The top of its head in the world, following it as it hops, lunges and turns
    private Vector3 HeadPoint => transform.TransformPoint(headLocal);

    // Instead of everyone heading for the player's exact position, each enemy heads for its own spot on a ring
    // around them (starting from the side it's already on), drifts away from spots other enemies have taken,
    // and weaves a little on the way in, so crowds surround the player rather than piling up in one place.
    private Vector3 Steer(Vector3 toPlayer, float centerDistance, float stopDistance, out bool advance)
    {
        if (!hasSurroundAngle)
        {
            surroundAngle = Mathf.Atan2(-toPlayer.z, -toPlayer.x) * Mathf.Rad2Deg + Random.Range(-50f, 50f);
            hasSurroundAngle = true;
        }

        foreach (ModelEnemy other in active)
        {
            if (other == this || !other.hasSurroundAngle || other.behavior == null || other.Layer != Layer)
                continue;
            float delta = Mathf.DeltaAngle(other.surroundAngle, surroundAngle);
            if (Mathf.Abs(delta) >= SpreadDegrees)
                continue;
            float away = delta != 0f ? Mathf.Sign(delta) : (GetInstanceID() < other.GetInstanceID() ? 1f : -1f);
            surroundAngle += away * (SpreadDegrees - Mathf.Abs(delta)) * 2f * Time.deltaTime;
        }
        surroundAngle += (Mathf.PerlinNoise(age * 0.15f, wanderSeed) - 0.5f) * 30f * Time.deltaTime;

        float ring = stopDistance * 0.85f + radius;
        float a = surroundAngle * Mathf.Deg2Rad;
        Vector3 goal = player.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring;
        Vector3 toGoal = Flat(goal - transform.position);

        advance = toGoal.magnitude > 0.3f;
        Vector3 heading = toGoal.sqrMagnitude > 0.0001f ? toGoal.normalized : toPlayer;
        float wobble = (Mathf.PerlinNoise(age * 0.6f, wanderSeed + 7f) - 0.5f) * 2f * WobbleDegrees * Mathf.Clamp01(centerDistance / 6f);
        return Quaternion.Euler(0f, wobble, 0f) * heading;
    }

    private void Move(Vector3 direction, float distance, bool advance)
    {
        Vector3 position = transform.position;
        float step = Speed * Time.deltaTime;
        lift = 0f;

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

        position = Crowd(position, direction, advance);
        position.y = groundY + standHeight + lift;
        transform.position = position;
    }

    // Megabonk-style crowding. Enemies side by side push apart. One that's heading for the player with
    // another enemy blocking the way first tries to step around it, and if it stays stuck climbs up onto the very top
    // of its head, rides along up there for a few seconds, and falls off when it walks off or the one underneath dies.
    private Vector3 Crowd(Vector3 position, Vector3 direction, bool advance)
    {
        moveDirection = direction;
        advancing = advance;
        CrowdLayer layer = Layer;
        float support = 0f;    // height of the highest head it's standing on
        float climbTo = -1f;   // height of the head it's climbing onto, if any
        bool blocked = false;  // something's in the way this frame
        ModelEnemy supporter = null;
        Vector3 climbHead = Vector3.zero;

        if (layer != CrowdLayer.None)
        {
            foreach (ModelEnemy other in active)
            {
                if (other == this || other.behavior == null || other.Layer != layer)
                    continue;

                Vector3 away = Flat(position - other.transform.position);
                float gap = away.magnitude;
                float reach = (radius + other.radius) * 0.8f;
                if (gap >= reach)
                    continue;
                Vector3 awayDir = gap > 0.001f ? away / gap : Quaternion.Euler(0f, Random.value * 360f, 0f) * Vector3.forward;

                if (layer == CrowdLayer.Ground)
                {
                    // The standHeight that puts my lowest point exactly on the top of its head
                    Vector3 head = other.HeadPoint;
                    float otherTop = head.y - groundY - footGap;
                    if (standHeight >= otherTop - 0.1f)
                    {
                        // Up at its head height: only stand on it if I'm right over its head, otherwise fall
                        if (Flat(head - position).magnitude <= Mathf.Max(0.3f, other.radius * 0.5f) && otherTop > support)
                        {
                            support = otherTop;
                            supporter = other;
                        }
                        continue;
                    }
                    if (other.standHeight > standHeight + 0.05f)
                        continue; // it's climbing on or standing on me: its job, not mine

                    // Blocked by it on the way to the player: climb. If we're both blocked by each other, only one climbs.
                    bool inMyWay = Vector3.Dot(direction, -awayDir) > 0.3f;
                    bool imInItsWay = other.advancing && Vector3.Dot(other.moveDirection, awayDir) > 0.3f;
                    if (advance && inMyWay && otherTop <= MaxStackHeight
                        && (!imInItsWay || GetInstanceID() < other.GetInstanceID()))
                    {
                        blocked = true;
                        if (climbing || blockedTimer >= ClimbPatience)
                        {
                            // Scramble up and over toward the top of its head
                            if (otherTop > climbTo)
                            {
                                climbTo = otherTop;
                                climbHead = head;
                            }
                            continue;
                        }
                        // Not stuck for long yet: try to step around it first
                        position += Vector3.Cross(Vector3.up, awayDir) * orbitDirection * Speed * Time.deltaTime;
                    }
                }

                position += awayDir * Mathf.Min(reach - gap, SeparateSpeed * Time.deltaTime);
            }
        }

        blockedTimer = blocked ? blockedTimer + Time.deltaTime : Mathf.Max(0f, blockedTimer - 2f * Time.deltaTime);
        climbing = climbTo > standHeight;

        if (climbTo > standHeight)
        {
            // Move over its head at the same pace as climbing up, so it arrives right on top
            float upward = Mathf.Clamp01(ClimbSpeed * Time.deltaTime / (climbTo - standHeight));
            position = Vector3.Lerp(position, new Vector3(climbHead.x, position.y, climbHead.z), upward);
            standHeight = Mathf.MoveTowards(standHeight, climbTo, ClimbSpeed * Time.deltaTime);
            fallSpeed = 0f;
        }
        else if (standHeight > support)
        {
            fallSpeed += FallGravity * Time.deltaTime;
            standHeight = Mathf.Max(support, standHeight - fallSpeed * Time.deltaTime);
        }
        else
        {
            standHeight = support;
            fallSpeed = 0f;
        }

        // Riding: stay centred on its head as it moves, for a few seconds, then let go and maybe wander off
        if (supporter != null && !climbing)
        {
            rideTimer += Time.deltaTime;
            if (rideTimer < rideLimit)
            {
                Vector3 head = supporter.HeadPoint;
                position = Vector3.Lerp(position, new Vector3(head.x, position.y, head.z), Mathf.Min(1f, 20f * Time.deltaTime));
            }
        }
        else if (standHeight <= 0f)
        {
            rideTimer = 0f;
            rideLimit = Random.Range(2f, 5f);
        }
        return position;
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

    // Roughly how tall the model is in the world, so the swipe lines up with its body
    private float BodyHeight()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in body.GetComponentsInChildren<MeshRenderer>())
            bounds.Encapsulate(r.bounds);
        return Mathf.Clamp(bounds.max.y - groundY, 0.8f, 6f);
    }

    private bool IsMelee => !IsShooter && behavior.attack != "slam" && behavior.attack != "explode";

    // Flash white, swell, freeze for a moment, then attack toward wherever the player is now.
    // Melee enemies lock their aim at the start and show the slash zone, so the player can step out of it.
    private IEnumerator TelegraphThenAttack()
    {
        busy = true;
        MeleeSlash slash = null;
        Vector3 aim = Flat(player.position - transform.position);
        aim = aim.sqrMagnitude > 0.0001f ? aim.normalized : transform.forward;
        if (IsMelee)
        {
            float reach = radius + behavior.attackRange + MeleeLungeMeters;
            slash = MeleeSlash.Show(new Vector3(transform.position.x, groundY + 0.05f, transform.position.z), aim, reach,
                AttackTelegraph.FlashSeconds + AttackTelegraph.PauseSeconds, BodyHeight());
        }

        yield return AttackTelegraph.Play(body, body);
        busy = false;

        if (IsMelee)
        {
            StartCoroutine(Lunge(aim, slash));
            yield break;
        }

        Vector3 toPlayer = Flat(player.position - transform.position);
        Attack(toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : transform.forward);
    }

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
                StartCoroutine(Lunge(direction, null));
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

    private IEnumerator Lunge(Vector3 direction, MeleeSlash slash)
    {
        busy = true;
        if (slash != null)
            slash.Strike(LungeSeconds * LungeOutFraction);

        // Dash most of the way to the player, then snap back
        float dash = Mathf.Clamp(behavior.attackRange + 0.6f, 1.2f, 3f);
        Vector3 localForward = transform.InverseTransformDirection(direction) * dash / Mathf.Max(transform.localScale.x, 0.01f);
        float outSeconds = LungeSeconds * LungeOutFraction;
        bool hit = false;
        for (float t = 0f; t < LungeSeconds; t += Time.deltaTime)
        {
            float amount;
            if (t < outSeconds)
            {
                float k = t / outSeconds;
                amount = 1f - (1f - k) * (1f - k); // fast start, lands at full stretch
            }
            else
            {
                if (!hit)
                {
                    hit = true;
                    LungeHit(direction);
                }
                float k = (t - outSeconds) / (LungeSeconds - outSeconds);
                amount = 1f - k * k; // quick pull back
            }
            body.localPosition = bodyRestPosition + localForward * amount;
            yield return null;
        }
        body.localPosition = bodyRestPosition;
        if (!hit)
            LungeHit(direction);
        busy = false;
    }

    // Only connects if the player is still inside the slash zone, so walking out of it dodges the hit
    private void LungeHit(Vector3 direction)
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.magnitude - radius <= behavior.attackRange + MeleeLungeMeters && MeleeSlash.InArc(direction, toPlayer))
            HitPlayer(playerHealth, behavior.damage, Slows);
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
        model.localScale = bodyRestScale; // in case it died mid-telegraph while swollen

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
