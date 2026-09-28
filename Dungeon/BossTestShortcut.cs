using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Hold Shift + Ctrl and press O twice to prepare a boss encounter.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public class BossTestShortcut : MonoBehaviour
{
    [Min(0.1f)] public float doublePressWindow = 1f;
    private float firstPressTime = float.NegativeInfinity;

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        PlayerHealth health = GetComponent<PlayerHealth>();
        if (!Application.isFocused || keyboard == null || health == null ||
            health.IsDead || health.IsChoosingUpgrade ||
            !(keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) ||
            !(keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
        {
            firstPressTime = float.NegativeInfinity;
            return;
        }

        if (!keyboard.oKey.wasPressedThisFrame)
            return;

        float now = Time.unscaledTime;
        if (now - firstPressTime > doublePressWindow)
        {
            firstPressTime = now;
            return;
        }

        firstPressTime = float.NegativeInfinity;
        ActivateBossTest();
    }

    void OnDisable()
    {
        firstPressTime = float.NegativeInfinity;
    }

    public bool ActivateBossTest()
    {
        Player player = GetComponent<Player>();
        PlayerHealth health = GetComponent<PlayerHealth>();
        WeaponInventory inventory = GetComponent<WeaponInventory>();
        if (player == null || !player.isActiveAndEnabled || health == null ||
            health.IsDead || health.IsChoosingUpgrade || inventory == null)
        {
            Debug.LogWarning("首領測試快捷鍵需要存活的玩家、武器背包與地城場景。", this);
            return false;
        }

        FloorProgressionManager progression = FloorProgressionManager.EnsureInstance();
        if (progression == null)
        {
            Debug.LogWarning("無法使用首領測試快捷鍵：目前不是地城場景。", this);
            return false;
        }

        if (!progression.TryPrepareBossTest(player, out BossController boss,
            out Vector3 position, out Quaternion rotation))
            return false;

        inventory.CloseMenusForGameplay();
        inventory.GrantBossTestSupplies(boss.GetComponent<EnemyHealth>());
        player.Teleport(position, rotation);
        GetComponent<PlayerCursorController>()?.RefreshCursor();
        Debug.Log("已傳送至首領房間，獲得 5 把 100 級傳說武器與 10 個補血道具。", this);
        return true;
    }
}
