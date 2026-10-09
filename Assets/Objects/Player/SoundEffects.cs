using UnityEngine;

public class SoundEffects : MonoBehaviour
{


    [SerializeField] private AudioSource oneShotSource;
    [SerializeField] private AudioClip[] snowShoot;
    [SerializeField] private AudioClip snowGet;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    public void PlayShoot()
    {
        AudioClip clip = snowShoot[Random.Range(0, snowShoot.Length)];
        oneShotSource.PlayOneShot(clip, 1f);
    }

    public void PlaySnowGet()
    {
        oneShotSource.PlayOneShot(snowGet, 0.4f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
