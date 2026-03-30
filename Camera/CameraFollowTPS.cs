using UnityEngine;

public class CameraFollowTPS : MonoBehaviour
{
    public Transform target;
    public float mouseSensitivity = 200f;
    public Vector3 offset = new Vector3(0f, 2f, -4f);
    public float smoothTime = 0.15f;
    public float rotationSmoothSpeed = 10f;
    public float rotationX = 0f;
    private Vector3 currentVelocity;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void LateUpdate()
    {
        if (target == null) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        target.Rotate(Vector3.up * mouseX);

        rotationX -= mouseY;
        rotationX = Mathf.Clamp(rotationX, -30f, 60f);

        Vector3 wantedPos = target.position + target.rotation * offset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            wantedPos,
            ref currentVelocity,
            smoothTime
        );

        Quaternion targetRot = Quaternion.Euler(rotationX, target.eulerAngles.y, 0f);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            rotationSmoothSpeed * Time.deltaTime
        );
    }
    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }



}
