using System;
using System.Collections.Generic;

public enum Judgment
{
    Perfect,
    Good,
    Miss
}

/// <summary>판정이 어떤 상황에서 나왔는지. 화면 연출이 구분해서 쓴다.</summary>
public enum JudgeKind
{
    Hit,           // 단노트, 롱노트 시작, 놓친 노트
    HoldBreak,     // 롱노트를 중간에 뗌 (Miss)
    HoldComplete,  // 롱노트를 끝까지 누름 (점수에는 들어가지 않는 연출용 신호)
    Barrage        // 연타 구간 결과
}

/// <summary>
/// 판정 규칙 그 자체. Unity API를 쓰지 않아서 게임(Judge)과 채보 편집기의 테스트 플레이가 똑같이 쓴다.
/// - 단노트: 누른 시각으로 Perfect/Good, 못 치면 Miss
/// - 롱노트: 시작을 단노트처럼 판정하고, 끝날 때까지 누르고 있어야 한다. 중간에 떼면 Miss 한 번. 끝에서 떼는 시점은 판정하지 않는다.
/// - 연타: 구간 동안 계속 두드려야 한다. 입력 사이가 barrageMaxGap초보다 벌어지면(손을 멈추면) 그 순간 Miss,
///         구간 끝까지 간격을 지키면 Perfect. 구간에서 한 번도 안 치면 Miss.
/// 시각은 모두 입력 보정을 적용한 곡 시간(초)으로 받는다.
/// </summary>
public class JudgeCore
{
    public double perfectWindow = RhythmDefaults.PerfectWindow;
    public double goodWindow = RhythmDefaults.GoodWindow;
    public double holdReleaseGrace = RhythmDefaults.HoldReleaseGrace;
    public double barrageHitsPerSecond = RhythmDefaults.BarrageHitsPerSecond;   // 판정에는 안 씀 (NeededHits 표시용)
    public double barrageMaxGap = RhythmDefaults.BarrageMaxGap;

    /// <summary>레인별 시간순 노트 목록을 돌려주는 함수.</summary>
    public Func<Lane, IReadOnlyList<NoteData>> notes;

    readonly int[] cursor = new int[2];            // 레인별로 다음에 판정할 노트 인덱스 (Upper = 0, Lower = 1)
    readonly NoteData[] holding = new NoteData[2]; // 레인별로 지금 누르고 있어야 하는 롱노트
    readonly int[] barrageHits = new int[2];       // 레인별 현재 연타 입력 횟수
    readonly double[] barrageLast = new double[2]; // 레인별 마지막 연타 입력 시각

    /// <summary>노트, 결과, 레인, 오차(ms; 음수=일찍, 양수=늦게), 상황.</summary>
    public event Action<NoteData, Judgment, Lane, double, JudgeKind> Judged;

    /// <summary>연타 구간에서 입력이 들어올 때마다: 노트, 레인, 현재 횟수, 필요 횟수(간격 판정이라 항상 0).</summary>
    public event Action<NoteData, Lane, int, int> BarrageHit;

    public JudgeCore(Func<Lane, IReadOnlyList<NoteData>> notes)
    {
        this.notes = notes;
    }

    /// <summary>판정 상태를 처음으로 되돌린다.</summary>
    public void Reset()
    {
        for (int l = 0; l < 2; l++)
        {
            cursor[l] = 0;
            holding[l] = null;
            barrageHits[l] = 0;
            barrageLast[l] = 0;
        }
    }

    /// <summary>
    /// 곡 중간(time)부터 시작한다. 이미 판정 범위를 지난 노트는 판정 없이 건너뛴다.
    /// (진행 중인 롱노트·연타도 건너뛴다)
    /// </summary>
    public void StartAt(double time)
    {
        Reset();
        for (int l = 0; l < 2; l++)
        {
            var list = notes((Lane)l);
            while (cursor[l] < list.Count && list[cursor[l]].time < time - goodWindow) cursor[l]++;
        }
    }

    /// <summary>해당 레인에서 아직 판정되지 않은 가장 앞 노트. 없으면 null.</summary>
    public NoteData PeekNext(Lane lane)
    {
        var list = notes(lane);
        int i = cursor[(int)lane];
        return i < list.Count ? list[i] : null;
    }

    /// <summary>해당 레인에서 지금 누르고 있는 롱노트. 없으면 null.</summary>
    public NoteData HoldingNote(Lane lane)
    {
        return holding[(int)lane];
    }

    /// <summary>해당 레인의 지금 연타 횟수.</summary>
    public int BarrageCount(Lane lane)
    {
        return barrageHits[(int)lane];
    }

    /// <summary>연타 노트에 필요한 입력 횟수.</summary>
    public int NeededHits(NoteData note)
    {
        return note.count > 0 ? note.count : RhythmDefaults.BarrageCount(note.duration, barrageHitsPerSecond);
    }

    /// <summary>키를 누른 순간. t는 보정된 곡 시간.</summary>
    public void Press(Lane lane, double t)
    {
        ProcessMisses(t);   // 입력과 갱신의 순서가 정해져 있지 않으므로 먼저 지나간 노트를 정리한다.

        NoteData note = PeekNext(lane);
        if (note == null) return;
        int l = (int)lane;

        if (note.type == NoteType.Barrage)
        {
            if (t >= note.time - goodWindow)   // 구간 안의 입력은 횟수만 센다 (시작 직전 입력도 봐준다)
            {
                barrageHits[l]++;
                barrageLast[l] = t;
                if (BarrageHit != null) BarrageHit(note, lane, barrageHits[l], 0);
            }
            return;
        }

        double diff = t - note.time;      // 음수 = 일찍, 양수 = 늦게
        if (diff < -goodWindow) return;   // 아직 너무 이르다: 무시 (빈 입력에 페널티 없음)

        Judgment result = Math.Abs(diff) <= perfectWindow ? Judgment.Perfect : Judgment.Good;
        cursor[l]++;
        if (note.type == NoteType.Long && note.duration > 0) holding[l] = note;
        Emit(note, result, lane, diff * 1000.0, JudgeKind.Hit);
    }

    /// <summary>매 프레임. t는 보정된 곡 시간, isHeld는 레인의 키를 누르고 있는지.</summary>
    public void Update(double t, Func<Lane, bool> isHeld)
    {
        ProcessMisses(t);
        UpdateHolds(t, isHeld);
    }

    // 지나가 버린 노트를 정리한다. 단노트·롱노트 시작은 Miss, 연타는 구간이 끝나면 결과를 낸다.
    void ProcessMisses(double t)
    {
        for (int l = 0; l < 2; l++)
        {
            Lane lane = (Lane)l;
            while (true)
            {
                NoteData note = PeekNext(lane);
                if (note == null) break;

                if (note.type == NoteType.Barrage)
                {
                    double end = note.time + note.duration;
                    // 간격을 재는 기준: 마지막 입력 시각. 아직 안 쳤거나 구간 시작 전에 친 것이면 구간 시작 시각.
                    double from = barrageHits[l] > 0 ? Math.Max(barrageLast[l], note.time) : note.time;
                    double upTo = Math.Min(t, end);
                    if (upTo - from > barrageMaxGap)
                    {
                        FinishBarrage(lane, note, Judgment.Miss);    // 손을 멈춤 → 그 자리에서 Miss
                        continue;
                    }
                    if (t <= end) break;                            // 아직 구간 중
                    FinishBarrage(lane, note, barrageHits[l] > 0 ? Judgment.Perfect : Judgment.Miss);
                    continue;
                }

                if (t <= note.time + goodWindow) break;
                cursor[l]++;
                Emit(note, Judgment.Miss, lane, (t - note.time) * 1000.0, JudgeKind.Hit);
            }
        }
    }

    // 누르고 있어야 하는 롱노트를 확인한다. 끝나기 직전이면 완료, 그 전에 떼면 Miss.
    void UpdateHolds(double t, Func<Lane, bool> isHeld)
    {
        for (int l = 0; l < 2; l++)
        {
            NoteData h = holding[l];
            if (h == null) continue;

            if (t >= h.time + h.duration - holdReleaseGrace)
            {
                holding[l] = null;
                Emit(h, Judgment.Perfect, (Lane)l, 0.0, JudgeKind.HoldComplete);
            }
            else if (!isHeld((Lane)l))
            {
                holding[l] = null;
                Emit(h, Judgment.Miss, (Lane)l, 0.0, JudgeKind.HoldBreak);
            }
        }
    }

    void FinishBarrage(Lane lane, NoteData note, Judgment result)
    {
        int l = (int)lane;
        barrageHits[l] = 0;
        cursor[l]++;
        Emit(note, result, lane, 0.0, JudgeKind.Barrage);
    }

    void Emit(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        if (Judged != null) Judged(note, result, lane, errorMs, kind);
    }
}
