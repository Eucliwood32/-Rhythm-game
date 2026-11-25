using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>채보 편집기 창: 채보 프로젝트, JSON 내보내기·불러오기, 곡 설정과 파형</summary>
public partial class ChartEditorWindow
{
    // ───────────────────────── 프로젝트와 파일 ─────────────────────────

    void SetProject(ChartProject p)
    {
        if (project != null) AssetDatabase.SaveAssetIfDirty(project);
        audio.Pause();
        project = p;
        selection.Clear();
        message = "";
        EditorPrefs.SetString(PrefLastProject, p != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)) : "");
        audio.SetClip(p != null ? p.clip : null);
        Repaint();
    }

    void CreateProject()
    {
        string path = EditorUtility.SaveFilePanelInProject("새 채보 프로젝트", "NewChart", "asset",
            "채보 프로젝트를 저장할 위치를 고르세요. (예: Assets/Charts)");
        if (string.IsNullOrEmpty(path)) return;

        var p = CreateInstance<ChartProject>();
        AssetDatabase.CreateAsset(p, path);
        AssetDatabase.SaveAssets();
        SetProject(p);
        message = "새 프로젝트: " + path;
    }

    void ExportJson()
    {
        if (project == null) return;
        string folder = (Path.GetDirectoryName(AssetDatabase.GetAssetPath(project)) ?? "Assets").Replace('\\', '/');
        string path = EditorUtility.SaveFilePanelInProject("채보 JSON 내보내기", project.name, "json",
            "Composer의 Chart Json 칸에 넣을 파일을 저장합니다.", folder);
        if (string.IsNullOrEmpty(path)) return;

        int serious = 0;
        foreach (ChartIssue i in issues) if (i.serious) serious++;
        if (serious > 0 && !EditorUtility.DisplayDialog("채보 내보내기",
                "게임에서 판정이 꼬일 수 있는 문제가 " + serious + "개 있습니다. (오른쪽 '채보 검사' 참고)\n그래도 내보낼까요?",
                "내보내기", "취소"))
            return;

        var data = new ChartData { notes = ChartEditing.CleanCopy(project.notes) };
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        AssetDatabase.ImportAsset(path);
        AssetDatabase.SaveAssetIfDirty(project);
        message = "내보냄: " + path + " (" + data.notes.Count + "개)";
        Repaint();
    }

    void ImportJson()
    {
        if (project == null) return;
        string path = EditorUtility.OpenFilePanel("채보 JSON 불러오기", Application.dataPath, "json");
        if (string.IsNullOrEmpty(path)) return;

        ChartData data = null;
        try
        {
            data = JsonUtility.FromJson<ChartData>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[채보 편집기] JSON을 읽지 못했습니다: " + ex.Message);
        }
        if (data == null || data.notes == null)
        {
            message = "채보 JSON을 읽지 못했습니다: " + Path.GetFileName(path);
            Repaint();
            return;
        }

        if (project.notes.Count > 0 && !EditorUtility.DisplayDialog("채보 불러오기",
                "지금 있는 노트 " + project.notes.Count + "개를 불러온 채보 " + data.notes.Count + "개로 바꿉니다.\nCtrl+Z로 되돌릴 수 있습니다.",
                "바꾸기", "취소"))
            return;

        Undo.RecordObject(project, "채보 불러오기");
        project.notes = ChartEditing.CleanCopy(data.notes);
        selection.Clear();
        EditorUtility.SetDirty(project);
        message = "불러옴: " + Path.GetFileName(path) + " (" + project.notes.Count + "개)";
        Repaint();
    }

    // ───────────────────────── 곡 설정과 파형 ─────────────────────────

    static bool IsDecompressOnLoad(AudioClip clip, out AudioImporter importer)
    {
        importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
        if (importer == null) return true;
        return importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad;
    }

    void SetDecompressOnLoad(AudioImporter importer)
    {
        if (importer == null) return;
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        waveClip = null;   // 다시 읽게 한다
        audio.SetClip(project != null ? project.clip : null);
        message = "Load Type을 Decompress On Load로 바꿨습니다.";
    }

    void BuildWaveform(AudioClip clip)
    {
        waveClip = clip;
        wavePeaks = null;
        waveError = null;
        if (clip == null) return;

        AudioImporter importer;
        if (!IsDecompressOnLoad(clip, out importer)) return;   // 경고는 따로 보여준다

        if (clip.loadState != AudioDataLoadState.Loaded)
        {
            clip.LoadAudioData();
            if (clip.loadState == AudioDataLoadState.Failed)
            {
                waveError = "곡 데이터를 불러오지 못했습니다.";
                return;
            }
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                waveClip = null;   // 아직 불러오는 중이면 다음 프레임에 다시 시도
                return;
            }
        }

        float[] mono = MonoSamples(clip);
        if (mono == null)
        {
            waveError = "파형을 읽지 못했습니다. 편집은 그대로 할 수 있습니다.";
            return;
        }

        var peaks = new float[(mono.Length + WaveBlock - 1) / WaveBlock];
        for (int i = 0; i < mono.Length; i++)
        {
            float a = Math.Abs(mono[i]);
            int block = i / WaveBlock;
            if (a > peaks[block]) peaks[block] = a;
        }

        float max = 1e-6f;
        foreach (float p in peaks) max = Math.Max(max, p);
        for (int i = 0; i < peaks.Length; i++) peaks[i] /= max;
        wavePeaks = peaks;
    }
}
