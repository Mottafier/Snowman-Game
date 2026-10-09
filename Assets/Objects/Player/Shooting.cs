using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[RequireComponent(typeof(CharacterController))]
public class SnowShooter : MonoBehaviour
{
    [Header("Refs")]
    public GameObject snowballPrefab;  // assign Snowball.prefab
    public Transform muzzle;           // assign your Muzzle transform
    public Transform aimFrom;          // optional: your Camera or HeadPivot

    [Header("Tuning")]
    public float muzzleSpeed = 20f;

    float nextFireTime;
    CharacterController cc;

    private bool isShooting = false;
    private PlayerSnow playerSnow;  

    [SerializeField] private List<GameObject> enemyPrefabs;

    private SoundEffects sounds;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        playerSnow = GetComponent<PlayerSnow>();
        sounds = GetComponent<SoundEffects>();
    }
    


    // PlayerInput (Behavior = Invoke Unity Events) will call this
    public void OnAttack(InputValue v)
    {
        if (!v.isPressed) return;

        isShooting = true;
    }

    public void OnInteract(InputValue v)
    {
        foreach(var c in enemyPrefabs)
        {
            Instantiate(c, new Vector3(0,2f,0), Quaternion.identity);
        }
    }

    private void FixedUpdate()
    {
        if(!isShooting) return;
        isShooting = false;
        if (playerSnow.SnowAmount < 1f) return;

        sounds.PlayShoot();

        // Choose a forward direction (camera-forward is nicest for aim)
        Vector3 forward = (aimFrom ? aimFrom.forward : muzzle.forward);

        // Spawn and shoot
        var go = Instantiate(snowballPrefab, muzzle.position, Quaternion.LookRotation(forward));
        var rb = go.GetComponent<Rigidbody>();
        rb.linearVelocity = forward * muzzleSpeed;

        Debug.Log($"MUZZLE world pos: {muzzle.position}  local: {muzzle.localPosition}  parent: {muzzle.parent?.name}");

        // Ignore hitting the player
        var ballCol = go.GetComponent<Collider>();
        if (ballCol)
        {
            // Ignore CharacterController capsule
            if (cc) Physics.IgnoreCollision(ballCol, cc);
            // Ignore any other colliders on the player
            foreach (var c in GetComponentsInChildren<Collider>())
                if (c && c != ballCol) Physics.IgnoreCollision(ballCol, c);
        }

        playerSnow.SpendSnow(1);
    }
}