using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 에디터(Play 모드가 아닐 때)에서 곡을 재생한다.
/// 씬에 보이지 않는 임시 오브젝트에 AudioSource를 붙여서 쓰고, 창이 닫히면 지운다.
/// 메트로놈·노트 클릭음은 오디오 시계(dspTime)에 예약해서 박자에 정확히 맞춘다.
/// </summary>
public class EditorAudio : IDisposable
{
    public enum ClickKind { Accent, Beat, Note }

    const int ClickVoices = 12;

    GameObject host;
    AudioSource source;
    AudioSource[] clickSources;
    AudioClip accentClip, beatClip, noteClip;
    int nextVoice;

    AudioClip clip;
    double pausedTime;
    bool playing;

    // 곡 시간 → 오디오 시계 변환 (dspTime = 곡 시간 + dspOffset)
    double dspOffset;
    bool haveOffset;

    // 부드러운 곡 시계: 오디오 위치는 버퍼 단위로 뚝뚝 움직이므로 실제 시간으로 사이를 채운다
    double smoothSong, smoothReal;
    bool smoothValid;

    public AudioClip Clip { get { return clip; } }
    public bool IsPlaying { get { return playing; } }
    public double Length { get { return clip != null ? clip.length : 0; } }

    /// <summary>클릭음 크기 0~1.</summary>
    public float clickVolume = 0.8f;

    /// <summary>현재 재생 위치(초).</summary>
    public double Time
    {
        get
        {
            if (playing && source != null && clip != null) return (double)source.timeSamples / clip.frequency;
            return pausedTime;
        }
    }

    /// <summary>
    /// 부드럽게 흐르는 현재 재생 위치(초). 화면 스크롤과 키 입력 시각에 쓴다.
    /// 재생 중이 아니면 Time과 같다.
    /// </summary>
    public double SmoothTime
    {
        get
        {
            if (!playing || !smoothValid) return Time;
            return smoothSong + (EditorApplication.timeSinceStartup - smoothReal);
        }
    }

    /// <summary>재생 중이고 곡 시간과 오디오 시계의 관계를 알고 있어서 클릭음을 예약할 수 있는지.</summary>
    public bool CanSchedule { get { return playing && haveOffset; } }

    public void SetClip(AudioClip newClip)
    {
        Pause();
        clip = newClip;
        pausedTime = 0;
        if (source != null) source.clip = clip;
    }

    public void Play()
    {
        if (clip == null) return;
        EnsureSource();
        if (pausedTime >= clip.length - 0.01) pausedTime = 0;

        int sample = ToSample(pausedTime);
        source.clip = clip;
        source.timeSamples = sample;
        source.Play();
        source.timeSamples = sample;
        playing = true;
        haveOffset = false;
        smoothValid = false;
    }

    public void Pause()
    {
        if (!playing) return;
        pausedTime = Time;
        if (source != null) source.Stop();
        StopClicks();
        playing = false;
    }

    public void Toggle()
    {
        if (playing) Pause(); else Play();
    }

    public void Seek(double time)
    {
        pausedTime = Math.Max(0, Math.Min(Length, time));
        if (playing && source != null)
        {
            source.timeSamples = ToSample(pausedTime);
            StopClicks();
            haveOffset = false;
            smoothValid = false;
        }
    }

    /// <summary>매 에디터 프레임 호출. 화면을 다시 그려야 하면 true.</summary>
    public bool Tick()
    {
        if (!playing) return false;
        if (source == null || !source.isPlaying)
        {
            playing = false;          // 곡 끝까지 재생됨
            pausedTime = Length;
            StopClicks();
            return true;
        }

        // 부드러운 곡 시계: 실제 시간으로 예측하고, 오디오 위치 쪽으로 조금씩 당긴다
        double real = EditorApplication.timeSinceStartup;
        double actual = Time;
        if (!smoothValid)
        {
            smoothSong = actual;
            smoothReal = real;
            smoothValid = true;
        }
        else
        {
            double predicted = smoothSong + (real - smoothReal);
            double err = actual - predicted;
            predicted = Math.Abs(err) > 0.08 ? actual : predicted + err * 0.05;
            smoothSong = predicted;
            smoothReal = real;
        }

        // 곡 시간과 오디오 시계의 차이를 부드럽게 추정한다 (둘 다 오디오 버퍼 단위로 움직인다)
        double estimate = AudioSettings.dspTime - Time;
        if (!haveOffset || Math.Abs(estimate - dspOffset) > 0.25)
        {
            dspOffset = estimate;
            haveOffset = true;
        }
        else
        {
            dspOffset += (estimate - dspOffset) * 0.1;
        }
        return true;
    }

    /// <summary>곡 시간 songTime에 클릭음을 예약한다. 이미 지난 시각이면 무시한다.</summary>
    public void ScheduleClick(double songTime, ClickKind kind)
    {
        if (!CanSchedule) return;
        double dsp = songTime + dspOffset;
        if (dsp < AudioSettings.dspTime) return;

        EnsureClicks();
        AudioSource voice = clickSources[nextVoice];
        nextVoice = (nextVoice + 1) % clickSources.Length;
        voice.Stop();
        voice.clip = kind == ClickKind.Accent ? accentClip : kind == ClickKind.Beat ? beatClip : noteClip;
        voice.volume = clickVolume;
        voice.PlayScheduled(dsp);
    }

    /// <summary>예약된 클릭음을 모두 취소한다.</summary>
    public void StopClicks()
    {
        if (clickSources == null) return;
        foreach (AudioSource s in clickSources)
            if (s != null) s.Stop();
    }

    public void Dispose()
    {
        playing = false;
        if (host != null) UnityEngine.Object.DestroyImmediate(host);
        if (accentClip != null) UnityEngine.Object.DestroyImmediate(accentClip);
        if (beatClip != null) UnityEngine.Object.DestroyImmediate(beatClip);
        if (noteClip != null) UnityEngine.Object.DestroyImmediate(noteClip);
        host = null;
        source = null;
        clickSources = null;
        accentClip = beatClip = noteClip = null;
    }

    int ToSample(double time)
    {
        if (clip == null) return 0;
        return Math.Max(0, Math.Min(clip.samples - 1, (int)(time * clip.frequency)));
    }

    void EnsureSource()
    {
        if (source != null) return;
        host = EditorUtility.CreateGameObjectWithHideFlags("ChartEditorAudio", HideFlags.HideAndDontSave, typeof(AudioSource));
        source = host.GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
    }

    void EnsureClicks()
    {
        EnsureSource();
        if (clickSources != null) return;

        clickSources = new AudioSource[ClickVoices];
        for (int i = 0; i < ClickVoices; i++)
        {
            AudioSource s = host.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 0f;
            clickSources[i] = s;
        }
        accentClip = MakeClick("Accent", 1760f, 0.05f, 1f);
        beatClip = MakeClick("Beat", 1175f, 0.04f, 0.7f);
        noteClip = MakeClick("Note", 2400f, 0.03f, 0.6f);
    }

    /// <summary>
    /// 곡 전체를 모노 샘플(-1~1)로 읽는다. 곡의 Load Type이 Decompress On Load이고 데이터가 불러와져 있어야 한다.
    /// 못 읽으면 null. (Unity 메인 스레드에서만 부를 수 있다)
    /// </summary>
    public static float[] ReadMono(AudioClip clip)
    {
        if (clip == null) return null;
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        if (clip.loadState != AudioDataLoadState.Loaded) return null;

        int channels = Math.Max(1, clip.channels);
        int frames = clip.samples;
        var mono = new float[frames];
        const int chunkFrames = 65536;
        var buffer = new float[chunkFrames * channels];
        for (int offset = 0; offset < frames; offset += chunkFrames)
        {
            int n = Math.Min(chunkFrames, frames - offset);
            float[] data = n == chunkFrames ? buffer : new float[n * channels];
            if (!clip.GetData(data, offset)) return null;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += data[i * channels + c];
                mono[offset + i] = sum / channels;
            }
        }
        return mono;
    }

    static AudioClip MakeClick(string name, float freq, float seconds, float volume)
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
        int n = Math.Max(1, (int)(rate * seconds));
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 90f) * volume;
        }
        AudioClip c = AudioClip.Create("ChartEditorClick" + name, n, 1, rate, false);
        c.SetData(data, 0);
        c.hideFlags = HideFlags.HideAndDontSave;
        return c;
    }
}
