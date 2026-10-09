using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;

// Listens on localhost for text sent from the terminal (Tools/spawn-text.cmd)
// and spawns it as 3D text in front of the main camera.
public class TerminalTextSpawner : MonoBehaviour
{
    public const int Port = 5055;

    [SerializeField] private float distanceFromCamera = 3f;
    [SerializeField] private float fontSize = 3f;
    [SerializeField] private Color textColor = Color.white;

    private TcpListener listener;
    private Thread listenThread;
    private readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();

    // Create the spawner automatically so it doesn't need to be added to the scene
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateInstance()
    {
        if (FindAnyObjectByType<TerminalTextSpawner>() != null)
            return;

        var go = new GameObject(nameof(TerminalTextSpawner));
        DontDestroyOnLoad(go);
        go.AddComponent<TerminalTextSpawner>();
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
            Debug.LogWarning($"TerminalTextSpawner: couldn't listen on port {Port}: {e.Message}");
            listener = null;
            return;
        }

        listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "TerminalTextSpawner" };
        listenThread.Start();
        Debug.Log($"TerminalTextSpawner: listening on 127.0.0.1:{Port}");
    }

    void OnDisable()
    {
        listener?.Stop();
        listener = null;
        listenThread = null;
    }

    // Runs on a background thread: accept one connection at a time, read one line of text
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
                        pending.Enqueue(line.Trim());
                }
            }
            catch (SocketException)
            {
                return; // listener was stopped
            }
            catch (System.ObjectDisposedException)
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
        while (pending.TryDequeue(out string text))
            SpawnText(text);
    }

    private void SpawnText(string text)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning($"TerminalTextSpawner: no main camera to spawn \"{text}\" in front of");
            return;
        }

        Transform camTransform = cam.transform;
        var go = new GameObject($"Text: {text}");
        go.transform.position = camTransform.position + camTransform.forward * distanceFromCamera;
        go.transform.rotation = Quaternion.LookRotation(camTransform.forward, Vector3.up);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = textColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
        tmp.textWrappingMode = TextWrappingModes.Normal;
    }
}
