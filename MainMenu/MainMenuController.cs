using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    private const string VolumeKey = "Settings.MasterVolume";
    private const string FullscreenKey = "Settings.Fullscreen";
    private const string ResolutionKey = "Settings.Resolution";

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Map1";

    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Settings UI")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Dropdown resolutionDropdown;

    private readonly List<Resolution> availableResolutions = new List<Resolution>();

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Start()
    {
        BuildResolutionOptions();

        volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(VolumeKey, 1f));
        fullscreenToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1);
        resolutionDropdown.SetValueWithoutNotify(
            Mathf.Clamp(PlayerPrefs.GetInt(ResolutionKey, FindCurrentResolution()), 0, availableResolutions.Count - 1));

        ApplySettings(false);
        ShowMainMenu();
    }

    public void JoinGame()
    {
        if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            Debug.LogError($"Cannot load '{gameSceneName}'. Add it to Build Settings.");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    public void ShowSettings()
    {
        mainPanel.SetActive(false);
        settingsPanel.SetActive(true);
    }

    public void ShowMainMenu()
    {
        settingsPanel.SetActive(false);
        mainPanel.SetActive(true);
    }

    public void SaveSettings()
    {
        ApplySettings(true);
        ShowMainMenu();
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void BuildResolutionOptions()
    {
        availableResolutions.Clear();
        resolutionDropdown.ClearOptions();

        var labels = new List<string>();
        var seen = new HashSet<string>();
        foreach (Resolution resolution in Screen.resolutions)
        {
            string key = $"{resolution.width}x{resolution.height}";
            if (!seen.Add(key))
                continue;

            availableResolutions.Add(resolution);
            labels.Add($"{resolution.width} × {resolution.height}");
        }

        if (availableResolutions.Count == 0)
        {
            availableResolutions.Add(Screen.currentResolution);
            labels.Add($"{Screen.width} × {Screen.height}");
        }

        resolutionDropdown.AddOptions(labels);
    }

    private int FindCurrentResolution()
    {
        for (int resolutionIndex = 0; resolutionIndex < availableResolutions.Count; resolutionIndex++)
        {
            if (availableResolutions[resolutionIndex].width == Screen.width &&
                availableResolutions[resolutionIndex].height == Screen.height)
                return resolutionIndex;
        }

        return availableResolutions.Count - 1;
    }

    private void ApplySettings(bool save)
    {
        float volume = volumeSlider.value;
        bool fullscreen = fullscreenToggle.isOn;
        int resolutionIndex = Mathf.Clamp(resolutionDropdown.value, 0, availableResolutions.Count - 1);
        Resolution resolution = availableResolutions[resolutionIndex];

        AudioListener.volume = volume;
        Screen.SetResolution(resolution.width, resolution.height, fullscreen);

        if (!save)
            return;

        PlayerPrefs.SetFloat(VolumeKey, volume);
        PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(ResolutionKey, resolutionIndex);
        PlayerPrefs.Save();
    }
}
