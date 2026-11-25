using System;
using System.Collections.Generic;

// 채보 편집기의 계산 코드. Unity API를 쓰지 않아서 Unity 없이도 테스트할 수 있다.
// 화면 좌표 <-> 곡 시간 변환, 노트 클릭 판정, 이동, 길이 조절, 정렬을 담당한다.

/// <summary>노트의 어느 부분을 눌렀는지.</summary>
public enum NotePart
{
    None,
    Head,   // 노트 머리 (판정되는 시각)
    Body,   // 롱노트·연타의 몸통
    Tail    // 롱노트·연타의 끝 (잡고 끌면 길이 조절)
}

/// <summary>
/// 세로 타임라인의 배치. 판정선이 아래에 있고, 위로 갈수록 미래 시각이다.
/// 판정선 위치의 시각이 viewTime(= 재생 위치)이다.
/// </summary>
public struct TimelineLayout
{
    public float judgeY;     // 판정선의 y
    public double viewTime;  // 판정선 위치의 곡 시간(초)
    public float pps;        // 1초당 픽셀 수 (확대 정도)
    public float centerX;    // 두 레인 사이 경계의 x
    public float laneWidth;  // 레인 하나의 폭

    public float LaneX(Lane lane)
    {
        return centerX + (lane == Lane.Upper ? -laneWidth / 2f : laneWidth / 2f);
    }

    /// <summary>노트 막대의 반폭.</summary>
    public float NoteHalfWidth
    {
        get { return Math.Max(8f, laneWidth / 2f - 6f); }
    }

    public double TimeAt(float y)
    {
        return viewTime + (judgeY - y) / pps;
    }

    public float YAt(double time)
    {
        return judgeY - (float)((time - viewTime) * pps);
    }

    /// <summary>x 위치의 레인. 레인 밖이면 -1. (0 = A/상단, 1 = B/하단)</summary>
    public int LaneAt(float x)
    {
        if (x < centerX - laneWidth || x > centerX + laneWidth) return -1;
        return x < centerX ? 0 : 1;
    }
}

/// <summary>드래그를 시작할 때의 노트 상태. 이동은 항상 이 상태를 기준으로 계산한다.</summary>
public class NoteSnapshot
{
    public NoteData note;
    public double time;
    public Lane lane;
}

public static class ChartEditing
{
    public const float HeadHalfHeight = 7f;   // 노트 머리 높이의 절반 (px)
    public const float TailGrab = 6f;         // 끝 손잡이를 잡을 수 있는 범위 (px)

    /// <summary>시각을 1ms 단위로 반올림한다.</summary>
    public static double Round(double t)
    {
        return ChartMath.RoundMs(t);
    }

    public static bool HasLength(NoteData n)
    {
        return n.type != NoteType.Tap && n.duration > 0;
    }

    public static int BarrageCount(double duration, double hitsPerSecond)
    {
        return RhythmDefaults.BarrageCount(duration, hitsPerSecond);
    }

    /// <summary>
    /// (x, y)에 있는 노트를 찾는다. 겹치면 머리 > 끝 손잡이 > 몸통 순으로 가깝게 누른 것을 고른다.
    /// 없으면 -1.
    /// </summary>
    public static int HitTest(IList<NoteData> notes, TimelineLayout layout, float x, float y, out NotePart part)
    {
        part = NotePart.None;
        int best = -1;
        float bestScore = float.MaxValue;
        float hw = layout.NoteHalfWidth;

        for (int i = 0; i < notes.Count; i++)
        {
            NoteData n = notes[i];
            if (Math.Abs(x - layout.LaneX(n.lane)) > hw + 2f) continue;

            float headY = layout.YAt(n.time);
            float headDist = Math.Abs(y - headY);
            if (headDist <= HeadHalfHeight + 2f && headDist < bestScore)
            {
                best = i; bestScore = headDist; part = NotePart.Head;
            }

            if (!HasLength(n)) continue;

            float endY = layout.YAt(n.time + n.duration);
            float tailDist = Math.Abs(y - endY);
            if (tailDist <= TailGrab && tailDist + 0.5f < bestScore)
            {
                best = i; bestScore = tailDist + 0.5f; part = NotePart.Tail;
            }
            else if (y < headY && y > endY && 100f < bestScore)
            {
                best = i; bestScore = 100f; part = NotePart.Body;
            }
        }
        return best;
    }

    /// <summary>사각형(화면 좌표)에 걸치는 노트들. 롱노트·연타는 몸통이 걸쳐도 포함한다.</summary>
    public static List<NoteData> InRect(IList<NoteData> notes, TimelineLayout layout, float xMin, float yMin, float xMax, float yMax)
    {
        var result = new List<NoteData>();
        double tFrom = layout.TimeAt(yMax);
        double tTo = layout.TimeAt(yMin);
        float hw = layout.NoteHalfWidth;

        foreach (NoteData n in notes)
        {
            float lx = layout.LaneX(n.lane);
            if (lx + hw < xMin || lx - hw > xMax) continue;
            if (n.time + n.duration < tFrom || n.time > tTo) continue;
            result.Add(n);
        }
        return result;
    }

    public static List<NoteSnapshot> Snapshot(IEnumerable<NoteData> notes)
    {
        var list = new List<NoteSnapshot>();
        foreach (NoteData n in notes)
        {
            list.Add(new NoteSnapshot { note = n, time = n.time, lane = n.lane });
        }
        return list;
    }

    /// <summary>이동량을 줄여서 어떤 노트도 0초 앞으로 가지 않게 한다.</summary>
    public static double ClampDelta(List<NoteSnapshot> snapshots, double delta)
    {
        if (snapshots.Count == 0) return delta;
        double min = double.MaxValue;
        foreach (NoteSnapshot s in snapshots) min = Math.Min(min, s.time);
        return Math.Max(delta, -min);
    }

    /// <summary>드래그 시작 상태를 기준으로 노트들을 옮긴다. flipLane이면 레인을 서로 바꾼다.</summary>
    public static void Move(List<NoteSnapshot> snapshots, double delta, bool flipLane)
    {
        foreach (NoteSnapshot s in snapshots)
        {
            s.note.time = Math.Max(0, Round(s.time + delta));
            s.note.lane = flipLane ? Other(s.lane) : s.lane;
        }
    }

    public static Lane Other(Lane lane)
    {
        return lane == Lane.Upper ? Lane.Lower : Lane.Upper;
    }

    /// <summary>롱노트·연타의 끝 시각을 정한다. 연타는 길이에 맞춰 필요 횟수도 다시 계산한다.</summary>
    public static void SetEnd(NoteData n, double endTime, double hitsPerSecond)
    {
        n.duration = Round(Math.Max(0, endTime - n.time));
        if (n.type == NoteType.Barrage) n.count = BarrageCount(n.duration, hitsPerSecond);
    }

    /// <summary>노트 종류를 바꾸고, 종류에 맞게 길이와 횟수를 정리한다.</summary>
    public static void ApplyType(NoteData n, NoteType type, double defaultLength, double hitsPerSecond)
    {
        n.type = type;
        if (type == NoteType.Tap)
        {
            n.duration = 0;
            n.count = 0;
            return;
        }
        if (n.duration <= 0) n.duration = defaultLength;
        if (type == NoteType.Long) n.count = 0;
        else if (n.count <= 0) n.count = BarrageCount(n.duration, hitsPerSecond);
    }

    /// <summary>시간순, 같은 시각이면 A 레인 먼저.</summary>
    public static void Sort(List<NoteData> notes)
    {
        ChartMath.SortNotes(notes);
    }

    /// <summary>내보내기용 복사본. 시간순으로 정렬하고 값을 1ms 단위로 정리한다.</summary>
    public static List<NoteData> CleanCopy(IEnumerable<NoteData> notes)
    {
        var list = new List<NoteData>();
        foreach (NoteData n in notes)
        {
            bool hasLength = n.type != NoteType.Tap;
            NoteData c = n.Clone();
            c.time = Round(Math.Max(0, n.time));
            c.duration = hasLength ? Round(Math.Max(0, n.duration)) : 0;
            c.count = n.type == NoteType.Barrage ? Math.Max(1, n.count) : 0;
            list.Add(c);
        }
        Sort(list);
        return list;
    }

    public static NoteData Clone(NoteData n)
    {
        return n.Clone();
    }

    /// <summary>복사: 가장 앞 노트를 0초로 한 복사본.</summary>
    public static List<NoteData> CopyRelative(IEnumerable<NoteData> notes)
    {
        var list = new List<NoteData>();
        double first = double.MaxValue;
        foreach (NoteData n in notes) first = Math.Min(first, n.time);
        foreach (NoteData n in notes)
        {
            NoteData c = Clone(n);
            c.time = Round(n.time - first);
            list.Add(c);
        }
        return list;
    }

    /// <summary>붙여넣기: 복사본을 time 위치에 놓은 새 노트들.</summary>
    public static List<NoteData> PasteAt(IEnumerable<NoteData> clipboard, double time)
    {
        var list = new List<NoteData>();
        foreach (NoteData n in clipboard)
        {
            NoteData c = Clone(n);
            c.time = Round(Math.Max(0, time + n.time));
            list.Add(c);
        }
        return list;
    }

    /// <summary>좌우 반전: A ↔ B.</summary>
    public static void Mirror(IEnumerable<NoteData> notes)
    {
        foreach (NoteData n in notes) n.lane = Other(n.lane);
    }

    /// <summary>격자에 맞추기: 시작과 끝(롱노트·연타)을 격자에 붙인다.</summary>
    public static void Quantize(IEnumerable<NoteData> notes, List<TempoPoint> tempo, int division, double hitsPerSecond)
    {
        foreach (NoteData n in notes)
        {
            double end = n.time + n.duration;
            n.time = TempoMap.Snap(tempo, n.time, division);
            if (HasLength(n))
            {
                double snappedEnd = TempoMap.Snap(tempo, end, division);
                if (snappedEnd <= n.time) snappedEnd = end;
                SetEnd(n, snappedEnd, hitsPerSecond);
            }
        }
    }

    /// <summary>곡 시간을 m:ss.fff 형태로.</summary>
    public static string FormatTime(double t)
    {
        if (t < 0) t = 0;
        int m = (int)(t / 60);
        double s = t - m * 60;
        return m + ":" + s.ToString("00.000");
    }
}

/// <summary>탭 템포: 박자에 맞춰 키를 누르면 BPM을 계산한다. 2초 넘게 쉬면 처음부터 다시 센다.</summary>
public class TapTempo
{
    const int MaxTaps = 16;
    const double ResetAfter = 2.0;
    readonly List<double> taps = new List<double>();

    public int Count { get { return taps.Count; } }

    /// <summary>계산된 BPM. 두 번 이상 눌러야 나온다(그 전에는 0).</summary>
    public double Bpm
    {
        get
        {
            if (taps.Count < 2) return 0;
            double span = taps[taps.Count - 1] - taps[0];
            return span > 0 ? 60.0 * (taps.Count - 1) / span : 0;
        }
    }

    public void Tap(double now)
    {
        if (taps.Count > 0 && now - taps[taps.Count - 1] > ResetAfter) taps.Clear();
        taps.Add(now);
        if (taps.Count > MaxTaps) taps.RemoveAt(0);
    }

    public void Reset()
    {
        taps.Clear();
    }
}
