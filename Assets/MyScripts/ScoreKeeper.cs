using System;

/// <summary>Add 한 번으로 바뀐 것 (Referee가 이벤트를 낼지 정하는 데 쓴다).</summary>
public struct ScoreChange
{
    public bool health;   // 체력 변화가 있었음 (회복·피해 값이 0이 아님)
    public bool score;    // 점수가 올랐음
    public bool combo;    // 콤보가 바뀌었음
}

/// <summary>
/// 체력·점수·콤보 규칙 그 자체. Unity API를 쓰지 않아서 게임(Referee)과 채보 편집기의 테스트 플레이가 똑같이 쓴다.
/// - Perfect/Good: 콤보 +1, 점수(콤보가 쌓일수록 배율 상승), 체력 회복
/// - Miss: 콤보 0, 체력 감소
/// </summary>
public class ScoreKeeper
{
    public float maxHealth = 100f;
    public float missDamage = 10f;
    public float perfectHeal = 1f;
    public float goodHeal = 0f;
    public int perfectScore = 300;
    public int goodScore = 100;
    public int comboStep = 10;
    public float bonusPerStep = 0.1f;
    public int maxBonusSteps = 10;
    /// <summary>점수에 추가로 곱하는 배율 (피버 타임 중 2). Referee가 정한다.</summary>
    public float scoreMultiplier = 1f;

    public float Health { get; private set; }
    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int Perfect { get; private set; }
    public int Good { get; private set; }
    public int Miss { get; private set; }

    /// <summary>체력이 0이 됐는지.</summary>
    public bool IsDead => Health <= 0f;

    public ScoreKeeper()
    {
        Reset();
    }

    public void Reset()
    {
        Health = maxHealth;
        Score = Combo = MaxCombo = Perfect = Good = Miss = 0;
    }

    public ScoreChange Add(Judgment result)
    {
        var change = new ScoreChange();
        switch (result)
        {
            case Judgment.Perfect:
                Perfect++;
                AddCombo(ref change);
                AddScore(perfectScore, ref change);
                ChangeHealth(perfectHeal, ref change);
                break;
            case Judgment.Good:
                Good++;
                AddCombo(ref change);
                AddScore(goodScore, ref change);
                ChangeHealth(goodHeal, ref change);
                break;
            case Judgment.Miss:
                Miss++;
                if (Combo != 0) { Combo = 0; change.combo = true; }
                ChangeHealth(-missDamage, ref change);
                break;
        }
        return change;
    }

    /// <summary>체력을 amount만큼 회복한다 (하트 학생). 최대 체력을 넘지 않는다. 실제로 바뀌었으면 true.</summary>
    public bool Heal(float amount)
    {
        float before = Health;
        Health = Math.Max(0f, Math.Min(maxHealth, Health + amount));
        return Health != before;
    }

    public StageResult Result()
    {
        return new StageResult { score = Score, maxCombo = MaxCombo, perfect = Perfect, good = Good, miss = Miss };
    }

    void AddCombo(ref ScoreChange change)
    {
        Combo++;
        if (Combo > MaxCombo) MaxCombo = Combo;
        change.combo = true;
    }

    void AddScore(int baseScore, ref ScoreChange change)
    {
        int step = Math.Min(Combo / Math.Max(1, comboStep), maxBonusSteps);
        float multiplier = 1f + step * bonusPerStep;
        Score += (int)Math.Round(baseScore * multiplier * scoreMultiplier);
        change.score = true;
    }

    void ChangeHealth(float delta, ref ScoreChange change)
    {
        if (Math.Abs(delta) < 1e-6f) return;
        Health = Math.Max(0f, Math.Min(maxHealth, Health + delta));
        change.health = true;
    }
}
