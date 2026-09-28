using UnityEngine;

public class CameraFollowTPS : MonoBehaviour
{
    public Transform target;

    [Header("Mouse")]
    public float mouseSensitivity = 200f;

    [Header("Camera Offset")]
    public float distance = 7f;     // 相機離角色多遠
    public float height = 6f;       // 相機高度
    public float pitch = 25f;       // 往下看的角度

    [Header("Smooth")]
    public float smoothTime = 0.12f;
    public float rotationSmoothSpeed = 12f;

    private Vector3 currentVelocity;
    private float yaw;
    private Player playerController;

    void Awake()
    {
        // A world-space follow rig must not also inherit the player's movement.
        // The current scene has CameraRig parented under the player, which
        // applies translation once through the hierarchy and again below.
        if (transform.parent != null)
            transform.SetParent(null, true);

        if (target != null)
            playerController = target.GetComponentInParent<Player>();
    }

    void Start()
    {
        if (target != null)
            yaw = target.eulerAngles.y;
    }

    void OnEnable()
    {
        if (target != null)
            yaw = target.eulerAngles.y;

        SnapToTarget();
    }

    void LateUpdate()
    {
        if (target == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        yaw += mouseX;

        Quaternion camRotation = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 targetCenter = target.position + Vector3.up * (1.2f + GetStanceHeightOffset());
        Vector3 wantedPos = targetCenter + camRotation * new Vector3(0f, 0f, -distance);
        wantedPos.y = target.position.y + height;

        // Existing scenes may have serialized the old value as zero.
        float effectiveSmoothTime = smoothTime > 0f ? smoothTime : 0.12f;
        transform.position = Vector3.SmoothDamp(
            transform.position,
            wantedPos,
            ref currentVelocity,
            effectiveSmoothTime
        );

        Quaternion lookRot = Quaternion.LookRotation(targetCenter - transform.position);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            lookRot,
            rotationSmoothSpeed * Time.deltaTime
        );
    }

    public float GetPitch()
    {
        return pitch;
    }

    public float GetYaw()
    {
        return yaw;
    }

    public void SetLook(float newPitch, float newYaw)
    {
        pitch = Mathf.Clamp(newPitch, 10f, 60f);
        yaw = newYaw;
        SnapToTarget();
    }

    void OnValidate()
    {
        distance = Mathf.Max(0.1f, distance);
        height = Mathf.Max(0f, height);
        smoothTime = Mathf.Max(0f, smoothTime);
        rotationSmoothSpeed = Mathf.Max(0f, rotationSmoothSpeed);
    }

    public void SnapToTarget()
    {
        if (target == null) return;

        Vector3 targetCenter = target.position + Vector3.up * (1.2f + GetStanceHeightOffset());
        Quaternion camRotation = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 wantedPos = targetCenter + camRotation * new Vector3(0f, 0f, -distance);
        wantedPos.y = target.position.y + height;

        transform.position = wantedPos;
        transform.rotation = Quaternion.LookRotation(targetCenter - transform.position);
        currentVelocity = Vector3.zero;
    }

    float GetStanceHeightOffset()
    {
        if (playerController == null && target != null)
            playerController = target.GetComponentInParent<Player>();
        return playerController != null ? playerController.CameraHeightOffset : 0f;
    }
}
