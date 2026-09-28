using UnityEngine;

// Run after UI updates so an Escape event cannot leave a stale cursor state.
[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public class PlayerCursorController : MonoBehaviour
{
    private WeaponInventory inventory;
    private PlayerHealth health;

    void OnEnable()
    {
        RefreshCursor();
    }

    void Start()
    {
        RefreshCursor();
    }

    void LateUpdate()
    {
        RefreshCursor();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            RefreshCursor();
        else
            SetCursor(true);
    }

    public void RefreshCursor()
    {
        if (!isActiveAndEnabled)
            return;

        if (inventory == null)
            inventory = GetComponent<WeaponInventory>();
        if (health == null)
            health = GetComponent<PlayerHealth>();

        bool menuOpen = (inventory != null && (inventory.IsOpen || inventory.IsPaused)) ||
                        (health != null && health.IsChoosingUpgrade);
        SetCursor(!Application.isFocused || menuOpen);
    }

    void OnDisable()
    {
        SetCursor(true);
    }

    static void SetCursor(bool visible)
    {
#if UNITY_EDITOR
        if (!visible)
            AllowEditorCursorCapture();
#endif
        // Reassert both values even if their getters still report the desired state.
        // The Editor or window system may have released the actual pointer independently.
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = visible;
    }

#if UNITY_EDITOR
    private static readonly System.Type GameViewType =
        typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
    private static readonly System.Reflection.MethodInfo AllowCursorLockAndHide =
        GameViewType?.GetMethod("AllowCursorLockAndHide",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null);
    private static readonly object[] AllowCaptureArguments = { true };
    private static bool editorCaptureWarningLogged;

    static void AllowEditorCursorCapture()
    {
        // GameView disables this separate permission on Escape. Cursor setters alone
        // cannot restore it. Only restore it in the already-focused, running Game view.
        UnityEditor.EditorWindow focusedWindow = UnityEditor.EditorWindow.focusedWindow;
        if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPaused ||
            focusedWindow == null || GameViewType == null || !GameViewType.IsInstanceOfType(focusedWindow))
            return;

        if (AllowCursorLockAndHide == null)
        {
            if (!editorCaptureWarningLogged)
            {
                Debug.LogWarning("Cannot restore Game view cursor capture in this Unity version. Click the Game view to capture the cursor.");
                editorCaptureWarningLogged = true;
            }
            return;
        }

        AllowCursorLockAndHide.Invoke(focusedWindow, AllowCaptureArguments);
    }
#endif
}
