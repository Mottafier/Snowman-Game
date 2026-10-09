using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Listens on localhost for low poly model recipes sent from the terminal (Tools/spawn-model.cmd)
// and builds each one in front of the player.
public class TerminalModelSpawner : MonoBehaviour
{
    public const int Port = 5055;

    [SerializeField] private float gapInFrontOfPlayer = 1.5f;
    [SerializeField] private float minModelSize = 0.3f;
    [SerializeField] private float maxModelSize = 6f;
    [SerializeField] private float popInSeconds = 0.35f;

    private TcpListener listener;
    private Thread listenThread;
    private readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();

    // JSON shape sent by Tools/spawn_model.py (fields are filled in by JsonUtility)
#pragma warning disable 0649
    [Serializable]
    private class ModelSpec
    {
        public string name;
        public PartSpec[] parts;
    }

    [Serializable]
    private class PartSpec
    {
        public string shape;
        public Rgb color;
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scale;
    }

    [Serializable]
    private class Rgb
    {
        public float r, g, b;
    }
#pragma warning restore 0649

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
                SpawnModel(JsonUtility.FromJson<ModelSpec>(json));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TerminalModelSpawner: couldn't build model: {e.Message}");
            }
        }
    }

    private void SpawnModel(ModelSpec spec)
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

        // Build the model at the origin so its bounds are easy to measure
        Transform model = BuildModel(spec);
        Bounds bounds = MeasureBounds(model);

        // Shrink huge things and enlarge tiny things so they're easy to see
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float scale = largest > maxModelSize ? maxModelSize / largest
                    : largest < minModelSize ? minModelSize / largest
                    : 1f;

        // The holder sits on the ground in front of the player; the model is offset so its base and center line up with it
        var holder = new GameObject(spec.name).transform;
        model.SetParent(holder, false);
        model.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up);
        forward.Normalize();

        float depth = Mathf.Max(bounds.size.x, bounds.size.z) * scale;
        Vector3 spot = cam.transform.position + forward * (gapInFrontOfPlayer + depth / 2f);
        spot.y = GroundHeight(spot, cam.transform.position.y);

        holder.SetPositionAndRotation(spot, Quaternion.LookRotation(-forward, Vector3.up));
        StartCoroutine(PopIn(holder, scale));

        Debug.Log($"TerminalModelSpawner: spawned \"{spec.name}\" ({spec.parts.Length} parts)");
    }

    private static Transform BuildModel(ModelSpec spec)
    {
        var root = new GameObject("Model").transform;
        foreach (PartSpec part in spec.parts)
        {
            Mesh mesh = MeshFor(part.shape);
            if (mesh == null)
                continue;

            var color = part.color != null ? new Color(part.color.r, part.color.g, part.color.b) : Color.white;
            LowPolyBuilder.Part(root, part.shape, mesh, color, part.position, part.scale, part.rotation);
        }
        return root;
    }

    private static Mesh MeshFor(string shape)
    {
        switch (shape)
        {
            case "sphere": return LowPolyBuilder.Sphere;
            case "cube": return LowPolyBuilder.Box;
            case "cylinder": return LowPolyBuilder.Cylinder;
            case "cone": return LowPolyBuilder.Cone;
            default: return null;
        }
    }

    private static Bounds MeasureBounds(Transform model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // Find the ground under a point, ignoring anything we hit above the player's eyes
    private static float GroundHeight(Vector3 point, float eyeHeight)
    {
        var from = new Vector3(point.x, eyeHeight, point.z);
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return 0f;
    }

    private IEnumerator PopIn(Transform holder, float targetScale)
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
        if (holder != null)
            holder.localScale = Vector3.one * targetScale;
    }
}
