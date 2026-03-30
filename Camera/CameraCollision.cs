using System.Diagnostics;
using UnityEngine;

public class CameraCollision : MonoBehaviour
{
    public Transform target;
    public float distance = 5f;
    public float minDistance = 1.0f;
    public float smooth = 10f;
    public LayerMask collisionLayer;
    private float currentDistance;

    void Start()
    {
        currentDistance = distance;
    }

    void LateUpdate()
    {
        Vector3 desiredDir = (transform.position - target.position).normalized;
        Vector3 desiredPos = target.position + desiredDir * distance;

        RaycastHit hit;
       
        if (Physics.Raycast(target.position, desiredDir, out hit, distance, collisionLayer))
        {
            currentDistance = Mathf.Clamp(hit.distance, minDistance, distance);
        }
        else
        {
            currentDistance = distance;
        }

        transform.position = Vector3.Lerp(transform.position, target.position + desiredDir * currentDistance, smooth * Time.deltaTime);
    }
}
