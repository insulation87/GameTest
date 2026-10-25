using System;
using UnityEngine;

[Serializable] public sealed class CloudRelayConfig
{
    public string relayUrl = "";
    public bool IsValid => Uri.TryCreate(relayUrl, UriKind.Absolute, out var url) && url.Scheme == "wss" &&
        string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment) && url.AbsolutePath == "/coop";
    public static CloudRelayConfig Load()
    {
        var text = Resources.Load<TextAsset>("CloudRelayConfig");
        try { return text == null ? new CloudRelayConfig() : JsonUtility.FromJson<CloudRelayConfig>(text.text) ?? new CloudRelayConfig(); }
        catch (ArgumentException) { return new CloudRelayConfig(); }
    }
}
