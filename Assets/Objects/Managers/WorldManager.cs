using System.Drawing;
using UnityEngine;

public class WorldManager : MonoBehaviour
{
    [SerializeField] private Snowpile snowTilePrefab;

    [SerializeField] private int worldWidth = 100;
    [SerializeField] private int worldHeight = 100;

    private Snowpile[,] snowpiles;

    void Start()
    {
        snowpiles = new Snowpile[worldWidth, worldHeight];

        for (int x = 0; x < worldWidth; x++)
        {
            for (int z = 0; z < worldHeight; z++)
            {
                Vector3 position = new Vector3(x, 0, z);
                Snowpile snow = Instantiate(snowTilePrefab, position, Quaternion.identity, transform);
                 

                snowpiles[x,z] = snow;
            }
        }
    }

    public int Collect(int x, int z)
    {
        Snowpile snowpile = snowpiles[x, z];

        if (snowpile == null)
            return 0;

        return snowpile.Shrink() ? 1 : 0;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
