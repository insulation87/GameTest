using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Uses the shipped player prefab's camera hierarchy beside Map1's extra stationary camera.</summary>
[InitializeOnLoad]
public static class PixelArtGameplayCameraReview
{
    const string Key = "PixelArtGameplayCameraReview.Pending";
    static CameraSwitch views;
    static GameObject actor;
    static Camera stationary;
    static Vector3 beforeMove;
    static int stage, minimumFrame, assertions;
    static double deadline;
    static float gameDeadline;

    static PixelArtGameplayCameraReview() { EditorApplication.playModeStateChanged += StateChanged; }

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        stage = assertions = 0; minimumFrame = Time.frameCount;
        deadline = EditorApplication.timeSinceStartup; gameDeadline = Time.unscaledTime;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (Time.frameCount < minimumFrame || EditorApplication.timeSinceStartup < deadline || Time.unscaledTime < gameDeadline) return;
        try
        {
            if (stage == 0)
            {
                stationary = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                stationary.tag = "MainCamera";
                stationary.depth = -1;
                stationary.transform.position = new Vector3(0, 1, -10);
                var parent = new GameObject("Isolated Player Fixture"); parent.SetActive(false);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Player/Player.prefab");
                Check(prefab != null, "Actual player prefab available");
                actor = UnityEngine.Object.Instantiate(prefab, parent.transform);
                // Isolate camera behavior without starting inventory, account saves or combat.
                // Serialized camera/controller/target references are retained exactly as shipped.
                MonoBehaviour[] behaviors = actor.GetComponentsInChildren<MonoBehaviour>(true);
                // Remove dependent UI components before their health components.
                Array.Reverse(behaviors);
                foreach (MonoBehaviour behavior in behaviors)
                    if (!(behavior is CameraSwitch) && !(behavior is FPSCameraLook) &&
                        !(behavior is CameraFollowTPS) && !(behavior is CameraCollision) &&
                        behavior.GetType().Namespace != "UnityEngine.Rendering.Universal")
                        UnityEngine.Object.DestroyImmediate(behavior);
                Rigidbody body = actor.GetComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
                views = actor.GetComponent<CameraSwitch>();
                parent.SetActive(true);
                Wait(1.6f);
            }
            else if (stage == 1)
            {
                Check(views.ActiveCamera == views.fpsCamera && views.fpsCamera.isActiveAndEnabled, "Enter game in first person");
                Check(stationary.GetComponent<PixelArtCamera>() == null && stationary.targetTexture == null,
                    "Stationary scene camera never acquires a fullscreen pixel output");
                PresentedView(views.fpsCamera);
                views.fpsController.SetLook(20, 90);
                beforeMove = views.fpsCamera.transform.position;
                actor.transform.position += new Vector3(3, 0, 4);
                Wait(.25f);
            }
            else if (stage == 2)
            {
                Check(Vector3.Distance(views.fpsCamera.transform.position - beforeMove, new Vector3(3, 0, 4)) < .02f,
                    "First-person camera follows translated player");
                Check(Quaternion.Angle(views.fpsCamera.transform.rotation, Quaternion.Euler(20, 90, 0)) < .1f,
                    "First-person view follows look rotation");
                PresentedView(views.fpsCamera);
                SetMode(false);
                Wait(.25f);
            }
            else if (stage == 3)
            {
                Check(views.ActiveCamera == views.tpsCamera && !views.fpsCamera.gameObject.activeSelf,
                    "Switch selects third-person camera");
                Check(Mathf.Abs(Mathf.DeltaAngle(views.tpsController.GetYaw(), 90)) < .1f,
                    "Switch retains look yaw");
                PresentedView(views.tpsCamera);
                // Let the shipped follow and collision smoothing settle before measuring movement.
                Wait(1f);
            }
            else if (stage == 4)
            {
                beforeMove = views.tpsCamera.transform.position;
                actor.transform.position += new Vector3(-2, 0, 1);
                Wait(1f);
            }
            else if (stage == 5)
            {
                Check(Vector3.Distance(views.tpsCamera.transform.position - beforeMove, new Vector3(-2, 0, 1)) < .03f,
                    "Third-person rig follows translated player; actual delta " +
                    (views.tpsCamera.transform.position - beforeMove).ToString("F4") +
                    ", rig " + views.tpsController.transform.position.ToString("F4") +
                    ", target " + views.tpsController.target.position.ToString("F4"));
                views.tpsController.SetLook(30, 160);
                Time.timeScale = 0;
                PresentedView(views.tpsCamera);
                Wait(.85f);
            }
            else if (stage == 6)
            {
                PresentedView(views.tpsCamera);
                Check(stationary.transform.position == new Vector3(0, 1, -10), "Unrelated camera stays stationary");
                Time.timeScale = 1;
                SetMode(true);
                Wait(.25f);
            }
            else
            {
                PresentedView(views.fpsCamera);
                Check(Mathf.Abs(Mathf.DeltaAngle(views.fpsController.GetYaw(), 160)) < .1f,
                    "Switch back retains third-person yaw");
                Check(views.tpsCamera.targetTexture == null && views.tpsCamera.transform.Find("Pixel World Display") == null,
                    "Inactive view releases its presentation");
                Debug.Log("PIXEL_GAMEPLAY_CAMERA_PASS: " + assertions + " prefab integration checks");
                Finish(0); return;
            }
            stage++;
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    static void PresentedView(Camera expected)
    {
        Check(PixelArtDirector.ActiveGameplayCamera == expected, "Director selects the current gameplay view");
        Transform display = expected.transform.Find("Pixel World Display");
        Check(display != null && display.gameObject.activeInHierarchy, "Current view is presented");
        Check(display.Find("World Pixels").GetComponent<RawImage>().texture == expected.targetTexture && expected.targetTexture != null,
            "Displayed pixels come from the moving gameplay camera");
        int presentations = 0;
        foreach (PixelArtCamera effect in UnityEngine.Object.FindObjectsByType<PixelArtCamera>(FindObjectsSortMode.None))
        {
            Transform output = effect.transform.Find("Pixel World Display");
            if (output != null && output.gameObject.activeInHierarchy) presentations++;
        }
        Check(presentations == 1, "Exactly one fullscreen world image despite the extra scene camera");
        Camera screen = expected.transform.Find("Pixel Screen Output").GetComponent<Camera>();
        Check(screen.isActiveAndEnabled && screen.targetTexture == null, "Screen output stays enabled for selected view");
    }

    static void SetMode(bool firstPerson)
    {
        typeof(CameraSwitch).GetMethod("SetMode", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(views, new object[] { firstPerson });
    }

    static void Wait(float seconds)
    {
        minimumFrame = Time.frameCount + 4;
        deadline = EditorApplication.timeSinceStartup + seconds;
        gameDeadline = Time.unscaledTime + seconds;
    }

    static void Check(bool success, string message)
    {
        assertions++;
        if (!success) throw new InvalidOperationException("Gameplay camera regression: " + message);
    }

    static void Finish(int exit)
    {
        Time.timeScale = 1;
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
        if (Application.isBatchMode) EditorApplication.Exit(exit); else EditorApplication.ExitPlaymode();
    }
}
