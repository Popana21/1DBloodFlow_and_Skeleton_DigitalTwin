using UnityEngine;

public class HeartbeatAudioController : MonoBehaviour
{
    public enum PlaybackMode
    {
        CombinedClip,
        SeparateLubDub
    }

    [Header("References")]
    public HeartbeatController heartbeatController;
    public AudioSource audioSourceA;
    public AudioSource audioSourceB;

    [Header("Playback")]
    public PlaybackMode playbackMode = PlaybackMode.CombinedClip;

    [Header("Clips")]
    public AudioClip heartbeatClip;
    public AudioClip lubClip;
    public AudioClip dubClip;

    [Header("Timing")]
    [Range(0.05f, 0.45f)]
    public float dubDelayFractionOfCycle = 0.22f; // fraction of one heartbeat cycle
    public double scheduleLeadTime = 0.08;        // seconds
    public double scheduleWindow = 0.25;          // seconds

    [Header("Volume")]
    [Range(0f, 1f)] public float lubVolume = 0.8f;
    [Range(0f, 1f)] public float dubVolume = 0.7f;
    [Range(0f, 1f)] public float masterVolume = 1f;

    [Header("Debug")]
    public bool mute = false;

    private double _nextLubDspTime;
    private bool _initialized;

    void Start()
    {
        if (heartbeatController == null)
            heartbeatController = FindAnyObjectByType<HeartbeatController>();

        EnsureAudioSources();
        InitializeSchedule();
    }

    void Update()
    {
        if (mute || heartbeatController == null || !HasRequiredClips())
            return;

        double now = AudioSettings.dspTime;
        double cycle = GetCycleDuration();
        if (cycle <= 1e-6) return;

        if (!_initialized)
            InitializeSchedule();

        // Keep a short DSP queue filled ahead of time to avoid audible timing jitter.
        while (_nextLubDspTime < now + scheduleWindow)
        {
            ScheduleBeat(_nextLubDspTime, cycle);
            _nextLubDspTime += cycle;
        }
    }

    void EnsureAudioSources()
    {
        if (audioSourceA == null)
            audioSourceA = gameObject.AddComponent<AudioSource>();
        if (audioSourceB == null)
            audioSourceB = gameObject.AddComponent<AudioSource>();

        ConfigureSource(audioSourceA);
        ConfigureSource(audioSourceB);
    }

    void ConfigureSource(AudioSource src)
    {
        if (src == null) return;
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f; // 2D background sound
    }

    void InitializeSchedule()
    {
        _nextLubDspTime = AudioSettings.dspTime + scheduleLeadTime;
        _initialized = true;
    }

    bool HasRequiredClips()
    {
        if (playbackMode == PlaybackMode.CombinedClip)
            return heartbeatClip != null;

        return lubClip != null && dubClip != null;
    }

    void ScheduleBeat(double lubTime, double cycleDuration)
    {
        if (playbackMode == PlaybackMode.CombinedClip)
        {
            ScheduleClip(audioSourceA, heartbeatClip, lubTime, 1f);
            return;
        }

        double dubTime = lubTime + cycleDuration * dubDelayFractionOfCycle;

        // Alternate sources so overlapping scheduled clips do not interrupt one another.
        ScheduleClip(audioSourceA, lubClip, lubTime, lubVolume);
        ScheduleClip(audioSourceB, dubClip, dubTime, dubVolume);
    }

    void ScheduleClip(AudioSource src, AudioClip clip, double dspTime, float volume)
    {
        if (src == null || clip == null) return;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume * masterVolume);
        src.PlayScheduled(dspTime);
    }

    double GetCycleDuration()
    {
        float bpm = Mathf.Max(1f, heartbeatController != null ? heartbeatController.bpm : 60f);
        return 60.0 / bpm;
    }

    public void RestartSync()
    {
        _initialized = false;
        InitializeSchedule();
    }

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
    }

    public void SetMuted(bool value)
    {
        mute = value;
        if (mute)
        {
            if (audioSourceA != null) audioSourceA.Stop();
            if (audioSourceB != null) audioSourceB.Stop();
            _initialized = false;
        }
        else
        {
            RestartSync();
        }
    }
}
