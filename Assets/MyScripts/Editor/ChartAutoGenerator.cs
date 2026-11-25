using System;
using System.Collections.Generic;

// 채보 자동 생성. Unity API를 쓰지 않아서 백그라운드 스레드에서 돌리고 따로 테스트할 수 있다.
// 1) Analyze: 곡을 짧은 구간으로 나눠 소리가 갑자기 커지는 정도(스펙트럼 flux), 음높이 중심, 음량을 구한다. (무겁다, 곡마다 한 번)
// 2) Generate: 분석 결과에서 소리가 터지는 지점(온셋)을 골라 노트를 만든다. (가볍다, 설정을 바꿔 여러 번)
// 음악을 이해하는 게 아니라 타격 지점을 잡는 것이라서 결과는 초안이다.

public enum AutoLaneMode { Alternate, Pitch, Random }

public class AutoGenSettings
{
    public double minGap = 0.28;          // 노트 사이 최소 간격(초): 난이도
    public int sensitivity = 60;          // 0~100, 클수록 노트가 많다
    public AutoLaneMode laneMode = AutoLaneMode.Alternate;
    public bool makeLongs = true;
    public double minLongLength = 0.6;    // 이보다 길게 이어지는 소리만 롱노트
    public bool makeBarrages = true;
    public int minBarrageCount = 6;       // 0.2초 이내 간격으로 이만큼 이어지면 연타
    public bool snapToGrid;               // 박자 격자에 맞춘다
    public int seed = 1;                  // 랜덤 레인용
}

public class OnsetAnalysis
{
    public float[] flux;       // 프레임별 소리 변화량
    public float[] centroid;   // 프레임별 음높이 중심 (클수록 고음)
    public float[] rms;        // 프레임별 음량 (앞뒤로 부드럽게)
    public double hop;         // 프레임 간격(초)
    public double offset;      // 프레임 시작 → 시각 보정(초)
    public double duration;    // 곡 길이(초)
}

public static class ChartAutoGenerator
{
    const int FrameSize = 1024;
    const int HopSize = 512;
    const double BarrageGap = 0.2;   // 연타로 묶는 소리 간격

    /// <summary>
    /// 곡 분석. mono는 모노 샘플(-1~1), progress는 0~1 진행률을 받는다(null 가능), cancelled가 true를 돌려주면 중단하고 null.
    /// </summary>
    public static OnsetAnalysis Analyze(float[] mono, int sampleRate, Action<float> progress, Func<bool> cancelled)
    {
        int frames = Math.Max(0, (mono.Length - FrameSize) / HopSize);
        var flux = new float[frames];
        var cen = new float[frames];
        var rmsRaw = new float[frames];
        var window = new float[FrameSize];
        for (int i = 0; i < FrameSize; i++) window[i] = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * i / FrameSize);

        var re = new float[FrameSize];
        var im = new float[FrameSize];
        var prev = new float[FrameSize / 2];

        for (int f = 0; f < frames; f++)
        {
            if ((f & 1023) == 0)
            {
                if (cancelled != null && cancelled()) return null;
                if (progress != null) progress((float)f / Math.Max(1, frames));
            }

            int o = f * HopSize;
            double e2 = 0;
            for (int i = 0; i < FrameSize; i++)
            {
                float v = mono[o + i];
                e2 += v * v;
                re[i] = v * window[i];
                im[i] = 0f;
            }
            rmsRaw[f] = (float)Math.Sqrt(e2 / FrameSize);
            Fft(re, im);

            double fl = 0, sw = 0, sm = 0;
            for (int k = 1; k < FrameSize / 2; k++)
            {
                float m = (float)Math.Log(1.0 + Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                float d = m - prev[k];
                if (d > 0) fl += d;
                prev[k] = m;
                sw += k * m;
                sm += m;
            }
            flux[f] = (float)fl;
            cen[f] = sm > 0 ? (float)(sw / sm) : 0f;
        }

        var rms = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double a = 0; int c = 0;
            for (int j = Math.Max(0, f - 2); j <= Math.Min(frames - 1, f + 2); j++) { a += rmsRaw[j]; c++; }
            rms[f] = (float)(a / c);
        }

        if (progress != null) progress(1f);
        return new OnsetAnalysis
        {
            flux = flux,
            centroid = cen,
            rms = rms,
            hop = (double)HopSize / sampleRate,
            offset = (double)(FrameSize / 2) / sampleRate,
            duration = (double)mono.Length / sampleRate
        };
    }

    struct Peak { public double t; public float f, c; public int i; }
    struct Bar { public double t, end; public int size; public float c; }
    struct Ev { public double t; public bool isBar; public Peak p; public Bar b; }

    /// <summary>[start, end] 구간의 노트를 만든다. tempo·snapDivision은 snapToGrid일 때만 쓴다.</summary>
    public static List<NoteData> Generate(OnsetAnalysis a, AutoGenSettings s, double start, double end,
        List<TempoPoint> tempo, int snapDivision)
    {
        var result = new List<NoteData>();
        if (a == null || a.flux.Length < 3) return result;
        if (end <= 0 || end > a.duration) end = a.duration;

        float[] flux = a.flux;
        int n = flux.Length;
        int w = Math.Max(2, (int)Math.Round(0.1 / a.hop));
        var ps = new double[n + 1];
        for (int i = 0; i < n; i++) ps[i + 1] = ps[i] + flux[i];
        double globalMean = ps[n] / n;
        double thr = 1 + (100 - Clamp(s.sensitivity, 0, 100)) / 100.0 * 1.5;

        // 1) 온셋 후보
        var raw = new List<Peak>();
        var picks = new List<Peak>();
        for (int i = 1; i < n - 1; i++)
        {
            int lo = Math.Max(0, i - w), hi = Math.Min(n, i + w + 1);
            double localMean = (ps[hi] - ps[lo]) / (hi - lo);
            if (flux[i] > thr * localMean && flux[i] > 0.5 * globalMean && flux[i] >= flux[i - 1] && flux[i] > flux[i + 1])
            {
                double t = i * a.hop + a.offset;
                if (t < start || t > end) continue;
                var p = new Peak { t = t, f = flux[i], c = a.centroid[i], i = i };
                raw.Add(p);
                if (picks.Count > 0 && t - picks[picks.Count - 1].t < s.minGap)
                {
                    if (p.f > picks[picks.Count - 1].f) picks[picks.Count - 1] = p;   // 가까우면 더 센 쪽만
                }
                else picks.Add(p);
            }
        }

        // 2) 연타: 짧은 간격으로 이어지는 소리 묶음
        var bars = new List<Bar>();
        if (s.makeBarrages)
        {
            int min = Math.Max(3, s.minBarrageCount);
            int runStart = 0;
            for (int k = 1; k <= raw.Count; k++)
            {
                if (k == raw.Count || raw[k].t - raw[k - 1].t > BarrageGap)
                {
                    int size = k - runStart;
                    if (size >= min)
                    {
                        double t1 = raw[runStart].t, t2 = raw[k - 1].t;
                        float c = 0;
                        for (int j = runStart; j < k; j++) c += raw[j].c;
                        bars.Add(new Bar { t = t1, end = Math.Max(t2, t1 + 0.3), size = size, c = c / size });
                    }
                    runStart = k;
                }
            }
        }

        var events = new List<Ev>();
        foreach (Peak p in picks)
        {
            bool inBar = false;
            foreach (Bar b in bars) if (p.t >= b.t - 0.03 && p.t <= b.end + 0.03) { inBar = true; break; }
            if (!inBar) events.Add(new Ev { t = p.t, p = p });
        }
        foreach (Bar b in bars) events.Add(new Ev { t = b.t, isBar = true, b = b });
        events.Sort((x, y) => x.t.CompareTo(y.t));

        // 3) 레인
        var cs = new List<float>();
        foreach (Peak p in picks) cs.Add(p.c);
        cs.Sort();
        float median = cs.Count > 0 ? cs[cs.Count / 2] : 0f;
        var rng = new Random(s.seed);

        double minLong = Math.Max(0.2, s.minLongLength);
        for (int k = 0; k < events.Count; k++)
        {
            Ev ev = events[k];
            float c = ev.isBar ? ev.b.c : ev.p.c;
            Lane lane;
            if (s.laneMode == AutoLaneMode.Alternate) lane = (Lane)(k % 2);
            else if (s.laneMode == AutoLaneMode.Random) lane = rng.NextDouble() < 0.5 ? Lane.Upper : Lane.Lower;
            else lane = c < median ? Lane.Lower : Lane.Upper;   // 저음 = 아래(B), 고음 = 위(A)

            var note = new NoteData { time = Round(ev.t), lane = lane };
            if (ev.isBar)
            {
                note.type = NoteType.Barrage;
                note.duration = Round(ev.b.end - ev.b.t);
                note.count = Math.Max(3, (int)Math.Round(ev.b.size * 0.6));
            }
            else if (s.makeLongs)
            {
                // 4) 롱노트: 다음 노트 직전까지 음량이 처음의 절반 이상으로 이어지면 길게
                double limit = (k + 1 < events.Count ? events[k + 1].t : end) - 0.12;
                if (limit - ev.t >= minLong)
                {
                    int i0 = ev.p.i;
                    float refRms = 0;
                    for (int j = i0; j <= Math.Min(n - 1, i0 + w); j++) refRms = Math.Max(refRms, a.rms[j]);
                    int jj = i0 + w;
                    double sustainEnd = ev.t;
                    while (jj < n && jj * a.hop + a.offset < limit && a.rms[jj] >= 0.5f * refRms)
                    {
                        sustainEnd = jj * a.hop + a.offset;
                        jj++;
                    }
                    if (sustainEnd - ev.t >= minLong)
                    {
                        note.type = NoteType.Long;
                        note.duration = Round(sustainEnd - ev.t);
                    }
                }
            }
            result.Add(note);
        }

        if (s.snapToGrid && snapDivision > 0 && tempo != null && tempo.Count > 0) SnapAndClean(result, tempo, snapDivision);
        ChartMath.SortNotes(result);
        return result;
    }

    // 격자에 맞춘 뒤, 같은 레인에서 겹치거나 너무 붙은 노트를 정리한다.
    static void SnapAndClean(List<NoteData> notes, List<TempoPoint> tempo, int division)
    {
        foreach (NoteData nd in notes)
        {
            double endT = nd.time + nd.duration;
            nd.time = TempoMap.Snap(tempo, nd.time, division);
            if (nd.type != NoteType.Tap)
            {
                double e = TempoMap.Snap(tempo, endT, division);
                nd.duration = e > nd.time ? Round(e - nd.time) : Round(Math.Max(0.1, nd.duration));
            }
        }

        for (int l = 0; l < 2; l++)
        {
            var lane = notes.FindAll(x => (int)x.lane == l);
            ChartMath.SortNotes(lane);
            NoteData prev = null;
            foreach (NoteData cur in lane)
            {
                if (prev != null)
                {
                    double prevEnd = prev.time + (prev.type != NoteType.Tap ? prev.duration : 0);
                    if (cur.time - prev.time < ChartValidator.MinGap || cur.time <= prevEnd - 1e-9)
                    {
                        if (prev.type != NoteType.Tap && cur.time - prev.time >= ChartValidator.MinGap)
                        {
                            // 앞 롱노트·연타를 이 노트 앞에서 끊는다
                            prev.duration = Round(Math.Max(0, cur.time - ChartValidator.MinGap - prev.time));
                            if (prev.duration < 0.1) { prev.type = NoteType.Tap; prev.duration = 0; prev.count = 0; }
                        }
                        else
                        {
                            notes.Remove(cur);   // 같은 자리로 모인 노트는 하나만 남긴다
                            continue;
                        }
                    }
                }
                prev = cur;
            }
        }
    }

    static double Round(double t) { return ChartMath.RoundMs(t); }
    static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

    // 제자리 FFT (길이는 2의 거듭제곱)
    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                float tr = re[i]; re[i] = re[j]; re[j] = tr;
                float ti = im[i]; im[i] = im[j]; im[j] = ti;
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1f, ci = 0f;
                for (int k = 0; k < len / 2; k++)
                {
                    int u = i + k, v = u + len / 2;
                    float xr = re[v] * cr - im[v] * ci;
                    float xi = re[v] * ci + im[v] * cr;
                    re[v] = re[u] - xr; im[v] = im[u] - xi;
                    re[u] += xr; im[u] += xi;
                    float t = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = t;
                }
            }
        }
    }
}
