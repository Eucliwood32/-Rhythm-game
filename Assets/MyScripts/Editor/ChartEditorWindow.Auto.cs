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
            return;
        }
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            message = "곡 데이터를 아직 불러오는 중입니다. 잠시 뒤에 다시 누르세요.";
            generateWhenReady = false;
            return;
        }

        // 샘플 읽기는 Unity 메인 스레드에서(파형 때 읽은 것을 다시 씀), 분석은 백그라운드에서
        float[] mono = MonoSamples(clip);
        if (mono == null)
        {
            message = "곡 샘플을 읽지 못했습니다.";
            generateWhenReady = false;
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
            return;
        }
        if (task.Result == null)
        {
            message = "분석을 취소했습니다.";
            generateWhenReady = false;
            return;
        }

        analysis = task.Result;
        analysisClip = analysisTaskClip;
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

        int longs = 0, bars = 0;
        foreach (NoteData n in made)
        {
            if (n.type == NoteType.Long) longs++;
            else if (n.type == NoteType.Barrage) bars++;
        }
        message = "자동 생성 " + made.Count + "개 (롱노트 " + longs + ", 연타 " + bars + ")";
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
