using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CraftingSlot : MonoBehaviour
{
    [SerializeField] private Image icon;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }


    public void Show()
    {
        icon.enabled = true;
    }

    public void Hide()
    {
        icon.enabled = false;
    }
}
