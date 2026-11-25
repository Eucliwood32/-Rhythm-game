using System;
using UnityEngine;

/// <summary>
/// 플레이어 입력과 노트 시간을 비교해서 판정을 내리고 이벤트로 알린다.
/// 점수·체력 같은 규칙은 모른다(그건 Referee가 한다).
/// 판정 규칙 자체는 JudgeCore에 있고(채보 편집기의 테스트 플레이도 같은 것을 쓴다),
/// 이 컴포넌트는 곡 시간·키 입력·Inspector 설정을 JudgeCore에 연결한다.
/// </summary>
public class Judge : MonoBehaviour
{
    public const string PrefInputOffset = RhythmDefaults.PrefInputOffset;

    [SerializeField] MusicPlayer music;
    [SerializeField] RhythmInput input;
    [SerializeField] Composer composer;

    [Header("판정 범위 (초, 노트 시간 기준 앞뒤 ±)")]
    [Tooltip("이 범위 안이면 Perfect")]
    [SerializeField] double perfectWindow = RhythmDefaults.PerfectWindow;
    [Tooltip("이 범위 안이면 Good. 이 범위를 지나도록 못 치면 Miss")]
    [SerializeField] double goodWindow = RhythmDefaults.GoodWindow;

    [Header("롱노트 / 연타")]
    [Tooltip("롱노트가 끝나기 이만큼(초) 전에 키를 떼는 것은 봐준다")]
    [SerializeField] double holdReleaseGrace = RhythmDefaults.HoldReleaseGrace;
    [Tooltip("연타 구간에서 입력과 입력 사이가 이 시간(초)보다 벌어지면 Miss. 클수록 쉬워진다. (0.3 = 초당 약 3.3번 이상)")]
    [SerializeField] double barrageMaxGap = RhythmDefaults.BarrageMaxGap;

    [Header("입력 지연 보정")]
    [Tooltip("입력이 늘 늦게 들어오면 양수. 게임 중 - / = 키로 5ms씩 조절하고 저장된다. 채보 편집기와 같은 값을 쓴다.")]
    [SerializeField] double inputOffsetMs = 0;

    JudgeCore core;

    /// <summary>판정 결과, 레인, 오차(ms; 음수=일찍, 양수=늦게). 연타와 롱노트 끊김은 오차 0. Referee가 쓴다.</summary>
    public event Action<Judgment, Lane, double> OnJudged;

    /// <summary>OnJudged와 같은 판정에 노트와 상황을 붙인 것. 롱노트 완료도 여기로만 온다. 화면 연출이 쓴다.</summary>
    public event Action<NoteData, Judgment, Lane, double, JudgeKind> OnNoteJudged;

    /// <summary>연타 구간에서 입력이 들어올 때마다: 노트, 레인, 현재 횟수, 필요 횟수.</summary>
    public event Action<NoteData, Lane, int, int> OnBarrageHit;

    /// <summary>입력 지연 보정(ms). 바꾸면 저장된다.</summary>
    public double InputOffsetMs
    {
        get { return inputOffsetMs; }
        set
        {
            inputOffsetMs = Math.Max(-300, Math.Min(300, value));
            PlayerPrefs.SetFloat(PrefInputOffset, (float)inputOffsetMs);
            PlayerPrefs.Save();
        }
    }

    // 보정을 적용한 판정 기준 시각
    double Now => music.SongTime - inputOffsetMs / 1000.0;

    JudgeCore Core
    {
        get
        {
            if (core == null)
            {
                core = new JudgeCore(lane => composer.GetNotes(lane));
                core.Judged += Emit;
                core.BarrageHit += (n, l, h, need) => { if (OnBarrageHit != null) OnBarrageHit(n, l, h, need); };
            }
            ApplySettings(core);
            return core;
        }
    }

    /// <summary>이 Judge의 Inspector 판정 설정을 JudgeCore에 적용한다. (채보 편집기 테스트 플레이가 씬 설정을 따르는 데 쓴다)</summary>
    public void ApplySettings(JudgeCore target)
    {
        target.perfectWindow = perfectWindow;
        target.goodWindow = goodWindow;
        target.holdReleaseGrace = holdReleaseGrace;
        target.barrageMaxGap = barrageMaxGap;
    }

    void Awake()
    {
        if (music == null) music = FindAnyObjectByType<MusicPlayer>();
        if (input == null) input = FindAnyObjectByType<RhythmInput>();
        if (composer == null) composer = FindAnyObjectByType<Composer>();
        if (PlayerPrefs.HasKey(PrefInputOffset)) inputOffsetMs = PlayerPrefs.GetFloat(PrefInputOffset);
    }

    void OnEnable()
    {
        if (music == null || input == null || composer == null)
        {
            Debug.LogError("[Judge] MusicPlayer, RhythmInput, Composer를 씬에서 찾지 못했습니다.");
            enabled = false;
            return;
        }
        input.OnPressed += HandlePressed;
    }

    void OnDisable()
    {
        if (input != null) input.OnPressed -= HandlePressed;
    }

    void Update()
    {
        if (!music.IsStarted) return;
        Core.Update(Now, input.IsHeld);
    }

    /// <summary>새 곡을 시작하기 전에 판정 상태를 처음으로 되돌린다.</summary>
    public void ResetState()
    {
        Core.Reset();
    }

    /// <summary>해당 레인에서 아직 판정되지 않은 가장 앞 노트. 없으면 null.</summary>
    public NoteData PeekNext(Lane lane)
    {
        return Core.PeekNext(lane);
    }

    /// <summary>해당 레인에서 지금 누르고 있는 롱노트. 없으면 null.</summary>
    public NoteData HoldingNote(Lane lane)
    {
        return Core.HoldingNote(lane);
    }

    /// <summary>연타 노트에 필요한 입력 횟수.</summary>
    public int NeededHits(NoteData note)
    {
        return Core.NeededHits(note);
    }

    void HandlePressed(Lane lane)
    {
        if (!music.IsStarted) return;
        // 키가 실제로 눌린 순간의 곡 시간으로 판정한다. 이 함수는 키가 눌린 뒤 조금 늦게(보통 한 프레임 이내) 불리므로
        // RhythmInput이 잰 지연(LastPressLatency)만큼 빼서 되돌린다.
        Core.Press(lane, Now - input.LastPressLatency);
    }

    void Emit(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        // 롱노트 완료는 점수 대상이 아니므로 Referee용 이벤트에는 보내지 않는다.
        if (kind != JudgeKind.HoldComplete && OnJudged != null) OnJudged(result, lane, errorMs);
        if (OnNoteJudged != null) OnNoteJudged(note, result, lane, errorMs, kind);
    }
}
