using UnityEngine;

// A walking bob for any enemy: while it moves, `target` rocks from side to side and stretches up and
// squashes down with each step. Faster movement means bigger, quicker bobs; standing still settles back to normal.
// Runs as a layer on top of whatever else moves the target: it takes last frame's effect off first thing
// each frame (so attack animations and telegraphs see the untouched scale) and puts the new one on at the end.
[DefaultExecutionOrder(-10000)]
public class WalkBob : MonoBehaviour
{
    private const float FullEffectSpeed = 0.8f; // m/s at which the bob is at full strength
    private const float MaxSpeed = 15f;         // faster than this is a teleport or dash, not walking
    private const float MaxRollDegrees = 7f;
    private const float StretchAmount = 0.09f;  // 9% taller at the top of a step, 9% squashed at the bottom
    private const float BaseStepsPerSecond = 1.6f;
    private const float StepsPerMeterPerSecond = 2.2f;
    private const float MaxStepsPerSecond = 5f;

    private Transform target;
    private Vector3 lastPosition;
    private float intensity;
    private float phase;

    // What was applied last frame, so it can be taken off again
    private Vector3 appliedScale = Vector3.one;
    private Quaternion appliedRoll = Quaternion.identity;
    private Vector3 scaleAfter;
    private Quaternion rotationAfter;
    private bool applied;

    // `target` is what gets bobbed; the speed is measured from this object's own movement
    public static WalkBob Add(GameObject enemy, Transform target)
    {
        var bob = enemy.AddComponent<WalkBob>();
        bob.target = target;
        bob.lastPosition = enemy.transform.position;
        bob.phase = Random.value * Mathf.PI * 2f; // so a crowd doesn't bob in unison
        return bob;
    }

    void Update()
    {
        Undo();
    }

    void LateUpdate()
    {
        if (target == null || Time.deltaTime <= 0f)
            return;

        Vector3 moved = transform.position - lastPosition;
        moved.y = 0f;
        lastPosition = transform.position;
        float speed = moved.magnitude / Time.deltaTime;
        if (speed > MaxSpeed)
            speed = 0f;

        // Ease in and out so starting and stopping don't snap
        float wanted = Mathf.Clamp01(speed / FullEffectSpeed);
        intensity = Mathf.MoveTowards(intensity, wanted, 6f * Time.deltaTime);
        if (intensity < 0.001f)
            return;

        float steps = Mathf.Min(BaseStepsPerSecond + speed * StepsPerMeterPerSecond, MaxStepsPerSecond);
        phase += steps * Mathf.PI * Time.deltaTime; // one full sway every two steps

        float roll = Mathf.Sin(phase) * MaxRollDegrees * intensity;
        // Highest as the weight passes over a foot, lowest as it lands: twice per sway
        float stretch = Mathf.Cos(phase * 2f) * StretchAmount * intensity;
        appliedScale = new Vector3(1f - stretch * 0.5f, 1f + stretch, 1f - stretch * 0.5f);
        appliedRoll = Quaternion.Euler(0f, 0f, roll);

        Vector3 scale = target.localScale;
        target.localScale = new Vector3(scale.x * appliedScale.x, scale.y * appliedScale.y, scale.z * appliedScale.z);
        target.localRotation = target.localRotation * appliedRoll;
        scaleAfter = target.localScale;
        rotationAfter = target.localRotation;
        applied = true;
    }

    // Only take it off if nothing else has rewritten the value since (an Animator will have)
    private void Undo()
    {
        if (!applied || target == null)
        {
            applied = false;
            return;
        }

        if ((target.localScale - scaleAfter).sqrMagnitude < 1e-8f)
        {
            Vector3 s = target.localScale;
            target.localScale = new Vector3(s.x / appliedScale.x, s.y / appliedScale.y, s.z / appliedScale.z);
        }

        if (Quaternion.Angle(target.localRotation, rotationAfter) < 0.01f)
            target.localRotation = target.localRotation * Quaternion.Inverse(appliedRoll);

        applied = false;
    }
}
