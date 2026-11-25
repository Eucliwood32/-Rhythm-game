using System;

/// <summary>
/// 게임과 채보 편집기가 함께 쓰는 기본값과 규칙을 한 곳에 모은 것. Unity API를 쓰지 않는다.
/// 판정 범위를 바꾸려면 여기의 기본값을 고치거나, 씬의 Judge Inspector에서 고친다.
/// (채보 편집기의 테스트 플레이는 씬에 Judge가 있으면 그 값을, 없으면 이 기본값을 쓴다)
/// </summary>
public static class RhythmDefaults
{
    /// <summary>이 범위(초) 안이면 Perfect.</summary>
    public const double PerfectWindow = 0.040;
    /// <summary>이 범위(초) 안이면 Good. 지나도록 못 치면 Miss. 같은 레인 노트 간격 검사 기준이기도 하다.</summary>
    public const double GoodWindow = 0.080;
    /// <summary>롱노트가 끝나기 이만큼(초) 전에 떼는 것은 봐준다.</summary>
    public const double HoldReleaseGrace = 0.15;
    /// <summary>연타 구간에서 입력과 입력 사이가 이 시간(초)보다 벌어지면 그 연타는 Miss.</summary>
    public const double BarrageMaxGap = 0.3;
    /// <summary>(채보 편집기용) 연타 노트의 표시 횟수를 길이로 정할 때 쓰는 초당 입력 횟수. 판정에는 쓰지 않는다.</summary>
    public const double BarrageHitsPerSecond = 6;

    /// <summary>입력 지연 보정(ms)을 저장하는 키. 게임과 채보 편집기가 같은 값을 쓴다.</summary>
    public const string PrefInputOffset = "Judge.InputOffsetMs";

    /// <summary>연타 길이에 맞는 필요 입력 횟수 (최소 1).</summary>
    public static int BarrageCount(double duration, double hitsPerSecond)
    {
        return Math.Max(1, (int)Math.Round(duration * hitsPerSecond));
    }

    /// <summary>판정 글자.</summary>
    public static string JudgmentText(Judgment j)
    {
        return j == Judgment.Perfect ? "PERFECT" : j == Judgment.Good ? "GOOD" : "MISS";
    }
}
