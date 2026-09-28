using UnityEngine;

public class CameraSwitch : MonoBehaviour
{
    public Camera tpsCamera;
    public Camera fpsCamera;

    public CameraFollowTPS tpsController;
    public FPSCameraLook fpsController;

    [Header("Initial View")]
    [SerializeField] private bool startInFirstPerson = true;

    private bool isFPS;

    void Start()
    {
        SetMode(startInFirstPerson);
    }

    void Update()
    {
        if (Time.timeScale <= 0f)
            return;

        if (Input.GetKeyDown(KeyCode.C))
        {
            GameAudio.Local(GameSound.CameraSwitch);
            SetMode(!isFPS);
        }
    }

    void SetMode(bool toFPS)
    {
        if (tpsCamera == null || fpsCamera == null ||
            tpsController == null || fpsController == null)
        {
            Debug.LogError("CameraSwitch references are incomplete.", this);
            enabled = false;
            return;
        }

        float pitch;
        float yaw;

        if (isFPS)
        {
            pitch = fpsController.GetPitch();
            yaw = fpsController.GetYaw();
        }
        else
        {
            pitch = tpsController.GetPitch();
            yaw = tpsController.GetYaw();
        }

        isFPS = toFPS;

        tpsCamera.gameObject.SetActive(!isFPS);
        fpsCamera.gameObject.SetActive(isFPS);

        tpsController.enabled = !isFPS;
        fpsController.enabled = isFPS;

        if (isFPS)
        {
            fpsController.SetLook(pitch, yaw);
        }
        else
        {
            tpsController.SetLook(pitch, yaw);
            tpsController.SnapToTarget();
        }
    }
}
