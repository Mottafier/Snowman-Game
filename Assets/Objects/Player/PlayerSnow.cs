using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSnow : MonoBehaviour
{
    InputAction collectAction;

    [SerializeField] public int snowAmount = 0;
    [SerializeField] public Snowbar snowBar;
    [SerializeField] private AudioSource collectSnowAudio;
    [SerializeField] private GameObject snowBase;
    [SerializeField] private SimpleFirstPerson simpleFPS;

    [SerializeField] private WorldManager worldManager;

    private Coroutine fadeRoutine;
    public int SnowAmount => snowAmount;

    public bool isCollecting {get; private set;}
    private float playerScale = 1f;
    private int snowCapacity = 5;
    private float minScale = 0.3f;
    private float maxScale = 1f;
    private bool buildBase = false;
    [SerializeField] public float collectCombo = 0f;
    private float collectProgress = 0f;

    // isGrounded can drop out for a frame (e.g. when the collect squash changes the capsule), so treat
    // the player as grounded if they touched the ground very recently
    private const float GroundedGraceSeconds = 0.25f;

    private CharacterController controller;
    private bool collectHeld;
    private float lastGroundedTime = float.NegativeInfinity;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    public void OnCollect(InputValue value)
    {
        collectHeld = value.isPressed;
    }
    public void OnPlaceBase(InputValue value)
    {
        if (value.isPressed)
        {
            buildBase = true;
        }
    }

    public void SpendSnow(int amount)
    {
        snowAmount -= amount;
    }
    public void AddSnow(int amount)
    {
        snowAmount += amount;
    }

    public void StartCollectSound()
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        collectSnowAudio.volume = 0.4f;

        if (!collectSnowAudio.isPlaying)
            collectSnowAudio.Play();
    }

    public void StopCollectSound()
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeOut(0.05f));
    }

    private IEnumerator FadeOut(float duration)
    {
        float startVolume = collectSnowAudio.volume;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            collectSnowAudio.volume = Mathf.Lerp(startVolume, 0f, t / duration);
            yield return null;
        }

        collectSnowAudio.Stop();
        collectSnowAudio.volume = 1f; // Reset for next time
    }

    // Update is called once per frame
    void Update()
    {

        float snowPercent = SnowAmount / snowCapacity;
        float targetScale = 1f;
        Vector3 collectMod = Vector3.one;

        snowBar.collectCombo = collectCombo;

        // Snow can only be scooped up while standing on the ground
        if (controller == null || controller.isGrounded)
            lastGroundedTime = Time.time;
        bool grounded = Time.time - lastGroundedTime < GroundedGraceSeconds;

        // Once full, the collect button has to be pressed again (same as when a tile runs out of snow)
        if (SnowAmount >= snowCapacity)
            collectHeld = false;

        // In the air, collecting just pauses; it picks back up on landing if the button is still held
        isCollecting = collectHeld && grounded;

        if (isCollecting)
        {
            // Build combo while continuously collecting
            collectCombo += Time.deltaTime;

            // 0 -> 1 over 5 seconds
            float t = Mathf.Clamp01(collectCombo / 5f);

            // Starts at 0.5 snow/sec, accelerates toward 3.5 snow/sec
            float collectRate = 0.5f + 3f * Mathf.Pow(t, 3f);

            // Build fractional progress toward the next whole snow
            collectProgress += collectRate * Time.deltaTime;

            // Whenever we've accumulated enough for 1 whole snow...
            while (collectProgress >= 1f && SnowAmount < snowCapacity)
            {
                int snowGain = worldManager.Collect(
                    Mathf.RoundToInt(transform.position.x),
                    Mathf.RoundToInt(transform.position.z)
                );

                if (snowGain > 0)
                {
                    AddSnow(snowGain);
                    collectProgress -= 1f;
                }
                else
                {
                    // Current tile has no snow, so don't consume the progress.
                    isCollecting = false;
                    collectHeld = false;
                    break;
                }
            }

            StartCollectSound();

            collectMod = new Vector3(1.1f, 0.9f, 1.1f);
        }
        else
        {
            collectCombo = 0f;
            collectProgress = 0f;

            StopCollectSound();
        }

        snowBar.snowAmount = snowAmount;

        snowPercent = (float)SnowAmount / snowCapacity;
        targetScale = Mathf.Lerp(minScale, maxScale, snowPercent);


        transform.localScale = collectMod * targetScale;

    }
    private void FixedUpdate()
    {
        if (buildBase && SnowAmount >= 1f)
        {
            BuildBase();
            buildBase = false;
        }
    }
    private void BuildBase()
    {
        float gridSize = 1f;

        Vector3 playerPosition = transform.position;

        Vector3 snappedPosition = new Vector3(
         Mathf.Round(transform.position.x / gridSize) * gridSize,
         Mathf.Round(transform.position.y / 0.5f) * 0.5f,
         Mathf.Round(transform.position.z / gridSize) * gridSize
     );
        SpendSnow(1);
        Debug.Log($"Placing base at {transform.position.y}");
        Instantiate(snowBase, snappedPosition, Quaternion.identity);

        simpleFPS.Jump();

    }

}
