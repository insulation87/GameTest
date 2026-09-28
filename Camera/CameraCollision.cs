using UnityEngine;

[DefaultExecutionOrder(100)]
public class CameraCollision : MonoBehaviour
{
    public Transform target;
    public Vector3 targetOffset = new Vector3(0f, 1.2f, 0f);
    public float distance = 5f;
    public float minDistance = 1.0f;
    public float smooth = 10f;
    public float wallPadding = 0.15f;
    public LayerMask collisionLayer;
    private float currentDistance;
    private float distanceVelocity;
    private Player playerController;

    void OnEnable()
    {
        if (target != null)
            playerController = target.GetComponentInParent<Player>();

        Vector3 desiredPosition = GetDesiredPosition();
        currentDistance = target == null
            ? distance
            : Vector3.Distance(GetTargetPosition(), desiredPosition);
        distanceVelocity = 0f;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 rayOrigin = GetTargetPosition();
        Vector3 desiredPosition = GetDesiredPosition();
        Vector3 desiredVector = desiredPosition - rayOrigin;
        float desiredDistance = desiredVector.magnitude;

        if (desiredDistance <= Mathf.Epsilon)
            return;

        Vector3 desiredDir = desiredVector / desiredDistance;

        float targetDistance = desiredDistance;
        if (Physics.Raycast(
            rayOrigin,
            desiredDir,
            out RaycastHit hit,
            desiredDistance,
            collisionLayer,
            QueryTriggerInteraction.Ignore))
        {
            targetDistance = Mathf.Clamp(
                hit.distance - wallPadding,
                minDistance,
                desiredDistance
            );
        }

        float smoothTime = 1f / Mathf.Max(0.01f, smooth);
        currentDistance = Mathf.SmoothDamp(
            currentDistance,
            targetDistance,
            ref distanceVelocity,
            smoothTime
        );

        transform.position = rayOrigin + desiredDir * currentDistance;
    }

    Vector3 GetDesiredPosition()
    {
        return transform.parent != null ? transform.parent.position : transform.position;
    }

    Vector3 GetTargetPosition()
    {
        if (playerController == null && target != null)
            playerController = target.GetComponentInParent<Player>();

        float stanceOffset = playerController != null
            ? playerController.CameraHeightOffset
            : 0f;
        return target.position + targetOffset + Vector3.up * stanceOffset;
    }

    void OnValidate()
    {
        distance = Mathf.Max(0.1f, distance);
        minDistance = Mathf.Max(0.01f, minDistance);
        smooth = Mathf.Max(0.01f, smooth);
        wallPadding = Mathf.Max(0f, wallPadding);
    }
}
