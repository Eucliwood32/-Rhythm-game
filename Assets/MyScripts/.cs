using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 채보 편집기 창 (메뉴: Tools > 채보 편집기).
/// DJMAX식 세로 타임라인에서 노트를 찍고 고친 뒤, Composer가 읽는 JSON으로 내보낸다.
/// 1단계: 곡 재생, 노트 찍기·이동·삭제·길이 조절, 범위 선택, 속성 패널, 되돌리기, JSON 내보내기·불러오기.
/// 2단계: BPM 구간, 박자 격자·스냅, 메트로놈·노트 클릭음, 탭 템포, 구간 반복, 미니맵,
///        복사·붙여넣기·좌우 반전·격자에 맞추기, 채보 검사.
/// </summary>
public class ChartEditorWindow : EditorWindow
{
    enum EditTool { Select, Tap, Long, Barrage }
    enum DragMode { None, Move, Resize, Box, Seek, MiniSeek }

    class Warning
    {
        public string text;
        public string button;   // null이면 버튼 없음
        public Action action;
    }

    // 배치
    const float TopBarHeight = 22f;
    const float StatusHeight = 20f;
    const float PanelWidth = 290f;
    const float RulerWidth = 64f;
    const float MinimapWidth = 44f;
    const float JudgeFromBottom = 70f;
    const float MinPps = 40f;
    const float MaxPps = 800f;

    // 노트 기본값
    const double BarrageHitsPerSecond = 6.0;   // Judge의 기본값과 같다
    const double DefaultHoldLength = 0.5;
    const double MinHoldLength = 0.1;

    // 파형
    const int WaveBlock = 512;

    // 클릭음을 미리 예약하는 시간(초)
    const double ClickLookahead = 0.15;

    const string PrefLastProject = "ChartEditor.LastProject";

    static readonly string[] ToolNames = { "선택", "단노트", "롱노트", "연타" };
    static readonly string[] LaneNames = { "A (상단)", "B (하단)" };
    static readonly string[] TypeNames = { "단노트", "롱노트", "연타" };
    static readonly int[] SnapValues = { 1, 2, 3, 4, 6, 8, 0 };
    static readonly string[] SnapNames = { "1/4", "1/8", "1/12", "1/16", "1/24", "1/32", "끄기" };

    static readonly Color ColBg = new Color(0.10f, 0.11f, 0.14f);
    static readonly Color ColPanel = new Color(0.16f, 0.17f, 0.20f);
    static readonly Color ColRuler = new Color(0.13f, 0.14f, 0.17f);
    static readonly Color ColA = new Color(0.88f, 0.35f, 0.31f);
    static readonly Color ColB = new Color(0.23f, 0.53f, 0.94f);
    static readonly Color ColBarrage = new Color(0.95f, 0.72f, 0.29f);
    static readonly Color ColJudge = new Color(1f, 0.85f, 0.25f);
    static readonly Color ColWave = new Color(1f, 1f, 1f, 0.13f);
    static readonly Color ColLine = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color ColWhite = new Color(1f, 1f, 1f, 0.9f);
    static readonly Color ColTempo = new Color(1f, 0.55f, 0.15f);
    static readonly Color ColLoop = new Color(0.3f, 0.9f, 0.5f);
    static readonly Color ColIssue = new Color(1f, 0.25f, 0.25f);

    [SerializeField] ChartProject project;
    [SerializeField] EditTool tool = EditTool.Tap;
    [SerializeField] float pps = 150f;
    [SerializeField] int snapDivision = 2;
    [SerializeField] bool metronome;
    [SerializeField] bool noteClicks = true;
    [SerializeField] float clickVolume = 0.8f;
    [SerializeField] bool loopOn;
    [SerializeField] double loopStart;
    [SerializeField] double loopEnd;
    [SerializeField] bool roundTapBpm = true;
    [SerializeField] bool foldNote = true, foldTempo = true, foldPlay = true, foldCheck = true, foldHelp;

    EditorAudio audio = new EditorAudio();
    readonly List<NoteData> selection = new List<NoteData>();
    List<NoteData> clipboard;
    readonly TapTempo tap = new TapTempo();
    readonly List<GridLine> gridBuffer = new List<GridLine>();
    List<ChartIssue> issues = new List<ChartIssue>();
    readonly HashSet<NoteData> issueNotes = new HashSet<NoteData>();
    double scheduledUntil;
    Vector2 panelScroll, issueScroll;

    // 드래그 상태
    DragMode drag;
    Vector2 dragStart;
    Vector2 dragNow;
    double dragStartTime;
    int dragStartLane;
    List<NoteSnapshot> dragOrig;
    NoteSnapshot dragAnchor;
    NoteData dragNote;
    int undoGroup;

    // 파형
    AudioClip waveClip;
    float[] wavePeaks;
    string waveError;

    string message = "";
    double lastRedoAt, lastPasteAt;
    [NonSerialized] GUIStyle noteLabel, rulerLabel, laneHeader, tempoLabel;   // 스크립트가 다시 컴파일될 때 새로 만든다

    [MenuItem("Tools/채보 편집기")]
    static void Open()
    {
        GetWindow<ChartEditorWindow>("채보 편집기");
    }

    void OnEnable()
    {
        if (audio == null) audio = new EditorAudio();
        minSize = new Vector2(820f, 460f);

        if (project == null)
        {
            string guid = EditorPrefs.GetString(PrefLastProject, "");
            if (guid != "") project = AssetDatabase.LoadAssetAtPath<ChartProject>(AssetDatabase.GUIDToAssetPath(guid));
        }
        if (project != null) audio.SetClip(project.clip);

        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.update += OnEditorUpdate;
    }

    void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.update -= OnEditorUpdate;
        audio.Dispose();
        if (project != null) AssetDatabase.SaveAssetIfDirty(project);
    }

    void OnUndoRedo()
    {
        selection.Clear();   // 되돌리면 노트 객체가 새로 만들어지므로 선택을 비운다
        Repaint();
    }

    void OnEditorUpdate()
    {
        if (audio.Tick())
        {
            if (audio.IsPlaying && loopOn && loopEnd > loopStart + 0.05 && audio.Time >= loopEnd)
            {
                audio.Seek(loopStart);
                scheduledUntil = loopStart;
            }
            ScheduleClicks();
            Repaint();
        }

        AudioClip clip = project != null ? project.clip : null;
        if (clip != waveClip)
        {
            BuildWaveform(clip);
            Repaint();
        }
    }

    // 메트로놈과 노트 클릭음을 조금 앞서 예약한다
    void ScheduleClicks()
    {
        if (project == null || !audio.CanSchedule || (!metronome && !noteClicks)) return;
        audio.clickVolume = clickVolume;

        double now = audio.Time;
        if (scheduledUntil < now - 0.05 || scheduledUntil > now + 0.5) scheduledUntil = now;   // 위치가 바뀌었으면 다시 시작
        double to = now + ClickLookahead;
        if (loopOn && loopEnd > loopStart + 0.05) to = Math.Min(to, loopEnd);
        if (to <= scheduledUntil) return;

        if (metronome && project.tempo != null && project.tempo.Count > 0)
        {
            TempoMap.Beats(project.tempo, scheduledUntil, to, gridBuffer);
            foreach (GridLine g in gridBuffer)
                audio.ScheduleClick(g.time, g.kind == GridKind.Measure ? EditorAudio.ClickKind.Accent : EditorAudio.ClickKind.Beat);
        }
        if (noteClicks)
        {
            foreach (NoteData n in project.notes)
                if (n.time >= scheduledUntil && n.time < to) audio.ScheduleClick(n.time, EditorAudio.ClickKind.Note);
        }
        scheduledUntil = to;
    }

    // ───────────────────────── 전체 화면 ─────────────────────────

    void OnGUI()
    {
        EnsureStyles();
        float w = position.width;
        float h = position.height;

        DrawTopBar(new Rect(0, 0, w, TopBarHeight));

        if (project == null)
        {
            DrawEmpty(new Rect(0, TopBarHeight, w, h - TopBarHeight));
            return;
        }
        if (project.notes == null) project.notes = new List<NoteData>();
        if (project.tempo == null || project.tempo.Count == 0) project.tempo = new List<TempoPoint> { new TempoPoint { time = 0, bpm = 120 } };
        if (audio.Clip != project.clip) audio.SetClip(project.clip);

        if (Event.current.type == EventType.Layout) RefreshIssues();

        List<Warning> warnings = CollectWarnings();
        float infoHeight = 26f + warnings.Count * 42f;
        DrawInfoRow(new Rect(0, TopBarHeight, w, infoHeight), warnings);

        float top = TopBarHeight + infoHeight;
        Rect main = new Rect(0, top, w, Mathf.Max(0, h - top - StatusHeight));
        Rect timeline = new Rect(main.x, main.y, Mathf.Max(260f, main.width - PanelWidth), main.height);
        Rect panel = new Rect(timeline.xMax, main.y, Mathf.Max(0, main.width - timeline.width), main.height);

        HandleKeyboard();
        DoTimeline(timeline);
        DrawPanel(panel);
        DrawStatus(new Rect(0, h - StatusHeight, w, StatusHeight));
    }

    void RefreshIssues()
    {
        issues = ChartValidator.Check(project.notes, audio.Length);
        issueNotes.Clear();
        foreach (ChartIssue i in issues) if (i.note != null) issueNotes.Add(i.note);
    }

    void DrawTopBar(Rect r)
    {
        GUILayout.BeginArea(r, EditorStyles.toolbar);
        GUILayout.BeginHorizontal();

        EditorGUI.BeginChangeCheck();
        var picked = (ChartProject)EditorGUILayout.ObjectField(project, typeof(ChartProject), false, GUILayout.Width(170));
        if (EditorGUI.EndChangeCheck()) SetProject(picked);

        if (GUILayout.Button("새로 만들기", EditorStyles.toolbarButton, GUILayout.Width(70)))
            EditorApplication.delayCall += CreateProject;

        GUILayout.Space(8);
        using (new EditorGUI.DisabledScope(project == null || project.clip == null))
        {
            if (GUILayout.Button(audio.IsPlaying ? "일시정지" : "재생", EditorStyles.toolbarButton, GUILayout.Width(56)))
                audio.Toggle();
        }
        GUILayout.Label(ChartEditing.FormatTime(audio.Time) + " / " + ChartEditing.FormatTime(audio.Length),
            EditorStyles.miniLabel, GUILayout.Width(116));

        GUILayout.Space(8);
        tool = (EditTool)GUILayout.Toolbar((int)tool, ToolNames, EditorStyles.toolbarButton, GUILayout.Width(220));

        GUILayout.Space(8);
        GUILayout.Label("스냅", EditorStyles.miniLabel);
        int snapIndex = Math.Max(0, Array.IndexOf(SnapValues, snapDivision));
        snapIndex = EditorGUILayout.Popup(snapIndex, SnapNames, EditorStyles.toolbarPopup, GUILayout.Width(52));
        snapDivision = SnapValues[snapIndex];

        GUILayout.FlexibleSpace();
        GUILayout.Label("확대", EditorStyles.miniLabel);
        pps = GUILayout.HorizontalSlider(pps, MinPps, MaxPps, GUILayout.Width(70));
        GUILayout.Space(8);

        using (new EditorGUI.DisabledScope(project == null))
        {
            if (GUILayout.Button("JSON 불러오기", EditorStyles.toolbarButton, GUILayout.Width(82)))
                EditorApplication.delayCall += ImportJson;
            if (GUILayout.Button("JSON 내보내기", EditorStyles.toolbarButton, GUILayout.Width(82)))
                EditorApplication.delayCall += ExportJson;
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void DrawEmpty(Rect r)
    {
        GUILayout.BeginArea(new Rect(r.x + 20, r.y + 20, Mathf.Min(520, r.width - 40), r.height - 40));
        EditorGUILayout.HelpBox(
            "채보 프로젝트가 없습니다.\n\n" +
            "새로 만들려면 아래 버튼을 누르세요. 이미 있는 프로젝트는 위쪽 칸에 끌어다 놓으면 됩니다.\n" +
            "프로젝트 하나에 곡 하나와 그 곡의 채보가 들어갑니다.",
            MessageType.Info);
        GUILayout.Space(8);
        if (GUILayout.Button("새 채보 프로젝트 만들기", GUILayout.Height(30)))
            EditorApplication.delayCall += CreateProject;
        GUILayout.EndArea();
    }

    List<Warning> CollectWarnings()
    {
        var list = new List<Warning>();
        AudioClip clip = project.clip;

        if (clip == null)
        {
            list.Add(new Warning { text = "곡이 없습니다. 위의 '곡' 칸에 오디오 파일을 끌어다 놓으세요." });
            return list;
        }

        AudioImporter importer;
        if (!IsDecompressOnLoad(clip, out importer))
        {
            list.Add(new Warning
            {
                text = "곡의 Load Type이 Decompress On Load가 아니라서 파형을 읽을 수 없습니다. 게임 싱크에도 이 설정을 권장합니다.",
                button = "설정 바꾸기",
                action = () => SetDecompressOnLoad(importer)
            });
        }
        if (EditorUtility.audioMasterMute)
        {
            list.Add(new Warning
            {
                text = "에디터 오디오가 음소거되어 있습니다. Game 창 위쪽의 음소거(스피커) 버튼을 끄세요.",
                button = "음소거 끄기",
                action = () => EditorUtility.audioMasterMute = false
            });
        }
        if (waveError != null) list.Add(new Warning { text = waveError });
        return list;
    }

    void DrawInfoRow(Rect r, List<Warning> warnings)
    {
        GUILayout.BeginArea(new Rect(r.x + 6, r.y + 3, r.width - 12, r.height - 3));

        EditorGUI.BeginChangeCheck();
        var clip = (AudioClip)EditorGUILayout.ObjectField("곡", project.clip, typeof(AudioClip), false, GUILayout.MaxWidth(440));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(project, "곡 변경");
            project.clip = clip;
            EditorUtility.SetDirty(project);
            audio.SetClip(clip);
        }

        foreach (Warning wn in warnings)
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox(wn.text, MessageType.Warning);
            if (wn.button != null && GUILayout.Button(wn.button, GUILayout.Width(110), GUILayout.Height(36)))
                EditorApplication.delayCall += () => { wn.action(); Repaint(); };
            GUILayout.EndHorizontal();
        }
        GUILayout.EndArea();
    }

    void DrawStatus(Rect r)
    {
        EditorGUI.DrawRect(r, ColPanel);
        string text = "노트 " + project.notes.Count + "개";
        if (selection.Count > 0) text += "   |   선택 " + selection.Count + "개";
        text += "   |   스냅 " + SnapNames[Math.Max(0, Array.IndexOf(SnapValues, snapDivision))];
        if (loopOn) text += "   |   반복 " + ChartEditing.FormatTime(loopStart) + " ~ " + ChartEditing.FormatTime(loopEnd);
        if (issues.Count > 0) text += "   |   검사 문제 " + issues.Count + "개";
        if (message != "") text += "   |   " + message;
        GUI.Label(new Rect(r.x + 8, r.y + 2, r.width - 16, r.height - 2), text, EditorStyles.miniLabel);
    }

    // ───────────────────────── 오른쪽 패널 ─────────────────────────

    void DrawPanel(Rect r)
    {
        EditorGUI.DrawRect(r, ColPanel);
        GUILayout.BeginArea(new Rect(r.x + 8, r.y + 6, r.width - 12, r.height - 10));
        panelScroll = EditorGUILayout.BeginScrollView(panelScroll);

        foldNote = EditorGUILayout.Foldout(foldNote, "선택한 노트", true, EditorStyles.foldoutHeader);
        if (foldNote) DrawSelectionSection();

        GUILayout.Space(6);
        foldTempo = EditorGUILayout.Foldout(foldTempo, "BPM · 격자", true, EditorStyles.foldoutHeader);
        if (foldTempo) DrawTempoSection();

        GUILayout.Space(6);
        foldPlay = EditorGUILayout.Foldout(foldPlay, "재생 · 구간 반복", true, EditorStyles.foldoutHeader);
        if (foldPlay) DrawPlaySection();

        GUILayout.Space(6);
        foldCheck = EditorGUILayout.Foldout(foldCheck, "채보 검사 (" + issues.Count + ")", true, EditorStyles.foldoutHeader);
        if (foldCheck) DrawCheckSection();

        GUILayout.Space(6);
        foldHelp = EditorGUILayout.Foldout(foldHelp, "조작", true, EditorStyles.foldoutHeader);
        if (foldHelp) DrawHelpSection();

        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawSelectionSection()
    {
        if (selection.Count == 0)
        {
            EditorGUILayout.HelpBox("노트를 클릭하면 여기서 시각, 레인, 종류, 길이를 고칠 수 있습니다.", MessageType.None);
            return;
        }

        if (selection.Count == 1) DrawNoteFields(selection[0]);
        else GUILayout.Label(selection.Count + "개 선택됨");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("좌우 반전 (M)")) MirrorSelection();
        if (GUILayout.Button("격자에 맞추기 (Q)")) QuantizeSelection();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("복사 (Ctrl+C)")) CopySelection();
        if (GUILayout.Button("삭제 (Del)")) DeleteSelection();
        GUILayout.EndHorizontal();
    }

    void DrawNoteFields(NoteData n)
    {
        EditorGUI.BeginChangeCheck();
        double time = EditorGUILayout.DoubleField("시각 (초)", n.time);
        int lane = EditorGUILayout.Popup("레인", (int)n.lane, LaneNames);
        int type = EditorGUILayout.Popup("종류", (int)n.type, TypeNames);
        double duration = n.duration;
        int count = n.count;
        if (n.type != NoteType.Tap) duration = EditorGUILayout.DoubleField("길이 (초)", n.duration);
        if (n.type == NoteType.Barrage) count = EditorGUILayout.IntField("필요 입력 횟수", n.count);

        if (!EditorGUI.EndChangeCheck()) return;

        Undo.RecordObject(project, "노트 수정");
        n.time = ChartEditing.Round(Math.Max(0, time));
        n.lane = (Lane)lane;
        if (n.type != NoteType.Tap) n.duration = ChartEditing.Round(Math.Max(0, duration));
        if (n.type == NoteType.Barrage) n.count = Math.Max(1, count);
        if ((NoteType)type != n.type) ChartEditing.ApplyType(n, (NoteType)type, DefaultHoldLength, BarrageHitsPerSecond);
        ChartEditing.Sort(project.notes);
        EditorUtility.SetDirty(project);
    }

    void DrawTempoSection()
    {
        List<TempoPoint> tempo = project.tempo;
        int current = TempoMap.SegmentAt(tempo, audio.Time);

        int remove = -1;
        EditorGUI.BeginChangeCheck();
        double[] times = new double[tempo.Count];
        double[] bpms = new double[tempo.Count];
        for (int i = 0; i < tempo.Count; i++)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label((i == current ? "▶" : "  ") + (i == 0 ? "첫 박" : "변경 " + i), GUILayout.Width(52));
            times[i] = EditorGUILayout.DoubleField(tempo[i].time, GUILayout.Width(70));
            GUILayout.Label("초", GUILayout.Width(16));
            bpms[i] = EditorGUILayout.DoubleField(tempo[i].bpm, GUILayout.Width(56));
            GUILayout.Label("BPM", GUILayout.Width(30));
            using (new EditorGUI.DisabledScope(tempo.Count <= 1))
            {
                if (GUILayout.Button("×", GUILayout.Width(22))) remove = i;
            }
            GUILayout.EndHorizontal();
        }
        if (EditorGUI.EndChangeCheck() && remove < 0)
        {
            Undo.RecordObject(project, "BPM 수정");
            for (int i = 0; i < tempo.Count; i++)
            {
                tempo[i].time = ChartEditing.Round(Math.Max(0, times[i]));
                tempo[i].bpm = Math.Max(1, Math.Min(999, bpms[i]));
            }
            TempoMap.Sort(tempo);
            EditorUtility.SetDirty(project);
        }
        if (remove >= 0)
        {
            Undo.RecordObject(project, "BPM 구간 삭제");
            tempo.RemoveAt(remove);
            EditorUtility.SetDirty(project);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("첫 박 = 재생 위치")) SetFirstBeatHere();
        if (GUILayout.Button("여기서 BPM 변경")) AddTempoHere();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("탭 (T)", GUILayout.Width(70))) DoTap();
        double bpm = TapBpm();
        GUILayout.Label(tap.Count >= 2 ? bpm.ToString("0.##") + " BPM  (" + tap.Count + "회)" : "박자에 맞춰 누르세요", EditorStyles.miniLabel);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        roundTapBpm = GUILayout.Toggle(roundTapBpm, "정수로", GUILayout.Width(60));
        using (new EditorGUI.DisabledScope(tap.Count < 2))
        {
            if (GUILayout.Button("지금 구간에 적용")) ApplyTapBpm();
        }
        GUILayout.EndHorizontal();
        EditorGUILayout.LabelField("메트로놈을 켜고 들으며 첫 박과 BPM을 맞추세요. BPM 변경 지점마다 새 마디가 시작됩니다.", EditorStyles.wordWrappedMiniLabel);
    }

    void DrawPlaySection()
    {
        metronome = EditorGUILayout.ToggleLeft("메트로놈 (마디 첫 박은 높은 소리)", metronome);
        noteClicks = EditorGUILayout.ToggleLeft("노트 클릭음", noteClicks);
        clickVolume = EditorGUILayout.Slider("클릭 크기", clickVolume, 0f, 1f);

        GUILayout.Space(4);
        loopOn = EditorGUILayout.ToggleLeft("구간 반복 (L)", loopOn);
        GUILayout.BeginHorizontal();
        loopStart = Math.Max(0, EditorGUILayout.DoubleField(loopStart, GUILayout.Width(70)));
        GUILayout.Label("~", GUILayout.Width(12));
        loopEnd = Math.Max(0, EditorGUILayout.DoubleField(loopEnd, GUILayout.Width(70)));
        GUILayout.Label("초");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("시작 = 여기 ([)")) SetLoopStart();
        if (GUILayout.Button("끝 = 여기 (])")) SetLoopEnd();
        GUILayout.EndHorizontal();
        if (loopOn && loopEnd <= loopStart + 0.05)
            EditorGUILayout.HelpBox("끝이 시작보다 뒤에 있어야 반복합니다.", MessageType.None);
    }

    void DrawCheckSection()
    {
        if (issues.Count == 0)
        {
            EditorGUILayout.LabelField("문제 없음", EditorStyles.miniLabel);
            return;
        }

        int serious = 0;
        foreach (ChartIssue i in issues) if (i.serious) serious++;
        EditorGUILayout.LabelField("문제 " + issues.Count + "개 (판정이 꼬일 수 있는 것 " + serious + "개). 누르면 이동합니다.", EditorStyles.wordWrappedMiniLabel);

        issueScroll = EditorGUILayout.BeginScrollView(issueScroll, GUILayout.Height(Mathf.Min(170, 22 * issues.Count + 6)));
        ChartIssue clicked = null;
        foreach (ChartIssue i in issues)
        {
            string label = (i.serious ? "● " : "○ ") + ChartEditing.FormatTime(i.time) + "  " + i.message;
            if (GUILayout.Button(label, EditorStyles.miniButton)) clicked = i;
        }
        EditorGUILayout.EndScrollView();
        if (clicked != null) GoToIssue(clicked);
    }

    void DrawHelpSection()
    {
        GUILayout.Label(
            "레인 클릭: 노트 추가 (1~4: 도구 바꾸기)\n" +
            "롱노트·연타: 레인을 위로 드래그\n" +
            "노트 드래그: 이동 (선택한 노트 함께)\n" +
            "끝 손잡이 드래그: 길이 조절\n" +
            "Alt 누른 채 드래그: 스냅 없이\n" +
            "선택 도구 / Shift+드래그: 범위 선택\n" +
            "Ctrl+클릭: 선택 추가·해제   Ctrl+A: 전체 선택\n" +
            "Ctrl+C / Ctrl+V: 복사 / 판정선 위치에 붙여넣기\n" +
            "M: 좌우 반전   Q: 격자에 맞추기   Delete: 삭제\n" +
            "Ctrl+Z: 되돌리기   Ctrl+X / Ctrl+Y: 다시 실행\n" +
            "T: 탭 템포   [ / ]: 반복 시작 / 끝   L: 반복 켜기\n" +
            "휠: 시간 이동   Ctrl+휠: 확대·축소\n" +
            "왼쪽 눈금·오른쪽 미니맵 클릭: 위치 이동\n" +
            "Space: 재생 / 일시정지",
            EditorStyles.wordWrappedMiniLabel);
    }

    // ───────────────────────── 타임라인 ─────────────────────────

    TimelineLayout MakeLayout(float w, float h)
    {
        float usable = w - RulerWidth;
        return new TimelineLayout
        {
            judgeY = h - JudgeFromBottom,
            viewTime = audio.Time,
            pps = pps,
            centerX = RulerWidth + usable / 2f,
            laneWidth = Mathf.Clamp(usable / 2f - 20f, 40f, 130f)
        };
    }

    void DoTimeline(Rect r)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;

        if (project.clip == null)
        {
            EditorGUI.DrawRect(r, ColBg);
            GUI.Label(new Rect(r.x + 20, r.y + 20, r.width - 40, 40), "곡을 연결하면 타임라인이 나타납니다.", EditorStyles.whiteLabel);
            return;
        }

        GUI.BeginClip(r);
        float mainWidth = r.width - MinimapWidth;
        TimelineLayout layout = MakeLayout(mainWidth, r.height);
        Rect mini = new Rect(mainWidth, 0, MinimapWidth, r.height);
        EventType type = e.GetTypeForControl(id);
        if (type == EventType.Repaint)
        {
            DrawTimeline(layout, mainWidth, r.height);
            DrawMinimap(mini, layout, r.height);
        }
        else
        {
            HandleTimelineInput(e, type, id, layout, mainWidth, r.height, mini);
        }
        GUI.EndClip();
    }

    void DrawTimeline(TimelineLayout L, float w, float h)
    {
        float leftX = L.centerX - L.laneWidth;
        float lanesW = L.laneWidth * 2;

        EditorGUI.DrawRect(new Rect(0, 0, w, h), ColBg);
        EditorGUI.DrawRect(new Rect(leftX, 0, L.laneWidth, h), new Color(ColA.r, ColA.g, ColA.b, 0.07f));
        EditorGUI.DrawRect(new Rect(L.centerX, 0, L.laneWidth, h), new Color(ColB.r, ColB.g, ColB.b, 0.07f));
        EditorGUI.DrawRect(new Rect(L.centerX, 0, 1, h), ColLine);

        DrawWaveform(L, h);

        double tTop = L.TimeAt(0), tBottom = L.TimeAt(h);

        // 구간 반복 영역
        if (loopEnd > loopStart + 0.01)
        {
            float y0 = L.YAt(loopStart), y1 = L.YAt(loopEnd);
            float a = loopOn ? 0.10f : 0.04f;
            EditorGUI.DrawRect(new Rect(leftX, y1, lanesW, y0 - y1), new Color(ColLoop.r, ColLoop.g, ColLoop.b, a));
            EditorGUI.DrawRect(new Rect(leftX, y0, lanesW, 1), new Color(ColLoop.r, ColLoop.g, ColLoop.b, 0.6f));
            EditorGUI.DrawRect(new Rect(leftX, y1, lanesW, 1), new Color(ColLoop.r, ColLoop.g, ColLoop.b, 0.6f));
        }

        // 박자 격자 (마디 / 박 / 분할)
        TempoMap.GridLines(project.tempo, Math.Max(0, tBottom), tTop, Math.Max(1, snapDivision), 6.0 / L.pps, gridBuffer);
        foreach (GridLine g in gridBuffer)
        {
            float y = L.YAt(g.time);
            if (g.kind == GridKind.Measure)
            {
                EditorGUI.DrawRect(new Rect(leftX, y, lanesW, 2), new Color(1f, 1f, 1f, 0.42f));
                GUI.Label(new Rect(leftX + 3, y - 15, 50, 14), g.measure.ToString(), rulerLabel);
            }
            else if (g.kind == GridKind.Beat)
            {
                EditorGUI.DrawRect(new Rect(leftX, y, lanesW, 1), new Color(1f, 1f, 1f, 0.2f));
            }
            else
            {
                EditorGUI.DrawRect(new Rect(leftX, y, lanesW, 1), new Color(1f, 1f, 1f, 0.07f));
            }
        }

        // 왼쪽 시간 눈금
        EditorGUI.DrawRect(new Rect(0, 0, RulerWidth, h), ColRuler);
        double step = L.pps >= 100 ? 1 : L.pps >= 50 ? 2 : 5;
        for (double t = Math.Ceiling(Math.Max(0, tBottom) / step) * step; t <= tTop; t += step)
        {
            float y = L.YAt(t);
            EditorGUI.DrawRect(new Rect(RulerWidth - 8, y, 8, 1), ColWhite);
            string label = ChartEditing.FormatTime(t);
            GUI.Label(new Rect(4, y - 14, RulerWidth - 8, 14), label.Substring(0, label.Length - 4), rulerLabel);   // m:ss
        }
        if (loopEnd > loopStart + 0.01)
        {
            float y0 = L.YAt(loopStart), y1 = L.YAt(loopEnd);
            EditorGUI.DrawRect(new Rect(RulerWidth - 5, y1, 5, y0 - y1), new Color(ColLoop.r, ColLoop.g, ColLoop.b, loopOn ? 0.9f : 0.35f));
        }

        // BPM 변경 지점
        foreach (TempoPoint p in project.tempo)
        {
            if (p.time < tBottom - 1 || p.time > tTop + 1) continue;
            float y = L.YAt(p.time);
            EditorGUI.DrawRect(new Rect(0, y - 1, RulerWidth, 2), ColTempo);
            EditorGUI.DrawRect(new Rect(leftX, y, lanesW, 1), new Color(ColTempo.r, ColTempo.g, ColTempo.b, 0.7f));
            GUI.Label(new Rect(2, y + 1, RulerWidth - 2, 14), p.bpm.ToString("0.##") + " BPM", tempoLabel);
        }

        float endY = L.YAt(audio.Length);
        if (endY > 0 && endY < h) EditorGUI.DrawRect(new Rect(leftX, endY, lanesW, 2), new Color(1f, 0.3f, 0.3f, 0.6f));

        // 노트 (선택한 노트는 위에 그린다)
        var selected = new HashSet<NoteData>(selection);
        foreach (NoteData n in project.notes)
            if (!selected.Contains(n)) DrawNote(n, L, h, false);
        foreach (NoteData n in selection)
            DrawNote(n, L, h, true);

        // 판정선 (= 재생 위치)
        EditorGUI.DrawRect(new Rect(leftX - 14, L.judgeY - 1, lanesW + 28, 3), ColJudge);
        GUI.Label(new Rect(2, L.judgeY + 4, RulerWidth - 4, 16), ChartEditing.FormatTime(L.viewTime), rulerLabel);

        GUI.Label(new Rect(L.LaneX(Lane.Upper) - 40, 4, 80, 18), "A", laneHeader);
        GUI.Label(new Rect(L.LaneX(Lane.Lower) - 40, 4, 80, 18), "B", laneHeader);

        if (drag == DragMode.Box) DrawOutline(BoxRect(), ColWhite, 1);
    }

    void DrawNote(NoteData n, TimelineLayout L, float h, bool selected)
    {
        float x = L.LaneX(n.lane);
        float hw = L.NoteHalfWidth;
        float y = L.YAt(n.time);
        bool hasLength = ChartEditing.HasLength(n);
        float ey = hasLength ? L.YAt(n.time + n.duration) : y;
        if (y < -20f || ey > h + 20f) return;

        Color col = n.type == NoteType.Barrage ? ColBarrage : (n.lane == Lane.Upper ? ColA : ColB);

        if (hasLength && n.type == NoteType.Long)
        {
            float bw = hw * 0.45f;
            EditorGUI.DrawRect(new Rect(x - bw, ey, bw * 2, y - ey), new Color(col.r, col.g, col.b, 0.45f));
            EditorGUI.DrawRect(new Rect(x - hw, ey - 3, hw * 2, 6), col);    // 끝 손잡이
        }
        else if (hasLength && n.type == NoteType.Barrage)
        {
            EditorGUI.DrawRect(new Rect(x - hw, ey, hw * 2, y - ey), new Color(col.r, col.g, col.b, 0.15f));
            for (float yy = ey + 4; yy < y - 8; yy += 10)
                EditorGUI.DrawRect(new Rect(x - hw, yy, hw * 2, 4), new Color(col.r, col.g, col.b, 0.5f));
            DrawOutline(new Rect(x - hw, ey, hw * 2, y - ey), col, 1);
            EditorGUI.DrawRect(new Rect(x - hw, ey - 3, hw * 2, 6), col);    // 끝 손잡이
        }

        // 머리
        Rect head = new Rect(x - hw, y - ChartEditing.HeadHalfHeight, hw * 2, ChartEditing.HeadHalfHeight * 2);
        EditorGUI.DrawRect(head, col);
        EditorGUI.DrawRect(new Rect(head.x, head.y, head.width, 3), new Color(1f, 1f, 1f, 0.3f));
        if (n.type == NoteType.Long) GUI.Label(head, "LONG", noteLabel);
        else if (n.type == NoteType.Barrage) GUI.Label(head, "연타 ×" + n.count, noteLabel);

        if (issueNotes.Contains(n))
            DrawOutline(new Rect(head.x - 4, head.y - 4, head.width + 8, head.height + 8), ColIssue, 2);

        if (selected)
        {
            DrawOutline(new Rect(head.x - 2, head.y - 2, head.width + 4, head.height + 4), Color.white, 2);
            if (hasLength) DrawOutline(new Rect(x - hw - 2, ey - 5, hw * 2 + 4, 10), Color.white, 2);
        }
    }

    void DrawWaveform(TimelineLayout L, float h)
    {
        if (wavePeaks == null || project.clip == null) return;
        double blockSeconds = (double)WaveBlock / project.clip.frequency;
        float maxHalf = L.laneWidth * 0.95f;

        for (float y = 0; y < h; y += 2f)
        {
            int i0 = (int)(L.TimeAt(y + 2f) / blockSeconds);
            int i1 = (int)(L.TimeAt(y) / blockSeconds);
            if (i1 < 0 || i0 >= wavePeaks.Length) continue;
            float peak = 0f;
            for (int i = Math.Max(0, i0); i <= i1 && i < wavePeaks.Length; i++) peak = Math.Max(peak, wavePeaks[i]);
            float half = peak * maxHalf;
            if (half >= 1f) EditorGUI.DrawRect(new Rect(L.centerX - half, y, half * 2, 2f), ColWave);
        }
    }

    // 오른쪽 미니맵: 곡 전체 (아래가 처음, 위가 끝)
    void DrawMinimap(Rect m, TimelineLayout L, float h)
    {
        EditorGUI.DrawRect(m, ColRuler);
        EditorGUI.DrawRect(new Rect(m.x, m.y, 1, m.height), ColLine);
        double len = audio.Length;
        if (len <= 0) return;

        float inner = m.height - 8f;
        float bottom = m.yMax - 4f;
        float cx = m.x + m.width / 2f;

        if (wavePeaks != null)
        {
            int rows = Mathf.Max(1, (int)(inner / 2f));
            for (int row = 0; row < rows; row++)
            {
                int i0 = (int)((double)row / rows * wavePeaks.Length);
                int i1 = (int)((double)(row + 1) / rows * wavePeaks.Length);
                float peak = 0f;
                for (int i = i0; i < i1 && i < wavePeaks.Length; i++) peak = Math.Max(peak, wavePeaks[i]);
                float half = peak * (m.width / 2f - 3f);
                if (half >= 0.5f) EditorGUI.DrawRect(new Rect(cx - half, bottom - (row + 1) * 2f, half * 2, 2f), ColWave);
            }
        }

        if (loopEnd > loopStart + 0.01)
        {
            float ly0 = bottom - (float)(loopStart / len) * inner, ly1 = bottom - (float)(loopEnd / len) * inner;
            EditorGUI.DrawRect(new Rect(m.x + 1, ly1, 4, Mathf.Max(1, ly0 - ly1)), new Color(ColLoop.r, ColLoop.g, ColLoop.b, loopOn ? 0.9f : 0.35f));
        }

        foreach (NoteData n in project.notes)
        {
            float y = bottom - (float)(n.time / len) * inner;
            Color c = issueNotes.Contains(n) ? ColIssue : n.type == NoteType.Barrage ? ColBarrage : n.lane == Lane.Upper ? ColA : ColB;
            float x = n.lane == Lane.Upper ? m.x + 8 : m.x + m.width / 2f + 3;
            EditorGUI.DrawRect(new Rect(x, y - 1, m.width / 2f - 11, 2), c);
        }

        // 지금 보이는 범위와 재생 위치
        float vTop = bottom - (float)(Math.Min(len, L.TimeAt(0)) / len) * inner;
        float vBottom = bottom - (float)(Math.Max(0, L.TimeAt(h)) / len) * inner;
        DrawOutline(new Rect(m.x + 2, vTop, m.width - 4, Mathf.Max(2, vBottom - vTop)), new Color(1f, 1f, 1f, 0.5f), 1);
        float py = bottom - (float)(audio.Time / len) * inner;
        EditorGUI.DrawRect(new Rect(m.x + 1, py - 1, m.width - 2, 2), ColJudge);
    }

    double MinimapTimeAt(Rect m, float y)
    {
        float inner = m.height - 8f;
        float bottom = m.yMax - 4f;
        return Math.Max(0, Math.Min(audio.Length, (bottom - y) / inner * audio.Length));
    }

    void HandleTimelineInput(Event e, EventType type, int id, TimelineLayout L, float w, float h, Rect mini)
    {
        Vector2 mp = e.mousePosition;
        bool inside = new Rect(0, 0, w + mini.width, h).Contains(mp);

        switch (type)
        {
            case EventType.MouseDown:
                if (e.button != 0 || !inside) return;
                GUIUtility.keyboardControl = 0;
                GUIUtility.hotControl = id;
                dragStart = dragNow = mp;
                if (mini.Contains(mp))
                {
                    drag = DragMode.MiniSeek;
                    audio.Seek(MinimapTimeAt(mini, mp.y));
                }
                else
                {
                    MouseDown(e, L, mp);
                }
                e.Use();
                Repaint();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl != id) return;
                dragNow = mp;
                if (drag == DragMode.MiniSeek) audio.Seek(MinimapTimeAt(mini, mp.y));
                else MouseDrag(e, L, mp);
                e.Use();
                Repaint();
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl != id) return;
                GUIUtility.hotControl = 0;
                MouseUp(e, L);
                e.Use();
                Repaint();
                break;

            case EventType.ScrollWheel:
                if (!inside) return;
                if (e.control || e.command)
                    pps = Mathf.Clamp(pps * (e.delta.y < 0 ? 1.15f : 0.87f), MinPps, MaxPps);
                else
                    audio.Seek(audio.Time - e.delta.y / 3f * 60f / pps);   // 휠을 올리면 앞으로(미래로)
                e.Use();
                Repaint();
                break;
        }
    }

    // 격자에 맞춘 시각 (Alt를 누르고 있으면 스냅하지 않음)
    double SnapTime(double t, Event e)
    {
        if (e != null && e.alt) return ChartEditing.Round(Math.Max(0, t));
        return TempoMap.Snap(project.tempo, Math.Max(0, t), snapDivision);
    }

    void MouseDown(Event e, TimelineLayout L, Vector2 mp)
    {
        drag = DragMode.None;

        if (mp.x < RulerWidth)
        {
            drag = DragMode.Seek;
            audio.Seek(L.TimeAt(mp.y));
            return;
        }

        bool additive = e.control || e.command;
        NotePart part;
        int index = ChartEditing.HitTest(project.notes, L, mp.x, mp.y, out part);

        if (index >= 0)
        {
            NoteData n = project.notes[index];
            if (part == NotePart.Tail && !additive && !e.shift)
            {
                selection.Clear();
                selection.Add(n);
                BeginUndo("노트 길이 조절");
                drag = DragMode.Resize;
                dragNote = n;
                return;
            }

            if (additive || e.shift)
            {
                if (selection.Contains(n)) selection.Remove(n); else selection.Add(n);
            }
            else if (!selection.Contains(n))
            {
                selection.Clear();
                selection.Add(n);
            }

            if (selection.Contains(n))
            {
                BeginUndo("노트 이동");
                StartMove(L, mp, n);
            }
            return;
        }

        int lane = L.LaneAt(mp.x);
        if (tool == EditTool.Select || e.shift || lane < 0)
        {
            if (!additive) selection.Clear();
            drag = DragMode.Box;
            return;
        }

        // 새 노트
        BeginUndo("노트 추가");
        var note = new NoteData
        {
            time = SnapTime(L.TimeAt(mp.y), e),
            lane = (Lane)lane,
            type = tool == EditTool.Long ? NoteType.Long : tool == EditTool.Barrage ? NoteType.Barrage : NoteType.Tap
        };
        project.notes.Add(note);
        selection.Clear();
        selection.Add(note);

        if (note.type == NoteType.Tap)
        {
            StartMove(L, mp, note);
        }
        else
        {
            drag = DragMode.Resize;
            dragNote = note;
        }
        message = "";
    }

    void StartMove(TimelineLayout L, Vector2 mp, NoteData anchor)
    {
        drag = DragMode.Move;
        dragStartTime = L.TimeAt(mp.y);
        dragStartLane = (int)anchor.lane;
        dragOrig = ChartEditing.Snapshot(selection);
        dragAnchor = dragOrig.Find(s => s.note == anchor);
    }

    void MouseDrag(Event e, TimelineLayout L, Vector2 mp)
    {
        switch (drag)
        {
            case DragMode.Seek:
                audio.Seek(L.TimeAt(mp.y));
                break;

            case DragMode.Move:
            {
                double delta = L.TimeAt(mp.y) - dragStartTime;
                if (dragAnchor != null) delta = SnapTime(dragAnchor.time + delta, e) - dragAnchor.time;   // 잡은 노트가 격자에 붙게
                delta = ChartEditing.ClampDelta(dragOrig, delta);
                int lane = L.LaneAt(mp.x);
                bool flip = lane >= 0 && lane != dragStartLane;
                Undo.RecordObject(project, "노트 이동");
                ChartEditing.Move(dragOrig, delta, flip);
                break;
            }

            case DragMode.Resize:
                Undo.RecordObject(project, "노트 길이 조절");
                ChartEditing.SetEnd(dragNote, SnapTime(L.TimeAt(mp.y), e), BarrageHitsPerSecond);
                break;
        }
    }

    void MouseUp(Event e, TimelineLayout L)
    {
        switch (drag)
        {
            case DragMode.Box:
            {
                Rect box = BoxRect();
                foreach (NoteData n in ChartEditing.InRect(project.notes, L, box.xMin, box.yMin, box.xMax, box.yMax))
                    if (!selection.Contains(n)) selection.Add(n);
                break;
            }

            case DragMode.Resize:
                if (dragNote.duration < MinHoldLength)
                {
                    Undo.RecordObject(project, "노트 길이 조절");
                    double beat = TempoMap.BeatLength(project.tempo[Math.Max(0, TempoMap.SegmentAt(project.tempo, dragNote.time))]);
                    ChartEditing.SetEnd(dragNote, dragNote.time + (snapDivision > 0 ? beat : DefaultHoldLength), BarrageHitsPerSecond);
                }
                EndUndo();
                break;

            case DragMode.Move:
                EndUndo();
                break;
        }
        drag = DragMode.None;
        dragNote = null;
        dragOrig = null;
        dragAnchor = null;
    }

    Rect BoxRect()
    {
        return Rect.MinMaxRect(
            Mathf.Min(dragStart.x, dragNow.x), Mathf.Min(dragStart.y, dragNow.y),
            Mathf.Max(dragStart.x, dragNow.x), Mathf.Max(dragStart.y, dragNow.y));
    }

    // ───────────────────────── 키보드와 명령 ─────────────────────────

    void HandleKeyboard()
    {
        Event e = Event.current;
        if (EditorGUIUtility.editingTextField) return;   // 숫자 칸을 고치는 중이면 손대지 않는다

        if (e.type == EventType.ValidateCommand)
        {
            switch (e.commandName)
            {
                case "SoftDelete":
                case "Delete":
                case "SelectAll":
                case "Cut":
                case "Copy":
                case "Paste":
                    e.Use();
                    break;
            }
        }
        else if (e.type == EventType.ExecuteCommand)
        {
            switch (e.commandName)
            {
                case "SoftDelete":
                case "Delete":
                    DeleteSelection(); e.Use(); break;
                case "SelectAll":
                    SelectAll(); e.Use(); break;
                case "Cut":
                    Redo(); e.Use(); break;   // 요청에 따라 Ctrl+X = 다시 실행
                case "Copy":
                    CopySelection(); e.Use(); break;
                case "Paste":
                    Paste(); e.Use(); break;
            }
        }
        else if (e.type == EventType.KeyDown)
        {
            bool ctrl = e.control || e.command;
            bool used = true;

            if (ctrl)
            {
                switch (e.keyCode)
                {
                    case KeyCode.X: Redo(); break;
                    case KeyCode.A: SelectAll(); break;
                    case KeyCode.C: CopySelection(); break;
                    case KeyCode.V: Paste(); break;
                    default: used = false; break;
                }
            }
            else
            {
                switch (e.keyCode)
                {
                    case KeyCode.Space: audio.Toggle(); break;
                    case KeyCode.Delete:
                    case KeyCode.Backspace: DeleteSelection(); break;
                    case KeyCode.M: MirrorSelection(); break;
                    case KeyCode.Q: QuantizeSelection(); break;
                    case KeyCode.T: DoTap(); break;
                    case KeyCode.LeftBracket: SetLoopStart(); break;
                    case KeyCode.RightBracket: SetLoopEnd(); break;
                    case KeyCode.L: loopOn = !loopOn; message = loopOn ? "구간 반복 켬" : "구간 반복 끔"; break;
                    case KeyCode.Alpha1: tool = EditTool.Select; break;
                    case KeyCode.Alpha2: tool = EditTool.Tap; break;
                    case KeyCode.Alpha3: tool = EditTool.Long; break;
                    case KeyCode.Alpha4: tool = EditTool.Barrage; break;
                    default: used = false; break;
                }
            }

            if (used)
            {
                e.Use();
                Repaint();
            }
        }
    }

    void Redo()
    {
        // Ctrl+X가 키 입력과 Cut 명령으로 두 번 들어와도 한 번만 실행
        double now = EditorApplication.timeSinceStartup;
        if (now - lastRedoAt < 0.15) return;
        lastRedoAt = now;
        Undo.PerformRedo();
    }

    void SelectAll()
    {
        selection.Clear();
        selection.AddRange(project.notes);
        Repaint();
    }

    void DeleteSelection()
    {
        if (project == null || selection.Count == 0) return;
        Undo.RecordObject(project, "노트 삭제");
        var doomed = new HashSet<NoteData>(selection);
        int removed = project.notes.RemoveAll(n => doomed.Contains(n));
        selection.Clear();
        EditorUtility.SetDirty(project);
        message = removed + "개 삭제";
        Repaint();
    }

    void CopySelection()
    {
        if (project == null || selection.Count == 0) return;
        clipboard = ChartEditing.CopyRelative(selection);
        message = clipboard.Count + "개 복사";
    }

    void Paste()
    {
        if (project == null || clipboard == null || clipboard.Count == 0) return;
        // Ctrl+V가 키 입력과 Paste 명령으로 두 번 들어와도 한 번만 실행
        double now = EditorApplication.timeSinceStartup;
        if (now - lastPasteAt < 0.15) return;
        lastPasteAt = now;

        double at = SnapTime(audio.Time, null);
        List<NoteData> added = ChartEditing.PasteAt(clipboard, at);
        Undo.RecordObject(project, "노트 붙여넣기");
        project.notes.AddRange(added);
        ChartEditing.Sort(project.notes);
        EditorUtility.SetDirty(project);
        selection.Clear();
        selection.AddRange(added);
        message = added.Count + "개 붙여넣기 (" + ChartEditing.FormatTime(at) + ")";
    }

    void MirrorSelection()
    {
        if (project == null || selection.Count == 0) return;
        Undo.RecordObject(project, "좌우 반전");
        ChartEditing.Mirror(selection);
        ChartEditing.Sort(project.notes);
        EditorUtility.SetDirty(project);
        message = selection.Count + "개 좌우 반전";
    }

    void QuantizeSelection()
    {
        if (project == null || selection.Count == 0) return;
        if (snapDivision <= 0)
        {
            message = "스냅이 꺼져 있어서 격자에 맞출 수 없습니다.";
            return;
        }
        Undo.RecordObject(project, "격자에 맞추기");
        ChartEditing.Quantize(selection, project.tempo, snapDivision, BarrageHitsPerSecond);
        ChartEditing.Sort(project.notes);
        EditorUtility.SetDirty(project);
        message = selection.Count + "개를 " + SnapNames[Array.IndexOf(SnapValues, snapDivision)] + " 격자에 맞춤";
    }

    void GoToIssue(ChartIssue issue)
    {
        audio.Seek(issue.time);
        selection.Clear();
        if (issue.note != null && project.notes.Contains(issue.note)) selection.Add(issue.note);
        message = issue.message;
        Repaint();
    }

    // ───────────────────────── BPM · 탭 템포 · 구간 반복 ─────────────────────────

    void SetFirstBeatHere()
    {
        double t = ChartEditing.Round(audio.Time);
        if (project.tempo.Count > 1 && t >= project.tempo[1].time)
        {
            message = "첫 박은 다음 BPM 변경 지점보다 앞이어야 합니다.";
            return;
        }
        Undo.RecordObject(project, "첫 박 설정");
        project.tempo[0].time = t;
        EditorUtility.SetDirty(project);
        message = "첫 박 = " + ChartEditing.FormatTime(t);
    }

    void AddTempoHere()
    {
        double t = SnapTime(audio.Time, null);
        if (t <= project.tempo[0].time)
        {
            message = "BPM 변경은 첫 박보다 뒤에 추가할 수 있습니다.";
            return;
        }
        double bpm = project.tempo[Math.Max(0, TempoMap.SegmentAt(project.tempo, t))].bpm;
        Undo.RecordObject(project, "BPM 변경 추가");
        TempoMap.AddPoint(project.tempo, t, bpm);
        EditorUtility.SetDirty(project);
        message = ChartEditing.FormatTime(t) + "에 BPM 변경 추가 (값을 고치세요)";
    }

    void DoTap()
    {
        tap.Tap(EditorApplication.timeSinceStartup);
        if (tap.Count >= 2) message = "탭 템포 " + TapBpm().ToString("0.##") + " BPM";
        Repaint();
    }

    double TapBpm()
    {
        double bpm = tap.Bpm;
        return roundTapBpm ? Math.Round(bpm) : Math.Round(bpm * 100) / 100;
    }

    void ApplyTapBpm()
    {
        if (tap.Count < 2) return;
        int seg = Math.Max(0, TempoMap.SegmentAt(project.tempo, audio.Time));
        Undo.RecordObject(project, "탭 템포 적용");
        project.tempo[seg].bpm = Math.Max(1, TapBpm());
        EditorUtility.SetDirty(project);
        message = (seg == 0 ? "첫" : seg + "번째 변경") + " 구간 BPM = " + project.tempo[seg].bpm.ToString("0.##");
        tap.Reset();
    }

    void SetLoopStart()
    {
        loopStart = ChartEditing.Round(audio.Time);
        if (loopEnd <= loopStart) loopEnd = ChartEditing.Round(Math.Min(audio.Length, loopStart + 4));
        message = "반복 시작 " + ChartEditing.FormatTime(loopStart);
    }

    void SetLoopEnd()
    {
        loopEnd = ChartEditing.Round(audio.Time);
        if (loopStart >= loopEnd) loopStart = ChartEditing.Round(Math.Max(0, loopEnd - 4));
        loopOn = true;
        message = "반복 " + ChartEditing.FormatTime(loopStart) + " ~ " + ChartEditing.FormatTime(loopEnd);
    }

    void BeginUndo(string name)
    {
        Undo.IncrementCurrentGroup();
        undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(name);
        Undo.RecordObject(project, name);
    }

    void EndUndo()
    {
        Undo.RecordObject(project, Undo.GetCurrentGroupName());
        ChartEditing.Sort(project.notes);
        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.SetDirty(project);
    }

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

        int channels = Math.Max(1, clip.channels);
        int frames = clip.samples;
        var peaks = new float[(frames + WaveBlock - 1) / WaveBlock];
        const int chunkFrames = WaveBlock * 256;
        var buffer = new float[chunkFrames * channels];

        for (int offset = 0; offset < frames; offset += chunkFrames)
        {
            int n = Math.Min(chunkFrames, frames - offset);
            float[] data = n == chunkFrames ? buffer : new float[n * channels];
            if (!clip.GetData(data, offset))
            {
                waveError = "파형을 읽지 못했습니다. 편집은 그대로 할 수 있습니다.";
                return;
            }
            for (int i = 0; i < n * channels; i++)
            {
                float a = Math.Abs(data[i]);
                int block = (offset + i / channels) / WaveBlock;
                if (a > peaks[block]) peaks[block] = a;
            }
        }

        float max = 1e-6f;
        foreach (float p in peaks) max = Math.Max(max, p);
        for (int i = 0; i < peaks.Length; i++) peaks[i] /= max;
        wavePeaks = peaks;
    }

    // ───────────────────────── 그리기 도우미 ─────────────────────────

    static void DrawOutline(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    void EnsureStyles()
    {
        if (noteLabel != null && rulerLabel != null && laneHeader != null && tempoLabel != null) return;
        noteLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };
        noteLabel.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
        rulerLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.LowerLeft };
        rulerLabel.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        laneHeader = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
        laneHeader.normal.textColor = Color.white;
        tempoLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperLeft, fontSize = 9 };
        tempoLabel.normal.textColor = ColTempo;
    }
}
