using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>채보 편집기 창: 자동 생성</summary>
public partial class ChartEditorWindow
{
    // ───────────────────────── 자동 생성 ─────────────────────────

    // ───────────────────────── BPM 자동 감지 ─────────────────────────

    void RequestDetectBpm()
    {
        if (project == null || project.clip == null || analysisTask != null) return;
        if (analysis != null && analysisClip == project.clip)
        {
            ShowDetected();
            return;
        }
        detectWhenReady = true;
        StartAnalysis();
    }

    void ShowDetected()
    {
        detectedFactor = 1;
        detectedTempo = null;
        if (analysis == null || analysis.tempo == null || analysis.tempo.Count == 0)
        {
            message = "박자를 찾지 못했습니다. 박이 뚜렷하지 않은 곡이면 탭 템포(T)로 맞추세요.";
            return;
        }
        detectedTempo = new List<TempoPoint>();
        foreach (TempoPoint p in analysis.tempo) detectedTempo.Add(new TempoPoint { time = p.time, bpm = p.bpm });
        message = "BPM 감지: " + DetectedText() + " — 'BPM · 격자'에서 확인하고 적용하세요.";
    }

    string DetectedText()
    {
        if (detectedTempo == null || detectedTempo.Count == 0) return "";
        string s = (detectedTempo[0].bpm * detectedFactor).ToString("0.##") + " BPM, 첫 박 " + ChartEditing.FormatTime(detectedTempo[0].time);
        if (detectedTempo.Count > 1) s += " (BPM 바뀜 " + (detectedTempo.Count - 1) + "번)";
        return s;
    }

    void ApplyDetected()
    {
        if (detectedTempo == null || detectedTempo.Count == 0) return;
        Undo.RecordObject(project, "BPM 자동 감지 적용");
        project.tempo.Clear();
        foreach (TempoPoint p in detectedTempo)
            project.tempo.Add(new TempoPoint { time = p.time, bpm = Math.Round(p.bpm * detectedFactor * 100) / 100 });
        EditorUtility.SetDirty(project);
        message = "BPM 적용: " + DetectedText() + ". 메트로놈을 켜고 맞는지 들어 보세요.";
        detectedTempo = null;
    }

    // ───────────────────────── 외부 분석 ─────────────────────────

    void LoadExternalAnalysis()
    {
        if (project == null || project.clip == null) return;
        string path = EditorUtility.OpenFilePanel("외부 분석 불러오기 (chart_analyze.py가 만든 .json)", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;
        ExternalAnalysisFile file;
        try
        {
            file = JsonUtility.FromJson<ExternalAnalysisFile>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            message = "외부 분석 파일을 읽지 못했습니다: " + e.Message;
            return;
        }
        if (file == null || file.onsets == null || file.onsets.Length == 0)
        {
            message = "외부 분석 파일에 타격 정보(onsets)가 없습니다.";
            return;
        }
        if (file.duration > 0 && Math.Abs(file.duration - project.clip.length) > 2)
        {
            if (!EditorUtility.DisplayDialog("외부 분석", "분석 파일의 곡 길이(" + ChartEditing.FormatTime(file.duration) + ")가 지금 곡(" +
                    ChartEditing.FormatTime(project.clip.length) + ")과 다릅니다. 다른 곡의 분석일 수 있습니다. 그래도 쓸까요?", "쓰기", "취소"))
                return;
        }
        analysisCancel = true;
        analysisTask = null;
        analysis = ChartAutoGenerator.FromExternal(file);
        analysisClip = project.clip;
        message = analysis.source + " 불러옴: 타격 " + analysis.onsets.Count + "개, 박 " + analysis.beats.Length + "개, 구간 " + analysis.sections.Count + "개";
    }

    void UseInternalAnalysis()
    {
        analysis = null;
        analysisClip = null;
        message = "내부 분석으로 되돌렸습니다. 다음 자동 생성 때 곡을 다시 분석합니다.";
    }

    // ───────────────────────── 자동 생성 ─────────────────────────

    void RequestGenerate()
    {
        if (project == null || project.clip == null || analysisTask != null) return;
        if (analysis != null && analysisClip == project.clip)
        {
            RunGenerate();
            return;
        }
        generateWhenReady = true;
        StartAnalysis();
    }

    void StartAnalysis()
    {
        AudioClip clip = project.clip;
        AudioImporter importer;
        if (!IsDecompressOnLoad(clip, out importer))
        {
            message = "곡의 Load Type을 Decompress On Load로 바꿔야 분석할 수 있습니다.";
            generateWhenReady = false;
            detectWhenReady = false;
            return;
        }
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            message = "곡 데이터를 아직 불러오는 중입니다. 잠시 뒤에 다시 누르세요.";
            generateWhenReady = false;
            detectWhenReady = false;
            return;
        }

        // 샘플 읽기는 Unity 메인 스레드에서(파형 때 읽은 것을 다시 씀), 분석은 백그라운드에서
        float[] mono = MonoSamples(clip);
        if (mono == null)
        {
            message = "곡 샘플을 읽지 못했습니다.";
            generateWhenReady = false;
            detectWhenReady = false;
            return;
        }

        int rate = clip.frequency;
        analysisCancel = false;
        analysisProgress = 0f;
        analysisTaskClip = clip;
        analysisTask = Task.Run(() => ChartAutoGenerator.Analyze(mono, rate, p => analysisProgress = p, () => analysisCancel));
        message = "곡 분석 중...";
    }

    void FinishAnalysis()
    {
        Task<OnsetAnalysis> task = analysisTask;
        analysisTask = null;

        if (task.IsFaulted || task.IsCanceled)
        {
            message = "분석에 실패했습니다: " + (task.Exception != null ? task.Exception.GetBaseException().Message : "취소");
            generateWhenReady = false;
            detectWhenReady = false;
            return;
        }
        if (task.Result == null)
        {
            message = "분석을 취소했습니다.";
            generateWhenReady = false;
            detectWhenReady = false;
            return;
        }

        analysis = task.Result;
        analysisClip = analysisTaskClip;
        message = "곡 분석 완료: " + analysis.bpm.ToString("0.##") + " BPM, 구간 " + analysis.sections.Count + "개";
        if (detectWhenReady && project != null && project.clip == analysisClip)
        {
            detectWhenReady = false;
            ShowDetected();
        }
        if (generateWhenReady && project != null && project.clip == analysisClip)
        {
            generateWhenReady = false;
            RunGenerate();
        }
    }

    void RunGenerate()
    {
        double len = audio.Length;
        double from = 0, to = len;
        if (autoRange == 1)
        {
            if (loopEnd <= loopStart + 0.05)
            {
                message = "구간 반복 범위가 없습니다. [ / ] 로 정하세요.";
                return;
            }
            from = loopStart;
            to = loopEnd;
        }
        else if (autoRange == 2)
        {
            from = audio.Time;
        }

        var settings = new AutoGenSettings
        {
            minGap = DifficultyGaps[Mathf.Clamp(autoDifficulty, 0, DifficultyGaps.Length - 1)],
            maxNps = DifficultyNps[Mathf.Clamp(autoDifficulty, 0, DifficultyNps.Length - 1)],
            preferBeats = autoPreferBeats,
            sectionDensity = autoSectionDensity,
            makeChords = autoChords,
            maxJack = autoMaxJack,
            sensitivity = autoSensitivity,
            laneMode = autoLane,
            makeLongs = autoLongs,
            minLongLength = autoMinLong,
            makeBarrages = autoBarrages,
            minBarrageCount = autoMinBarrage,
            snapToGrid = autoSnap && snapDivision > 0,
            seed = Environment.TickCount
        };
        List<NoteData> made = ChartAutoGenerator.Generate(analysis, settings, from, to, project.tempo, snapDivision);

        int existing = 0;
        foreach (NoteData n in project.notes) if (n.time >= from && n.time <= to) existing++;
        if (existing > 0 && !EditorUtility.DisplayDialog("자동 생성",
                ChartEditing.FormatTime(from) + " ~ " + ChartEditing.FormatTime(to) + " 안의 노트 " + existing + "개를 지우고 " + made.Count + "개를 새로 만듭니다.\nCtrl+Z로 되돌릴 수 있습니다.",
                "만들기", "취소"))
            return;

        Undo.RecordObject(project, "자동 생성");
        project.notes.RemoveAll(n => n.time >= from && n.time <= to);
        project.notes.AddRange(made);
        ChartEditing.Sort(project.notes);
        EditorUtility.SetDirty(project);
        selection.Clear();
        selection.AddRange(made);

        int longs = 0, bars = 0, chords = 0;
        var times = new HashSet<double>();
        foreach (NoteData n in made)
        {
            if (n.type == NoteType.Long) longs++;
            else if (n.type == NoteType.Barrage) bars++;
            if (!times.Add(n.time)) chords++;
        }
        message = "자동 생성 " + made.Count + "개 (롱노트 " + longs + ", 연타 " + bars + ", 동시치기 " + chords + ") · " + analysis.source;
        Repaint();
    }

    /// <summary>곡의 모노 샘플. 같은 곡이면 한 번 읽은 것을 다시 쓴다. 못 읽으면 null.</summary>
    float[] MonoSamples(AudioClip clip)
    {
        if (clip == null) return null;
        if (monoClip == clip && monoSamples != null) return monoSamples;
        monoSamples = EditorAudio.ReadMono(clip);
        monoClip = monoSamples != null ? clip : null;
        return monoSamples;
    }

    /// <summary>파형 블록 i0~i1(포함)의 최대 크기.</summary>
    float PeakBetween(int i0, int i1)
    {
        float peak = 0f;
        for (int i = Math.Max(0, i0); i <= i1 && i < wavePeaks.Length; i++) peak = Math.Max(peak, wavePeaks[i]);
        return peak;
    }
}
