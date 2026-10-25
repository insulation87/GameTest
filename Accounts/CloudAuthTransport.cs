using System;
using System.Collections;
using System.Text;
using UnityEngine.Networking;

public sealed class CloudAuthResponse
{
    public long status;
    public string body;
    public bool networkError;
    public bool Success => !networkError && status >= 200 && status < 300;
}

public interface ICloudAuthTransport
{
    IEnumerator Send(string method, string endpoint, string json, string accessToken, Action<CloudAuthResponse> complete);
}

// This transport accepts Auth endpoints only; there is no progress-upload endpoint.
public sealed class CloudAuthTransport : ICloudAuthTransport
{
    readonly CloudAuthConfig config;
    public CloudAuthTransport(CloudAuthConfig config) { this.config = config; }
    public IEnumerator Send(string method, string endpoint, string json, string accessToken, Action<CloudAuthResponse> complete)
    {
        if (!config.IsValid || !Allowed(endpoint))
        { complete(new CloudAuthResponse { status = 0, networkError = true }); yield break; }
        using (var request = new UnityWebRequest(config.projectUrl.TrimEnd('/') + "/auth/v1/" + endpoint, method))
        {
            request.timeout = 15;
            request.redirectLimit = 0;
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("apikey", config.publishableKey);
            request.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(accessToken)) request.SetRequestHeader("Authorization", "Bearer " + accessToken);
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            yield return request.SendWebRequest();
            complete(new CloudAuthResponse {
                status = request.responseCode, body = request.downloadHandler.text,
                networkError = request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError
            });
        }
    }
    static bool Allowed(string endpoint) => endpoint == "signup" || endpoint == "recover" || endpoint == "verify" ||
        endpoint == "user" || endpoint == "token?grant_type=password" || endpoint == "token?grant_type=refresh_token" ||
        endpoint == "logout?scope=local" || endpoint == "resend";
}
