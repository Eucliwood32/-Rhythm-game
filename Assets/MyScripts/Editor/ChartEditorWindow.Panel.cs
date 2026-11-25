using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>채보 편집기 창: 오른쪽 패널 (선택한 노트, BPM · 격자, 재생 · 구간 반복, 테스트 · 녹화, 자동 생성, 채보 검사, 조작 도움말)</summary>
public partial class ChartEditorWindow
{
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
        foldTest = EditorGUILayout.Foldout(foldTest, "테스트 플레이 · 녹화", true, EditorStyles.foldoutHeader);
        if (foldTest) DrawTestSection();

        GUILayout.Space(6);
        foldAuto = EditorGUILayout.Foldout(foldAuto, "자동 생성", true, EditorStyles.foldoutHeader);
        if (foldAuto) DrawAutoSection();

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
        if ((NoteType)type != n.type) ChartEditing.ApplyType(n, (NoteType)type, DefaultHoldLength, RhythmDefaults.BarrageHitsPerSecond);
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

    void DrawTestSection()
    {
        if (playMode != PlayMode.None)
        {
            EditorGUILayout.HelpBox(playMode == PlayMode.Test
                ? "테스트 중: A(위) / B(아래)로 치세요. Space 또는 Esc로 끝냅니다."
                : "녹화 중: A / B를 누르면 노트가 찍힙니다. 길게 누르면 롱노트. Space 또는 Esc로 끝냅니다.", MessageType.Info);
            if (GUILayout.Button("끝내기")) StopPlayMode();
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("여기서 테스트 (P)")) StartTest(false);
        if (GUILayout.Button("처음부터 (Shift+P)")) StartTest(true);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("녹화 시작 (R)")) StartRecord();

        EditorGUI.BeginChangeCheck();
        inputOffsetMs = EditorGUILayout.IntSlider("입력 보정 (ms)", inputOffsetMs, -150, 150);
        if (EditorGUI.EndChangeCheck()) SaveInputOffset();
        EditorGUILayout.LabelField("입력이 늘 늦으면 +. 게임과 같은 값을 씁니다. 판정 범위와 체력·점수 규칙은 열려 있는 씬의 Judge·Referee 설정을 따릅니다(없으면 기본값).", EditorStyles.wordWrappedMiniLabel);

        recordSnap = EditorGUILayout.ToggleLeft("녹화한 노트를 격자에 맞추기", recordSnap);
        recordLongThreshold = EditorGUILayout.Slider("롱노트로 볼 누름 (초)", recordLongThreshold, 0.15f, 1f);

        if (testSummary != null)
        {
            GUILayout.Space(4);
            EditorGUILayout.LabelField("지난 테스트", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(testSummary, EditorStyles.wordWrappedMiniLabel);
            if (testHasError)
            {
                int suggest = Mathf.RoundToInt((float)testAvgError);
                string dir = suggest > 0 ? "늦게 침" : suggest < 0 ? "일찍 침" : "딱 맞음";
                EditorGUILayout.LabelField("평균 오차 " + suggest.ToString("+0;-0;0") + "ms (" + dir + ")", EditorStyles.wordWrappedMiniLabel);
                if (Math.Abs(suggest) >= 5 && GUILayout.Button("보정에 반영 (" + suggest.ToString("+0;-0;0") + "ms)"))
                {
                    inputOffsetMs = Mathf.Clamp(inputOffsetMs + suggest, -150, 150);
                    SaveInputOffset();
                    testHasError = false;
                    message = "입력 보정 = " + inputOffsetMs + "ms";
                }
            }
        }
    }

    void DrawAutoSection()
    {
        if (project.clip != null)
        {
            AudioImporter importer;
            if (!IsDecompressOnLoad(project.clip, out importer))
            {
                EditorGUILayout.HelpBox("곡의 Load Type을 Decompress On Load로 바꿔야 분석할 수 있습니다. (위쪽 경고의 '설정 바꾸기')", MessageType.Warning);
                return;
            }
        }

        autoDifficulty = EditorGUILayout.Popup("난이도", autoDifficulty, DifficultyNames);
        autoSensitivity = EditorGUILayout.IntSlider("감도", autoSensitivity, 0, 100);
        autoLane = (AutoLaneMode)EditorGUILayout.Popup("레인", (int)autoLane, AutoLaneNames);
        autoLongs = EditorGUILayout.ToggleLeft("롱노트 만들기 (길게 이어지는 소리)", autoLongs);
        if (autoLongs) autoMinLong = EditorGUILayout.Slider("  최소 길이 (초)", autoMinLong, 0.3f, 2f);
        autoBarrages = EditorGUILayout.ToggleLeft("연타 만들기 (빽빽하게 이어지는 소리)", autoBarrages);
        if (autoBarrages) autoMinBarrage = EditorGUILayout.IntSlider("  최소 개수", autoMinBarrage, 3, 20);
        using (new EditorGUI.DisabledScope(snapDivision <= 0))
        {
            autoSnap = EditorGUILayout.ToggleLeft("격자에 맞추기 (지금 스냅 " + SnapNames[Math.Max(0, Array.IndexOf(SnapValues, snapDivision))] + ")", autoSnap);
        }
        autoRange = EditorGUILayout.Popup("범위", autoRange, AutoRangeNames);
        if (autoRange == 1 && loopEnd <= loopStart + 0.05)
            EditorGUILayout.HelpBox("구간 반복 범위가 없습니다. [ / ] 로 정하세요.", MessageType.None);

        if (analysisTask != null)
        {
            Rect bar = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(true));
            EditorGUI.ProgressBar(bar, analysisProgress, "곡 분석 중... " + Mathf.RoundToInt(analysisProgress * 100) + "%");
            if (GUILayout.Button("취소"))
            {
                analysisCancel = true;
                generateWhenReady = false;
            }
        }
        else if (GUILayout.Button("자동 생성", GUILayout.Height(24)))
        {
            EditorApplication.delayCall += RequestGenerate;
        }
        EditorGUILayout.LabelField("결과는 초안입니다. 범위 안의 기존 노트를 바꾸고, Ctrl+Z 한 번으로 되돌릴 수 있습니다. 같은 곡은 두 번째부터 분석 없이 바로 만들어집니다.", EditorStyles.wordWrappedMiniLabel);
    }

    void DrawHelpSection()
    {
        GUILayout.Label(
            "레인 클릭: 노트 추가 (1 단노트 · 2 롱노트 · 3 연타 · 4 선택)\n" +
            "롱노트·연타: 레인을 위로 드래그\n" +
            "노트 드래그: 이동 (선택한 노트 함께)\n" +
            "끝 손잡이 드래그: 길이 조절\n" +
            "Alt 누른 채 드래그: 스냅 없이\n" +
            "선택 도구 / Shift+드래그: 범위 선택\n" +
            "레인 밖 빈 곳 드래그: 악보를 잡고 끌어 위치 이동\n" +
            "Ctrl+클릭: 선택 추가·해제   Ctrl+A: 전체 선택\n" +
            "Ctrl+C / Ctrl+V: 복사 / 판정선 위치에 붙여넣기\n" +
            "M: 좌우 반전   Q: 격자에 맞추기   Delete: 삭제\n" +
            "Ctrl+Z: 되돌리기   Ctrl+X / Ctrl+Y: 다시 실행\n" +
            "T: 탭 템포   [ / ]: 반복 시작 / 끝   L: 반복 켜기\n" +
            "휠: 시간 이동   Ctrl+휠: 확대·축소   BPM: 위쪽 칸에 숫자 입력\n" +
            "왼쪽 눈금·오른쪽 미니맵 클릭: 위치 이동\n" +
            "P: 여기서 테스트 (Shift+P 처음부터)   R: 키 녹화\n" +
            "테스트·녹화 중 A/B: 치기   Space·Esc: 끝내기\n" +
            "Space: 재생 / 일시정지",
            EditorStyles.wordWrappedMiniLabel);
    }
}
