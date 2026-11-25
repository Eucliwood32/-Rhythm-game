using UnityEngine;

/// <summary>
/// 게임과 채보 편집기가 함께 쓰는 색. 여기만 고치면 노트·판정 글자·편집기 색이 같이 바뀐다.
/// </summary>
public static class GameColors
{
    public static readonly Color LaneUpper = new Color(0.88f, 0.35f, 0.31f);   // A 레인 (위)
    public static readonly Color LaneLower = new Color(0.23f, 0.53f, 0.94f);   // B 레인 (아래)
    public static readonly Color Barrage = new Color(0.98f, 0.76f, 0.25f);     // 연타

    public static readonly Color Perfect = new Color(1f, 0.9f, 0.3f);
    public static readonly Color Good = new Color(0.45f, 0.9f, 1f);
    public static readonly Color Miss = new Color(1f, 0.4f, 0.4f);

    /// <summary>STAGE CLEAR, 등급 같은 강조 글자.</summary>
    public static Color Highlight => Perfect;
    /// <summary>GAME OVER, 보스 등장 같은 위험 글자.</summary>
    public static Color Danger => Miss;

    public static Color Lane(Lane lane)
    {
        return lane == global::Lane.Upper ? LaneUpper : LaneLower;
    }

    public static Color ForJudgment(Judgment j)
    {
        return j == Judgment.Perfect ? Perfect : j == Judgment.Good ? Good : Miss;
    }
}
