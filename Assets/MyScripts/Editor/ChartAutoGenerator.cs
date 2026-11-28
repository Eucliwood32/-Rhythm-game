using System;
using System.Collections.Generic;

// 채보 자동 생성. Unity API를 쓰지 않아서 백그라운드 스레드에서 돌리고 따로 테스트할 수 있다.
//
// 1) Analyze (무겁다, 곡마다 한 번)
//    - 짧은 구간마다 주파수를 72개 대역으로 나눠 크기를 잰다(스펙트로그램).
//    - HPSS: 시간 방향으로 매끈한 소리(기타·보컬 같은 지속음)와 주파수 방향으로 매끈한 소리(드럼 같은 타격음)를 나눈다.
//    - 타격음에서 저음(킥) / 중음(스네어) / 고음(심벌) 대역별 온셋 곡선을 SuperFlux 방식으로 구한다.
//    - 온셋 곡선으로 BPM을 찾고(자기상관 + 정밀 탐색), 박 위치를 동적 계획법으로 추적하고, 마디 첫 박(다운비트)을 정한다.
//    - 마디별 특징이 크게 바뀌는 곳에서 곡을 구간(벌스·코러스 등)으로 나누고, 구간마다 세기(0~1)를 매긴다.
//    외부 분석 파일(Tools/chart_analyze.py가 만든 JSON)을 불러오면 이 단계를 대신한다.
//
// 2) Generate (가볍다, 설정을 바꿔 여러 번)
//    - 대역별 온셋을 모아 사건(타격)으로 묶고, 박자 격자 위에 있는 소리를 우선한다.
//    - 구간 세기에 따라 노트 간격과 기준을 바꾼다(코러스는 촘촘하게, 조용한 구간은 듬성하게).
//    - 레인: 드럼 모드면 킥 = B(아래), 스네어·심벌 = A(위). 강박에서 킥과 스네어가 같이 치면 동시치기.
//    - 빽빽한 킥(투베이스)·스네어 연속은 연타, 타격 뒤 지속음이 길게 이어지면 롱노트.
//    - 같은 레인 연속 개수와 초당 노트 수를 제한해서 칠 수 있는 채보로 다듬는다.
// 결과는 초안이다.

public enum AutoLaneMode { Alternate, Pitch, Random, Drums }

public class AutoGenSettings
{
    public double minGap = 0.28;          // 노트 사이 최소 간격(초): 난이도
    public double maxNps = 5.5;           // 1초 안의 최대 노트 수: 난이도
    public int sensitivity = 60;          // 0~100, 클수록 노트가 많다
    public AutoLaneMode laneMode = AutoLaneMode.Drums;
    public bool makeLongs = true;
    public double minLongLength = 0.6;    // 이보다 길게 이어지는 소리만 롱노트
    public bool makeBarrages = true;
    public int minBarrageCount = 6;       // 0.2초 이내 간격으로 이만큼 이어지면 연타
    public bool makeChords = true;        // 강박의 킥 + 스네어를 동시치기로
    public bool preferBeats = true;       // 박자 격자 위의 소리를 우선
    public bool sectionDensity = true;    // 곡 구간 세기에 따라 밀도 조절
    public int maxJack = 4;               // 같은 레인 최대 연속 (빠른 간격일 때)
    public bool snapToGrid;               // 박자 격자에 맞춘다 (채보의 BPM 기준)
    public int seed = 1;                  // 랜덤 레인용
}

/// <summary>곡의 한 구간(벌스·코러스 등)과 그 세기(0 = 가장 조용, 1 = 가장 셈).</summary>
public class SongSection
{
    public double start, end;
    public float intensity;
}

// ───────── 외부 분석 파일 형식 (Tools/chart_analyze.py가 만든다. Unity JsonUtility로 읽을 수 있게 단순한 구조) ─────────

[Serializable]
public class ExternalAnalysisFile
{
    public int version = 1;
    public string source;              // 예: "librosa + demucs"
    public double duration;
    public double bpm;
    public double[] beats;             // 박 시각(초)
    public double[] downbeats;         // 마디 첫 박 시각(초)
    public ExternalOnset[] onsets;     // 타격 지점
    public ExternalSustain[] sustains; // 길게 이어지는 소리
    public ExternalSection[] sections; // 곡 구간
}

[Serializable]
public class ExternalOnset
{
    public double t;       // 시각(초)
    public string kind;    // "kick" / "snare" / "hihat" / "other"
    public float s;        // 세기 0~1
}

[Serializable]
public class ExternalSustain
{
    public double start, end;
    public float s;
}

[Serializable]
public class ExternalSection
{
    public double start, end;
    public float intensity;
}

/// <summary>분석 결과. 내부 분석이면 곡선(low~rms)이, 외부 분석이면 onsets·sustains가 채워진다. 박자와 구간은 둘 다.</summary>
public class OnsetAnalysis
{
    public double hop;         // 프레임 간격(초)
    public double offset;      // 프레임 시작 → 시각 보정(초)
    public double duration;    // 곡 길이(초)

    // 내부 분석: 프레임별 곡선 (대략 0~1로 정규화)
    public float[] low, mid, high;   // 대역별 타격 온셋 (킥 / 스네어 / 심벌)
    public float[] perc;             // 타격음 전체 온셋
    public float[] lowShare;         // 타격의 저음 비율 (대역 수로 나눈 평균끼리 비교, 1에 가까우면 킥)
    public float[] highShare;        // 타격의 고음 비율 (1에 가까우면 심벌·하이햇)
    public float[] harm;             // 지속음 세기
    public float[] centroid;         // 음높이 중심 (Hz)
    public float[] rms;              // 음량

    // 외부 분석
    public List<ExternalOnset> onsets;
    public List<ExternalSustain> sustains;

    // 박자와 구간
    public double bpm;
    public double[] beats = new double[0];
    public bool[] downbeat = new bool[0];
    public List<TempoPoint> tempo = new List<TempoPoint>();
    public List<SongSection> sections = new List<SongSection>();

    public string source = "내부 분석";
    public bool IsExternal { get { return onsets != null; } }
}

public static class ChartAutoGenerator
{
    const int FrameSize = 2048;
    const int HopSize = 512;
    const int BandCount = 72;
    const double BarrageGap = 0.18;  // 연타로 묶는 소리 간격 (킥·스네어만. 하이햇은 묶지 않는다)
    const double ClusterGap = 0.035; // 이 안의 대역별 온셋은 한 타격으로 묶는다

    // ═════════════════════════════════ 1. 분석 ═════════════════════════════════

    /// <summary>
    /// 곡 분석. mono는 모노 샘플(-1~1), progress는 0~1 진행률을 받는다(null 가능), cancelled가 true를 돌려주면 중단하고 null.
    /// </summary>
    public static OnsetAnalysis Analyze(float[] mono, int sampleRate, Action<float> progress, Func<bool> cancelled)
    {
        int frames = mono.Length >= FrameSize ? (mono.Length - FrameSize) / HopSize + 1 : 0;
        var a = new OnsetAnalysis
        {
            hop = (double)HopSize / sampleRate,
            offset = (double)(FrameSize / 2) / sampleRate,
            duration = (double)mono.Length / sampleRate
        };
        if (frames < 8)
        {
            a.low = a.mid = a.high = a.perc = a.harm = a.centroid = a.rms = a.lowShare = a.highShare = new float[Math.Max(0, frames)];
            return a;
        }

        // ── 대역 나누기: 30Hz ~ 16kHz를 로그 간격으로 ──
        double top = Math.Min(16000.0, sampleRate * 0.5 * 0.95);
        var bandLo = new List<int>();
        var bandHi = new List<int>();
        var bandHz = new List<double>();
        int prevHi = 1;
        for (int b = 0; b < BandCount; b++)
        {
            double f0 = 30.0 * Math.Pow(top / 30.0, (double)b / BandCount);
            double f1 = 30.0 * Math.Pow(top / 30.0, (double)(b + 1) / BandCount);
            int lo = Math.Max(prevHi, (int)(f0 * FrameSize / sampleRate));
            int hi = Math.Max(lo + 1, (int)(f1 * FrameSize / sampleRate));
            if (lo >= FrameSize / 2) break;
            hi = Math.Min(hi, FrameSize / 2);
            if (hi <= lo) continue;
            bandLo.Add(lo); bandHi.Add(hi);
            bandHz.Add((lo + hi) * 0.5 * sampleRate / FrameSize);
            prevHi = hi;
        }
        int B = bandLo.Count;

        // ── 스펙트로그램 ──
        var S = new float[frames * B];
        var cen = new float[frames];
        var rmsRaw = new float[frames];
        var window = new float[FrameSize];
        for (int i = 0; i < FrameSize; i++) window[i] = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * i / FrameSize);
        var re = new float[FrameSize];
        var im = new float[FrameSize];
        var mag = new float[FrameSize / 2];

        for (int f = 0; f < frames; f++)
        {
            if ((f & 511) == 0)
            {
                if (cancelled != null && cancelled()) return null;
                if (progress != null) progress(0.5f * f / frames);
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

            double sw = 0, sm = 0;
            for (int k = 1; k < FrameSize / 2; k++)
            {
                float m = (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                mag[k] = m;
                sw += k * m;
                sm += m;
            }
            cen[f] = sm > 0 ? (float)(sw / sm * sampleRate / FrameSize) : 0f;
            for (int b = 0; b < B; b++)
            {
                double p = 0;
                for (int k = bandLo[b]; k < bandHi[b]; k++) p += mag[k] * mag[k];
                S[f * B + b] = (float)Math.Sqrt(p);
            }
        }

        // ── HPSS: 시간 방향 중앙값 = 지속음, 주파수 방향 중앙값 = 타격음, 부드러운 마스크로 나눈다 ──
        var H = new float[frames * B];
        var P = new float[frames * B];
        const int halfT = 8, halfF = 4;
        var buf = new float[2 * halfT + 1];
        for (int b = 0; b < B; b++)
        {
            if (cancelled != null && cancelled()) return null;
            for (int f = 0; f < frames; f++)
            {
                int c = 0;
                for (int j = Math.Max(0, f - halfT); j <= Math.Min(frames - 1, f + halfT); j++) buf[c++] = S[j * B + b];
                H[f * B + b] = Median(buf, c);
            }
            if (progress != null) progress(0.5f + 0.15f * b / B);
        }
        var bufF = new float[2 * halfF + 1];
        for (int f = 0; f < frames; f++)
        {
            for (int b = 0; b < B; b++)
            {
                int c = 0;
                for (int j = Math.Max(0, b - halfF); j <= Math.Min(B - 1, b + halfF); j++) bufF[c++] = S[f * B + j];
                P[f * B + b] = Median(bufF, c);
            }
            if ((f & 2047) == 0 && cancelled != null && cancelled()) return null;
        }
        var Sp = new float[frames * B];
        var Sh = new float[frames * B];
        for (int i = 0; i < Sp.Length; i++)
        {
            double h2 = (double)H[i] * H[i], p2 = (double)P[i] * P[i];
            double mp = p2 / (h2 + p2 + 1e-12);
            Sp[i] = (float)(S[i] * mp);
            Sh[i] = (float)(S[i] * (1 - mp));
        }
        H = null; P = null;
        if (progress != null) progress(0.7f);

        // ── 대역별 SuperFlux 온셋 (타격음에서) ──
        float refS = Percentile(Sp, 0.995f);
        if (refS <= 0) refS = 1e-6f;
        var L = new float[frames * B];
        for (int i = 0; i < L.Length; i++) L[i] = (float)Math.Log(1 + 100.0 * Sp[i] / refS);

        var low = new float[frames];
        var midE = new float[frames];
        var highE = new float[frames];
        var perc = new float[frames];
        // 소리 모양 판단용: 로그가 아닌 실제 크기의 증가량. 로그는 거의 조용한 대역의 작은 소리도 크게 키워서 킥·스네어 구분을 흐린다.
        var linLow = new float[frames];
        var linMid = new float[frames];
        var linHigh = new float[frames];
        const int lag = 2;   // 약 23ms 전 프레임과 비교 (떨리는 소리에 덜 반응)
        for (int f = lag; f < frames; f++)
        {
            for (int b = 0; b < B; b++)
            {
                float prevMax = L[(f - lag) * B + b];
                if (b > 0) prevMax = Math.Max(prevMax, L[(f - lag) * B + b - 1]);
                if (b + 1 < B) prevMax = Math.Max(prevMax, L[(f - lag) * B + b + 1]);
                float prevLin = Sp[(f - lag) * B + b];
                if (b > 0) prevLin = Math.Max(prevLin, Sp[(f - lag) * B + b - 1]);
                if (b + 1 < B) prevLin = Math.Max(prevLin, Sp[(f - lag) * B + b + 1]);
                float dl = Sp[f * B + b] - prevLin;
                if (dl > 0)
                {
                    if (bandHz[b] < 150) linLow[f] += dl;
                    else if (bandHz[b] < 3500) linMid[f] += dl;
                    else if (bandHz[b] > 5000) linHigh[f] += dl;
                }
                float d = L[f * B + b] - prevMax;
                if (d <= 0) continue;
                perc[f] += d;
                double hz = bandHz[b];
                if (hz < 150) low[f] += d;
                else if (hz < 3500) midE[f] += d;
                else if (hz > 5000) highE[f] += d;
            }
        }

        // ── 지속음 세기 (80Hz ~ 5kHz) ──
        var harmRaw = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            double sum = 0;
            for (int b = 0; b < B; b++) if (bandHz[b] >= 80 && bandHz[b] <= 5000) sum += Sh[f * B + b];
            harmRaw[f] = (float)sum;
        }
        float refH = Percentile(harmRaw, 0.99f);
        var harm = new float[frames];
        for (int f = 0; f < frames; f++) harm[f] = refH > 0 ? (float)Math.Log(1 + 9.0 * harmRaw[f] / refH) / (float)Math.Log(10) : 0f;
        harm = Smooth(harm, 2);

        // 타격의 에너지가 어느 대역에 몰려 있는지 = 소리 모양 (킥은 저음, 스네어는 중음, 하이햇은 고음)
        a.lowShare = new float[frames];
        a.highShare = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            // 타격은 2~3프레임에 걸쳐 퍼지므로 앞 1 ~ 뒤 2프레임을 합쳐서 본다(첫 프레임의 딸깍 소리에 휘둘리지 않게).
            double l = 0, m = 0, h = 0;
            for (int j = Math.Max(0, f - 1); j <= Math.Min(frames - 1, f + 2); j++) { l += linLow[j]; m += linMid[j]; h += linHigh[j]; }
            double sum = l + m + h;
            a.lowShare[f] = sum > 1e-9 ? (float)(l / sum) : 0f;
            a.highShare[f] = sum > 1e-9 ? (float)(h / sum) : 0f;
        }
        a.low = Normalize(low);
        a.mid = Normalize(midE);
        a.high = Normalize(highE);
        a.perc = Normalize(perc);
        a.harm = harm;
        a.centroid = cen;
        a.rms = Normalize(Smooth(rmsRaw, 2));
        if (progress != null) progress(0.8f);

        // ── 박자 ──
        if (cancelled != null && cancelled()) return null;
        var env = new float[frames];
        for (int f = 0; f < frames; f++) env[f] = a.perc[f] + 0.5f * a.low[f];
        TrackBeats(a, env);
        if (progress != null) progress(0.92f);

        // ── 구간 ──
        FindSections(a);
        if (progress != null) progress(1f);
        return a;
    }

    /// <summary>외부 분석 파일을 분석 결과로 바꾼다. 박이 있으면 BPM 구간도 다시 계산한다.</summary>
    public static OnsetAnalysis FromExternal(ExternalAnalysisFile file)
    {
        var a = new OnsetAnalysis
        {
            duration = file.duration,
            onsets = new List<ExternalOnset>(),
            sustains = new List<ExternalSustain>(),
            source = "외부 분석" + (string.IsNullOrEmpty(file.source) ? "" : " (" + file.source + ")")
        };
        if (file.onsets != null) foreach (ExternalOnset o in file.onsets) if (o != null) a.onsets.Add(o);
        a.onsets.Sort((x, y) => x.t.CompareTo(y.t));
        if (file.sustains != null) foreach (ExternalSustain s in file.sustains) if (s != null && s.end > s.start) a.sustains.Add(s);
        a.sustains.Sort((x, y) => x.start.CompareTo(y.start));
        if (a.duration <= 0 && a.onsets.Count > 0) a.duration = a.onsets[a.onsets.Count - 1].t + 1;

        double[] beats = file.beats ?? new double[0];
        Array.Sort(beats);
        a.beats = beats;
        a.downbeat = new bool[beats.Length];
        if (file.downbeats != null && file.downbeats.Length > 0)
        {
            foreach (double d in file.downbeats)
            {
                int i = NearestIndex(beats, d);
                if (i >= 0 && Math.Abs(beats[i] - d) < 0.08) a.downbeat[i] = true;
            }
        }
        else
        {
            for (int i = 0; i < beats.Length; i++) a.downbeat[i] = i % 4 == 0;
        }
        a.bpm = file.bpm;
        if (beats.Length >= 8) a.tempo = TempoFromBeats(beats, a.downbeat);
        if (a.bpm <= 0 && a.tempo.Count > 0) a.bpm = a.tempo[0].bpm;

        if (file.sections != null)
            foreach (ExternalSection s in file.sections)
                if (s != null && s.end > s.start) a.sections.Add(new SongSection { start = s.start, end = s.end, intensity = Clamp01(s.intensity) });
        a.sections.Sort((x, y) => x.start.CompareTo(y.start));
        return a;
    }

    // ── 박자 추적 ──

    static void TrackBeats(OnsetAnalysis a, float[] env)
    {
        int n = env.Length;
        double hop = a.hop;
        // 지역 평균을 빼서 꾸준히 큰 소리(기타 벽)는 지우고 튀는 부분만 남긴다.
        var o = new float[n];
        int w = Math.Max(1, (int)Math.Round(0.25 / hop));
        var ps = Prefix(env);
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - w), hi = Math.Min(n, i + w + 1);
            o[i] = Math.Max(0f, env[i] - (float)((ps[hi] - ps[lo]) / (hi - lo)));
        }
        double energy = 0;
        for (int i = 0; i < n; i++) energy += o[i];
        if (energy <= 1e-6) return;

        // 1) 자기상관으로 대략의 BPM (60~240), 배수·절반 박자의 상관도 더하고 140 BPM 근처를 조금 선호
        int lagMin = Math.Max(2, (int)Math.Floor(60.0 / (240 * hop)));
        int lagMax = Math.Min(n / 2, (int)Math.Ceiling(60.0 / (60 * hop)) * 2 + 2);
        if (lagMax <= lagMin + 2) return;
        var r = new double[lagMax + 1];
        for (int l = lagMin / 2; l <= lagMax; l++)
        {
            if (l <= 0) continue;
            double s = 0;
            for (int i = 0; i + l < n; i++) s += o[i] * o[i + l];
            r[l] = s / (n - l);
        }
        double bestBpm = 120, bestScore = double.MinValue;
        for (double bpm = 60; bpm <= 240; bpm += 0.5)
        {
            double lagF = 60.0 / (bpm * hop);
            double s = Interp(r, lagF) + 0.5 * Interp(r, lagF * 2) + 0.25 * Interp(r, lagF / 2);
            double oct = Math.Log(bpm / 140.0, 2);
            s *= Math.Exp(-0.5 * oct * oct / (1.1 * 1.1));
            if (s > bestScore) { bestScore = s; bestBpm = bpm; }
        }

        // 2) 정밀 BPM: 곡 전체 온셋을 박 주기로 접었을 때 한 위상에 가장 많이 모이는 BPM
        double onBeat, offBeat;
        double fine = RefineBpm(o, hop, bestBpm, out onBeat, out offBeat);
        // 절반 박자로 잘못 잡았는지: 박과 박 사이(반 박 위치)에도 박만큼 센 타격이 꾸준히 있으면 실제 박은 두 배 빠르다.
        // (빠른 메탈은 자기상관이 절반 BPM을 고르기 쉽다)
        if (offBeat >= 0.6 * onBeat && fine * 2 <= 240)
        {
            double on2, off2;
            fine = RefineBpm(o, hop, fine * 2, out on2, out off2);
        }

        // 3) 동적 계획법 박 추적 (Ellis 2007): 온셋이 큰 곳에 박을 두되, 간격이 주기에서 벗어나면 벌점
        double P = 60.0 / (fine * hop);
        double mean = 0, sd = 0;
        for (int i = 0; i < n; i++) mean += o[i];
        mean /= n;
        for (int i = 0; i < n; i++) sd += (o[i] - mean) * (o[i] - mean);
        sd = Math.Sqrt(sd / n) + 1e-9;
        var C = new double[n];
        var back = new int[n];
        const double tight = 100;
        int jMin = (int)Math.Round(P / 2), jMax = (int)Math.Round(2 * P);
        for (int i = 0; i < n; i++)
        {
            double local = o[i] / sd;
            double bestPrev = 0; int bp = -1;
            for (int d = jMin; d <= jMax; d++)
            {
                int j = i - d;
                if (j < 0) break;
                double lg = Math.Log(d / P);
                double v = C[j] - tight * lg * lg;
                if (bp < 0 || v > bestPrev) { bestPrev = v; bp = j; }
            }
            C[i] = local + (bp >= 0 ? bestPrev : 0);
            back[i] = bp;
        }
        int last = n - 1, tail = Math.Max(0, n - (int)Math.Ceiling(P));
        for (int i = tail; i < n; i++) if (C[i] > C[last]) last = i;
        var idx = new List<int>();
        for (int i = last; i >= 0; i = back[i]) { idx.Add(i); if (back[i] < 0) break; }
        idx.Reverse();

        // 첫 소리 전의 박(무음 인트로)은 버린다.
        float firstThr = 0.15f;
        int firstSound = 0;
        while (firstSound < n && env[firstSound] < firstThr) firstSound++;
        var beats = new List<double>();
        foreach (int i in idx) if (i >= firstSound - (int)(P / 2)) beats.Add(i * hop + a.offset);
        if (beats.Count < 8) return;

        // 4) 마디 첫 박: 4가지 위상 중 킥과 화음 변화가 가장 많이 모이는 위상
        var harmRise = new float[n];
        int rl = Math.Max(1, (int)Math.Round(0.05 / hop));
        for (int i = rl; i < n; i++) harmRise[i] = Math.Max(0f, a.harm[i] - a.harm[i - rl]);
        int bestPhase = 0; double bestPhaseScore = double.MinValue;
        for (int k = 0; k < TempoMap.BeatsPerMeasure; k++)
        {
            double s = 0;
            for (int i = k; i < beats.Count; i += TempoMap.BeatsPerMeasure)
            {
                int f = FrameOf(a, beats[i]);
                s += MaxAround(a.low, f, 2) + 0.7 * MaxAround(harmRise, f, 3);
            }
            if (s > bestPhaseScore) { bestPhaseScore = s; bestPhase = k; }
        }
        a.beats = beats.ToArray();
        a.downbeat = new bool[beats.Count];
        for (int i = bestPhase; i < beats.Count; i += TempoMap.BeatsPerMeasure) a.downbeat[i] = true;
        a.tempo = TempoFromBeats(a.beats, a.downbeat);
        a.bpm = a.tempo.Count > 0 ? a.tempo[0].bpm : Math.Round(fine * 100) / 100;
    }

    // bpm 근처 ±3%에서, 온셋을 박 주기로 접었을 때 한 위상에 가장 많이 모이는 BPM. onBeat/offBeat = 그 위상과 반 박 뒤의 세기.
    static double RefineBpm(float[] o, double hop, double around, out double onBeat, out double offBeat)
    {
        const int bins = 48;
        var hist = new double[bins];
        double fine = around, fineScore = double.MinValue;
        onBeat = offBeat = 0;
        for (double bpm = around * 0.97; bpm <= around * 1.03; bpm += 0.01)
        {
            double period = 60.0 / (bpm * hop);
            Array.Clear(hist, 0, bins);
            for (int i = 0; i < o.Length; i++)
            {
                if (o[i] <= 0) continue;
                double ph = i / period;
                ph -= Math.Floor(ph);
                hist[(int)(ph * bins) % bins] += o[i];
            }
            double best = 0; int bestK = 0;
            for (int k = 0; k < bins; k++)
            {
                double v = hist[(k + bins - 1) % bins] + hist[k] + hist[(k + 1) % bins];
                if (v > best) { best = v; bestK = k; }
            }
            if (best > fineScore)
            {
                fineScore = best; fine = bpm; onBeat = best;
                int h = (bestK + bins / 2) % bins;
                offBeat = hist[(h + bins - 1) % bins] + hist[h] + hist[(h + 1) % bins];
            }
        }
        return fine;
    }

    /// <summary>
    /// 박 시각들을 BPM 구간으로 바꾼다. 일정한 BPM으로 설명이 안 되는 곳에서 구간을 나누고, 각 구간은 마디 첫 박에서 시작한다.
    /// </summary>
    public static List<TempoPoint> TempoFromBeats(double[] beats, bool[] downbeat)
    {
        var result = new List<TempoPoint>();
        int n = beats.Length;
        if (n < 4) return result;

        // 직선(시각 = 시작 + 번호 × 박 길이)으로 맞추다가 오차가 35ms를 넘으면 새 구간 (구간은 최소 16박)
        var segs = new List<int[]>();
        int s0 = 0;
        for (int e = s0 + 8; e < n; e++)
        {
            double slope, icpt, err;
            Fit(beats, s0, e, out slope, out icpt, out err);
            if (err > 0.035 && e - s0 >= 16)
            {
                segs.Add(new[] { s0, e - 1 });
                s0 = e - 1;
                e = s0 + 8;
            }
        }
        segs.Add(new[] { s0, n - 1 });

        // 박 길이가 1% 안으로 같은 이웃 구간은 합친다
        var merged = new List<int[]>();
        foreach (int[] sg in segs)
        {
            if (merged.Count > 0)
            {
                int[] prev = merged[merged.Count - 1];
                double s1, i1, e1, s2, i2, e2;
                Fit(beats, prev[0], prev[1], out s1, out i1, out e1);
                Fit(beats, sg[0], sg[1], out s2, out i2, out e2);
                if (Math.Abs(s1 - s2) / s1 < 0.01) { prev[1] = sg[1]; continue; }
            }
            merged.Add(new[] { sg[0], sg[1] });
        }

        foreach (int[] sg in merged)
        {
            double slope, icpt, err;
            Fit(beats, sg[0], sg[1], out slope, out icpt, out err);
            if (slope <= 0) continue;
            double bpm = 60.0 / slope;
            bpm = Math.Abs(bpm - Math.Round(bpm)) < 0.05 ? Math.Round(bpm) : Math.Round(bpm * 100) / 100;
            int d = sg[0];
            while (d <= sg[1] && !(d < downbeat.Length && downbeat[d])) d++;
            if (d > sg[1]) d = sg[0];
            double t = ChartMath.RoundMs(icpt + slope * (d - sg[0]));
            if (result.Count > 0 && t <= result[result.Count - 1].time + 0.5) continue;
            result.Add(new TempoPoint { time = Math.Max(0, t), bpm = bpm });
        }
        return result;
    }

    // 박 번호 s~e를 직선으로 맞춘다. slope = 박 길이, icpt = s번 박 시각, err = 최대 오차.
    static void Fit(double[] t, int s, int e, out double slope, out double icpt, out double err)
    {
        int m = e - s + 1;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < m; i++) { double y = t[s + i]; sx += i; sy += y; sxx += i * i; sxy += i * y; }
        double den = m * sxx - sx * sx;
        slope = den != 0 ? (m * sxy - sx * sy) / den : 0;
        icpt = (sy - slope * sx) / m;
        err = 0;
        for (int i = 0; i < m; i++) err = Math.Max(err, Math.Abs(t[s + i] - (icpt + slope * i)));
    }

    // ── 곡 구간 ──

    static void FindSections(OnsetAnalysis a)
    {
        a.sections = new List<SongSection>();
        int n = a.perc.Length;
        if (n == 0) return;

        // 경계 후보: 마디 첫 박 (없으면 2초마다)
        var bounds = new List<double>();
        for (int i = 0; i < a.beats.Length; i++) if (a.downbeat[i]) bounds.Add(a.beats[i]);
        if (bounds.Count < 6)
        {
            bounds.Clear();
            for (double t = 0; t < a.duration; t += 2.0) bounds.Add(t);
        }
        if (bounds[0] > 0.5) bounds.Insert(0, 0);
        bounds.Add(a.duration);
        int M = bounds.Count - 1;   // 마디(칸) 수
        if (M < 2)
        {
            a.sections.Add(new SongSection { start = 0, end = a.duration, intensity = 0.5f });
            return;
        }

        // 칸마다 특징: 킥·스네어·심벌 온셋, 지속음, 음량의 평균
        var feats = new double[M][];
        var raw = new double[M];
        for (int m = 0; m < M; m++)
        {
            int f0 = Math.Max(0, FrameOf(a, bounds[m])), f1 = Math.Min(n, Math.Max(f0 + 1, FrameOf(a, bounds[m + 1])));
            double l = 0, md = 0, h = 0, hm = 0, rm = 0;
            for (int f = f0; f < f1; f++) { l += a.low[f]; md += a.mid[f]; h += a.high[f]; hm += a.harm[f]; rm += a.rms[f]; }
            int c = Math.Max(1, f1 - f0);
            feats[m] = new[] { l / c, md / c, h / c, hm / c, rm / c };
            raw[m] = (l + md + 0.5 * h) / c + 2 * rm / c;
        }
        // 특징마다 평균 0, 표준편차 1로
        for (int d = 0; d < 5; d++)
        {
            double mu = 0, sd = 0;
            for (int m = 0; m < M; m++) mu += feats[m][d];
            mu /= M;
            for (int m = 0; m < M; m++) sd += (feats[m][d] - mu) * (feats[m][d] - mu);
            sd = Math.Sqrt(sd / M) + 1e-9;
            for (int m = 0; m < M; m++) feats[m][d] = (feats[m][d] - mu) / sd;
        }
        // 경계 j 앞뒤 4칸 평균의 차이 = 변화량
        var nov = new double[M];
        for (int j = 1; j < M; j++)
        {
            int K = Math.Min(4, Math.Min(j, M - j));
            double dist = 0;
            for (int d = 0; d < 5; d++)
            {
                double before = 0, after = 0;
                for (int k = 1; k <= K; k++) { before += feats[j - k][d]; after += feats[j + k - 1][d]; }
                double diff = (before - after) / K;
                dist += diff * diff;
            }
            nov[j] = Math.Sqrt(dist);
        }
        double nm = 0, ns = 0;
        for (int j = 1; j < M; j++) nm += nov[j];
        nm /= Math.Max(1, M - 1);
        for (int j = 1; j < M; j++) ns += (nov[j] - nm) * (nov[j] - nm);
        ns = Math.Sqrt(ns / Math.Max(1, M - 1));

        var cand = new List<int>();
        for (int j = 1; j < M; j++)
            if (nov[j] > nm + 0.5 * ns && nov[j] >= nov[j - 1] && (j + 1 >= M || nov[j] >= nov[j + 1])) cand.Add(j);
        cand.Sort((x, y) => nov[y].CompareTo(nov[x]));
        var chosen = new List<int>();
        foreach (int j in cand)
        {
            bool ok = j >= 4 && M - j >= 4;
            foreach (int c in chosen) if (Math.Abs(c - j) < 4) { ok = false; break; }
            if (ok) chosen.Add(j);
        }
        chosen.Sort();
        chosen.Insert(0, 0);
        chosen.Add(M);

        var secRaw = new List<double>();
        for (int k = 0; k + 1 < chosen.Count; k++)
        {
            double sum = 0;
            for (int m = chosen[k]; m < chosen[k + 1]; m++) sum += raw[m];
            secRaw.Add(sum / (chosen[k + 1] - chosen[k]));
            a.sections.Add(new SongSection { start = bounds[chosen[k]], end = bounds[chosen[k + 1]] });
        }
        double mn = double.MaxValue, mx = double.MinValue;
        foreach (double v in secRaw) { mn = Math.Min(mn, v); mx = Math.Max(mx, v); }
        // 세기 = (최소~최대 비율 + 순위 비율) / 2. 유난히 센 구간 하나 때문에 나머지가 전부 0 근처로 눌리지 않게 순위를 섞는다.
        int S = a.sections.Count;
        for (int k = 0; k < S; k++)
        {
            double mm = mx - mn > 1e-9 ? (secRaw[k] - mn) / (mx - mn) : 0.5;
            int below = 0;
            for (int q = 0; q < S; q++) if (secRaw[q] < secRaw[k]) below++;
            double rank = S > 1 ? (double)below / (S - 1) : 0.5;
            a.sections[k].intensity = (float)(0.5 * mm + 0.5 * rank);
        }
    }

    // ═════════════════════════════════ 2. 생성 ═════════════════════════════════

    // 타격 하나. 대역별 세기(0이면 그 대역은 안 침)와 점수를 갖고, 마지막에 노트가 된다.
    class Ev
    {
        public double t;
        public float low, mid, high, other, cen, harmRise;
        public double score;
        public bool onBeat, chord;
        public Lane lane;
        public NoteType type;
        public double duration;
        public int count;
        public Ev Copy() { return (Ev)MemberwiseClone(); }
    }

    struct BandPeak { public double t; public float v; public int band; public float cen, harmRise; public bool lowOnly; }   // band: 0 킥, 1 스네어, 2 심벌, 3 기타

    /// <summary>[start, end] 구간의 노트를 만든다. tempo·snapDivision은 snapToGrid일 때만 쓴다.</summary>
    public static List<NoteData> Generate(OnsetAnalysis a, AutoGenSettings s, double start, double end,
        List<TempoPoint> tempo, int snapDivision)
    {
        var result = new List<NoteData>();
        if (a == null) return result;
        if (end <= 0 || end > a.duration) end = a.duration;
        if (end <= start) return result;

        double sens = Clamp(s.sensitivity, 0, 100) / 100.0;
        double absMin = 0.08 + (1 - sens) * 0.35;      // 이보다 약한 소리는 버린다 (정규화 단위)
        double relThr = 1 + (1 - sens) * 1.2;          // 주변 평균의 몇 배여야 온셋인지

        // 1) 대역별 온셋
        var peaks = a.IsExternal ? ExternalPeaks(a, absMin * 0.8) : InternalPeaks(a, relThr, absMin * 0.8);
        peaks.RemoveAll(p => p.t < start - 0.01 || p.t > end + 0.01);
        peaks.Sort((x, y) => x.t.CompareTo(y.t));

        // 2) 연타: 빽빽한 킥(투베이스) → B, 빽빽한 스네어 → A
        var bars = new List<Ev>();
        if (s.makeBarrages)
        {
            int min = Math.Max(3, s.minBarrageCount);
            for (int band = 0; band < 2; band++)
            {
                var list = peaks.FindAll(p => p.band == band);
                int rs = 0;
                for (int k = 1; k <= list.Count; k++)
                {
                    if (k < list.Count && list[k].t - list[k - 1].t <= BarrageGap) continue;
                    int size = k - rs;
                    if (size >= min)
                    {
                        double t1 = list[rs].t, t2 = list[k - 1].t;
                        bars.Add(new Ev
                        {
                            t = t1, type = NoteType.Barrage, lane = band == 0 ? Lane.Lower : Lane.Upper,
                            duration = Math.Max(0.3, t2 - t1), count = Math.Max(3, (int)Math.Round(size * 0.6)), score = 99
                        });
                    }
                    rs = k;
                }
            }
            // 겹치는 연타는 하나만: 킥(B) 연타를 먼저, 같은 레인끼리는 긴 쪽. 빽빽한 킥은 딸깍 소리가 중음에도 연타처럼 잡히기 때문.
            bars.Sort((x, y) => x.lane != y.lane ? y.lane.CompareTo(x.lane) : y.duration.CompareTo(x.duration));
            var keep = new List<Ev>();
            foreach (Ev b in bars)
            {
                bool overlap = false;
                foreach (Ev k in keep) if (b.t < k.t + k.duration + 0.2 && k.t < b.t + b.duration + 0.2) { overlap = true; break; }
                if (!overlap) keep.Add(b);
            }
            bars = keep;
        }

        // 3) 대역별 온셋을 타격 사건으로 묶기 (35ms 안이면 같은 타격)
        var evs = new List<Ev>();
        for (int k = 0; k < peaks.Count;)
        {
            int j = k;
            var ev = new Ev { t = peaks[k].t };
            float bestV = -1, lowOnly = 0;
            bool notKick = false;   // 전체 타격 곡선이 "킥이 아니다"라고 본 타격인지
            while (j < peaks.Count && peaks[j].t - peaks[k].t <= ClusterGap)
            {
                BandPeak p = peaks[j];
                if (p.band == 0 && p.lowOnly) { lowOnly = Math.Max(lowOnly, p.v); j++; continue; }
                if (p.band != 0 && p.band != 3 && !a.IsExternal) notKick = true;
                if (p.band == 0) ev.low = Math.Max(ev.low, p.v);
                else if (p.band == 1) ev.mid = Math.Max(ev.mid, p.v);
                else if (p.band == 2) ev.high = Math.Max(ev.high, p.v);
                else ev.other = Math.Max(ev.other, p.v);
                float w = p.band == 2 ? p.v * 0.6f : p.v;
                if (w > bestV) { bestV = w; ev.t = p.t; ev.cen = p.cen; }
                ev.harmRise = Math.Max(ev.harmRise, p.harmRise);
                j++;
            }
            // 저음 곡선에만 잡힌 킥: 같은 순간 전체 곡선이 스네어·심벌로 본 타격이면 그 울림일 뿐이라 뺀다.
            if (ev.low == 0 && lowOnly > 0 && !notKick) ev.low = lowOnly;
            if (bestV < 0) { ev.t = peaks[k].t; ev.cen = peaks[k].cen; }
            ev.score = Math.Max(Math.Max(ev.low, ev.mid), Math.Max(ev.high * 0.6f, ev.other * 0.8f));
            if (ev.score > 0) evs.Add(ev);
            k = j;
        }
        // 연타 구간 안(과 바로 뒤)의 타격은 뺀다
        evs.RemoveAll(e => bars.Exists(b => e.t >= b.t - 0.05 && e.t <= b.t + b.duration + 0.12));

        // 4) 박자 가중치와 구간 세기
        bool haveBeats = s.preferBeats && a.beats != null && a.beats.Length >= 4;
        foreach (Ev e in evs)
        {
            if (haveBeats)
            {
                bool down;
                double w = BeatWeight(a, e.t, out down, out e.onBeat);
                e.score *= w;
            }
            double I = s.sectionDensity ? IntensityAt(a, e.t) : 0.5;
            e.score /= Lerp(1.25, 0.85, I);   // 센 구간은 기준을 낮춘다(= 점수를 올린다)
        }
        evs.RemoveAll(e => e.score < absMin);

        // 5) 간격 정리: 최소 간격보다 가까우면 점수가 높은 쪽만 (구간 세기에 따라 간격이 바뀐다)
        var picks = new List<Ev>();
        foreach (Ev e in evs)
        {
            double gap = s.minGap * (s.sectionDensity ? Lerp(1.35, 0.75, IntensityAt(a, e.t)) : 1.0);
            if (picks.Count > 0 && e.t - picks[picks.Count - 1].t < gap)
            {
                if (e.score > picks[picks.Count - 1].score) picks[picks.Count - 1] = e;
            }
            else picks.Add(e);
        }

        // 6) 레인 (+ 동시치기)
        var cs = new List<float>();
        foreach (Ev e in picks) cs.Add(e.cen);
        cs.Sort();
        float median = cs.Count > 0 ? cs[cs.Count / 2] : 0f;
        var rng = new Random(s.seed);
        var taps = new List<Ev>();
        Lane prevLane = Lane.Lower;
        for (int k = 0; k < picks.Count; k++)
        {
            Ev e = picks[k];
            switch (s.laneMode)
            {
                case AutoLaneMode.Alternate: e.lane = (Lane)(k % 2); break;
                case AutoLaneMode.Random: e.lane = rng.NextDouble() < 0.5 ? Lane.Upper : Lane.Lower; break;
                case AutoLaneMode.Pitch:
                    e.lane = a.IsExternal ? (e.low >= Math.Max(e.mid, e.high) ? Lane.Lower : Lane.Upper)
                                          : (e.cen < median ? Lane.Lower : Lane.Upper);
                    break;
                default:   // 드럼: 킥 = B, 스네어·심벌 = A, 그 밖의 소리는 번갈아
                    if (e.low > 0 && e.low >= 0.75f * Math.Max(e.mid, e.high * 0.8f)) e.lane = Lane.Lower;
                    else if (e.mid > 0 || e.high > 0) e.lane = Lane.Upper;
                    else e.lane = prevLane == Lane.Upper ? Lane.Lower : Lane.Upper;
                    break;
            }
            prevLane = e.lane;
            e.type = NoteType.Tap;
            taps.Add(e);

            // 동시치기: 박 위에서 킥과 스네어가 둘 다 세게 칠 때, 앞뒤에 여유가 있으면
            // (새 지속음이 시작되는 순간은 중음이 커도 스네어가 아니라 화음이라서 뺀다)
            if (s.makeChords && e.onBeat && e.low >= 0.5f && e.mid >= 0.5f && e.harmRise < 0.5f)
            {
                double gap = s.minGap * 1.2;
                bool roomBefore = k == 0 || e.t - picks[k - 1].t >= gap;
                bool roomAfter = k + 1 >= picks.Count || picks[k + 1].t - e.t >= gap;
                if (roomBefore && roomAfter)
                {
                    e.chord = true;
                    Ev twin = e.Copy();
                    twin.lane = e.lane == Lane.Upper ? Lane.Lower : Lane.Upper;
                    taps.Add(twin);
                }
            }
        }
        taps.Sort((x, y) => x.t != y.t ? x.t.CompareTo(y.t) : x.lane.CompareTo(y.lane));

        // 7) 롱노트: 타격 뒤 지속음이 이어지는 곳 (같은 레인 다음 노트 전까지)
        if (s.makeLongs)
        {
            double minLong = Math.Max(0.2, s.minLongLength);
            var all = new List<Ev>(taps);
            all.AddRange(bars);
            var added = new List<Ev>();
            foreach (Ev e in taps)
            {
                if (e.chord) continue;
                double limit = LaneLimit(all, e, e.lane, Math.Min(end, e.t + 4.0));
                double sus = limit - e.t >= minLong ? SustainEnd(a, e.t, limit) : e.t;
                if (sus - e.t >= minLong)
                {
                    e.type = NoteType.Long;
                    e.duration = sus - e.t;
                    continue;
                }
                // 이 레인은 곧 다음 노트가 와서 못 누르고 있으면, 비어 있는 반대 레인에 지속음 롱노트를 따로 놓는다
                // (예: 화음이 울리는 순간의 킥 → 킥은 B 단노트, 화음은 A 롱노트로 동시에)
                if (s.laneMode != AutoLaneMode.Drums) continue;
                Lane other = e.lane == Lane.Upper ? Lane.Lower : Lane.Upper;
                if (!LaneFree(all, other, e.t, e)) continue;
                double limit2 = LaneLimit(all, e, other, Math.Min(end, e.t + 4.0));
                if (limit2 - e.t < minLong) continue;
                double sus2 = SustainEnd(a, e.t, limit2);
                if (sus2 - e.t < minLong) continue;
                Ev hold = e.Copy();
                hold.lane = other; hold.type = NoteType.Long; hold.duration = sus2 - e.t; hold.chord = true;
                e.chord = true;   // 둘이 한 묶음: 연속·밀도 정리에서 한쪽만 빠지지 않게
                added.Add(hold);
                all.Add(hold);
            }
            taps.AddRange(added);
        }

        var notes = new List<Ev>(taps);
        notes.AddRange(bars);
        notes.Sort((x, y) => x.t.CompareTo(y.t));

        // 8) 칠 수 있게: 같은 레인 연속 제한, 초당 노트 수 제한
        LimitJacks(notes, Math.Max(1, s.maxJack));
        LimitDensity(notes, a, s);

        foreach (Ev e in notes)
        {
            var nd = new NoteData { time = Round(e.t), lane = e.lane, type = e.type };
            if (e.type != NoteType.Tap) nd.duration = Round(e.duration);
            if (e.type == NoteType.Barrage) nd.count = e.count;
            result.Add(nd);
        }

        if (s.snapToGrid && snapDivision > 0 && tempo != null && tempo.Count > 0) SnapAndClean(result, tempo, snapDivision);
        else Clean(result);
        ChartMath.SortNotes(result);
        return result;
    }

    // 내부 곡선에서 타격 지점을 찾고, 소리 모양(저음·고음 비율)으로 킥·스네어·심벌을 가린다.
    // 전체 타격 곡선과 저음 곡선 두 곳에서 찾는다(빽빽한 곡에서 킥이 전체 곡선에 묻히는 경우가 있어서).
    static List<BandPeak> InternalPeaks(OnsetAnalysis a, double relThr, double absMin)
    {
        var res = new List<BandPeak>();
        int n = a.perc.Length;
        int w = Math.Max(2, (int)Math.Round(0.1 / a.hop));
        int near = Math.Max(1, (int)Math.Round(0.03 / a.hop));
        int rl = Math.Max(1, (int)Math.Round(0.05 / a.hop));
        float[][] envs = { a.perc, a.low };
        for (int which = 0; which < 2; which++)
        {
            float[] e = envs[which];
            var ps = Prefix(e);
            for (int i = 1; i < n - 1; i++)
            {
                float v = e[i];
                if (v < absMin) continue;
                int lo = Math.Max(0, i - w), hi = Math.Min(n, i + w + 1);
                if (v < relThr * (ps[hi] - ps[lo]) / (hi - lo)) continue;
                bool isMax = true;
                for (int j = Math.Max(0, i - near); j <= Math.Min(n - 1, i + near); j++)
                    if (e[j] > v || (e[j] == v && j < i)) { isMax = false; break; }
                if (!isMax) continue;

                double t = i * a.hop + a.offset;
                float rise = Math.Max(0f, MaxAround(a.harm, i + rl, 1) - a.harm[Math.Max(0, i - rl)]);
                var bp = new BandPeak { t = t, cen = a.centroid[i], harmRise = rise };
                float ls = a.lowShare[i], hs = a.highShare[i];
                if (which == 1)
                {
                    // 저음 곡선의 봉우리: 저음 에너지가 실제로 어느 정도 있을 때만 킥 (스네어의 약한 저음 울림은 뺀다)
                    // (아주 센 저음은 빽빽한 투베이스처럼 앞 킥 울림에 가려 비율이 낮게 나와도 킥으로 본다)
                    if (ls >= 0.3f || v >= 0.8f) { bp.band = 0; bp.v = v; bp.lowOnly = true; res.Add(bp); }
                    continue;
                }
                if (ls >= 0.65f) { bp.band = 0; bp.v = Math.Max(v, a.low[i]); res.Add(bp); }
                else if (hs >= 0.45f) { bp.band = 2; bp.v = Math.Max(v * 0.8f, a.high[i]); res.Add(bp); }
                else if (ls >= 0.3f)
                {
                    // 저음과 중음이 섞인 타격 = 킥 + 스네어가 같이 쳤을 수 있다 → 둘 다 넣는다(동시치기 후보)
                    bp.band = 0; bp.v = a.low[i]; res.Add(bp);
                    bp.band = 1; bp.v = a.mid[i]; res.Add(bp);
                }
                else { bp.band = 1; bp.v = Math.Max(v, a.mid[i]); res.Add(bp); }
            }
        }
        return res;
    }

    static List<BandPeak> ExternalPeaks(OnsetAnalysis a, double absMin)
    {
        var res = new List<BandPeak>();
        foreach (ExternalOnset o in a.onsets)
        {
            if (o.s < absMin) continue;
            string k = (o.kind ?? "").ToLowerInvariant();
            int band = k == "kick" ? 0 : k == "snare" ? 1 : (k == "hihat" || k == "cymbal") ? 2 : 3;
            res.Add(new BandPeak { t = o.t, v = Clamp01(o.s), band = band });
        }
        return res;
    }

    // 박자 격자 위치에 따른 점수 배율. 다운비트 1.35, 박 1.2, 8분 1.0, 16분 0.85, 셋잇단 0.9, 격자 밖 0.55
    static double BeatWeight(OnsetAnalysis a, double t, out bool downbeat, out bool onBeat)
    {
        downbeat = false; onBeat = false;
        double[] b = a.beats;
        int i = NearestIndex(b, t);
        if (i < 0) return 1;
        int i0 = b[i] <= t ? i : i - 1;
        if (i0 < 0) i0 = 0;
        int i1 = Math.Min(b.Length - 1, i0 + 1);
        double len = i1 > i0 ? b[i1] - b[i0] : (b.Length > 1 ? b[1] - b[0] : 0.5);
        if (len <= 0) return 1;
        double u = (t - b[i0]) / len;
        double[] fr = { 0, 1, 0.5, 0.25, 0.75, 1.0 / 3, 2.0 / 3 };
        double[] wt = { 1.2, 1.2, 1.0, 0.85, 0.85, 0.9, 0.9 };
        double bestD = double.MaxValue, w = 0.55; int bestK = -1;
        for (int k = 0; k < fr.Length; k++)
        {
            double d = Math.Abs(u - fr[k]) * len;
            if (d < bestD) { bestD = d; bestK = k; }
        }
        if (bestD > 0.045) return 0.55;
        w = wt[bestK];
        if (bestK <= 1)
        {
            onBeat = true;
            int bi = bestK == 0 ? i0 : i1;
            if (bi < a.downbeat.Length && a.downbeat[bi]) { downbeat = true; w = 1.35; }
        }
        return w;
    }

    static double IntensityAt(OnsetAnalysis a, double t)
    {
        if (a.sections == null) return 0.5;
        foreach (SongSection s in a.sections) if (t >= s.start && t < s.end) return s.intensity;
        return 0.5;
    }

    // 롱노트 끝: 타격 직후 지속음이 충분히 크고 새로 시작한 소리일 때, 처음 세기의 60% 아래로 떨어지기 전까지.
    static double SustainEnd(OnsetAnalysis a, double t, double limit)
    {
        if (a.IsExternal)
        {
            foreach (ExternalSustain s in a.sustains)
                if (Math.Abs(s.start - t) <= 0.08) return Math.Min(s.end, limit);
            return t;
        }
        int n = a.harm.Length;
        int i0 = FrameOf(a, t);
        if (i0 < 0 || i0 >= n) return t;
        int rl = Math.Max(1, (int)Math.Round(0.05 / a.hop));
        float before = a.harm[Math.Max(0, i0 - rl)];
        float peak = 0;
        for (int j = i0; j <= Math.Min(n - 1, i0 + rl); j++) peak = Math.Max(peak, a.harm[j]);
        if (peak < 0.35f || peak - before < 0.05f) return t;   // 약하거나, 원래 울리던 소리면 롱노트 아님
        double sustain = t;
        for (int j = i0 + rl; j < n; j++)
        {
            double tj = j * a.hop + a.offset;
            if (tj >= limit || a.harm[j] < 0.6f * peak) break;
            sustain = tj;
        }
        return sustain;
    }

    // 같은 레인이 빠르게(0.35초 이내) maxJack번 넘게 이어지면, 다른 레인이 비어 있을 때 넘기고 아니면 뺀다.
    static void LimitJacks(List<Ev> notes, int maxJack)
    {
        int run = 0; Ev prev = null;
        for (int k = 0; k < notes.Count; k++)
        {
            Ev e = notes[k];
            if (e.type == NoteType.Barrage || e.chord) { run = 0; prev = null; continue; }
            if (prev != null && prev.lane == e.lane && e.t - prev.t < 0.35) run++;
            else run = 1;
            if (run > maxJack)
            {
                Lane other = e.lane == Lane.Upper ? Lane.Lower : Lane.Upper;
                if (LaneFree(notes, other, e.t, e))
                {
                    e.lane = other;
                    run = 1;
                }
                else
                {
                    notes.RemoveAt(k);
                    k--;
                    continue;
                }
            }
            prev = e;
        }
    }

    // e 다음에 lane 레인에 오는 노트 직전(0.12초 여유)까지. 없으면 cap.
    static double LaneLimit(List<Ev> all, Ev e, Lane lane, double cap)
    {
        double limit = cap;
        foreach (Ev o in all)
            if (o != e && o.lane == lane && o.t > e.t + 1e-6 && o.t - 0.12 < limit) limit = o.t - 0.12;
        return limit;
    }

    static bool LaneFree(List<Ev> notes, Lane lane, double t, Ev self)
    {
        foreach (Ev o in notes)
        {
            if (o == self || o.lane != lane) continue;
            double oEnd = o.t + (o.type != NoteType.Tap ? o.duration : 0);
            if (Math.Abs(o.t - t) < ChartValidator.MinGap * 1.5) return false;
            if (t >= o.t && t <= oEnd + 0.05) return false;
        }
        return true;
    }

    // 1초 안의 노트 수가 상한을 넘으면 점수가 가장 낮은 단노트부터 뺀다. 상한은 구간 세기에 따라 조금 바뀐다.
    static void LimitDensity(List<Ev> notes, OnsetAnalysis a, AutoGenSettings s)
    {
        if (s.maxNps <= 0) return;
        for (int k = 0; k < notes.Count; k++)
        {
            double t0 = notes[k].t;
            double cap = s.maxNps * (s.sectionDensity ? Lerp(0.8, 1.15, IntensityAt(a, t0)) : 1.0);
            while (true)
            {
                int count = 0, weakest = -1;
                for (int j = k; j < notes.Count && notes[j].t < t0 + 1.0; j++)
                {
                    count++;
                    Ev e = notes[j];
                    if (e.type == NoteType.Tap && !e.chord && (weakest < 0 || e.score < notes[weakest].score)) weakest = j;
                }
                if (count <= Math.Ceiling(cap) || weakest < 0) break;
                notes.RemoveAt(weakest);
                if (weakest < k) k--;
            }
        }
    }

    // 격자에 맞춘 뒤 정리한다.
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
        Clean(notes);
    }

    // 같은 레인에서 겹치거나 너무 붙은 노트를 정리한다.
    static void Clean(List<NoteData> notes)
    {
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

    // ═════════════════════════════════ 도구 ═════════════════════════════════

    static int FrameOf(OnsetAnalysis a, double t) { return (int)Math.Round((t - a.offset) / a.hop); }

    static float MaxAround(float[] e, int i, int r)
    {
        float m = 0;
        for (int j = Math.Max(0, i - r); j <= Math.Min(e.Length - 1, i + r); j++) m = Math.Max(m, e[j]);
        return m;
    }

    static int NearestIndex(double[] sorted, double t)
    {
        if (sorted == null || sorted.Length == 0) return -1;
        int i = Array.BinarySearch(sorted, t);
        if (i >= 0) return i;
        i = ~i;
        if (i <= 0) return 0;
        if (i >= sorted.Length) return sorted.Length - 1;
        return t - sorted[i - 1] <= sorted[i] - t ? i - 1 : i;
    }

    static double Interp(double[] r, double x)
    {
        int i = (int)Math.Floor(x);
        if (i < 0 || i + 1 >= r.Length) return 0;
        double f = x - i;
        return r[i] * (1 - f) + r[i + 1] * f;
    }

    static double[] Prefix(float[] e)
    {
        var ps = new double[e.Length + 1];
        for (int i = 0; i < e.Length; i++) ps[i + 1] = ps[i] + e[i];
        return ps;
    }

    static float[] Smooth(float[] e, int r)
    {
        var o = new float[e.Length];
        for (int f = 0; f < e.Length; f++)
        {
            double s = 0; int c = 0;
            for (int j = Math.Max(0, f - r); j <= Math.Min(e.Length - 1, f + r); j++) { s += e[j]; c++; }
            o[f] = (float)(s / c);
        }
        return o;
    }

    // 99번째 백분위수로 나눠 대략 0~1로 만든다 (드문 아주 큰 값은 2에서 자른다).
    static float[] Normalize(float[] e)
    {
        float r = Percentile(e, 0.99f);
        var o = new float[e.Length];
        if (r <= 0) return o;
        for (int i = 0; i < e.Length; i++) o[i] = Math.Min(2f, e[i] / r);
        return o;
    }

    static float Percentile(float[] e, float q)
    {
        if (e.Length == 0) return 0;
        int step = Math.Max(1, e.Length / 50000);
        var s = new List<float>();
        for (int i = 0; i < e.Length; i += step) s.Add(e[i]);
        s.Sort();
        return s[Math.Min(s.Count - 1, (int)(q * (s.Count - 1)))];
    }

    static float Median(float[] buf, int c)
    {
        // 작은 배열이라 삽입 정렬
        for (int i = 1; i < c; i++)
        {
            float v = buf[i]; int j = i - 1;
            while (j >= 0 && buf[j] > v) { buf[j + 1] = buf[j]; j--; }
            buf[j + 1] = v;
        }
        return buf[c / 2];
    }

    static double Lerp(double a, double b, double t) { return a + (b - a) * Math.Max(0, Math.Min(1, t)); }
    static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
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
