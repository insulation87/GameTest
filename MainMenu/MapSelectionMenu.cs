using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class MapSelectionMenu : MonoBehaviour
{
    Action<string> confirm;
    Action cancel;
    readonly Button[] cards = new Button[4];
    Text detail, preparationNotice;
    Button confirmButton;
    int selected;
    public static MapSelectionMenu Create(Transform parent, Action<string> confirm, Action cancel)
    {
        var root = AccountLoginPanel.Box(parent,"Map Selection",Vector2.zero,Vector2.zero,GameUITheme.Ink);
        GameUITheme.Stretch(root);
        var menu = root.gameObject.AddComponent<MapSelectionMenu>(); menu.confirm=confirm; menu.cancel=cancel; menu.Build();
        root.gameObject.SetActive(false); return menu;
    }
    public void Open(string scene, bool preparing = false)
    {
        gameObject.SetActive(true); Select(ExpeditionMaps.ForScene(scene).Index);
        preparationNotice.text = preparing ? "選擇出發目的地，保留本輪已購買的裝備、補給與金幣。" :
            "目前冒險已出發；開始新一輪會重置裝備與關卡進度，保留金幣和武器解鎖。";
        confirmButton.GetComponentInChildren<Text>().text = preparing ? "選擇此地圖" : "以此地圖開始新一輪";
    }
    void Build()
    {
        var panel = AccountLoginPanel.Box(transform,"Expedition Maps",Vector2.zero,new Vector2(1740,940),GameUITheme.Panel); GameUITheme.Frame(panel,true);
        GameUITheme.Label(panel,"旅人營地 · 選擇出發地圖",38,new Vector2(0,405),new Vector2(1620,76)).color=GameUITheme.Gold;
        GameUITheme.Label(panel,"所有地圖皆可自由選擇 · 每張地圖 3 關 · 由左至右難度提高",23,new Vector2(0,337),new Vector2(1620,48));
        for(int i=0;i<cards.Length;i++) {
            int index=i; var map=ExpeditionMaps.All[i];
            var card=AccountLoginPanel.Box(panel,map.Scene,new Vector2((i-1.5f)*410,50),new Vector2(390,470),GameUITheme.Recess);
            cards[i]=card.gameObject.AddComponent<Button>(); cards[i].targetGraphic=card.GetComponent<Image>(); cards[i].onClick.AddListener(()=>Select(index));
            GameUITheme.Label(card,"0"+(i+1)+"  /  "+new[]{"入門","進階","困難","極難"}[i],21,new Vector2(0,193),new Vector2(350,40)).color=GameUITheme.Gold;
            var sceneArt=new GameObject("Landscape",typeof(RectTransform),typeof(CanvasRenderer),typeof(MapLandscapeGraphic)); sceneArt.transform.SetParent(card,false);
            sceneArt.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,93); sceneArt.GetComponent<RectTransform>().sizeDelta=new Vector2(350,140);
            sceneArt.GetComponent<MapLandscapeGraphic>().Index=i; sceneArt.GetComponent<MapLandscapeGraphic>().raycastTarget=false;
            GameUITheme.Label(card,map.Name,29,new Vector2(0,-10),new Vector2(350,52));
            GameUITheme.Label(card,map.Description,19,new Vector2(0,-70),new Vector2(345,70)).color=GameUITheme.Muted;
            GameUITheme.Label(card,"第 1 關  "+map.Stages[0]+"\n第 2 關  "+map.Stages[1]+"\n第 3 關  "+map.Stages[2],20,new Vector2(0,-163),new Vector2(350,105));
        }
        detail=GameUITheme.Label(panel,"",22,new Vector2(0,-239),new Vector2(1630,65)); detail.color=GameUITheme.Teal;
        preparationNotice=GameUITheme.Label(panel,"",21,new Vector2(0,-301),new Vector2(1630,50)); preparationNotice.color=GameUITheme.Muted;
        MultiplayerMenu.Button(panel,"返回",new Vector2(-300,-389),new Vector2(480,66),()=>cancel?.Invoke());
        confirmButton=MultiplayerMenu.Button(panel,"選擇此地圖",new Vector2(300,-389),new Vector2(590,66),()=>confirm?.Invoke(ExpeditionMaps.All[selected].Scene));
    }
    public void Select(int index)
    {
        selected=Mathf.Clamp(index,0,cards.Length-1);
        for(int i=0;i<cards.Length;i++) { GameUITheme.StyleButton(cards[i]); cards[i].GetComponent<Image>().color=i==selected ? new Color(.20f,.31f,.34f) : GameUITheme.Recess; }
        var map=ExpeditionMaps.All[selected];
        detail.color=GameUITheme.Teal;
        detail.text=$"已選：{map.Name}　｜　敵人生命 ×{map.Health:0.##}　傷害 ×{map.Damage:0.##}　生成頻率 ×{map.SpawnRate:0.##}";
    }
    public void ShowError(string message) { detail.text=message; detail.color=GameUITheme.Gold; }
    void Update() { if (!GameSceneFlow.IsLoading && Input.GetKeyDown(KeyCode.Escape)) cancel?.Invoke(); }
}

public sealed class MapLandscapeGraphic : MaskableGraphic
{
    public int Index;
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); var rect=rectTransform.rect; var map=ExpeditionMaps.All[Mathf.Clamp(Index,0,3)];
        Quad(mesh,rect.xMin,rect.yMin,rect.width,rect.height,map.Sky);
        Quad(mesh,rect.xMin,rect.yMin,rect.width,rect.height*.36f,map.Ground);
        for(int i=0;i<7;i++) {
            float x=rect.xMin+16+i*48, h=35+(i*17+Index*11)%40;
            if(Index==1) { Quad(mesh,x,-12,10,h,map.Foliage); Quad(mesh,x-9,15,9,9,map.Foliage); }
            else if(Index==2) { Quad(mesh,x,-15,22,h,map.Foliage); Quad(mesh,x-4,h-35,30,10,map.Highlight); }
            else if(Index==3) { Quad(mesh,x,-18,24,h,map.Rock); Quad(mesh,x+5,-20,40,4,new Color(1,.38f,.10f)); }
            else { Quad(mesh,x,-22,8,h,map.Rock); Quad(mesh,x-10,h-40,29,28,map.Foliage); }
        }
    }
    static void Quad(VertexHelper mesh,float x,float y,float width,float height,Color color)
    {
        int start=mesh.currentVertCount;
        mesh.AddVert(new Vector3(x,y),color,Vector2.zero); mesh.AddVert(new Vector3(x,y+height),color,Vector2.zero);
        mesh.AddVert(new Vector3(x+width,y+height),color,Vector2.zero); mesh.AddVert(new Vector3(x+width,y),color,Vector2.zero);
        mesh.AddTriangle(start,start+1,start+2);mesh.AddTriangle(start,start+2,start+3);
    }
}
