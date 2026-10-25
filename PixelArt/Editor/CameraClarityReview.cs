using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CameraClarityReview
{
    const string Pending = "CameraClarityReview.Pending";
    static IEnumerator checks;
    static double deadline;
    static int assertions;
    static readonly List<string> errors = new List<string>();
    static CameraClarityReview() { EditorApplication.playModeStateChanged += StateChanged; }
    public static void Run()
    {
        LoginSceneSetup.PlayFromCurrentScene();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending,false)) return;
        SessionState.SetBool(Pending,false); assertions=0; errors.Clear(); checks=Checks();
        deadline=EditorApplication.timeSinceStartup+.2;
        Application.logMessageReceived+=Log; EditorApplication.update+=Tick;
    }
    static void Log(string message,string stack,LogType type)
    { if ((type==LogType.Error || type==LogType.Exception) && !message.Contains("burst JIT compiler") && !stack.Contains("UnityEditor.Search")) errors.Add(message); }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup<deadline) return;
        try
        {
            if (!checks.MoveNext())
            {
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../camera-result.txt")),"PASS "+assertions+" camera clarity integration checks");
                Finish(0); return;
            }
            deadline=EditorApplication.timeSinceStartup+(checks.Current is float delay?delay:.1f);
        }
        catch(Exception ex) { Debug.LogException(ex); Finish(1); }
    }
    static void Finish(int code)
    { Time.timeScale=1; Application.logMessageReceived-=Log; EditorApplication.update-=Tick; EditorApplication.Exit(code); }
    static void Check(bool condition,string message)
    { if (!condition) throw new InvalidOperationException(message); assertions++; Debug.Log("PASS "+message); }
    static void Mode(CameraSwitch views,bool first)
    { typeof(CameraSwitch).GetMethod("SetMode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(views,new object[]{first}); }
    static IEnumerator Checks()
    {
        var actor=new GameObject("Camera Fixture"); actor.SetActive(false);
        var views=actor.AddComponent<CameraSwitch>();
        var fps=new GameObject("First Person",typeof(Camera)).GetComponent<Camera>(); fps.transform.SetParent(actor.transform,false);
        var tps=new GameObject("Third Person",typeof(Camera)).GetComponent<Camera>(); tps.transform.SetParent(actor.transform,false);
        views.fpsCamera=fps; views.tpsCamera=tps;
        views.fpsController=fps.gameObject.AddComponent<FPSCameraLook>(); views.fpsController.player=actor.transform;
        views.tpsController=tps.gameObject.AddComponent<CameraFollowTPS>(); views.tpsController.target=actor.transform;
        var stationary=new GameObject("Unrelated Main Camera",typeof(Camera)).GetComponent<Camera>(); stationary.depth=-1;
        actor.SetActive(true); yield return 1f;
        var effect=fps.GetComponent<PixelArtCamera>();
        Check(effect!=null && effect.nativeResolution,"The active first-person camera defaults to native resolution");
        Check(fps.targetTexture.width==Screen.width && fps.targetTexture.height==Screen.height,"The world uses every display pixel rather than a 360p enlargement");
        Check(fps.targetTexture.filterMode==FilterMode.Point && fps.targetTexture.antiAliasing==1,"Presentation does not introduce interpolation blur");
        Check(stationary.GetComponent<PixelArtCamera>()==null && stationary.targetTexture==null,"The stationary scene camera cannot cover the gameplay view");
        Check(!fps.transform.Find("Pixel World Display/World Pixels").GetComponent<RawImage>().raycastTarget,"Full-resolution world output does not block HUD input");
        Vector3 before=fps.transform.position; actor.transform.position+=new Vector3(3,0,4); yield return .15f;
        Check(Vector3.Distance(fps.transform.position-before,new Vector3(3,0,4))<.01f,"The sharper first-person view follows movement correctly");
        views.fpsController.SetLook(15,75); yield return .1f;
        Check(Quaternion.Angle(fps.transform.rotation,Quaternion.Euler(15,75,0))<.1f,"Camera look remains aligned with aiming");
        Mode(views,false); yield return .25f;
        Check(views.ActiveCamera==tps && tps.targetTexture.width==Screen.width && tps.targetTexture.height==Screen.height,"Third-person switching also renders at full resolution");
        Check(fps.targetTexture==null && fps.transform.Find("Pixel World Display")==null,"Inactive views release their render texture and overlay");
        var thirdEffect=tps.GetComponent<PixelArtCamera>(); thirdEffect.nativeResolution=false; thirdEffect.referenceHeight=180;
        Time.timeScale=0; yield return .15f;
        Check(tps.targetTexture.height<Screen.height && tps.targetTexture.filterMode==FilterMode.Point,"The optional low-resolution style remains available while paused");
        thirdEffect.nativeResolution=true; yield return .15f;
        Check(tps.targetTexture.width==Screen.width && tps.targetTexture.height==Screen.height,"Switching back rebuilds a sharp full-resolution output during pause");
        Mode(views,true); yield return .15f;
        Check(fps.targetTexture!=null && tps.targetTexture==null,"View switching releases the old output even when time is paused");
        int outputs=Object.FindObjectsByType<RawImage>(FindObjectsSortMode.None).Length;
        Check(outputs==1,"Only one fullscreen world output is visible");
        effect=fps.GetComponent<PixelArtCamera>(); effect.enabled=false; yield return .1f;
        Check(fps.targetTexture==null && fps.transform.Find("Pixel Screen Output")==null,"Disabling presentation restores direct camera output");
        var preview=new GameObject("External Preview",typeof(Camera)).GetComponent<Camera>(); preview.targetTexture=new RenderTexture(64,64,16);
        var custom=preview.gameObject.AddComponent<PixelArtCamera>();
        Check(!custom.enabled && preview.targetTexture.width==64,"Application-owned preview textures are preserved");
        Check(errors.Count==0,"No unexpected rendering errors: "+string.Join("; ",errors));
    }
}
