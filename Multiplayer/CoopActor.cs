using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class CoopActor : MonoBehaviour
{
    public int Id;
    public bool Local;
    public int LocalIndex;
    public string CharacterId = "breaker";
    public CoopInputFrame Frame { get; private set; } = new CoopInputFrame();
    public Rect Viewport => new Rect(0, 0, 1, 1);
    public bool Simulates => !CoopSession.IsClient;
    public bool HasRemoteInput { get; private set; }
    int previousHeld;
    float lastInput, nextCanvasScan;
    CoopInputFrame pending = new CoopInputFrame();
    Player player;
    CameraSwitch views;
    Vector3 networkPosition;
    Quaternion networkRotation;
    bool receivedPose;
    Camera lastPresentedCamera;

    void Awake() { player = GetComponent<Player>(); views = GetComponent<CameraSwitch>(); }

    void Start()
    {
        player = GetComponent<Player>(); views = GetComponent<CameraSwitch>();
        GetComponent<CharacterSkills>()?.RestoreProgress(new CharacterProgress { version = 1, id = CharacterId });
        GetComponent<PlayerHealth>().pauseWorldWhileDead = !CoopSession.IsOnline;
        if (!Local)
        {
            foreach (Camera camera in Cameras())
            {
                camera.enabled = false;
                if (camera.TryGetComponent(out AudioListener listener)) listener.enabled = false;
                if (camera.TryGetComponent(out PixelArtCamera pixels)) pixels.enabled = false;
            }
            if (views != null) { views.SetCoopView(true); views.enabled = false; }
            if (CoopSession.IsClient && views != null) { views.fpsController.enabled = false; views.tpsController.enabled = false; }
            GetComponent<PlayerCursorController>().enabled = false;
        }
        if (CoopSession.IsClient)
        {
            GetComponent<Rigidbody>().isKinematic = true;
            if (GetComponent<PickupMagnet>() != null) GetComponent<PickupMagnet>().enabled = false;
            if (GetComponent<FallDeathZone>() != null) GetComponent<FallDeathZone>().enabled = false;
        }
        ApplyPresentation();
    }
    public Camera[] Cameras()
    {
        var actorPlayer = player != null ? player : GetComponent<Player>();
        return new[] { actorPlayer.fpsCamera, actorPlayer.tpsCamera };
    }
    public void ReceiveInput(CoopInputFrame input)
    {
        HasRemoteInput = true;
        pending.move = Vector2.ClampMagnitude(input.move, 1);
        pending.held = input.held;
        pending.pressed |= input.pressed;
        pending.released |= input.released;
        pending.yaw = input.yaw; pending.pitch = input.pitch;
        pending.firstPerson = input.firstPerson;
        lastInput = Time.unscaledTime;
    }
    void Update()
    {
        if (Local)
        {
            Frame = CoopInput.Read(0, previousHeld);
            previousHeld = Frame.held;
        }
        else
        {
            Frame = new CoopInputFrame { move = pending.move, held = pending.held,
                pressed = pending.pressed, released = pending.released, yaw = pending.yaw, pitch = pending.pitch, firstPerson = pending.firstPerson };
            pending.ClearEdges();
            if (Time.unscaledTime - lastInput > .5f) Frame = new CoopInputFrame();
            if (player != null && !CoopSession.IsClient)
            {
                views?.SetCoopView(Frame.firstPerson);
                if (Frame.firstPerson) player.fpsCamera.GetComponent<FPSCameraLook>()?.SetLook(Frame.pitch, Frame.yaw);
                else player.tpsCamera.GetComponent<CameraFollowTPS>()?.SetLook(Frame.pitch, Frame.yaw);
            }
        }
        if (GameSceneFlow.IsLoading || DungeonVictoryUI.IsOpen) Frame = new CoopInputFrame();
        var inventory = GetComponent<WeaponInventory>();
        var health = GetComponent<PlayerHealth>();
        if (inventory != null && inventory.IsPaused || health != null && health.IsChoosingUpgrade)
        {
            Frame.move = Vector2.zero; Frame.look = Vector2.zero; Frame.held &= ~(int)CoopButton.Fire;
            Frame.pressed &= (int)(CoopButton.Inventory | CoopButton.Pause | CoopButton.View);
        }
        else if (inventory != null && inventory.IsOpen)
        {
            // Keep walking, running and jumping while the mouse operates the bag.
            Frame.look = Vector2.zero;
            Frame.held &= ~(int)(CoopButton.Fire | CoopButton.Skill | CoopButton.Echo);
            Frame.pressed &= (int)(CoopButton.Inventory | CoopButton.Pause | CoopButton.View | CoopButton.Jump | CoopButton.Run | CoopButton.Crouch);
        }
        if (Time.unscaledTime >= nextCanvasScan)
        { nextCanvasScan = Time.unscaledTime + .25f; ApplyPresentation(); }
    }
    void LateUpdate()
    {
        if (Local && views != null && views.ActiveCamera != lastPresentedCamera)
        { ApplyPresentation(); lastPresentedCamera = views.ActiveCamera; }
        if (CoopSession.IsClient && receivedPose)
        {
            float blend = Vector3.SqrMagnitude(transform.position - networkPosition) > 64 ? 1 :
                1 - Mathf.Exp(-20 * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, networkPosition, blend);
            if (!Local) transform.rotation = Quaternion.Slerp(transform.rotation, networkRotation, blend);
        }
    }
    public void ReceivePose(Vector3 position, Quaternion rotation)
    {
        networkPosition = position; networkRotation = rotation;
        if (!receivedPose) transform.SetPositionAndRotation(position, rotation);
        receivedPose = true;
    }
    void ApplyPresentation()
    {
        if (player == null) return;
        foreach (Camera camera in Cameras())
        {
            if (camera == null) continue;
            if (camera.TryGetComponent(out AudioListener listener)) listener.enabled = Local;
            if (!Local) { camera.enabled = false; continue; }
            if (camera.TryGetComponent(out PixelArtCamera pixels)) pixels.outputRect = Viewport;
            else camera.rect = Viewport;
            camera.cullingMask &= ~((1 << 24) | (1 << 25) | (1 << 28) | (1 << 29) | (1 << 30));
            camera.cullingMask |= (1 << 24) | (1 << 28);
            if (camera == player.fpsCamera) camera.cullingMask &= ~(1 << 26);
            else camera.cullingMask |= 1 << 26;
        }
        foreach (Canvas canvas in GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.GetComponentInParent<PixelArtCamera>() != null) continue;
            if (!Local) { canvas.enabled = false; continue; }
            Camera camera = views != null ? views.ActiveCamera : player.fpsCamera;
            if (camera == null) continue;
            // Keep HUD out of the world depth buffer, including the pixel world
            // render texture. A viewport wrapper preserves each player's layout.
            if (canvas.isRootCanvas)
            {
                var viewport = canvas.GetComponent<GameUIViewport>();
                if (viewport == null) viewport = canvas.gameObject.AddComponent<GameUIViewport>();
                viewport.SetViewport(Viewport, camera.targetDisplay);
            }
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 24;
        }
        // Body stays visible to teammates; each first-person view hides only its own body.
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            if (renderer.GetComponentInParent<Canvas>() == null && !IsCameraChild(renderer.transform))
                renderer.gameObject.layer = Local ? 26 : 0;
        if (Local && player.fpsCamera != null)
            foreach (Renderer renderer in player.fpsCamera.GetComponentsInChildren<Renderer>(true))
                if (renderer.GetComponentInParent<Canvas>() == null) renderer.gameObject.layer = 28;
        if (!Local && player.fpsCamera != null)
            foreach (Renderer renderer in player.fpsCamera.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer = 30;
    }
    bool IsCameraChild(Transform item) => player.fpsCamera != null && item.IsChildOf(player.fpsCamera.transform) ||
        player.tpsCamera != null && item.IsChildOf(player.tpsCamera.transform);
    void OnDestroy()
    {
        // Follow-camera rigs detach in Awake and are still owned by this actor.
        if (player == null) return;
        foreach (Camera camera in Cameras())
            if (camera != null && !camera.transform.IsChildOf(transform)) Destroy(camera.transform.root.gameObject);
    }
}
