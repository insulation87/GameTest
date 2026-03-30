using UnityEngine;

public class CameraSwitch : MonoBehaviour
{
    public Camera tpsCamera;
    public Camera fpsCamera;

    public MonoBehaviour tpsController;
    public MonoBehaviour fpsController;

    private bool isFPS = false;

    void Start()
    {
        if (tpsCamera == null || fpsCamera == null || tpsController == null || fpsController == null)
        {

            enabled = false;
            return;
        }
        tpsCamera.enabled = true;
        fpsCamera.enabled = false;

        tpsController.enabled = true;
        fpsController.enabled = false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            isFPS = !isFPS;

            tpsCamera.enabled = !isFPS;
            fpsCamera.enabled = isFPS;

            tpsController.enabled = !isFPS;
            fpsController.enabled = isFPS;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
