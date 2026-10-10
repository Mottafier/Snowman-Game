using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Listens on localhost for low poly model recipes sent from the terminal (Tools/spawn-model.cmd)
// and builds each one (or a whole group of copies) in front of the player as enemies.
public class TerminalModelSpawner : MonoBehaviour
{
    public const int Port = 5055;

    // Limits that keep big groups ("an army of bananas") from overwhelming the game
    private const int MaxPerRequest = 50;
    private const int MaxPartsPerRequest = 3000;
    private const int MaxAliveEnemies = 100;
    private const int SpawnsPerFrame = 5;

    [SerializeField] private float gapInFrontOfPlayer = 1.5f;
    [SerializeField] private float minModelSize = 0.3f;
    [SerializeField] private float maxModelSize = 6f;
    [SerializeField] private float popInSeconds = 0.35f;

    private TcpListener listener;
    private Thread listenThread;
    private readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
    private readonly List<GameObject> spawned = new List<GameObject>();

    // Create the spawner automatically so it doesn't need to be added to the scene
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstance()
    {
        if (FindAnyObjectByType<TerminalModelSpawner>() != null)
            return;

        var go = new GameObject(nameof(TerminalModelSpawner));
        DontDestroyOnLoad(go);
        go.AddComponent<TerminalModelSpawner>();
    }

    void OnEnable()
    {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
        }
        catch (SocketException e)
        {
            Debug.LogWarning($"TerminalModelSpawner: couldn't listen on port {Port}: {e.Message}");
            listener = null;
            return;
        }

        listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "TerminalModelSpawner" };
        listenThread.Start();
        Debug.Log($"TerminalModelSpawner: listening on 127.0.0.1:{Port}");
    }

    void OnDisable()
    {
        listener?.Stop();
        listener = null;
        listenThread = null;
    }

    // Runs on a background thread: accept one connection at a time, read one line of JSON
    private void ListenLoop()
    {
        var activeListener = listener;
        while (true)
        {
            try
            {
                using (TcpClient client = activeListener.AcceptTcpClient())
                using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
                {
                    string line = reader.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line))
                        pending.Enqueue(line);
                }
            }
            catch (SocketException)
            {
                return; // listener was stopped
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (IOException)
            {
                // client disconnected mid-read; keep listening
            }
        }
    }

    void Update()
    {
        while (pending.TryDequeue(out string json))
        {
            try
            {
                SpawnModel(JsonUtility.FromJson<ModelRecipe>(json));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TerminalModelSpawner: couldn't build model: {e.Message}");
            }
        }
    }

    private void SpawnModel(ModelRecipe spec)
    {
        if (spec?.parts == null || spec.parts.Length == 0)
        {
            Debug.LogWarning("TerminalModelSpawner: received a model with no parts");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning($"TerminalModelSpawner: no main camera to spawn \"{spec.name}\" in front of");
            return;
        }

        int count = AllowedCount(spec);
        if (count <= 0)
        {
            Debug.LogWarning($"TerminalModelSpawner: already {MaxAliveEnemies} enemies alive; defeat some before spawning \"{spec.name}\"");
            return;
        }

        // Build the model once at the origin so its bounds are easy to measure; every copy is cloned from it
        Transform model = ModelRecipe.Build(spec.parts);
        Bounds bounds = ModelRecipe.MeasureBounds(model);

        // Shrink huge things and enlarge tiny things so they're easy to see
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float scale = largest > maxModelSize ? maxModelSize / largest
                    : largest < minModelSize ? minModelSize / largest
                    : 1f;

        // The holder sits on the ground; the model is offset so its base and center line up with it
        var prototype = new GameObject(spec.name).transform;
        model.SetParent(prototype, false);
        model.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

        // One box around the whole model so snowballs can hit it
        var box = prototype.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, bounds.size.y / 2f, 0f);
        box.size = bounds.size;

        prototype.gameObject.SetActive(false);
        StartCoroutine(SpawnGroup(spec, prototype, bounds, scale, count, cam.transform));

        Debug.Log($"TerminalModelSpawner: spawning {count} x \"{spec.name}\" ({spec.parts.Length} parts each)");
    }

    // How many copies to actually spawn: what Claude asked for, within the per-request and alive limits
    private int AllowedCount(ModelRecipe spec)
    {
        spawned.RemoveAll(enemy => enemy == null);

        int count = Mathf.Clamp(spec.count, 1, MaxPerRequest);
        count = Mathf.Min(count, Mathf.Max(1, MaxPartsPerRequest / spec.parts.Length));
        return Mathf.Min(count, MaxAliveEnemies - spawned.Count);
    }

    // Places the copies in a sunflower-pattern cluster in front of the player, a few per frame to avoid a hitch
    private IEnumerator SpawnGroup(ModelRecipe spec, Transform prototype, Bounds bounds, float scale, int count, Transform cam)
    {
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(cam.up, Vector3.up);
        forward.Normalize();
        Quaternion facePlayer = Quaternion.LookRotation(-forward, Vector3.up);

        float footprint = Mathf.Max(bounds.size.x, bounds.size.z) * scale;
        float spacing = footprint * 1.2f + 0.3f;
        float clusterRadius = count > 1 ? spacing * 0.6f * Mathf.Sqrt(count) : 0f;
        Vector3 center = cam.position + forward * (gapInFrontOfPlayer + footprint / 2f + clusterRadius);

        for (int i = 0; i < count; i++)
        {
            float angle = i * 137.5f * Mathf.Deg2Rad;
            float distance = spacing * 0.6f * Mathf.Sqrt(i);
            Vector3 spot = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            spot.y = GroundHeight(spot, cam.position.y);

            Transform holder = Instantiate(prototype, spot, facePlayer);
            holder.name = spec.name;
            holder.localScale = Vector3.zero;
            holder.gameObject.SetActive(true);
            spawned.Add(holder.gameObject);

            Transform model = holder.Find("Model");
            StartCoroutine(PopIn(holder, scale, () =>
            {
                if (spec.behavior != null)
                    holder.gameObject.AddComponent<ModelEnemy>().Configure(spec.behavior, spec.projectile, model, footprint / 2f, bounds.size.y * scale);
            }));

            if (i % SpawnsPerFrame == SpawnsPerFrame - 1)
                yield return null;
        }

        Destroy(prototype.gameObject);
    }

    // Find the ground under a point, ignoring spawned enemies, trees and anything above the player's eyes
    private float GroundHeight(Vector3 point, float eyeHeight)
    {
        var from = new Vector3(point.x, eyeHeight, point.z);
        float ground = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 50f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!spawned.Contains(hit.collider.gameObject) && hit.collider.GetComponentInParent<TrailTree>() == null)
                ground = Mathf.Max(ground, hit.point.y);
        }
        return float.IsNegativeInfinity(ground) ? 0f : ground;
    }

    // onDone runs once the model has reached full size (EnemyHealth remembers the scale it starts at)
    private IEnumerator PopIn(Transform holder, float targetScale, Action onDone)
    {
        for (float t = 0f; t < popInSeconds; t += Time.deltaTime)
        {
            if (holder == null)
                yield break;

            // Overshoot slightly, then settle
            float x = t / popInSeconds;
            float s = 1f + 2.7f * Mathf.Pow(x - 1f, 3f) + 1.7f * Mathf.Pow(x - 1f, 2f);
            holder.localScale = Vector3.one * (targetScale * s);
            yield return null;
        }
        if (holder == null)
            yield break;
        holder.localScale = Vector3.one * targetScale;
        onDone();
    }
}
