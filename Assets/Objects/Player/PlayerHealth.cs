using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public int health = 100;

    [SerializeField] private TMP_Text healthText;
    [SerializeField] private GameoverManager gameOver;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }
    public void OnRestart(InputValue value)
    {
        if (value.isPressed)
        {
            gameOver.RestartGame();
        }
    }
    // Update is called once per frame
    void Update()
    {
        healthText.text= "HP: " + health.ToString();

    }
    

    public void TakeDamage(int dmg)
    {
        health -= dmg;
        DamageFlash.Play(Mathf.Lerp(0.5f, 1f, dmg / 25f));

        if (health <= 0)
        {
            health = 0;
            Die();
        }
    }

    public void Die()
    {
        // Disable playercontrols and show game over screen

        //this.gameObject.SetActive(false);
        gameOver.ShowGameOver();
    }
}
