using UnityEngine;

/// <summary>
/// 개발용 확인 도구. 곡 시간, 싱크 차이, 마지막 입력/판정, 레인별 다음 노트, 체력·점수·콤보, 입력 보정을
/// 화면 왼쪽 아래에 작게 띄우고 콘솔에 로그를 남긴다. F1로 켜고 끈다(GameFlow가 키를 처리).
/// 게임 로직에는 관여하지 않는다.
/// </summary>
public class DebugOverlay : MonoBehaviour
{
    [SerializeField] MusicPlayer music;
    [SerializeField] RhythmInput input;
    [SerializeField] Judge judge;
    [SerializeField] Referee referee;

    [Tooltip("처음에 보이게 할지. 게임 중 F1로 바꿀 수 있다.")]
    [SerializeField] bool visible = false;
    [Tooltip("입력·판정을 콘솔에 남길지")]
    [SerializeField] bool logToConsole = true;

    string lastInput = "-";
    string lastJudge = "-";
    GUIStyle style;
    GUIStyle box;

    public bool Visible { get { return visible; } set { visible = value; } }

    void Awake()
    {
        if (music == null) music = FindAnyObjectByType<MusicPlayer>();
        if (input == null) input = FindAnyObjectByType<RhythmInput>();
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (referee == null) referee = FindAnyObjectByType<Referee>();
    }

    void OnEnable()
    {
        if (music == null || input == null || judge == null || referee == null)
        {
            Debug.LogError("[DebugOverlay] MusicPlayer, RhythmInput, Judge, Referee를 씬에서 찾지 못했습니다.");
            enabled = false;
            return;
        }
        input.OnPressed += HandlePressed;
        judge.OnJudged += HandleJudged;
        referee.OnStageCleared += HandleCleared;
        referee.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        if (input != null) input.OnPressed -= HandlePressed;
        if (judge != null) judge.OnJudged -= HandleJudged;
        if (referee != null)
        {
            referee.OnStageCleared -= HandleCleared;
            referee.OnGameOver -= HandleGameOver;
        }
    }

    void HandlePressed(Lane lane)
    {
        double t = music.SongTime;
        lastInput = $"{lane} @ {t:F3}s";
        if (logToConsole) Debug.Log($"[Input] {lane} 레인 입력 - 곡 시간 {t:F3}초 ({t * 1000.0:F0}ms)");
    }

    void HandleJudged(Judgment result, Lane lane, double errorMs)
    {
        lastJudge = $"{result} ({lane}, {errorMs:+0;-0;0} ms)";
        if (logToConsole) Debug.Log($"[Judge] {result} - {lane} 레인, 오차 {errorMs:+0;-0;0}ms");
    }

    void HandleCleared(StageResult r)
    {
        Debug.Log($"[Referee] 스테이지 클리어 - 점수 {r.score}, 최대 콤보 {r.maxCombo}, Perfect {r.perfect} / Good {r.good} / Miss {r.miss}");
    }

    void HandleGameOver(StageResult r)
    {
        Debug.Log($"[Referee] 게임 오버 - 점수 {r.score}, 최대 콤보 {r.maxCombo}, Perfect {r.perfect} / Good {r.good} / Miss {r.miss}");
    }

    string NextText(Lane lane)
    {
        NoteData n = judge.PeekNext(lane);
        return n == null ? "-" : $"{n.time:F3}s (in {n.time - music.SongTime:F2}s)";
    }

    void OnGUI()
    {
        if (!visible) return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            style.normal.textColor = Color.white;
            box = new GUIStyle(GUI.skin.box);
        }

        double audioPos = music.AudioPosition;
        string drift = audioPos < 0 ? "-" : $"{(music.SongTime - audioPos) * 1000.0:F0} ms";

        string text =
            $"Song {music.SongTime:F3}s   Audio {audioPos:F3}s   Drift {drift}\n" +
            $"Input {lastInput}   Judge {lastJudge}\n" +
            $"Next A {NextText(Lane.Upper)}   Next B {NextText(Lane.Lower)}\n" +
            $"HP {referee.Health:F0}/{referee.MaxHealth:F0}  Score {referee.Score}  Combo {referee.Combo} (Max {referee.MaxCombo})  {referee.State}\n" +
            $"Input offset {judge.InputOffsetMs:+0;-0;0} ms  ( - / = 로 조절, F1 숨기기 )";

        Rect r = new Rect(10, Screen.height - 110, 620, 100);
        GUI.Box(r, GUIContent.none, box);
        GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, r.height - 8), text, style);
    }
}
