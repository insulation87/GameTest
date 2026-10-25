using System;
using UnityEngine;

[Serializable]
public sealed class CloudAuthConfig
{
    public string projectUrl = "";
    public string publishableKey = "";

    public bool IsValid => Uri.TryCreate(projectUrl, UriKind.Absolute, out Uri url) &&
        url.Scheme == "https" && string.IsNullOrEmpty(url.UserInfo) && string.IsNullOrEmpty(url.Query) &&
        string.IsNullOrEmpty(url.Fragment) && url.AbsolutePath.Trim('/') == "" &&
        !string.IsNullOrEmpty(publishableKey) && publishableKey.StartsWith("sb_publishable_", StringComparison.Ordinal) &&
        publishableKey.Length > 20 && publishableKey.IndexOfAny(new[] {' ', '\r', '\n', '\t'}) < 0;

    public static CloudAuthConfig Load()
    {
        var asset = Resources.Load<TextAsset>("CloudAuthConfig");
        CloudAuthConfig config = null;
        try { if (asset != null) config = JsonUtility.FromJson<CloudAuthConfig>(asset.text); }
        catch (ArgumentException) { }
        return config ?? new CloudAuthConfig();
    }
}
