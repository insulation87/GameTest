using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Render the actual mode-selection UI in an isolated editor scene.
public static class CoopPresentationReview
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Preview Camera", typeof(Camera)).GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = GameUITheme.Ink;
        camera.nearClipPlane = .1f; camera.farClipPlane = 50;
        var target = new RenderTexture(1600, 900, 24); camera.targetTexture = target;
        var canvas = new GameObject("Modes Preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2;
        GameUITheme.FitCanvas(canvas.GetComponent<CanvasScaler>());
        MultiplayerMenu.Create(canvas.transform, "Map1", () => false, () => { }).Open();
        Canvas.ForceUpdateCanvases(); camera.Render();
        var prior = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply(); RenderTexture.active = prior;
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../MultiplayerModes.png"));
        File.WriteAllBytes(path, pixels.EncodeToPNG()); Object.DestroyImmediate(pixels); camera.targetTexture = null; Object.DestroyImmediate(target);
        Debug.Log("Rendered mode-selection preview: " + path); EditorApplication.Exit(0);
    }
}
