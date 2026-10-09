using UnityEngine;
using System.Collections;

public class HeadRandomMove : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float randomX = Random.Range(-30f, 30f);
        if (randomX < -29.8f)
        {
            StartCoroutine(HeadMove());
        }
    }

    IEnumerator HeadMove()
    {
        while (true)
        {
            float randomX = Random.Range(-30f, 30f);
            float randomY = transform.localEulerAngles.y;
            float randomZ = transform.localEulerAngles.z;
            Quaternion targetRotation = Quaternion.Euler(randomX, randomY, randomZ);
            float duration = Random.Range(0.1f, 0.2f);
            float elapsedTime = 0f;
            Quaternion initialRotation = transform.localRotation;
            while (elapsedTime < duration)
            {
                transform.localRotation = Quaternion.Slerp(initialRotation, targetRotation, elapsedTime / duration);
                elapsedTime += Time.deltaTime;
                yield return null;
            }
            transform.localRotation = targetRotation;
            yield return new WaitForSeconds(Random.Range(1f, 3f));
        }
    }
}
