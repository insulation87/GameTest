using UnityEngine;

/// <summary>A physical camp doorway with one fixed adventure destination.</summary>
public sealed class TradingHubPortal : MonoBehaviour
{
    [SerializeField, Range(0, 3)] int mapIndex;
    public ExpeditionMapDefinition Map => ExpeditionMaps.All[mapIndex];
    public void Initialize(int index) => mapIndex = Mathf.Clamp(index, 0, ExpeditionMaps.All.Length - 1);
}
