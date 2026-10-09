using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class Snowbar : MonoBehaviour
{
    [SerializeField] private Slot[] slots; // Array to hold ammo counts for 5 slots

    [SerializeField] private Sprite snowball;

    [SerializeField] private CraftingSlot craftingSlot;

    private AudioSource audioSource;

    public int snowAmount;
    public float collectCombo;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {


        audioSource.pitch = Mathf.Lerp(0.7f, 1.2f, collectCombo/5);

        for (int i = 0; i < 5; i++)
        {

            if (i > snowAmount - 1) 
            { 
                slots[i].HideSnowball();
            }
            else slots[i].ShowSnowball();
        }

        if (snowAmount >= 5)
        {
            craftingSlot.Show();
        }
        else
        {
            craftingSlot.Hide();
        }
    }
}
