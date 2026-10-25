using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class GameSettingsData
{
    public int width, height;
    public FullScreenMode windowMode;
    public float masterVolume = 1f, musicVolume = 1f, effectsVolume = 1f;
    public float sensitivity = 1f, fieldOfView = 60f;
    public bool invertY, vSync, muteInBackground = true;
    public int frameRate = 120;
    public GameBindings bindings = GameControls.Defaults();
    public GameSettingsData Clone()
    {
        var clone = (GameSettingsData)MemberwiseClone();
        clone.bindings = bindings?.Clone();
        return clone;
    }
}

/// <summary>One settings store shared by the menu, gameplay, cameras and audio.</summary>
public static class GameSettings
{
    public static readonly int[] FrameRates = { 30, 60, 90, 120, 144, 165, 240, -1 };
    static GameSettingsData current;
    static string prefix = "Settings.";
    public static event Action Changed;
    public static GameSettingsData Current
    {
        get { if (current == null) Load(); return current; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { current = null; prefix = "Settings."; Changed = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap() => Load();

    public static GameSettingsData Defaults()
    {
        Resolution desktop = Screen.currentResolution;
        bool large = desktop.width >= 1920 && desktop.height >= 1080;
        return new GameSettingsData { width = large ? 1920 : 1280, height = large ? 1080 : 720,
            windowMode = FullScreenMode.FullScreenWindow };
    }

    // A distinct prefix permits integration checks without overwriting a player's preferences.
    public static void Load(string preferencePrefix = "Settings.", bool applyDisplay = true)
    {
        prefix = preferencePrefix;
        var data = Defaults();
        data.width = PlayerPrefs.GetInt(prefix + "Width", Mathf.Max(640, Screen.width));
        data.height = PlayerPrefs.GetInt(prefix + "Height", Mathf.Max(480, Screen.height));
        if (!PlayerPrefs.HasKey(prefix + "Width") && PlayerPrefs.HasKey(prefix + "Resolution"))
        {
            var old = new List<Vector2Int>();
            foreach (Resolution resolution in Screen.resolutions)
            {
                var size = new Vector2Int(resolution.width, resolution.height);
                if (!old.Contains(size)) old.Add(size);
            }
            int index = PlayerPrefs.GetInt(prefix + "Resolution", -1);
            if (index >= 0 && index < old.Count) { data.width = old[index].x; data.height = old[index].y; }
        }
        FullScreenMode initial = Screen.fullScreenMode == FullScreenMode.MaximizedWindow
            ? FullScreenMode.FullScreenWindow : Screen.fullScreenMode;
        if (PlayerPrefs.HasKey(prefix + "Fullscreen"))
            initial = PlayerPrefs.GetInt(prefix + "Fullscreen") == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        data.windowMode = (FullScreenMode)PlayerPrefs.GetInt(prefix + "WindowMode", (int)initial);
        data.masterVolume = PlayerPrefs.GetFloat(prefix + "MasterVolume", 1f);
        data.musicVolume = PlayerPrefs.GetFloat(prefix + "MusicVolume", 1f);
        data.effectsVolume = PlayerPrefs.GetFloat(prefix + "EffectsVolume", 1f);
        data.sensitivity = PlayerPrefs.GetFloat(prefix + "MouseSensitivity", 1f);
        data.fieldOfView = PlayerPrefs.GetFloat(prefix + "FieldOfView", 60f);
        data.invertY = PlayerPrefs.GetInt(prefix + "InvertY", 0) == 1;
        data.vSync = PlayerPrefs.GetInt(prefix + "VSync", 0) == 1;
        data.frameRate = PlayerPrefs.GetInt(prefix + "FrameRate", 120);
        data.muteInBackground = PlayerPrefs.GetInt(prefix + "MuteInBackground", 1) == 1;
        if (PlayerPrefs.HasKey(prefix + "BindingsV1"))
        {
            try { data.bindings = JsonUtility.FromJson<GameBindings>(PlayerPrefs.GetString(prefix + "BindingsV1")); }
            catch (ArgumentException) { data.bindings = GameControls.Defaults(); }
        }
        Apply(data, applyDisplay);
    }

    public static GameSettingsData Normalize(GameSettingsData source)
    {
        GameSettingsData data = (source ?? Defaults()).Clone();
        data.width = Mathf.Clamp(data.width, 640, 7680);
        data.height = Mathf.Clamp(data.height, 480, 4320);
        if (data.windowMode != FullScreenMode.Windowed && data.windowMode != FullScreenMode.FullScreenWindow &&
            data.windowMode != FullScreenMode.ExclusiveFullScreen) data.windowMode = FullScreenMode.FullScreenWindow;
        data.masterVolume = Clamp(data.masterVolume, 0f, 1f, 1f);
        data.musicVolume = Clamp(data.musicVolume, 0f, 1f, 1f);
        data.effectsVolume = Clamp(data.effectsVolume, 0f, 1f, 1f);
        data.sensitivity = Clamp(data.sensitivity, .2f, 3f, 1f);
        data.fieldOfView = Clamp(data.fieldOfView, 55f, 110f, 60f);
        if (Array.IndexOf(FrameRates, data.frameRate) < 0) data.frameRate = 120;
        data.bindings = GameControls.Normalize(data.bindings);
        return data;
    }

    static float Clamp(float value, float minimum, float maximum, float fallback) =>
        float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);

    public static bool DisplayChanged(GameSettingsData a, GameSettingsData b) =>
        a.width != b.width || a.height != b.height || a.windowMode != b.windowMode;

    public static void Apply(GameSettingsData data, bool applyDisplay = true)
    {
        current = Normalize(data);
        AudioListener.volume = current.masterVolume;
        QualitySettings.vSyncCount = current.vSync ? 1 : 0;
        Application.targetFrameRate = current.vSync ? -1 : current.frameRate;
        // Screen.SetResolution controls the standalone window, not the Unity Editor Game view.
        if (applyDisplay && !Application.isEditor)
            Screen.SetResolution(current.width, current.height, current.windowMode);
        Changed?.Invoke();
    }

    public static void Save()
    {
        GameSettingsData data = Current;
        PlayerPrefs.SetInt(prefix + "Width", data.width);
        PlayerPrefs.SetInt(prefix + "Height", data.height);
        PlayerPrefs.SetInt(prefix + "WindowMode", (int)data.windowMode);
        PlayerPrefs.SetInt(prefix + "Fullscreen", data.windowMode == FullScreenMode.Windowed ? 0 : 1);
        PlayerPrefs.SetFloat(prefix + "MasterVolume", data.masterVolume);
        PlayerPrefs.SetFloat(prefix + "MusicVolume", data.musicVolume);
        PlayerPrefs.SetFloat(prefix + "EffectsVolume", data.effectsVolume);
        PlayerPrefs.SetFloat(prefix + "MouseSensitivity", data.sensitivity);
        PlayerPrefs.SetFloat(prefix + "FieldOfView", data.fieldOfView);
        PlayerPrefs.SetInt(prefix + "InvertY", data.invertY ? 1 : 0);
        PlayerPrefs.SetInt(prefix + "VSync", data.vSync ? 1 : 0);
        PlayerPrefs.SetInt(prefix + "FrameRate", data.frameRate);
        PlayerPrefs.SetInt(prefix + "MuteInBackground", data.muteInBackground ? 1 : 0);
        PlayerPrefs.SetString(prefix + "BindingsV1", JsonUtility.ToJson(data.bindings));
        PlayerPrefs.Save();
    }

    public static List<Vector2Int> Resolutions()
    {
        var sizes = new List<Vector2Int>();
        Resolution desktop = Screen.currentResolution;
        foreach (Vector2Int size in new[] { new Vector2Int(800, 600), new Vector2Int(1024, 768),
            new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
            if (size.x <= desktop.width && size.y <= desktop.height) sizes.Add(size);
        foreach (Resolution resolution in Screen.resolutions)
        {
            var size = new Vector2Int(resolution.width, resolution.height);
            if (size.x >= 640 && size.y >= 480 && !sizes.Contains(size)) sizes.Add(size);
        }
        var currentSize = new Vector2Int(Current.width, Current.height);
        if (!sizes.Contains(currentSize)) sizes.Add(currentSize);
        sizes.Sort((a, b) => a.x == b.x ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        return sizes;
    }

    public static Vector2 ReadMouseLook(float baseSensitivity)
    {
        if (Time.timeScale <= 0f || !Application.isFocused || Cursor.lockState != CursorLockMode.Locked || DungeonVictoryUI.IsOpen)
            return Vector2.zero;
        // Mouse axes already describe frame displacement. A fixed baseline preserves the old 60-FPS feel.
        return ScaleMouseDelta(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")), baseSensitivity);
    }

    public static Vector2 ScaleMouseDelta(Vector2 frameDelta, float baseSensitivity)
    {
        float gain = baseSensitivity * Current.sensitivity / 60f;
        return new Vector2(frameDelta.x * gain, frameDelta.y * gain * (Current.invertY ? -1f : 1f));
    }
}
