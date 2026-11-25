using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>채보 편집기 창: 키보드 단축키, 선택·복사·붙여넣기·반전·격자 맞추기, BPM · 탭 템포 · 구간 반복, 되돌리기 묶음</summary>
public partial class ChartEditorWindow
{
    // ───────────────────────── 키보드와 명령 ─────────────────────────

    void HandleKeyboard()
    {
        Event e = Event.current;
        if (EditorGUIUtility.editingTextField) return;   // 숫자 칸을 고치는 중이면 손대지 않는다

        if (playMode != PlayMode.None)
        {
            HandlePlayKeys(e);
            return;
        }

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
                    case KeyCode.P: StartTest(e.shift); break;
                    case KeyCode.R: StartRecord(); break;
                    case KeyCode.LeftBracket: SetLoopStart(); break;
                    case KeyCode.RightBracket: SetLoopEnd(); break;
                    case KeyCode.L: loopOn = !loopOn; message = loopOn ? "구간 반복 켬" : "구간 반복 끔"; break;
                    case KeyCode.Alpha1:
                    case KeyCode.Keypad1: editTool = EditTool.Tap; break;
                    case KeyCode.Alpha2:
                    case KeyCode.Keypad2: editTool = EditTool.Long; break;
                    case KeyCode.Alpha3:
                    case KeyCode.Keypad3: editTool = EditTool.Barrage; break;
                    case KeyCode.Alpha4:
                    case KeyCode.Keypad4: editTool = EditTool.Select; break;
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
        ChartEditing.Quantize(selection, project.tempo, snapDivision, RhythmDefaults.BarrageHitsPerSecond);
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
}
