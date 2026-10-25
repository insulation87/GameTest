using UnityEngine;

public partial class Player
{
    const float CollisionSkin = .02f;
    readonly RaycastHit[] movementHits = new RaycastHit[64];
    readonly Collider[] rotationOverlaps = new Collider[64];
    PhysicsMaterial movementMaterial, originalBodyMaterial;

    void ConfigureBodyContact()
    {
        if (bodyCollider == null || movementMaterial != null) return;
        originalBodyMaterial = bodyCollider.sharedMaterial;
        // Velocity sets movement speed; friction must not reduce it or glue jumps to walls.
        movementMaterial = new PhysicsMaterial("Player Movement Contact")
        { staticFriction=0,dynamicFriction=0,frictionCombine=PhysicsMaterialCombine.Minimum,
            bounciness=0,bounceCombine=PhysicsMaterialCombine.Minimum };
        bodyCollider.sharedMaterial = movementMaterial;
    }

    void OnDestroy()
    {
        if (bodyCollider != null && bodyCollider.sharedMaterial == movementMaterial) bodyCollider.sharedMaterial = originalBodyMaterial;
        if (movementMaterial != null) Destroy(movementMaterial);
    }

    void StopHorizontalMotion()
    {
        if (rb != null && !rb.isKinematic)
        { Vector3 velocity=rb.linearVelocity; velocity.x=velocity.z=0; rb.linearVelocity=velocity; }
    }

    void SetMovementVelocity(Vector3 horizontal)
    {
        if (rb == null || rb.isKinematic) return;
        horizontal.y = 0;
        Vector3 travel = horizontal * Time.fixedDeltaTime;
        Vector3 resolved = Vector3.zero;
        // Sweep the actual stance-sized body, then retain travel along the wall.
        // Gravity/jumps remain controlled by the dynamic rigidbody and CCD.
        for (int pass = 0; pass < 3 && travel.sqrMagnitude > .000001f; pass++)
        {
            float distance = travel.magnitude;
            Vector3 direction = travel / distance;
            if (!FindMovementWall(rb.position + resolved, direction, distance, out RaycastHit wall))
            { resolved += travel; break; }
            float allowed = Mathf.Clamp(wall.distance - CollisionSkin,0,distance);
            resolved += direction * allowed;
            Vector3 normal = wall.normal; normal.y = 0; normal.Normalize();
            travel = Vector3.ProjectOnPlane(travel - direction * allowed,normal);
        }
        Vector3 velocity = resolved / Time.fixedDeltaTime;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;
    }

    bool FindMovementWall(Vector3 position,Vector3 direction,float distance,out RaycastHit nearest)
    {
        nearest = default;
        if (bodyCollider == null || !bodyCollider.enabled) return false;
        Vector3 scale = transform.lossyScale;
        Vector3 half = Vector3.Scale(bodyCollider.size,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z))) * .5f;
        // A small inset keeps floor contact and tiny numerical overlaps out of the sweep.
        half = Vector3.Max(half - Vector3.one * .005f,Vector3.one * .01f);
        Vector3 center = position + rb.rotation * Vector3.Scale(bodyCollider.center,scale);
        int count = Physics.BoxCastNonAlloc(center,half,direction,movementHits,rb.rotation,
            distance + CollisionSkin,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        if (count == movementHits.Length)
        { nearest = new RaycastHit { distance = 0, normal = -direction }; return true; }
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = movementHits[i];
            if (!BlocksBody(hit.collider)) continue;
            // Casts beginning in a tiny contact overlap report -direction rather
            // than a surface normal. Resolve it so floor contact cannot become a wall.
            if (hit.distance <= .001f)
            {
                Collider other = hit.collider;
                if (Physics.ComputePenetration(bodyCollider,position,rb.rotation,other,
                    other.transform.position,other.transform.rotation,out Vector3 separation,out _)) hit.normal = separation;
                else
                {
                    Vector3 fromSurface = center - other.ClosestPoint(center);
                    if (fromSurface.sqrMagnitude > .000001f) hit.normal = fromSurface.normalized;
                }
            }
            if (Mathf.Abs(hit.normal.y) > .65f || Vector3.Dot(direction,hit.normal) >= -.001f) continue;
            if (hit.distance < closest) { closest = hit.distance; nearest = hit; }
        }
        return closest < float.PositiveInfinity;
    }

    bool BlocksBody(Collider other)
    {
        return other != null && other != bodyCollider && other.attachedRigidbody != rb &&
            !other.transform.IsChildOf(transform) && !Physics.GetIgnoreLayerCollision(gameObject.layer,other.gameObject.layer) &&
            !Physics.GetIgnoreCollision(bodyCollider,other);
    }

    public void RotateWithCollision(Quaternion rotation,bool immediate = false)
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (bodyCollider == null) bodyCollider = GetComponent<BoxCollider>();
        if (rb == null) return;
        if (!CoopSession.IsClient && bodyCollider != null && bodyCollider.enabled)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 half = Vector3.Scale(bodyCollider.size,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z))) * .5f;
            Vector3 center = rb.position + rotation * Vector3.Scale(bodyCollider.center,scale);
            int count = Physics.OverlapBoxNonAlloc(center,half,rotationOverlaps,rotation,Physics.AllLayers,QueryTriggerInteraction.Ignore);
            if (count == rotationOverlaps.Length) return;
            for (int i = 0; i < count; i++)
            {
                Collider other = rotationOverlaps[i];
                if (BlocksBody(other) && Physics.ComputePenetration(bodyCollider,rb.position,rotation,
                    other,other.transform.position,other.transform.rotation,out Vector3 direction,out float depth) &&
                    Mathf.Abs(direction.y) < .65f && depth > .005f) return;
            }
        }
        if (immediate) rb.rotation = rotation; else rb.MoveRotation(rotation);
    }
}
