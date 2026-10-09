using UnityEngine;

public class PenguinWaddleAI : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 0.5f;        // how fast the penguin walks
    public float turnSpeed = 300f;        // how quickly it can turn
    public float moveDuration = 2f;       // how long it moves before stopping
    public float idleDuration = 1.5f;     // how long it pauses

    Animator animator;
    float stateTimer;
    bool isWalking;

    private EnemyAi enemyAi;


    Vector3 moveDirection;
    float sped;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        enemyAi = GetComponent<EnemyAi>();
    }

    void Start()
    {
        PickNewState();
    }

    void Update()
    {
        stateTimer -= Time.deltaTime;
        moveDirection = enemyAi.direction; // Get the direction from EnemyAi
        sped = enemyAi.sped; // Get the speed from EnemyAi


        // Smoothly rotate toward moveDirection
        if (moveDirection != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = targetRot;
        }

        

        if (stateTimer <= 0f)
        {
            //PickNewState();
        }

        // keep Animator parameter updated
        animator.SetBool("IsWalking", sped > 0f);
    }

    void PickNewState()
    {
        // 50% chance to walk, 50% to idle
        isWalking = Random.value > 0.5f;

        

        if (isWalking)
        {
            // Pick a random 2D direction
            Vector2 circle = Random.insideUnitCircle.normalized;
            moveDirection = new Vector3(circle.x, 0, circle.y);

            stateTimer = moveDuration;
        }
        else
        {
            moveDirection = Vector3.zero;
            stateTimer = idleDuration;
        }
    }
}