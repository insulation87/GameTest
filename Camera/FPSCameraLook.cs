using UnityEngine;

public class FPSCameraLook : MonoBehaviour
{
    public Transform player;
    public Rigidbody playerRb;
    public float mouseSensitivity = 200f;

    [Header("FPS Camera Offset")]
    public Vector3 cameraOffset = new Vector3(0f, 1.6f, 0f);
    public float forwardOffset = 0.7f;

    private float xRotation = 0f;
    private float yaw = 0f;
    private Quaternion targetPlayerRotation;
    private Player playerController;

    void Awake()
    {
        if (playerRb == null && player != null)
            playerRb = player.GetComponentInParent<Rigidbody>();
        if (player != null)
            playerController = player.GetComponentInParent<Player>();

        // This script writes a world-space camera pose every frame, so keeping
        // the camera parented to the moving player would apply movement twice.
        if (transform.parent != null)
            transform.SetParent(null, true);
    }

    void Start()
    {
        if (player != null)
            yaw = player.eulerAngles.y;

        targetPlayerRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void OnEnable()
    {
        if (player != null)
            yaw = player.eulerAngles.y;

        targetPlayerRotation = Quaternion.Euler(0f, yaw, 0f);

        ApplyTransformInstant();
    }

    void FixedUpdate()
    {
        if (playerRb != null)
            playerRb.MoveRotation(targetPlayerRotation);
    }

    void LateUpdate()
    {
        if (player == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        yaw += mouseX;
        targetPlayerRotation = Quaternion.Euler(0f, yaw, 0f);

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -60f, 60f);

        // 用 Rigidbody 轉角色，不直接改 transform.Rotate
        if (playerRb == null)
        {
            player.rotation = targetPlayerRotation;
        }

        Vector3 stanceOffset = Vector3.up * GetStanceHeightOffset();
        transform.position = player.position + Quaternion.Euler(0f, yaw, 0f) *
            (cameraOffset + Vector3.forward * forwardOffset) + stanceOffset;
        transform.rotation = Quaternion.Euler(xRotation, yaw, 0f);
    }

    void ApplyTransformInstant()
    {
        if (player == null) return;

        Vector3 stanceOffset = Vector3.up * GetStanceHeightOffset();
        transform.position = player.position + Quaternion.Euler(0f, yaw, 0f) *
            (cameraOffset + Vector3.forward * forwardOffset) + stanceOffset;
        transform.rotation = Quaternion.Euler(xRotation, yaw, 0f);
    }

    float GetStanceHeightOffset()
    {
        if (playerController == null && player != null)
            playerController = player.GetComponentInParent<Player>();
        return playerController != null ? playerController.CameraHeightOffset : 0f;
    }

    public float GetPitch()
    {
        return xRotation;
    }

    public float GetYaw()
    {
        return yaw;
    }

    public void SetLook(float pitch, float newYaw)
    {
        xRotation = Mathf.Clamp(pitch, -60f, 60f);
        yaw = newYaw;
        targetPlayerRotation = Quaternion.Euler(0f, yaw, 0f);

        if (playerRb != null)
            playerRb.rotation = targetPlayerRotation;
        else if (player != null)
            player.rotation = targetPlayerRotation;

        ApplyTransformInstant();
    }
}
