using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;

public sealed class CoopRoomHud : MonoBehaviour
{
    Text label;
    string address;
    float nextRefresh;
    Button inviteButton;
    FriendsMenu friendsMenu;
    void Start()
    {
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 70;
        GameUITheme.FitCanvas(gameObject.AddComponent<CanvasScaler>());
        gameObject.AddComponent<GraphicRaycaster>();
        inviteButton = MultiplayerMenu.Button(transform, "邀請好友", new Vector2(-140, 58), new Vector2(240, 48), () =>
        {
            if (friendsMenu == null) friendsMenu = FriendsMenu.Create(transform, () => friendsMenu.gameObject.SetActive(false));
            friendsMenu.Open();
        });
        inviteButton.GetComponent<RectTransform>().anchorMin = inviteButton.GetComponent<RectTransform>().anchorMax = Vector2.right;
        inviteButton.gameObject.SetActive(false);
        CloudRoomInviteService.Ensure();
        label = GameUITheme.Label(transform, "", 17, new Vector2(0, 18), new Vector2(1200, 34), TextAnchor.MiddleCenter);
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, 0);
        label.color = GameUITheme.Muted; label.raycastTarget = false;
        address = "127.0.0.1";
        if (CoopSession.Mode == CoopMode.Host)
        {
            try
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                    foreach (var entry in network.GetIPProperties().UnicastAddresses)
                        if (entry.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(entry.Address))
                        {
                            string candidate = entry.Address.ToString(); address = candidate;
                            if (candidate.StartsWith("192.168.") || candidate.StartsWith("10.")) goto Found;
                        }
            }
            catch (NetworkInformationException) { }
        }
        Found: Refresh();
    }
    void Update() { if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .5f; Refresh(); } }
    void Refresh()
    {
        if (label == null) return;
        if (!CoopSession.IsMultiplayer) { Destroy(gameObject); return; }
        inviteButton.gameObject.SetActive(CoopSession.IsInternet && CoopSession.Mode == CoopMode.Host && CoopSession.InCamp);
        inviteButton.interactable = CloudRoomInviteService.CanInvite;
        label.text = CoopSession.IsInternet ? "網際網路合作　｜房間代碼：" + CoopSession.RoomCode + "　｜" + CoopSession.Instance.PlayerCount + " / 4 人" :
            CoopSession.Mode == CoopMode.Host ? "連線房主　｜" + address + ":" + CoopSession.Instance.ListeningPort + "　｜" + CoopSession.Instance.PlayerCount + " / 4 人" :
            "連線合作　｜" + CoopSession.Instance.PlayerCount + " / 4 人　｜" + GameControls.Label(0, GameAction.Heal) + " 醫療包";
    }
}
