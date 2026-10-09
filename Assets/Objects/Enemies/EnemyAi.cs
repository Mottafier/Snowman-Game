using Unity.VisualScripting;
using UnityEditor.U2D;
using UnityEngine;

public class EnemyAi : MonoBehaviour
{

    public enum EnemyState { Idle, Walking, Pursuing }
    public EnemyState state = EnemyState.Idle;
    public Transform player;
    public Vector3 direction;
    public float sped;

    private Vector3 target;

    [SerializeField] private LayerMask enemyLayer;
    [SerializeField] private LayerMask wallLayer;
    [SerializeField] private EnemyAttack myAttack;

    void Awake()
    {
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        player = FindFirstObjectByType<PlayerSnow>().transform;
    }

    // Update is called once per frame
    void Update()
    {

        if(Random.value < 0.01f) // 1% chance to change state each frame
        {
            Vector3 randomOffset = new Vector3(Random.value * 2, 0, Random.value * 2);
            direction = player.position + randomOffset - transform.position;
            direction.y = 0;
            target = player.position + randomOffset;
            direction.Normalize();
            if (Physics.Raycast(transform.position, direction, out RaycastHit hit,Mathf.Infinity,wallLayer))
            {
                target = hit.point;
            }
        }



        switch (state)
        {
            case EnemyState.Idle: sped = 0f;  break;
            case EnemyState.Pursuing:
                sped = 0.6f;
                break;
        }

        transform.position += direction * Time.deltaTime * sped; // Move towards player at speed 2

        //CheckEnemyCollisions();

        if(Vector3.Distance(transform.position,target) < 0.6f)
        {
            if(state!= EnemyState.Idle)
            {
                Instantiate(myAttack, transform.position, Quaternion.identity);
                state = EnemyState.Idle;
            }
            state = EnemyState.Idle;
        }
        else
        {
            state = EnemyState.Pursuing;
        }
    }

    void CheckEnemyCollisions()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.5f, enemyLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            Vector3 pushDirection = (hits[i].transform.position - transform.position);
            pushDirection.y = 0f;
            pushDirection.Normalize();
            transform.position -= pushDirection * Time.deltaTime * 2f; // Push away from other enemies at speed 2

        }
    }
    void CheckStructureCollisions()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, 0.5f, enemyLayer);

        for (int i = 0; i < hits.Length; i++)
        {
            Vector3 pushDirection = (hits[i].transform.position - transform.position);
            pushDirection.y = 0f;
            pushDirection.Normalize();
            transform.position -= pushDirection * Time.deltaTime * 2f; // Push away from other enemies at speed 2

        }
    }

    
    
}
