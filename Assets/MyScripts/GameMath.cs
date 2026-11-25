using System;

/// <summary>
/// 게임 규칙에 쓰는 계산. Unity API를 쓰지 않아서 따로 테스트할 수 있다.
/// </summary>
public static class GameMath
{
    /// <summary>정확도 0~1. Perfect는 1, Good은 0.5, Miss는 0으로 친다. 판정이 하나도 없으면 1.</summary>
    public static double Accuracy(int perfect, int good, int miss)
    {
        int total = perfect + good + miss;
        if (total <= 0) return 1.0;
        return (perfect + 0.5 * good) / total;
    }

    /// <summary>등급. 클리어 못 했으면 F.</summary>
    public static string Rank(double accuracy, bool cleared)
    {
        if (!cleared) return "F";
        if (accuracy >= 0.95) return "S";
        if (accuracy >= 0.85) return "A";
        if (accuracy >= 0.70) return "B";
        return "C";
    }

    /// <summary>보스 남은 체력 비율 0~1. 성공한 판정 수 / 전체 노트 수만큼 깎인다.</summary>
    public static float BossHealth(int successfulHits, int totalNotes)
    {
        if (totalNotes <= 0) return 0f;
        double ratio = 1.0 - (double)successfulHits / totalNotes;
        return (float)Math.Max(0.0, Math.Min(1.0, ratio));
    }

    /// <summary>
    /// 곡 시작 전 카운트다운 문구. 3초보다 많이 남으면 READY, 3·2·1, 시작 직후 0.6초는 GO!, 그 뒤는 빈 문자열.
    /// </summary>
    public static string Countdown(double songTime)
    {
        if (songTime < 0)
        {
            int n = (int)Math.Ceiling(-songTime);
            return n > 3 ? "READY" : n.ToString();
        }
        return songTime < 0.6 ? "GO!" : "";
    }

    /// <summary>콤보가 every의 배수가 될 때마다 기술 이름을 외친다.</summary>
    public static bool IsShoutCombo(int combo, int every)
    {
        return every > 0 && combo > 0 && combo % every == 0;
    }
}
