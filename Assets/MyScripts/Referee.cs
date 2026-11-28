using System;
using UnityEngine;

public enum GameState
{
    Playing,
    Cleared,
    Over
}

/// <summary>스테이지가 끝났을 때의 결과 요약.</summary>
[Serializable]
public struct StageResult
{
    public int score;
    public int maxCombo;
    public int perfect;
    public int good;
    public int miss;
}

/// <summary>
/// 게임 규칙을 맡는 컴포넌트. Judge의 판정 이벤트를 받아서 체력, 점수, 콤보를 관리하고 클리어/게임오버를 결정한다.
/// 체력·점수·콤보 계산 자체는 ScoreKeeper에 있고(채보 편집기의 테스트 플레이도 같은 것을 쓴다),
/// 이 컴포넌트는 Inspector 설정과 이벤트, 곡 종료·게임 오버 처리를 맡는다.
/// 화면 표시는 하지 않는다(UI는 이 클래스의 이벤트나 프로퍼티를 읽는다).
/// </summary>
public class Referee : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] MusicPlayer music;

    [Header("체력")]
    [SerializeField] float maxHealth = 100f;
    [Tooltip("Miss 한 번에 깎이는 체력")]
    [SerializeField] float missDamage = 10f;
    [Tooltip("Perfect 한 번에 회복되는 체력")]
    [SerializeField] float perfectHeal = 1f;
    [Tooltip("Good 한 번에 회복되는 체력")]
    [SerializeField] float goodHeal = 0f;

    [Header("점수")]
    [SerializeField] int perfectScore = 300;
    [SerializeField] int goodScore = 100;
    [Tooltip("콤보가 이 값만큼 쌓일 때마다 점수 배율이 한 단계 오른다")]
    [SerializeField] int comboStep = 10;
    [Tooltip("한 단계당 늘어나는 배율 (0.1이면 +10%)")]
    [SerializeField] float bonusPerStep = 0.1f;
    [Tooltip("배율이 오를 수 있는 최대 단계 수")]
    [SerializeField] int maxBonusSteps = 10;

    [Header("피버 타임")]
    [Tooltip("게이지가 이만큼 차면 피버")]
    [SerializeField] float feverGaugeMax = 60f;
    [SerializeField] float feverPerfectGain = 1f;
    [SerializeField] float feverGoodGain = 0.5f;
    [Tooltip("Miss 한 번에 줄어드는 게이지")]
    [SerializeField] float feverMissPenalty = 6f;
    [Tooltip("피버 지속 시간(초)")]
    [SerializeField] float feverTime = 8f;
    [Tooltip("피버 중 점수 배율")]
    [SerializeField] float feverScoreMultiplier = 2f;

    float feverGauge, feverLeft;

    ScoreKeeper keeper;

    public float Health => Keeper.Health;
    public float MaxHealth => maxHealth;
    public int Score => Keeper.Score;
    public int Combo => Keeper.Combo;
    public int MaxCombo => Keeper.MaxCombo;
    public int PerfectCount => Keeper.Perfect;
    public int GoodCount => Keeper.Good;
    public int MissCount => Keeper.Miss;
    public GameState State { get; private set; } = GameState.Playing;

    /// <summary>피버 타임 중인지. StudentLauncher(더 멀리 날리기)·GameHud(게이지·화면 연출)가 읽는다.</summary>
    public bool IsFever { get; private set; }
    /// <summary>피버 게이지 0~1. 피버 중에는 남은 시간 비율.</summary>
    public float FeverGauge => IsFever ? Mathf.Clamp01(feverLeft / Mathf.Max(0.01f, feverTime)) : Mathf.Clamp01(feverGauge / Mathf.Max(0.01f, feverGaugeMax));
    /// <summary>피버가 켜지거나 꺼질 때 (true = 시작).</summary>
    public event Action<bool> OnFeverChanged;

    /// <summary>(현재 체력, 최대 체력)</summary>
    public event Action<float, float> OnHealthChanged;
    public event Action<int> OnScoreChanged;
    public event Action<int> OnComboChanged;
    public event Action<StageResult> OnStageCleared;
    public event Action<StageResult> OnGameOver;

    ScoreKeeper Keeper
    {
        get
        {
            if (keeper == null) keeper = CreateScoreKeeper();
            return keeper;
        }
    }

    /// <summary>이 Referee의 Inspector 설정을 적용한 새 ScoreKeeper. (채보 편집기 테스트 플레이가 씬 설정을 따르는 데 쓴다)</summary>
    public ScoreKeeper CreateScoreKeeper()
    {
        var k = new ScoreKeeper
        {
            maxHealth = maxHealth,
            missDamage = missDamage,
            perfectHeal = perfectHeal,
            goodHeal = goodHeal,
            perfectScore = perfectScore,
            goodScore = goodScore,
            comboStep = comboStep,
            bonusPerStep = bonusPerStep,
            maxBonusSteps = maxBonusSteps
        };
        k.Reset();
        return k;
    }

    void Awake()
    {
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (music == null) music = FindAnyObjectByType<MusicPlayer>();
        keeper = CreateScoreKeeper();
    }

    /// <summary>새 스테이지를 시작하기 전에 체력·점수·콤보를 처음으로 되돌린다. (Inspector에서 바꾼 값도 이때 반영된다)</summary>
    public void ResetForStage()
    {
        keeper = CreateScoreKeeper();
        State = GameState.Playing;
        feverGauge = 0f;
        SetFever(false);

        OnHealthChanged?.Invoke(Health, maxHealth);
        OnScoreChanged?.Invoke(Score);
        OnComboChanged?.Invoke(Combo);
    }

    void OnEnable()
    {
        if (judge == null || music == null)
        {
            Debug.LogError("[Referee] Judge와 MusicPlayer를 씬에서 찾지 못했습니다.");
            enabled = false;
            return;
        }
        judge.OnJudged += HandleJudged;
        music.OnSongFinished += HandleSongFinished;
    }

    void OnDisable()
    {
        if (judge != null) judge.OnJudged -= HandleJudged;
        if (music != null) music.OnSongFinished -= HandleSongFinished;
    }

    void HandleJudged(Judgment result, Lane lane, double errorMs)
    {
        if (State != GameState.Playing) return;

        Keeper.scoreMultiplier = IsFever ? feverScoreMultiplier : 1f;   // 피버 중 점수 2배
        ScoreChange change = Keeper.Add(result);

        // 피버 게이지: 성공하면 차고 Miss면 줄어든다. 피버 중에는 안 바뀜.
        if (!IsFever)
        {
            if (result == Judgment.Miss) feverGauge = Mathf.Max(0f, feverGauge - feverMissPenalty);
            else feverGauge += result == Judgment.Perfect ? feverPerfectGain : feverGoodGain;
            if (feverGauge >= feverGaugeMax) { feverLeft = feverTime; SetFever(true); }
        }
        if (change.combo) OnComboChanged?.Invoke(Combo);
        if (change.score) OnScoreChanged?.Invoke(Score);
        if (change.health)
        {
            OnHealthChanged?.Invoke(Health, maxHealth);
            if (Keeper.IsDead) GameOver();
        }
    }

    /// <summary>체력 회복 (하트 학생을 맞혔을 때 NoteDisplay가 부른다).</summary>
    public void Heal(float amount)
    {
        if (State != GameState.Playing) return;
        if (Keeper.Heal(amount)) OnHealthChanged?.Invoke(Health, maxHealth);
    }

    void Update()
    {
        if (!IsFever) return;
        feverLeft -= Time.deltaTime;
        if (feverLeft <= 0f) { feverGauge = 0f; SetFever(false); }
    }

    void SetFever(bool on)
    {
        if (IsFever == on) return;
        IsFever = on;
        OnFeverChanged?.Invoke(on);
    }

    void HandleSongFinished()
    {
        if (State != GameState.Playing) return;

        State = GameState.Cleared;
        SetFever(false);
        OnStageCleared?.Invoke(Keeper.Result());
    }

    void GameOver()
    {
        State = GameState.Over;
        SetFever(false);
        music.Stop();
        OnGameOver?.Invoke(Keeper.Result());
    }
}
