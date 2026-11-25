using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>채보 편집기 창: 테스트 플레이와 키 녹화</summary>
public partial class ChartEditorWindow
{
    // ───────────────────────── 테스트 플레이 · 키 녹화 ─────────────────────────

    double InputTime()
    {
        return audio.SmoothTime - inputOffsetMs / 1000.0;
    }

    void SaveInputOffset()
    {
        PlayerPrefs.SetFloat(RhythmDefaults.PrefInputOffset, inputOffsetMs);
        PlayerPrefs.Save();
    }

    void HandlePlayKeys(Event e)
    {
        if (e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand)
        {
            e.Use();   // 테스트·녹화 중에는 되돌리기·복사 같은 명령을 막는다
            return;
        }
        if (e.type != EventType.KeyDown && e.type != EventType.KeyUp) return;
        if (e.control || e.command || e.alt) return;

        int lane = e.keyCode == KeyCode.A ? 0 : e.keyCode == KeyCode.B ? 1 : -1;
        if (lane >= 0)
        {
            if (e.type == EventType.KeyDown)
            {
                if (!keyHeld[lane])   // 키를 누르고 있으면 반복 입력이 들어오므로 처음 한 번만
                {
                    keyHeld[lane] = true;
                    OnPlayKeyDown(lane);
                }
            }
            else if (keyHeld[lane])
            {
                keyHeld[lane] = false;
                OnPlayKeyUp(lane);
            }
            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Escape) StopPlayMode();
            e.Use();
        }
    }

    void OnPlayKeyDown(int lane)
    {
        double t = InputTime();
        if (playMode == PlayMode.Test)
        {
            testCore.Press((Lane)lane, t);
        }
        else if (playMode == PlayMode.Record)
        {
            var note = new NoteData { time = RecordTime(t), lane = (Lane)lane, type = NoteType.Tap };
            Undo.RecordObject(project, "키 녹화");
            project.notes.Add(note);
            EditorUtility.SetDirty(project);
            recNote[lane] = note;
            recPress[lane] = t;
            recCount++;
            ShowPopup(lane, "●", lane == 0 ? GameColors.LaneUpper : GameColors.LaneLower);
        }
    }

    void OnPlayKeyUp(int lane)
    {
        if (playMode != PlayMode.Record || recNote[lane] == null) return;
        NoteData note = recNote[lane];
        recNote[lane] = null;
        double t = InputTime();
        if (t - recPress[lane] < recordLongThreshold) return;

        double end = RecordTime(t);
        if (end - note.time < 0.1) return;
        Undo.RecordObject(project, "키 녹화");
        note.type = NoteType.Long;
        note.duration = ChartEditing.Round(end - note.time);
        EditorUtility.SetDirty(project);
    }

    double RecordTime(double t)
    {
        if (recordSnap && snapDivision > 0) return TempoMap.Snap(project.tempo, Math.Max(0, t), snapDivision);
        return ChartEditing.Round(Math.Max(0, t));
    }

    void ShowPopup(int lane, string text, Color color)
    {
        popupText[lane] = text;
        popupColor[lane] = color;
        popupAt[lane] = EditorApplication.timeSinceStartup;
    }

    void StartTest(bool fromBeginning)
    {
        if (project == null || project.clip == null) return;
        StopPlayMode();

        double judgeFrom = fromBeginning ? 0 : audio.Time;
        double start = fromBeginning ? 0 : Math.Max(0, judgeFrom - TestLeadIn);

        testLanes = new[] { new List<NoteData>(), new List<NoteData>() };
        foreach (NoteData n in project.notes) testLanes[(int)n.lane].Add(n);
        foreach (var list in testLanes) ChartMath.SortNotes(list);

        testCore = new JudgeCore(l => testLanes[(int)l]);
        // 열려 있는 씬의 Judge·Referee 설정을 그대로 쓴다 (없으면 기본값)
        Judge sceneJudge = FindAnyObjectByType<Judge>();
        Referee sceneReferee = FindAnyObjectByType<Referee>();
        if (sceneJudge != null) sceneJudge.ApplySettings(testCore);
        testScore = sceneReferee != null ? sceneReferee.CreateScoreKeeper() : new ScoreKeeper();
        testSettingsFrom = sceneJudge != null || sceneReferee != null ? "씬의 Judge·Referee 설정" : "기본 설정 (씬에 Judge 없음)";
        testDeadAt = -1;
        testCore.Judged += OnTestJudged;
        testCore.BarrageHit += (n, l, hits, need) => ShowPopup((int)l, "연타 " + hits, GameColors.Barrage);
        testCore.StartAt(judgeFrom);

        testHit.Clear();
        testMiss.Clear();
        testErrCount = 0;
        testErrSum = 0;
        for (int l = 0; l < 2; l++) { keyHeld[l] = false; popupText[l] = null; }
        selection.Clear();

        playMode = PlayMode.Test;
        audio.Seek(start);
        audio.Play();
        message = "테스트 플레이: A / B로 치세요. Space 또는 Esc로 끝";
        GUIUtility.keyboardControl = 0;   // 숫자 칸에 커서가 있으면 A/B가 거기로 들어가므로 뺀다
        Focus();
    }

    void OnTestJudged(NoteData n, Judgment r, Lane lane, double errorMs, JudgeKind kind)
    {
        if (kind == JudgeKind.HoldComplete)
        {
            testHit.Add(n);
            return;
        }

        testScore.Add(r);
        if (testScore.IsDead && testDeadAt < 0) testDeadAt = audio.Time;

        if (r == Judgment.Miss)
        {
            testMiss.Add(n);
        }
        else
        {
            bool holdingLong = kind == JudgeKind.Hit && n.type == NoteType.Long && n.duration > 0;
            if (!holdingLong) testHit.Add(n);
            if (kind == JudgeKind.Hit)
            {
                testErrSum += errorMs;
                testErrCount++;
            }
        }

        string text = RhythmDefaults.JudgmentText(r);
        if (kind == JudgeKind.Hit && r != Judgment.Miss) text += "  " + errorMs.ToString("+0;-0;0") + "ms";
        ShowPopup((int)lane, text, GameColors.ForJudgment(r));
    }

    void EndTest()
    {
        playMode = PlayMode.None;
        audio.Pause();
        for (int l = 0; l < 2; l++) keyHeld[l] = false;

        double acc = GameMath.Accuracy(testScore.Perfect, testScore.Good, testScore.Miss);
        bool survived = testDeadAt < 0;
        testSummary = "Perfect " + testScore.Perfect + " / Good " + testScore.Good + " / Miss " + testScore.Miss +
            "\n점수 " + testScore.Score + "   최대 콤보 " + testScore.MaxCombo + "   정확도 " + (acc * 100).ToString("0.0") + "%   등급 " + GameMath.Rank(acc, survived) +
            (survived ? "" : "\n" + ChartEditing.FormatTime(testDeadAt) + "에서 체력 0 (게임이라면 GAME OVER)") +
            "\n(" + testSettingsFrom + ")";
        testHasError = testErrCount >= 3;
        testAvgError = testErrCount > 0 ? testErrSum / testErrCount : 0;
        message = "테스트 끝: P " + testScore.Perfect + " / G " + testScore.Good + " / M " + testScore.Miss + ", " + testScore.Score + "점";

        testHit.Clear();
        testMiss.Clear();
        testCore = null;
        Repaint();
    }

    void StartRecord()
    {
        if (project == null || project.clip == null) return;
        StopPlayMode();

        Undo.IncrementCurrentGroup();
        recUndoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("키 녹화");
        recCount = 0;
        for (int l = 0; l < 2; l++) { keyHeld[l] = false; recNote[l] = null; popupText[l] = null; }
        selection.Clear();

        playMode = PlayMode.Record;
        audio.Seek(Math.Max(0, audio.Time - TestLeadIn));
        audio.Play();
        message = "녹화 중: A / B를 누르세요 (길게 누르면 롱노트). Space 또는 Esc로 끝";
        GUIUtility.keyboardControl = 0;
        Focus();
    }

    void EndRecord()
    {
        for (int l = 0; l < 2; l++)
        {
            if (keyHeld[l]) OnPlayKeyUp(l);
            keyHeld[l] = false;
            recNote[l] = null;
        }
        playMode = PlayMode.None;
        audio.Pause();

        Undo.RecordObject(project, "키 녹화");
        ChartEditing.Sort(project.notes);
        Undo.CollapseUndoOperations(recUndoGroup);
        EditorUtility.SetDirty(project);
        message = "녹화 " + recCount + "개 추가 (Ctrl+Z 한 번으로 모두 취소)";
        Repaint();
    }

    void StopPlayMode()
    {
        if (playMode == PlayMode.Test) EndTest();
        else if (playMode == PlayMode.Record) EndRecord();
    }
}
