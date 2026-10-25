using UnityEngine;

// Keep both camera rigs on the player's side of solid scenery, including during smoothing.
public static class CameraCollision
{
    static readonly RaycastHit[] hits = new RaycastHit[64];
    public static Vector3 Resolve(Transform owner,Vector3 pivot,Vector3 wanted,Camera view)
    {
        Vector3 delta = wanted - pivot; float distance = delta.magnitude;
        if (distance < .001f) return pivot;
        Vector3 direction = delta / distance;
        int count = Physics.SphereCastNonAlloc(pivot,.12f,direction,hits,distance,Physics.AllLayers,QueryTriggerInteraction.Ignore);
        float safeDistance = distance;
        float clearance = .03f + (view != null ? view.nearClipPlane : .1f);
        for (int i = 0; i < count; i++)
        {
            Collider other = hits[i].collider;
            if (other == null || owner != null && other.transform.IsChildOf(owner) ||
                other.GetComponentInParent<Player>() != null || other.GetComponentInParent<EnemyHealth>() != null ||
                other.GetComponentInParent<Projectile>() != null || PickupCollisionUtility.IsPickup(other)) continue;
            safeDistance = Mathf.Min(safeDistance,Mathf.Max(0,hits[i].distance - clearance));
        }
        if(count == hits.Length) safeDistance = 0;
        return pivot + direction * safeDistance;
    }
}
