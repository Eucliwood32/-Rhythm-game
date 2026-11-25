using System;
using UnityEngine;

/// <summary>
/// 곡을 재생하고, 게임 전체가 기준으로 삼을 "곡 시간"을 제공한다.
/// 곡 시간은 Time.time이 아니라 오디오 시스템의 dspTime을 기준으로 계산한다.
/// GameFlow가 있으면 GameFlow가 곡을 바꾸고 재생을 시작한다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    [Header("재생 설정")]
    [Tooltip("기본 곡. GameFlow의 스테이지에 곡이 지정되지 않으면 이 곡을 쓴다.")]
    [SerializeField] AudioClip clip;
    [Tooltip("GameFlow 없이 테스트할 때만 쓰인다. GameFlow가 있으면 자동으로 꺼진다.")]
    [SerializeField] bool playOnStart = true;

    [Tooltip("재생 예약 후 실제로 소리가 나기까지 대기 시간(초). 곡 시간이 이 값만큼 음수에서 시작한다. (카운트다운 길이)")]
    [SerializeField] double startDelay = 0.5;

    [Tooltip("곡 시간 자체를 미는 값(초). 노트 표시와 판정이 함께 밀린다. 보통 0으로 두고, 치는 타이밍 보정은 Judge의 입력 보정(- / = 키)을 쓴다.")]
    [SerializeField] double offsetSeconds = 0;

    AudioSource source;
    AudioClip defaultClip;  // Inspector에서 넣은 기본 곡
    double dspStartTime;   // 곡 시간 0초가 되는 dspTime 시각
    bool started;
    bool finished;

    /// <summary>곡이 끝까지 재생되면 한 번 발행된다.</summary>
    public event Action OnSongFinished;

    public bool IsStarted => started;
    public AudioClip Clip => clip;
    public double StartDelay => startDelay;

    /// <summary>GameFlow가 끈다.</summary>
    public bool PlayOnStart { get { return playOnStart; } set { playOnStart = value; } }

    /// <summary>
    /// 현재 곡 시간(초). 재생 시작 전 대기 구간에서는 음수, 소리가 나기 시작하는 순간 0.
    /// Play()를 호출하기 전에는 0을 반환한다.
    /// </summary>
    public double SongTime => started ? SmoothDspTime() - dspStartTime - offsetSeconds : 0;

    // AudioSettings.dspTime은 오디오 버퍼 단위로 한 번에 건너뛴다(설정에 따라 약 10~40ms마다). 그대로 쓰면
    // 곡 시간이 계단처럼 멈췄다 뛰어서, 같은 타이밍에 쳐도 판정 오차가 들쭉날쭉해진다.
    // 그래서 dspTime이 마지막으로 바뀐 뒤 흐른 실제 시간을 더해 부드럽게 이어 준다. dspTime이 바뀔 때마다 다시 맞추므로 어긋남이 쌓이지 않는다.
    double lastDsp = -1, lastDspRealtime, lastSmooth;
    double SmoothDspTime()
    {
        double dsp = AudioSettings.dspTime;
        double now = Time.realtimeSinceStartupAsDouble;   // 프레임과 상관없는 실제 경과 시간
        if (dsp != lastDsp) { lastDsp = dsp; lastDspRealtime = now; }   // dspTime이 새 값으로 뛰었으면 기준을 다시 잡는다.
        double smooth = lastDsp + (now - lastDspRealtime);
        if (smooth < lastSmooth) smooth = lastSmooth;   // 다시 맞출 때 아주 조금 뒤로 가는 일을 막는다.
        lastSmooth = smooth;
        return smooth;
    }

    /// <summary>현재 곡 시간(밀리초).</summary>
    public double SongTimeMs => SongTime * 1000.0;

    /// <summary>오디오가 실제로 재생 중인 위치(초). 곡 시간과 비교해 싱크를 확인하는 용도. 재생 중이 아니면 -1.</summary>
    public double AudioPosition => (source != null && source.isPlaying && clip != null)
        ? (double)source.timeSamples / clip.frequency : -1;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        defaultClip = clip;
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    void Update()
    {
        if (started && !finished && clip != null && SongTime >= clip.length)
        {
            finished = true;
            Debug.Log("[MusicPlayer] 곡 종료");
            OnSongFinished?.Invoke();
        }
    }

    /// <summary>곡을 바꾼다. null이면 Inspector에서 넣은 기본 곡으로 돌아간다.</summary>
    public void SetClip(AudioClip newClip)
    {
        clip = newClip != null ? newClip : defaultClip;
    }

    public void Play()
    {
        if (clip == null)
        {
            Debug.LogError("[MusicPlayer] clip이 비어 있습니다. Inspector에서 곡을 할당하세요.");
            return;
        }

        source.Stop();
        source.clip = clip;
        dspStartTime = AudioSettings.dspTime + startDelay;
        source.PlayScheduled(dspStartTime);

        started = true;
        finished = false;
        Debug.Log($"[MusicPlayer] 재생 예약: {clip.name} ({clip.length:F1}초)");
    }

    public void Stop()
    {
        source.Stop();
        started = false;
    }
}
