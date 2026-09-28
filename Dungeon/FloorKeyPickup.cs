using UnityEngine;

public class FloorKeyPickup : MonoBehaviour, IMagneticPickup
{
    private int floor;
    private bool collected;
    private Rigidbody rotationBody;

    public static FloorKeyPickup Create(Vector3 position, int sourceFloor, float lifetime)
    {
        GameObject pickupObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        pickupObject.name = $"Floor {sourceFloor} Progression Key";
        pickupObject.transform.position = position;
        pickupObject.transform.localScale = new Vector3(0.35f, 0.6f, 0.35f);

        Renderer pickupRenderer = pickupObject.GetComponent<Renderer>();
        if (pickupRenderer != null)
            pickupRenderer.material.color = new Color(1f, 0.75f, 0.05f, 1f);

        Collider pickupCollider = pickupObject.GetComponent<Collider>();
        pickupCollider.isTrigger = true;

        Rigidbody pickupBody = pickupObject.AddComponent<Rigidbody>();
        pickupBody.isKinematic = true;
        pickupBody.useGravity = false;

        FloorKeyPickup pickup = pickupObject.AddComponent<FloorKeyPickup>();
        pickup.floor = Mathf.Clamp(sourceFloor, 1, 3);
        Destroy(pickupObject, lifetime);
        return pickup;
    }

    void FixedUpdate()
    {
        if (rotationBody == null) rotationBody = GetComponent<Rigidbody>();
        if (rotationBody != null)
            rotationBody.MoveRotation(Quaternion.Euler(0f, 120f * Time.fixedDeltaTime, 0f) * rotationBody.rotation);
        else
            transform.Rotate(0f, 120f * Time.fixedDeltaTime, 0f, Space.World);
    }

    void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    public bool CanCollect(WeaponInventory inventory) => !collected && inventory != null;

    public bool TryCollect(Collider other)
    {
        if (collected || other == null)
            return false;
        WeaponInventory inventory = other.GetComponentInParent<WeaponInventory>();
        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (inventory == null || player == null || player.IsDead)
            return false;

        collected = true;
        inventory.AddFloorKey(floor);
        if (FloorProgressionManager.Instance != null)
            FloorProgressionManager.Instance.Collect(floor);
        Destroy(gameObject);
        return true;
    }
}
