using System.Collections.Generic;
using UnityEngine;

public enum MusicTheme { None, Menu, Dungeon, Boss1, Boss2, Boss3 }

// Two dedicated voices keep music independent of the transient sound-effect pool.
public sealed class GameMusic : MonoBehaviour
{
    [Range(0f, 1f)] public float musicVolume = .32f;
    [Min(.1f)] public float crossfadeSeconds = 1.8f;
    public MusicTheme CurrentTheme { get; private set; }
    readonly AudioSource[] sources = new AudioSource[2];
    readonly MusicTheme[] themes = new MusicTheme[2];
    readonly float[] levels = new float[2];
    readonly Dictionary<MusicTheme, AudioClip> clips = new Dictionary<MusicTheme, AudioClip>();
    readonly HashSet<MusicTheme> missing = new HashSet<MusicTheme>();
    MusicTheme desired;
    int target = -1;
    bool suspended;
    float duck = 1f, nextContextCheck;

    void Awake()
    {
        for (int i = 0; i < sources.Length; i++)
        {
            var node = new GameObject("背景音樂 " + (i + 1));
            node.transform.SetParent(transform, false);
            sources[i] = node.AddComponent<AudioSource>();
            sources[i].playOnAwake = false;
            sources[i].loop = true;
            sources[i].spatialBlend = 0f;
            sources[i].dopplerLevel = 0f;
            sources[i].priority = 180;
            sources[i].ignoreListenerPause = true;
            sources[i].volume = 0f;
        }
    }

    public void RefreshContext(Player player)
    {
        desired = SelectTheme(player);
        nextContextCheck = Time.unscaledTime + .25f;
    }

    public static MusicTheme SelectTheme(Player player)
    {
        if (player == null)
            return Object.FindFirstObjectByType<MainMenuController>() != null ||
                Object.FindFirstObjectByType<LoginSceneController>() != null ? MusicTheme.Menu : MusicTheme.None;
        PlayerHealth playerHealth = player.GetComponent<PlayerHealth>();
        if (playerHealth != null && playerHealth.IsDead) return MusicTheme.Dungeon;
        foreach (BossController boss in Object.FindObjectsByType<BossController>(FindObjectsSortMode.None))
        {
            EnemyHealth health = boss.GetComponent<EnemyHealth>();
            if (!boss.isActiveAndEnabled || health == null || health.IsDead) continue;
            EnemyAI ai = boss.GetComponent<EnemyAI>();
            bool inEncounter = ai != null && ai.HomeRoom != null ? ai.HomeRoom.Contains(player.transform.position) :
                Mathf.Abs(boss.transform.position.y - player.transform.position.y) < 6f &&
                (boss.transform.position - player.transform.position).sqrMagnitude <= 32f * 32f;
            if (inEncounter) return boss.Phase == 1 ? MusicTheme.Boss1 : boss.Phase == 2 ? MusicTheme.Boss2 : MusicTheme.Boss3;
        }
        return MusicTheme.Dungeon;
    }

    public void Tick(Player player, bool unfocused)
    {
        if (unfocused != suspended)
        {
            suspended = unfocused;
            foreach (AudioSource source in sources)
                if (suspended) source.Pause(); else source.UnPause();
        }
        if (suspended) return;
        if (Time.unscaledTime >= nextContextCheck) RefreshContext(player);
        SelectTrack();
        PlayerHealth health = player != null ? player.GetComponent<PlayerHealth>() : null;
        float duckTarget = health != null && health.IsDead ? .18f :
            Time.timeScale <= 0f ? .35f : 1f;
        duck = Mathf.MoveTowards(duck, duckTarget, Time.unscaledDeltaTime * 1.8f);
        for (int i = 0; i < sources.Length; i++)
        {
            levels[i] = Mathf.MoveTowards(levels[i], i == target ? 1f : 0f,
                Time.unscaledDeltaTime / Mathf.Max(.1f, crossfadeSeconds));
            sources[i].volume = levels[i] * Mathf.Clamp01(musicVolume) * GameSettings.Current.musicVolume * duck;
            if (i != target && levels[i] <= 0f && themes[i] != MusicTheme.None)
            {
                sources[i].Stop();
                sources[i].clip = null;
                themes[i] = MusicTheme.None;
            }
        }
    }

    void SelectTrack()
    {
        if (desired == MusicTheme.None) { target = -1; CurrentTheme = MusicTheme.None; return; }
        for (int i = 0; i < sources.Length; i++)
            if (themes[i] == desired) { target = i; CurrentTheme = desired; return; }

        // Finish a fade before replacing a third track. Rapid phase/scene changes cannot cut a playing voice.
        int free = levels[0] <= 0f ? 0 : levels[1] <= 0f ? 1 : -1;
        if (free < 0) return;
        if (!clips.TryGetValue(desired, out AudioClip clip))
        {
            if (missing.Contains(desired)) return;
            clip = Resources.Load<AudioClip>("Music/" + desired);
            if (clip == null)
            {
                missing.Add(desired);
                Debug.LogWarning("找不到背景音樂：" + desired, this);
                return;
            }
            clips.Add(desired, clip);
        }
        sources[free].Stop();
        sources[free].clip = clip;
        sources[free].volume = 0f;
        sources[free].Play();
        themes[free] = desired;
        CurrentTheme = desired;
        target = free;
    }
}
