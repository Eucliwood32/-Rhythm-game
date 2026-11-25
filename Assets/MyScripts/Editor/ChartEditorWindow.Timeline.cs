using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>채보 편집기 창: 세로 타임라인과 미니맵 그리기, 마우스 입력</summary>
public partial class ChartEditorWindow
{
    // ───────────────────────── 타임라인 ─────────────────────────

    TimelineLayout MakeLayout(float w, float h)
    {
        float usable = w - RulerWidth;
        return new TimelineLayout
        {
            judgeY = h - JudgeFromBottom,
            viewTime = audio.SmoothTime,
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
        if (editTool != EditTool.Select)
        {
            float laneL = layout.centerX - layout.laneWidth, laneR = layout.centerX + layout.laneWidth;
            EditorGUIUtility.AddCursorRect(new Rect(RulerWidth, 0, Mathf.Max(0, laneL - RulerWidth), r.height), MouseCursor.Pan);
            EditorGUIUtility.AddCursorRect(new Rect(laneR, 0, Mathf.Max(0, mainWidth - laneR), r.height), MouseCursor.Pan);
        }
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
        EditorGUI.DrawRect(new Rect(leftX, 0, L.laneWidth, h), new Color(GameColors.LaneUpper.r, GameColors.LaneUpper.g, GameColors.LaneUpper.b, 0.07f));
        EditorGUI.DrawRect(new Rect(L.centerX, 0, L.laneWidth, h), new Color(GameColors.LaneLower.r, GameColors.LaneLower.g, GameColors.LaneLower.b, 0.07f));
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
        if (playMode != PlayMode.None) DrawPlayOverlay(L, leftX, lanesW);
    }

    void DrawPlayOverlay(TimelineLayout L, float leftX, float lanesW)
    {
        double now = EditorApplication.timeSinceStartup;
        if (playMode == PlayMode.Record)
        {
            float blink = (float)(0.6 + 0.4 * Math.Sin(now * 6));
            GUI.Label(new Rect(leftX, 24, lanesW, 22), "● REC  " + recCount, new GUIStyle(popupStyle) { normal = { textColor = new Color(1f, 0.3f, 0.3f, blink) } });
        }
        else
        {
            if (testScore.Combo >= 2) GUI.Label(new Rect(leftX, 26, lanesW, 34), testScore.Combo.ToString(), comboStyle);
            GUI.Label(new Rect(leftX, 60, lanesW, 18),
                "P " + testScore.Perfect + "  G " + testScore.Good + "  M " + testScore.Miss + "   " + testScore.Score + "점   HP " + testScore.Health.ToString("0"),
                rulerLabel);
        }

        for (int l = 0; l < 2; l++)
        {
            if (popupText[l] == null || now - popupAt[l] > 0.6) continue;
            float a = 1f - (float)((now - popupAt[l]) / 0.6);
            Color c = popupColor[l]; c.a = a;
            var st = new GUIStyle(popupStyle);
            st.normal.textColor = c;
            float x = L.LaneX((Lane)l);
            GUI.Label(new Rect(x - 80, L.judgeY - 34, 160, 20), popupText[l], st);
        }

        for (int l = 0; l < 2; l++)
        {
            if (!keyHeld[l]) continue;
            float x = L.LaneX((Lane)l);
            EditorGUI.DrawRect(new Rect(x - L.laneWidth / 2f, L.judgeY - 2, L.laneWidth, 5), new Color(1f, 1f, 1f, 0.8f));
        }
    }

    void DrawNote(NoteData n, TimelineLayout L, float h, bool selected)
    {
        float x = L.LaneX(n.lane);
        float hw = L.NoteHalfWidth;
        float y = L.YAt(n.time);
        bool hasLength = ChartEditing.HasLength(n);
        float ey = hasLength ? L.YAt(n.time + n.duration) : y;
        if (y < -20f || ey > h + 20f) return;
        if (playMode == PlayMode.Test && testHit.Contains(n)) return;   // 맞힌 노트는 사라진다

        Color col = n.type == NoteType.Barrage ? GameColors.Barrage : (n.lane == Lane.Upper ? GameColors.LaneUpper : GameColors.LaneLower);

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

        if (playMode == PlayMode.Test && testMiss.Contains(n))
        {
            EditorGUI.DrawRect(head, new Color(0.1f, 0.1f, 0.12f, 0.65f));
            DrawOutline(head, ColIssue, 2);
        }
        else if (issueNotes.Contains(n))
        {
            DrawOutline(new Rect(head.x - 4, head.y - 4, head.width + 8, head.height + 8), ColIssue, 2);
        }

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
            float half = PeakBetween(i0, i1) * maxHalf;
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
                float half = PeakBetween(i0, i1 - 1) * (m.width / 2f - 3f);
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
            Color c = issueNotes.Contains(n) ? ColIssue : n.type == NoteType.Barrage ? GameColors.Barrage : n.lane == Lane.Upper ? GameColors.LaneUpper : GameColors.LaneLower;
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
        if (playMode != PlayMode.None && type != EventType.ScrollWheel) return;   // 테스트·녹화 중에는 편집하지 않는다
        if (playMode != PlayMode.None && type == EventType.ScrollWheel) { if (inside) e.Use(); return; }

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
        if (editTool == EditTool.Select || e.shift)
        {
            if (!additive) selection.Clear();
            drag = DragMode.Box;
            return;
        }
        if (lane < 0)
        {
            // 레인 밖 빈 곳: 악보를 손으로 잡고 끄는 것처럼 재생 위치를 픽셀 단위로 옮긴다
            drag = DragMode.Pan;
            dragStartTime = audio.Time;
            return;
        }

        // 새 노트
        BeginUndo("노트 추가");
        var note = new NoteData
        {
            time = SnapTime(L.TimeAt(mp.y), e),
            lane = (Lane)lane,
            type = editTool == EditTool.Long ? NoteType.Long : editTool == EditTool.Barrage ? NoteType.Barrage : NoteType.Tap
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

            case DragMode.Pan:
                // 아래로 끌면 악보가 따라 내려오면서 뒤쪽(미래)이 보인다
                audio.Seek(dragStartTime + (mp.y - dragStart.y) / L.pps);
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
                ChartEditing.SetEnd(dragNote, SnapTime(L.TimeAt(mp.y), e), RhythmDefaults.BarrageHitsPerSecond);
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
                    ChartEditing.SetEnd(dragNote, dragNote.time + (snapDivision > 0 ? beat : DefaultHoldLength), RhythmDefaults.BarrageHitsPerSecond);
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
}
