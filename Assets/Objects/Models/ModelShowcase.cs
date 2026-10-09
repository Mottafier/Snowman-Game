using UnityEngine;
using UnityEngine.SceneManagement;

// Places the hand-built low poly models in the world, just in front of the player's spawn point.
public static class ModelShowcase
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SpawnModels()
    {
        if (SceneManager.GetActiveScene().name != "World")
            return;

        Transform fish = LowPolyModels.BuildFish();
        fish.SetPositionAndRotation(new Vector3(48.5f, 1.1f, 36.5f), Quaternion.Euler(0f, -90f, 0f));

        Transform obama = LowPolyModels.BuildObama();
        obama.SetPositionAndRotation(new Vector3(51.5f, 0f, 36.5f), Quaternion.Euler(0f, 180f, 0f));
    }
}
