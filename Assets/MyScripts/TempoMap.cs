using System;
using System.Collections.Generic;

// BPM 구간 계산. Unity API를 쓰지 않아서 따로 테스트할 수 있다.
// 구간마다 시작 시각과 BPM이 있고, 각 구간의 시작이 그 구간 첫 마디의 첫 박이다(4/4 고정).
// 첫 구간의 시작 시각 = 곡의 첫 박 위치.

/// <summary>BPM 구간 하나: 이 시각부터 이 BPM.</summary>
[Serializable]
public class TempoPoint
{
    public double time;        // 구간 시작 (초)
    public double bpm = 120;
}

public enum GridKind { Measure, Beat, Sub }

/// <summary>격자선 하나.</summary>
public struct GridLine
{
    public double time;
    public GridKind kind;
    public int measure;     // 마디 번호 (1부터). Measure 선에서만 의미가 있다.
}

public static class TempoMap
{
    public const int BeatsPerMeasure = 4;

    public static void Sort(List<TempoPoint> points)
    {
        points.Sort((a, b) => a.time.CompareTo(b.time));
    }

    /// <summary>time이 속한 구간 번호. 첫 구간보다 앞이면 0(첫 구간을 앞으로 늘려 쓴다). 구간이 없으면 -1.</summary>
    public static int SegmentAt(List<TempoPoint> points, double time)
    {
        if (points == null || points.Count == 0) return -1;
        int seg = 0;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].time <= time + 1e-9) seg = i; else break;
        }
        return seg;
    }

    public static double BeatLength(TempoPoint p)
    {
        return 60.0 / Math.Max(1.0, p.bpm);
    }

    /// <summary>
    /// 격자에 맞춘 시각. division은 한 박을 몇 개로 나눌지 (1 = 1/4음표, 2 = 1/8, 3 = 1/12, 4 = 1/16 ...).
    /// division이 0 이하이거나 BPM 구간이 없으면 1ms 단위로만 반올림한다.
    /// </summary>
    public static double Snap(List<TempoPoint> points, double time, int division)
    {
        if (division <= 0 || points == null || points.Count == 0) return ChartMath.RoundMs(time);

        int seg = SegmentAt(points, time);
        TempoPoint p = points[seg];
        double step = BeatLength(p) / division;
        double snapped = p.time + Math.Round((time - p.time) / step) * step;

        // 다음 구간을 넘어가면 다음 구간의 시작에 붙인다
        if (seg + 1 < points.Count && snapped > points[seg + 1].time) snapped = points[seg + 1].time;

        snapped = ChartMath.RoundMs(snapped);
        return Math.Max(0, snapped);
    }

    /// <summary>[from, to] 사이의 격자선을 output에 담는다. 박 간격이 minSpacing초보다 촘촘한 분할선은 빼고 마디·박만 남긴다.</summary>
    public static void GridLines(List<TempoPoint> points, double from, double to, int division, double minSubSpacing, List<GridLine> output)
    {
        output.Clear();
        if (points == null || points.Count == 0) return;
        if (division <= 0) division = 1;

        int measuresBefore = 0;
        for (int seg = 0; seg < points.Count; seg++)
        {
            TempoPoint p = points[seg];
            double segStart = seg == 0 ? double.NegativeInfinity : p.time;
            double segEnd = seg + 1 < points.Count ? points[seg + 1].time : double.PositiveInfinity;
            double beat = BeatLength(p);

            if (segEnd > from && segStart < to)
            {
                int div = beat / division >= minSubSpacing ? division : 1;
                double step = beat / div;
                int perMeasure = BeatsPerMeasure * div;

                double lo = Math.Max(from, segStart);
                double hi = Math.Min(to, segEnd);
                long k0 = (long)Math.Ceiling((lo - p.time) / step - 1e-9);
                long k1 = (long)Math.Floor((hi - p.time) / step + 1e-9);
                for (long k = k0; k <= k1; k++)
                {
                    double t = p.time + k * step;
                    if (t >= segEnd - 1e-9) break;
                    GridKind kind = Mod(k, perMeasure) == 0 ? GridKind.Measure : Mod(k, div) == 0 ? GridKind.Beat : GridKind.Sub;
                    int measure = measuresBefore + (int)FloorDiv(k, perMeasure) + 1;
                    output.Add(new GridLine { time = t, kind = kind, measure = measure });
                }
            }

            if (seg + 1 < points.Count)
            {
                double beatsInSeg = (points[seg + 1].time - p.time) / beat;
                measuresBefore += Math.Max(1, (int)Math.Ceiling(beatsInSeg / BeatsPerMeasure - 1e-6));
            }
        }
    }

    /// <summary>[from, to) 사이의 박 시각 (메트로놈용). isMeasure는 마디의 첫 박인지.</summary>
    public static void Beats(List<TempoPoint> points, double from, double to, List<GridLine> output)
    {
        GridLines(points, from, to, 1, 0, output);
        output.RemoveAll(g => g.time >= to);
    }

    /// <summary>구간을 추가한다(시간순 유지). 같은 시각에 이미 있으면 그 구간의 BPM만 바꾼다. 추가·변경된 구간 번호를 돌려준다.</summary>
    public static int AddPoint(List<TempoPoint> points, double time, double bpm)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (Math.Abs(points[i].time - time) < 0.0005)
            {
                points[i].bpm = bpm;
                return i;
            }
        }
        var p = new TempoPoint { time = time, bpm = bpm };
        points.Add(p);
        Sort(points);
        return points.IndexOf(p);
    }

    static long Mod(long a, long m)
    {
        long r = a % m;
        return r < 0 ? r + m : r;
    }

    static long FloorDiv(long a, long b)
    {
        long q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }
}
