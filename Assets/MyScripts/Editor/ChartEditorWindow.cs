using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 채보 편집기 창 (메뉴: Tools > 채보 편집기).
/// DJMAX식 세로 타임라인에서 노트를 찍고 고친 뒤, Composer가 읽는 JSON으로 내보낸다.
/// 1단계: 곡 재생, 노트 찍기·이동·삭제·길이 조절, 범위 선택, 속성 패널, 되돌리기, JSON 내보내기·불러오기.
/// 2단계: BPM 구간, 박자 격자·스냅, 메트로놈·노트 클릭음, 탭 템포, 구간 반복, 미니맵,
///        복사·붙여넣기·좌우 반전·격자에 맞추기, 채보 검사.
/// 3단계: 테스트 플레이(게임과 같은 판정), 키 녹화, 자동 생성.
///
/// 이 파일: 상태(필드), 창 열기·닫기, 매 프레임 처리, 위쪽 막대·경고·상태줄, 그리기 도우미.
/// 나머지는 기능별 파일에 있다 (같은 클래스):
///   ChartEditorWindow.Panel.cs    오른쪽 패널
///   ChartEditorWindow.Timeline.cs 타임라인·미니맵 그리기, 마우스 입력
///   ChartEditorWindow.Edit.cs     단축키, 편집 명령, BPM·탭 템포·구간 반복
///   ChartEditorWindow.Files.cs    프로젝트, JSON, 곡 설정·파형
///   ChartEditorWindow.Play.cs     테스트 플레이, 키 녹화
///   ChartEditorWindow.Auto.cs     자동 생성
/// </summary>
public partial class ChartEditorWindow : EditorWindow
{
    enum EditTool { Tap, Long, Barrage, Select }   // 1·2·3·4 키 순서
    enum DragMode { None, Move, Resize, Box, Seek, MiniSeek, Pan }
    enum PlayMode { None, Test, Record }

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
    const double DefaultHoldLength = 0.5;
    const double MinHoldLength = 0.1;

    // 파형
    const int WaveBlock = 512;

    // 클릭음을 미리 예약하는 시간(초)
    const double ClickLookahead = 0.15;

    const string PrefLastProject = "ChartEditor.LastProject";
    const double TestLeadIn = 2.0;   // 테스트·녹화를 시작할 때 재생 위치보다 이만큼 앞에서 튼다

    static readonly string[] ToolNames = { "1 단노트", "2 롱노트", "3 연타", "4 선택" };
    static readonly string[] LaneNames = { "A (상단)", "B (하단)" };
    static readonly string[] TypeNames = { "단노트", "롱노트", "연타" };
    static readonly int[] SnapValues = { 1, 2, 3, 4, 6, 8, 0 };
    static readonly string[] SnapNames = { "1/4", "1/8", "1/12", "1/16", "1/24", "1/32", "끄기" };
    static readonly string[] DifficultyNames = { "쉬움", "보통", "어려움", "매우 어려움" };
    static readonly double[] DifficultyGaps = { 0.45, 0.28, 0.16, 0.10 };
    static readonly string[] AutoLaneNames = { "교대", "음높이 (저음 = B)", "랜덤" };
    static readonly string[] AutoRangeNames = { "곡 전체", "구간 반복 범위", "재생 위치부터 끝까지" };

    static readonly Color ColBg = new Color(0.10f, 0.11f, 0.14f);
    static readonly Color ColPanel = new Color(0.16f, 0.17f, 0.20f);
    static readonly Color ColRuler = new Color(0.13f, 0.14f, 0.17f);
    static readonly Color ColJudge = new Color(1f, 0.85f, 0.25f);
    static readonly Color ColWave = new Color(1f, 1f, 1f, 0.13f);
    static readonly Color ColLine = new Color(1f, 1f, 1f, 0.10f);
    static readonly Color ColWhite = new Color(1f, 1f, 1f, 0.9f);
    static readonly Color ColTempo = new Color(1f, 0.55f, 0.15f);
    static readonly Color ColLoop = new Color(0.3f, 0.9f, 0.5f);
    static readonly Color ColIssue = new Color(1f, 0.25f, 0.25f);

    [SerializeField] ChartProject project;
    [SerializeField] EditTool editTool = EditTool.Tap;
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
    [SerializeField] bool foldTest = true, foldAuto = true;

    // 테스트 플레이·녹화 설정
    [SerializeField] int inputOffsetMs;
    [SerializeField] bool recordSnap = true;
    [SerializeField] float recordLongThreshold = 0.3f;

    // 자동 생성 설정
    [SerializeField] int autoDifficulty = 1;
    [SerializeField] int autoSensitivity = 60;
    [SerializeField] AutoLaneMode autoLane = AutoLaneMode.Alternate;
    [SerializeField] bool autoLongs = true;
    [SerializeField] float autoMinLong = 0.6f;
    [SerializeField] bool autoBarrages = true;
    [SerializeField] int autoMinBarrage = 6;
    [SerializeField] bool autoSnap = true;
    [SerializeField] int autoRange;

    EditorAudio audio = new EditorAudio();
    readonly List<NoteData> selection = new List<NoteData>();
    List<NoteData> clipboard;
    readonly TapTempo tap = new TapTempo();
    readonly List<GridLine> gridBuffer = new List<GridLine>();
    List<ChartIssue> issues = new List<ChartIssue>();
    readonly HashSet<NoteData> issueNotes = new HashSet<NoteData>();
    double scheduledUntil;
    Vector2 panelScroll, issueScroll;

    // 테스트 플레이·녹화 상태
    PlayMode playMode;
    JudgeCore testCore;
    List<NoteData>[] testLanes;
    readonly HashSet<NoteData> testHit = new HashSet<NoteData>();
    readonly HashSet<NoteData> testMiss = new HashSet<NoteData>();
    ScoreKeeper testScore;      // 게임과 같은 체력·점수·콤보 규칙
    double testDeadAt;          // 체력이 0이 된 곡 시간 (없으면 -1)
    string testSettingsFrom;    // 판정·점수 설정을 어디서 가져왔는지
    int testErrCount;
    double testErrSum;
    readonly string[] popupText = new string[2];
    readonly Color[] popupColor = new Color[2];
    readonly double[] popupAt = new double[2];
    readonly bool[] keyHeld = new bool[2];
    readonly NoteData[] recNote = new NoteData[2];
    readonly double[] recPress = new double[2];
    int recCount, recUndoGroup;
    string testSummary;
    double testAvgError;
    bool testHasError;

    // 자동 생성 상태
    OnsetAnalysis analysis;
    AudioClip analysisClip;
    Task<OnsetAnalysis> analysisTask;
    AudioClip analysisTaskClip;
    volatile float analysisProgress;
    volatile bool analysisCancel;
    bool generateWhenReady;

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

    // 곡 샘플 (모노). 파형 그리기와 자동 생성 분석이 함께 쓴다
    AudioClip monoClip;
    float[] monoSamples;

    string message = "";
    double lastRedoAt, lastPasteAt;
    [NonSerialized] GUIStyle noteLabel, rulerLabel, laneHeader, tempoLabel, popupStyle, comboStyle;   // 스크립트가 다시 컴파일될 때 새로 만든다

    [MenuItem("Tools/채보 편집기")]
    static void Open()
    {
        GetWindow<ChartEditorWindow>("채보 편집기");
    }

    void OnEnable()
    {
        if (audio == null) audio = new EditorAudio();
        minSize = new Vector2(940f, 480f);
        if (PlayerPrefs.HasKey(RhythmDefaults.PrefInputOffset)) inputOffsetMs = Mathf.RoundToInt(PlayerPrefs.GetFloat(RhythmDefaults.PrefInputOffset));

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
        StopPlayMode();
        analysisCancel = true;
        audio.Dispose();
        if (project != null) AssetDatabase.SaveAssetIfDirty(project);
    }

    void OnLostFocus()
    {
        // 창 밖으로 포커스가 가면 키를 뗀 것으로 친다
        for (int l = 0; l < 2; l++)
        {
            if (!keyHeld[l]) continue;
            keyHeld[l] = false;
            OnPlayKeyUp(l);
        }
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
            if (playMode == PlayMode.Test)
            {
                if (audio.IsPlaying) testCore.Update(InputTime(), l => keyHeld[(int)l]);
                else EndTest();
            }
            else if (playMode == PlayMode.Record && !audio.IsPlaying)
            {
                EndRecord();
            }

            if (playMode == PlayMode.None && audio.IsPlaying && loopOn && loopEnd > loopStart + 0.05 && audio.Time >= loopEnd)
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

        if (analysisTask != null)
        {
            if (analysisTask.IsCompleted) FinishAnalysis();
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
        if (noteClicks && playMode == PlayMode.None)   // 테스트·녹화 중에는 정답 소리를 내지 않는다
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
        using (new EditorGUI.DisabledScope(project == null || project.clip == null))
        {
            if (GUILayout.Button(playMode == PlayMode.Test ? "■ 테스트 끝" : "▶ 테스트", EditorStyles.toolbarButton, GUILayout.Width(66)))
            {
                if (playMode == PlayMode.Test) StopPlayMode(); else StartTest(false);
            }
            if (GUILayout.Button(playMode == PlayMode.Record ? "■ 녹화 끝" : "● 녹화", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                if (playMode == PlayMode.Record) StopPlayMode(); else StartRecord();
            }
        }

        GUILayout.Space(8);
        editTool = (EditTool)GUILayout.Toolbar((int)editTool, ToolNames, EditorStyles.toolbarButton, GUILayout.Width(250));

        GUILayout.Space(8);
        GUILayout.Label("스냅", EditorStyles.miniLabel);
        int snapIndex = Math.Max(0, Array.IndexOf(SnapValues, snapDivision));
        snapIndex = EditorGUILayout.Popup(snapIndex, SnapNames, EditorStyles.toolbarPopup, GUILayout.Width(52));
        snapDivision = SnapValues[snapIndex];

        if (project != null && project.tempo != null && project.tempo.Count > 0)
        {
            GUILayout.Space(8);
            int seg = Math.Max(0, TempoMap.SegmentAt(project.tempo, audio.Time));
            GUILayout.Label(project.tempo.Count > 1 ? "BPM (" + (seg + 1) + "/" + project.tempo.Count + ")" : "BPM", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            double bpm = EditorGUILayout.DoubleField(project.tempo[seg].bpm, EditorStyles.toolbarTextField, GUILayout.Width(52));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(project, "BPM 수정");
                project.tempo[seg].bpm = Math.Max(1, Math.Min(999, bpm));
                EditorUtility.SetDirty(project);
            }
        }

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
        if (noteLabel != null && rulerLabel != null && laneHeader != null && tempoLabel != null && popupStyle != null && comboStyle != null) return;
        noteLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 9 };
        noteLabel.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
        rulerLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.LowerLeft };
        rulerLabel.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
        laneHeader = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
        laneHeader.normal.textColor = Color.white;
        tempoLabel = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperLeft, fontSize = 9 };
        tempoLabel.normal.textColor = ColTempo;
        popupStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
        comboStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
        comboStyle.normal.textColor = Color.white;
    }
}
