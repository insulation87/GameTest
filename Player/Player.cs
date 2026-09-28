using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class Player : MonoBehaviour
{
    [Header("Move")]
    public float walkSpeed = 5f;
    public float runSpeed = 9f;
    public float rotationSpeed = 12f;
    public float inputDeadZone = 0.1f;

    [Header("Crouch and Slide")]
    [Min(0.1f)] public float crouchSpeed = 3f;
    [Range(0.4f, 1.8f)] public float crouchedColliderHeight = 1.45f;
    [Range(0.4f, 1.8f)] public float slidingColliderHeight = 1.1f;
    [Min(0.1f)] public float slideSpeed = 12f;
    [Min(0.1f)] public float slideEndSpeed = 4f;
    [Min(0.1f)] public float slideDuration = 0.75f;
    [Min(0f)] public float slideCooldown = 0.25f;
    [Min(0f)] public float crouchedCameraDrop = 0.55f;

    [Header("Jump")]
    public float jumpForce = 5f;
    public LayerMask groundLayer = ~0;
    public float groundCheckDistance = 1f;

    [Header("Camera")]
    public Camera tpsCamera;
    public Camera fpsCamera;

    [Header("Debug")]
    public bool showDebugRay = false;

    private Rigidbody rb;
    private BoxCollider bodyCollider;
    private float currentSpeed;
    private Vector3 standingColliderSize;
    private Vector3 standingColliderCenter;

    private Vector3 moveDirection;
    private bool isGrounded;
    private bool isRunning;
    private bool isCrouching;
    private bool isSliding;
    private bool jumpRequested;
    private int airJumpsRemaining;
    private Vector3 slideDirection;
    private float slideTimeRemaining;
    private float nextSlideTime;
    private float cameraHeightOffset;

    public float RunSpeed => runSpeed;
    public bool IsGrounded => isGrounded;
    public bool IsRunning => isRunning;
    public bool IsCrouching => isCrouching;
    public bool IsSliding => isSliding;
    public float MovementAmount => moveDirection.magnitude;
    public float VerticalSpeed => rb != null ? rb.linearVelocity.y : 0f;
    public float CameraHeightOffset => cameraHeightOffset;

    void Awake()
    {
        if (GetComponent<CharacterMovementAudio>() == null)
            gameObject.AddComponent<CharacterMovementAudio>();
        if (GetComponent<PlayerCursorController>() == null)
            gameObject.AddComponent<PlayerCursorController>();
        if (GetComponent<BossTestShortcut>() == null)
            gameObject.AddComponent<BossTestShortcut>();
    }

    void Start()
    {
        // A newly loaded gameplay scene must never inherit a paused menu state.
        Time.timeScale = 1f;

        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<BoxCollider>();
        currentSpeed = walkSpeed;

        if (bodyCollider != null)
        {
            standingColliderSize = bodyCollider.size;
            standingColliderCenter = bodyCollider.center;
            crouchedColliderHeight = Mathf.Min(crouchedColliderHeight, standingColliderSize.y);
            slidingColliderHeight = Mathf.Min(slidingColliderHeight, crouchedColliderHeight);
        }

        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        if (GetComponent<AnimeCharacterVisual>() == null)
            gameObject.AddComponent<AnimeCharacterVisual>();
    }

    void Update()
    {
        if (Time.timeScale <= 0f)
        {
            ClearMovementInput();
            return;
        }

        ReadInput();
        HandleRun();
        HandleStanceInput();
        UpdateCameraHeight();

        if (!isSliding && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            jumpRequested = true;

    }

    void FixedUpdate()
    {
        if (Time.timeScale <= 0f)
            return;

        CheckGround();
        HandleJump();
        HandleMove();
        HandleRotation();
    }

    void ClearMovementInput()
    {
        moveDirection = Vector3.zero;
        jumpRequested = false;
        currentSpeed = 0f;
        isRunning = false;
    }

    void ReadInput()
    {
        float horizontal = 0f;
        float vertical = 0f;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                horizontal -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                horizontal += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                vertical -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                vertical += 1f;
        }

        Vector2 input = new Vector2(horizontal, vertical);

        if (input.magnitude < inputDeadZone)
        {
            moveDirection = Vector3.zero;
            return;
        }

        Camera activeCam = GetActiveCamera();
        if (activeCam == null)
        {
            moveDirection = Vector3.zero;
            return;
        }

        Vector3 camForward = activeCam.transform.forward;
        Vector3 camRight = activeCam.transform.right;

        camForward.y = 0f;
        camRight.y = 0f;

        camForward.Normalize();
        camRight.Normalize();

        moveDirection = camForward * vertical + camRight * horizontal;
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);
    }

    void HandleMove()
    {
        if (isSliding)
        {
            slideTimeRemaining -= Time.fixedDeltaTime;
            float slideProgress = 1f - Mathf.Clamp01(slideTimeRemaining / Mathf.Max(0.01f, slideDuration));
            float currentSlideSpeed = Mathf.Lerp(slideSpeed, slideEndSpeed, slideProgress);
            rb.MovePosition(rb.position + slideDirection * currentSlideSpeed * Time.fixedDeltaTime);

            if (slideTimeRemaining <= 0f || !isGrounded)
                FinishSlide();
            return;
        }

        if (moveDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 targetPosition = rb.position + moveDirection * currentSpeed * Time.fixedDeltaTime;
        rb.MovePosition(targetPosition);
    }

    void HandleRotation()
    {
        if (isSliding || moveDirection.sqrMagnitude < 0.0001f)
            return;

        // FPS 模式：不要在這裡轉，交給 FPSCameraLook
        if (IsFPSMode())
            return;

        Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        Quaternion smoothRotation = Quaternion.Slerp(
            rb.rotation,
            targetRotation,
            rotationSpeed * Time.fixedDeltaTime
        );

        rb.MoveRotation(smoothRotation);
    }

    void HandleRun()
    {
        PlayerHealth health = GetComponent<PlayerHealth>();
        float speedMultiplier = health != null ? health.MoveSpeedMultiplier : 1f;
        WeaponBuildSystem build = GetComponent<WeaponBuildSystem>();
        if (build != null)
            speedMultiplier *= build.MoveSpeedMultiplier;
        bool runKeyHeld = Keyboard.current != null &&
                          (Keyboard.current.leftShiftKey.isPressed ||
                           Keyboard.current.rightShiftKey.isPressed);
        isRunning = runKeyHeld && !isCrouching && !isSliding &&
                    moveDirection.sqrMagnitude > 0.0001f;
        float baseSpeed = isCrouching ? crouchSpeed : isRunning ? runSpeed : walkSpeed;
        currentSpeed = baseSpeed * speedMultiplier;
    }

    void HandleStanceInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        bool crouchPressed = keyboard.leftCtrlKey.wasPressedThisFrame;
        bool crouchHeld = keyboard.leftCtrlKey.isPressed;

        if (crouchPressed && isRunning && isGrounded &&
            moveDirection.sqrMagnitude > 0.0001f && Time.time >= nextSlideTime)
        {
            BeginSlide();
            return;
        }

        if (isSliding)
            return;

        if (crouchHeld)
            SetCrouching(true);
        else if (isCrouching)
            TryStandUp();
    }

    void BeginSlide()
    {
        GameAudio.Local(GameSound.Slide);
        isSliding = true;
        isCrouching = false;
        slideTimeRemaining = slideDuration;
        nextSlideTime = Time.time + slideDuration + slideCooldown;
        slideDirection = moveDirection.sqrMagnitude > 0.0001f
            ? moveDirection.normalized
            : transform.forward;
        ApplyCrouchedCollider();
    }

    void FinishSlide()
    {
        isSliding = false;

        bool keepCrouching = Keyboard.current != null &&
                             Keyboard.current.leftCtrlKey.isPressed;
        if (keepCrouching || !CanStandUp())
            SetCrouching(true);
        else
            SetCrouching(false);
    }

    void SetCrouching(bool crouching)
    {
        if (!crouching && !CanStandUp())
            return;

        if (isCrouching != crouching) GameAudio.Local(GameSound.Stance);
        isCrouching = crouching;
        if (crouching)
            ApplyCrouchedCollider();
        else
            RestoreStandingCollider();
    }

    void TryStandUp()
    {
        if (CanStandUp())
            SetCrouching(false);
    }

    void ApplyCrouchedCollider()
    {
        if (bodyCollider == null)
            return;

        float stanceHeight = isSliding ? slidingColliderHeight : crouchedColliderHeight;
        float heightDifference = standingColliderSize.y - stanceHeight;
        Vector3 crouchedSize = standingColliderSize;
        crouchedSize.y = stanceHeight;
        bodyCollider.size = crouchedSize;
        bodyCollider.center = standingColliderCenter - Vector3.up * (heightDifference * 0.5f);
    }

    void RestoreStandingCollider()
    {
        if (bodyCollider == null)
            return;

        bodyCollider.size = standingColliderSize;
        bodyCollider.center = standingColliderCenter;
    }

    bool CanStandUp()
    {
        if (bodyCollider == null || standingColliderSize == Vector3.zero)
            return true;

        Vector3 worldCenter = transform.TransformPoint(standingColliderCenter);
        Vector3 scaledSize = Vector3.Scale(standingColliderSize, transform.lossyScale);
        Vector3 halfExtents = scaledSize * 0.5f - Vector3.one * 0.025f;
        halfExtents.x = Mathf.Max(0.01f, halfExtents.x);
        halfExtents.y = Mathf.Max(0.01f, halfExtents.y);
        halfExtents.z = Mathf.Max(0.01f, halfExtents.z);

        Collider[] overlaps = Physics.OverlapBox(
            worldCenter,
            halfExtents,
            transform.rotation,
            groundLayer.value == 0 ? Physics.AllLayers : groundLayer.value,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider overlap in overlaps)
        {
            if (overlap != null && overlap != bodyCollider &&
                !overlap.transform.IsChildOf(transform))
                return false;
        }

        return true;
    }

    void UpdateCameraHeight()
    {
        float targetOffset = isCrouching || isSliding ? -crouchedCameraDrop : 0f;
        cameraHeightOffset = Mathf.MoveTowards(
            cameraHeightOffset,
            targetOffset,
            Time.deltaTime * 4.5f
        );
    }

    void HandleJump()
    {
        if (!jumpRequested)
            return;

        jumpRequested = false;

        if (isCrouching)
        {
            if (!CanStandUp())
                return;
            SetCrouching(false);
        }

        if (isGrounded)
        {
            GameAudio.Local(GameSound.Jump);
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }
        else if (airJumpsRemaining > 0)
        {
            GameAudio.Local(GameSound.Jump);
            Vector3 velocity = rb.linearVelocity;
            velocity.y = 0f;
            rb.linearVelocity = velocity;
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            airJumpsRemaining--;
        }
    }

    void CheckGround()
    {
        bool wasGrounded = isGrounded;
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        float rayDistance = groundCheckDistance + 0.1f;

        int layerMask = groundLayer.value == 0 ? Physics.DefaultRaycastLayers : groundLayer.value;
        isGrounded = Physics.Raycast(
            origin,
            Vector3.down,
            rayDistance,
            layerMask,
            QueryTriggerInteraction.Ignore);

        if (!wasGrounded && isGrounded && rb.linearVelocity.y < -1.5f)
            GameAudio.Local(GameSound.Land, Mathf.Clamp01(-rb.linearVelocity.y / 8f));
        if (isGrounded && rb.linearVelocity.y <= 0.1f)
        {
            PlayerHealth health = GetComponent<PlayerHealth>();
            airJumpsRemaining = health != null && health.HasDoubleJump ? 1 : 0;
        }

        if (showDebugRay)
        {
            Debug.DrawRay(origin, Vector3.down * rayDistance, isGrounded ? Color.green : Color.red);
        }
    }

    public bool TryGetTeleportPosition(Vector3 groundPoint, Quaternion rotation, out Vector3 position)
    {
        Vector3 size = Vector3.Scale(standingColliderSize, transform.lossyScale);
        Vector3 center = Vector3.Scale(standingColliderCenter, transform.lossyScale);
        position = groundPoint + Vector3.up * (size.y * 0.5f - center.y + 0.08f);
        if (DungeonHazardDirector.IsUnsafe(groundPoint, 1f)) return false;
        if (bodyCollider == null || size.sqrMagnitude < 0.01f)
            return false;

        Vector3 halfExtents = size * 0.5f - Vector3.one * 0.02f;
        foreach (Collider overlap in Physics.OverlapBox(position + rotation * center,
            halfExtents, rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            if (overlap != bodyCollider && !overlap.transform.IsChildOf(transform))
                return false;
        }
        return true;
    }

    public void Teleport(Vector3 position, Quaternion rotation)
    {
        GameAudio.UI(GameSound.Teleport);
        ClearMovementInput();
        isSliding = isCrouching = isRunning = false;
        jumpRequested = false;
        slideTimeRemaining = 0f;
        cameraHeightOffset = 0f;
        RestoreStandingCollider();
        transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();

        foreach (FPSCameraLook cameraLook in FindObjectsByType<FPSCameraLook>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cameraLook.player == transform) cameraLook.SetLook(0f, rotation.eulerAngles.y);
        foreach (CameraFollowTPS cameraLook in FindObjectsByType<CameraFollowTPS>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cameraLook.target == transform) cameraLook.SetLook(cameraLook.GetPitch(), rotation.eulerAngles.y);
    }

    void OnDisable()
    {
        isSliding = false;
        isCrouching = false;
        RestoreStandingCollider();
    }

    void OnValidate()
    {
        walkSpeed = Mathf.Max(0.1f, walkSpeed);
        runSpeed = Mathf.Max(walkSpeed, runSpeed);
        crouchSpeed = Mathf.Clamp(crouchSpeed, 0.1f, walkSpeed);
        crouchedColliderHeight = Mathf.Max(0.4f, crouchedColliderHeight);
        slidingColliderHeight = Mathf.Clamp(slidingColliderHeight, 0.4f, crouchedColliderHeight);
        slideSpeed = Mathf.Max(runSpeed, slideSpeed);
        slideEndSpeed = Mathf.Clamp(slideEndSpeed, 0.1f, slideSpeed);
        slideDuration = Mathf.Max(0.1f, slideDuration);
        slideCooldown = Mathf.Max(0f, slideCooldown);
        crouchedCameraDrop = Mathf.Max(0f, crouchedCameraDrop);
    }

    bool IsFPSMode()
    {
        return fpsCamera != null && fpsCamera.gameObject.activeInHierarchy;
    }

    Camera GetActiveCamera()
    {
        if (fpsCamera != null && fpsCamera.gameObject.activeInHierarchy)
            return fpsCamera;

        if (tpsCamera != null && tpsCamera.gameObject.activeInHierarchy)
            return tpsCamera;

        return Camera.main;
    }
}

[DisallowMultipleComponent]
public class AnimeCharacterVisual : MonoBehaviour
{
    private const string ModelResource = "Characters/AnimeGirl/AnimeGirlModel";
    [Min(0.01f)] public float animationBlendTime = 0.15f;

    private Player player;
    private PlayerHealth health;
    private GameObject model;
    private Animation animationPlayer;
    private Renderer[] modelRenderers;
    private readonly System.Collections.Generic.List<Renderer> hiddenLegacyRenderers =
        new System.Collections.Generic.List<Renderer>();
    private string currentAnimation;
    private bool firstPerson;
    private SkinnedMeshRenderer faceRenderer;
    private int blinkShape = -1;
    private float blinkTimer;

    void Start()
    {
        player = GetComponent<Player>();
        health = GetComponent<PlayerHealth>();
        GameObject prefab = Resources.Load<GameObject>(ModelResource);
        if (prefab == null)
        {
            Debug.LogError("Missing character model: Resources/" + ModelResource, this);
            enabled = false;
            return;
        }

        // Hide only the old player's body, leaving cameras, weapons and UI intact.
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.enabled &&
                (renderer.transform == transform || renderer.transform.parent == transform))
            {
                hiddenLegacyRenderers.Add(renderer);
                renderer.enabled = false;
            }
        }

        model = Instantiate(prefab, transform, false);
        model.name = "Blonde Anime Girl";
        BoxCollider body = GetComponent<BoxCollider>();
        float floorHeight = body != null ? body.center.y - body.size.y * 0.5f : -1f;
        model.transform.localPosition = Vector3.up * floorHeight;
        foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = gameObject.layer;

        modelRenderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (SkinnedMeshRenderer renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            int shape = renderer.sharedMesh.GetBlendShapeIndex("Blink");
            if (shape < 0) continue;
            faceRenderer = renderer;
            blinkShape = shape;
            break;
        }
        animationPlayer = model.GetComponentInChildren<Animation>();
        if (animationPlayer == null)
        {
            Debug.LogError("The character prefab has no skeletal animations.", this);
            return;
        }

        animationPlayer.playAutomatically = false;
        animationPlayer.cullingType = AnimationCullingType.AlwaysAnimate;
        foreach (AnimationState state in animationPlayer)
        {
            bool loops = state.name == "Idle" || state.name == "Walk" ||
                         state.name == "Run" || state.name == "Crouch" ||
                         state.name == "CrouchWalk";
            state.wrapMode = loops ? WrapMode.Loop : WrapMode.ClampForever;
        }
        PlayAnimation("Idle", true);
        UpdateVisibility(true);
    }

    void Update()
    {
        if (model == null || player == null || animationPlayer == null)
            return;

        UpdateVisibility(false);
        if (Time.timeScale <= 0f)
            return;

        string nextAnimation;
        if (health != null && health.IsDead)
            nextAnimation = "Death";
        else if (player.IsSliding)
            nextAnimation = "Slide";
        else if (!player.IsGrounded)
            nextAnimation = player.VerticalSpeed > 0.1f ? "Jump" : "Fall";
        else if (player.IsCrouching)
            nextAnimation = player.MovementAmount > 0.05f ? "CrouchWalk" : "Crouch";
        else if (player.MovementAmount > 0.05f && player.enabled)
            nextAnimation = player.IsRunning ? "Run" : "Walk";
        else
            nextAnimation = "Idle";

        PlayAnimation(nextAnimation, false);
        UpdateBlink();
    }

    void UpdateBlink()
    {
        if (faceRenderer == null || blinkShape < 0) return;
        if (health != null && health.IsDead)
        {
            faceRenderer.SetBlendShapeWeight(blinkShape, 100f);
            return;
        }
        blinkTimer = (blinkTimer + Time.deltaTime) % 4.2f;
        float blink = blinkTimer < 0.18f ? Mathf.Sin(blinkTimer / 0.18f * Mathf.PI) : 0f;
        faceRenderer.SetBlendShapeWeight(blinkShape, blink * 100f);
    }

    void PlayAnimation(string clipName, bool immediately)
    {
        if (clipName == currentAnimation)
            return;
        if (animationPlayer[clipName] == null)
        {
            Debug.LogError("Missing character animation: " + clipName, this);
            return;
        }

        currentAnimation = clipName;
        animationPlayer[clipName].time = 0f;
        if (immediately)
            animationPlayer.Play(clipName);
        else
            animationPlayer.CrossFade(clipName, animationBlendTime);
    }

    void UpdateVisibility(bool force)
    {
        bool isFirstPerson = player.fpsCamera != null &&
                             player.fpsCamera.gameObject.activeInHierarchy;
        if (!force && isFirstPerson == firstPerson)
            return;

        firstPerson = isFirstPerson;
        foreach (Renderer renderer in modelRenderers)
            renderer.shadowCastingMode = firstPerson
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
    }

    void OnDestroy()
    {
        if (model != null)
            Destroy(model);
        foreach (Renderer renderer in hiddenLegacyRenderers)
            if (renderer != null) renderer.enabled = true;
    }
}
