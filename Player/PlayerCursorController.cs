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
        // Reassert both values even if their getters still report the desired state.
        // The Editor or window system may have released the actual pointer independently.
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = visible;
    }
}
