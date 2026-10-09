using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class Slot : MonoBehaviour
{
    public enum ItemType
    {
        Empty,
        Snow,
        YellowSnow,
        RedSnow,
        Stick,
        Wood
    }

    [SerializeField] private ItemType Holding = ItemType.Empty;
    [SerializeField] private Image icon;

    private AudioSource audioSource;
    private AudioClip audioClip;

    public bool hiding = true;

    public void ShowSnowball()
    {
        if (hiding == true)
        {
            audioSource.Play();
            hiding = false;
            icon.enabled = true;

            StopAllCoroutines();
            StartCoroutine(Squish());
        }
    }
    public void HideSnowball()
    {
        if (hiding == false)
        {
            hiding = true;
            icon.enabled = false;
        }
    }

    private IEnumerator Squish()
    {
        RectTransform rt = icon.rectTransform;

        // Start tiny
        rt.localScale = Vector3.zero;

        // Grow past full size
        float duration = 0.08f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            rt.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * 1.2f, t / duration);
            yield return null;
        }

        // Shrink back to normal
        duration = 0.08f;
        t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            rt.localScale = Vector3.Lerp(Vector3.one * 1.2f, Vector3.one, t / duration);
            yield return null;
        }

        rt.localScale = Vector3.one;
    }

    private void Awake()
    {
        audioSource = GetComponentInParent<AudioSource>();
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

}
