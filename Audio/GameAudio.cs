using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(-1000)]
public sealed class GameAudio : MonoBehaviour
{
    const int UIChannels = 8, WorldChannels = 24;
    static GameAudio instance;
    static bool quitting;
    sealed class Voice
    {
        public AudioSource source;
        public bool paused;
        public float gain;
        public float started;
    }
    readonly Dictionary<GameSound, AudioClip> clips = new Dictionary<GameSound, AudioClip>();
    readonly Dictionary<GameSound, float> lastPlayed = new Dictionary<GameSound, float>();
    readonly Voice[] voices = new Voice[UIChannels + WorldChannels];
    readonly System.Random pitchRandom = new System.Random(91027);
    AudioSource ambience;
    float ambienceGain;
    GameMusic music;
    AudioListener fallbackListener;
    Player player;
    bool focused = true;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; quitting = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap() { Ensure(); }

    static GameAudio Ensure()
    {
        if (quitting || !Application.isPlaying) return null;
        if (instance == null) new GameObject("遊戲音效").AddComponent<GameAudio>();
        return instance;
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        AudioListener.volume = GameSettings.Current.masterVolume;
        foreach (GameSound cue in Enum.GetValues(typeof(GameSound))) clips.Add(cue, GameSoundBank.Create(cue));
        for (int i = 0; i < voices.Length; i++)
        {
            var node = new GameObject(i < UIChannels ? "介面音效" : "場景音效");
            node.transform.SetParent(transform, false);
            var source = node.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.ignoreListenerPause = i < UIChannels;
            source.dopplerLevel = 0f;
            source.minDistance = 3f;
            source.maxDistance = 38f;
            source.rolloffMode = AudioRolloffMode.Linear;
            voices[i] = new Voice { source = source };
        }
        ambience = gameObject.AddComponent<AudioSource>();
        ambience.playOnAwake = false;
        ambience.loop = true;
        ambience.clip = clips[GameSound.Ambience];
        ambience.volume = 0f;
        var listenerObject = new GameObject("備用音效接收器");
        listenerObject.transform.SetParent(transform, false);
        fallbackListener = listenerObject.AddComponent<AudioListener>();
        fallbackListener.enabled = false;
        music = gameObject.AddComponent<GameMusic>();
        SceneManager.sceneLoaded += SceneLoaded;
    }

    public static void UI(GameSound cue, float gain = 1f) { Ensure()?.Emit(cue, Vector3.zero, false, true, gain); }
    public static void Local(GameSound cue, float gain = 1f) { Ensure()?.Emit(cue, Vector3.zero, false, false, gain); }
    public static void At(GameSound cue, Vector3 position, float gain = 1f) { Ensure()?.Emit(cue, position, true, false, gain); }
    public static void Shot(WeaponType type)
    {
        if (CharacterWeapons.IsCharacterWeapon(type))
        {
            Local(CharacterWeapons.IsMelee(type) ? GameSound.Sword :
                type == WeaponType.Longbow || type == WeaponType.RepeatingCrossbow ? GameSound.Crossbow : GameSound.MagicCast);
            return;
        }
        switch (type)
        {
            case WeaponType.SubmachineGun: Local(GameSound.SubmachineGun); break;
            case WeaponType.Shotgun: Local(GameSound.Shotgun); break;
            case WeaponType.RPG: Local(GameSound.RPG); break;
            case WeaponType.Pistol: Local(GameSound.Pistol); break;
            case WeaponType.SniperRifle: Local(GameSound.SniperRifle); break;
            case WeaponType.LightMachineGun: Local(GameSound.MachineGun); break;
            default: Local(GameSound.Rifle); break;
        }
    }

    void Emit(GameSound cue, Vector3 position, bool spatial, bool ui, float gain)
    {
        if ((!focused && GameSettings.Current.muteInBackground && !Application.isBatchMode) || (!ui && Time.timeScale <= 0f) || gain <= 0f) return;
        float now = Time.unscaledTime;
        float cooldown = cue == GameSound.Hover ? .09f : cue == GameSound.Experience || cue == GameSound.Pickup ? .07f :
            cue == GameSound.Heal ? .3f : cue == GameSound.Denied ? .15f : .025f;
        if (lastPlayed.TryGetValue(cue, out float last) && now - last < cooldown) return;
        if (spatial && fallbackListener != null)
        {
            Vector3 listenerPosition = player != null ? player.transform.position : fallbackListener.transform.position;
            if ((position - listenerPosition).sqrMagnitude > 45f * 45f) return;
        }
        lastPlayed[cue] = now;
        int start = ui ? 0 : UIChannels, end = ui ? UIChannels : voices.Length;
        Voice chosen = voices[start];
        for (int i = start; i < end; i++)
        {
            Voice voice = voices[i];
            if (!voice.source.isPlaying && !voice.paused) { chosen = voice; break; }
            if (voice.started < chosen.started) chosen = voice;
        }
        chosen.source.Stop();
        chosen.paused = false;
        chosen.started = now;
        chosen.gain = GameSoundBank.Gain(cue) * Mathf.Clamp01(gain);
        chosen.source.volume = chosen.gain * GameSettings.Current.effectsVolume;
        chosen.source.spatialBlend = spatial ? 1f : 0f;
        chosen.source.transform.position = spatial ? position : transform.position;
        chosen.source.pitch = ui ? 1f : .96f + (float)pitchRandom.NextDouble() * .08f;
        chosen.source.clip = clips[cue];
        chosen.source.Play();
    }

    void Update()
    {
        bool unfocused = !focused && GameSettings.Current.muteInBackground && !Application.isBatchMode;
        int playing = 0;
        foreach (Voice voice in voices) if (voice != null && voice.source.isPlaying) playing++;
        float mixGain = 1f / Mathf.Max(1f, Mathf.Sqrt(playing / 4f));
        for (int i = 0; i < voices.Length; i++)
        {
            Voice voice = voices[i];
            if (voice == null) continue;
            bool pause = unfocused || (i >= UIChannels && Time.timeScale <= 0f);
            if (pause && !voice.paused && voice.source.isPlaying) { voice.source.Pause(); voice.paused = true; }
            else if (!pause && voice.paused) { voice.source.UnPause(); voice.paused = false; }
            voice.source.volume = voice.gain * mixGain * GameSettings.Current.effectsVolume;
        }
        float ambienceVolume = player != null && Time.timeScale > 0f && !unfocused ? .12f : 0f;
        ambienceGain = Mathf.MoveTowards(ambienceGain, ambienceVolume, Time.unscaledDeltaTime * .3f);
        ambience.volume = ambienceGain * GameSettings.Current.effectsVolume;
        if (ambience.volume > 0f && !ambience.isPlaying) ambience.Play();
        if (ambience.volume <= 0f && ambience.isPlaying) ambience.Stop();
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + .25f;
            ScanScene();
        }
        music.Tick(player, unfocused);
    }

    void LateUpdate()
    {
        Camera view = player != null && player.fpsCamera != null && player.fpsCamera.isActiveAndEnabled ? player.fpsCamera :
            player != null && player.tpsCamera != null && player.tpsCamera.isActiveAndEnabled ? player.tpsCamera : Camera.main;
        if (view != null) fallbackListener.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
        else if (player != null) fallbackListener.transform.position = player.transform.position;
    }

    void ScanScene()
    {
        if (player == null) player = FindFirstObjectByType<Player>();
        bool hasListener = false;
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener != fallbackListener && listener.isActiveAndEnabled) { hasListener = true; break; }
        fallbackListener.enabled = !hasListener;
        foreach (Selectable control in FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            if (control.GetComponent<GameUIAudio>() == null) control.gameObject.AddComponent<GameUIAudio>();
    }

    void SceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            for (int i = UIChannels; i < voices.Length; i++) { voices[i].source.Stop(); voices[i].paused = false; }
            ambience.Stop(); ambience.volume = ambienceGain = 0;
            player = null;
        }
        ScanScene();
        music.RefreshContext(player);
    }
    void OnApplicationFocus(bool value) { focused = value; }
    void OnApplicationQuit() { quitting = true; }
    void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= SceneLoaded;
        foreach (AudioClip clip in clips.Values) if (clip != null) Destroy(clip);
        instance = null;
    }
}

// Covers existing scene buttons and menus created dynamically at runtime.
public sealed class GameUIAudio : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, ISelectHandler, ISubmitHandler
{
    Selectable control;
    void Awake()
    {
        control = GetComponent<Selectable>();
        if (control is Slider slider) slider.onValueChanged.AddListener(SliderChanged);
        if (control is Toggle toggle) toggle.onValueChanged.AddListener(ToggleChanged);
        if (control is Dropdown dropdown) dropdown.onValueChanged.AddListener(DropdownChanged);
    }
    bool Available => control != null && control.IsActive() && control.IsInteractable();
    public void OnPointerEnter(PointerEventData data) { if (Available) GameAudio.UI(GameSound.Hover); }
    public void OnSelect(BaseEventData data) { if (Available) GameAudio.UI(GameSound.Hover); }
    public void OnPointerClick(PointerEventData data)
    {
        if (Available && data.button == PointerEventData.InputButton.Left && control is Button) GameAudio.UI(GameSound.Click);
    }
    public void OnSubmit(BaseEventData data) { if (Available && control is Button) GameAudio.UI(GameSound.Click); }
    void SliderChanged(float value) { if (Available) GameAudio.UI(GameSound.Hover); }
    void ToggleChanged(bool value) { if (Available) GameAudio.UI(GameSound.Click); }
    void DropdownChanged(int value) { if (Available) GameAudio.UI(GameSound.Click); }
    void OnDestroy()
    {
        if (control is Slider slider) slider.onValueChanged.RemoveListener(SliderChanged);
        if (control is Toggle toggle) toggle.onValueChanged.RemoveListener(ToggleChanged);
        if (control is Dropdown dropdown) dropdown.onValueChanged.RemoveListener(DropdownChanged);
    }
}

public sealed class CharacterMovementAudio : MonoBehaviour
{
    Player player;
    PlayerHealth health;
    EnemyHealth enemy;
    Vector3 lastPosition;
    float distance, nextHeartbeat;
    void Start()
    {
        player = GetComponent<Player>(); health = GetComponent<PlayerHealth>(); enemy = GetComponent<EnemyHealth>();
        lastPosition = transform.position;
    }
    void LateUpdate()
    {
        Vector3 delta = transform.position - lastPosition;
        lastPosition = transform.position;
        if (Time.timeScale <= 0f || (health != null && health.IsDead) || (enemy != null && enemy.IsDead)) { distance = 0; return; }
        if (delta.sqrMagnitude > 9f) { distance = 0; return; } // Teleport, not a footstep.
        if (player == null || (player.IsGrounded && !player.IsSliding))
        {
            delta.y = 0;
            distance += delta.magnitude;
            float stride = player != null && player.IsRunning ? 2.3f : 1.6f;
            if (distance >= stride)
            {
                distance %= stride;
                float gain = player != null && player.IsCrouching ? .45f : player != null && player.IsRunning ? 1f : .7f;
                if (player != null) GameAudio.Local(GameSound.Footstep, gain);
                else GameAudio.At(GameSound.Footstep, transform.position, .6f);
            }
        }
        else distance = 0;
        if (health != null && health.CurrentHealth / health.maxHealth <= .25f && Time.time >= nextHeartbeat)
        {
            nextHeartbeat = Time.time + 1.1f;
            GameAudio.Local(GameSound.LowHealth);
        }
    }
}
