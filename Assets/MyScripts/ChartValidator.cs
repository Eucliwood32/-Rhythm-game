using System;
using System.Collections.Generic;

// 채보 검사. 게임에서 판정이 꼬이거나 칠 수 없는 배치를 찾는다. Unity API를 쓰지 않는다.

public class ChartIssue
{
    public NoteData note;     // 문제가 있는 노트 (클릭하면 이 노트로 이동)
    public double time;
    public string message;
    public bool serious;      // true면 게임에서 판정이 꼬일 수 있는 문제
}

public static class ChartValidator
{
    /// <summary>같은 레인에서 이 간격(초)보다 가까우면 경고한다. (판정의 Good 범위와 같은 값)</summary>
    public const double MinGap = RhythmDefaults.GoodWindow;
    /// <summary>연타가 초당 이 횟수보다 많이 요구하면 경고한다.</summary>
    public const double MaxBarragePerSecond = 15;

    public static List<ChartIssue> Check(IList<NoteData> notes, double songLength)
    {
        var issues = new List<ChartIssue>();

        for (int l = 0; l < 2; l++)
        {
            var lane = new List<NoteData>();
            foreach (NoteData n in notes) if ((int)n.lane == l) lane.Add(n);
            ChartMath.SortNotes(lane);
            string laneName = l == 0 ? "A" : "B";

            for (int i = 1; i < lane.Count; i++)
            {
                NoteData prev = lane[i - 1], cur = lane[i];
                bool prevHasLength = prev.type != NoteType.Tap && prev.duration > 0;
                double prevEnd = prev.time + (prevHasLength ? prev.duration : 0);

                if (Math.Abs(cur.time - prev.time) < 0.0005)
                {
                    issues.Add(new ChartIssue { note = cur, time = cur.time, serious = true, message = laneName + " 레인에 같은 시각 노트가 두 개" });
                }
                else if (prevHasLength && cur.time <= prevEnd)
                {
                    string what = prev.type == NoteType.Long ? "롱노트" : "연타";
                    issues.Add(new ChartIssue { note = cur, time = cur.time, serious = true, message = laneName + " 레인 " + what + " 구간 안에 노트가 있음" });
                }
                else if (cur.time - prevEnd < MinGap)
                {
                    issues.Add(new ChartIssue { note = cur, time = cur.time, serious = false, message = laneName + " 레인 앞 노트와 너무 붙음 (" + ((cur.time - prevEnd) * 1000).ToString("0") + "ms)" });
                }
            }
        }

        foreach (NoteData n in notes)
        {
            bool hasLength = n.type != NoteType.Tap;
            if (hasLength && n.duration <= 0)
                issues.Add(new ChartIssue { note = n, time = n.time, serious = true, message = (n.type == NoteType.Long ? "롱노트" : "연타") + " 길이가 0" });

            if (n.type == NoteType.Barrage && n.duration > 0 && n.count / n.duration > MaxBarragePerSecond)
                issues.Add(new ChartIssue { note = n, time = n.time, serious = false, message = "연타 횟수가 너무 많음 (초당 " + (n.count / n.duration).ToString("0.0") + "회)" });

            if (songLength > 0)
            {
                double end = n.time + (hasLength ? Math.Max(0, n.duration) : 0);
                if (n.time > songLength)
                    issues.Add(new ChartIssue { note = n, time = n.time, serious = true, message = "곡이 끝난 뒤에 있는 노트" });
                else if (end > songLength)
                    issues.Add(new ChartIssue { note = n, time = n.time, serious = false, message = "곡 끝을 넘어가는 길이" });
            }
        }

        issues.Sort((a, b) => a.time.CompareTo(b.time));
        return issues;
    }
}
