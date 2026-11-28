// ============================================================================
// GameFlow.cs : 게임 전체 진행(타이틀 → 대사 → 스테이지 → 보스전 → 결과)을 관리하는 컴포넌트
// ----------------------------------------------------------------------------
// 6요소 중 담당: 영상의 Referee가 하던 "곡과 채보를 나눠주고 시작을 알리는" 역할. 점수 규칙은 Referee가 그대로 맡는다.
//
// 흐름: 타이틀 메뉴(시작 / 설정 / 종료) → (대사) → 스테이지 → STAGE CLEAR → (대사) → 보스 등장 → 보스전 → K.O. → (엔딩 대사) → 결과
//       실패하면 GAME OVER → 결과 (R: 그 스테이지 다시, Enter: 타이틀)
//       한 씬 안에서 전부 진행한다. 씬을 바꾸지 않고 UI를 켜고 끄며 화면을 전환한다.
//
// 상태 기계(State Machine):
//   phase 변수 하나가 "지금 어느 단계인가"를 나타내고, Update는 phase에 따라 다른 일을 한다.
//   예) Title이면 확인 키를 기다리고, StageClear면 타이머가 끝나기를 기다린다.
//   단계가 바뀌는 곳은 ShowTitle, PlayStory, StartSong, PlayStage, HandleCleared, HandleGameOver, ShowResult 같은 함수들이다.
//
// 스테이지 시작 순서 (StartSong):
//   곡 바꾸기(MusicPlayer.SetClip) → 채보 바꾸기(Composer.Load) → 판정·점수 초기화(Judge.ResetState, Referee.ResetForStage)
//   → 화면 노트 초기화(NoteDisplay.ResetForSong) → (보스면 보스 등장 연출) → 재생(MusicPlayer.Play)
//   순서가 중요하다: 채보를 먼저 바꿔야 NoteDisplay가 새 채보를 읽는다.
//
// 타이틀 메뉴: ↑↓(또는 W/S)로 고르고 Enter·Space·클릭으로 결정. 설정에서는 ←→로 값 조절, Esc로 돌아가기.
//   설정 값(음량, 입력 보정)은 PlayerPrefs에 저장돼서 다음 실행에도 유지된다.
// 배경: 스테이지마다 Background 칸의 배경 층(여러 겹)을 BackgroundDisplay가 뮤즈 대쉬처럼 흘린다.
//   비워 두면 BackgroundDisplay의 기본 배경을 쓴다.
//
// 단축키: F1 디버그 표시, - / = 입력 보정 ±5ms, Space·Enter·클릭 = 확인, Esc = 대사 건너뛰기, R = 다시 하기
//
// 연결: MusicPlayer, Composer, Judge, Referee, NoteDisplay, GameHud, BossDisplay, DebugOverlay를 모두 지휘한다.
//       Referee.OnStageCleared / OnGameOver --> 다음 단계로
// ============================================================================

using System;                      // Action, Math, [Serializable]
using System.Collections.Generic;  // List
using UnityEngine;
using UnityEngine.InputSystem;     // Keyboard, Mouse, Key

/// <summary>대화 한 줄 (말하는 사람 + 대사).</summary>
// [Serializable]이라 Inspector에서 대사 목록을 직접 편집할 수 있다.
[Serializable]
public class StoryLine
{
    public string speaker;                       // 말하는 사람. 비우면 해설(이름표 없음)
    [TextArea(2, 4)] public string text;         // [TextArea(최소 줄, 최대 줄)]: Inspector에서 여러 줄 입력 칸으로 보인다.
    [Tooltip("이 줄에서만 쓸 초상화 (표정). 비워두면 Portraits 목록에서 이름으로 찾은 기본 그림")]
    public Sprite portrait;

    // 생성자 두 개. 매개변수 없는 것은 Unity 직렬화(Inspector에서 새 줄 추가)에 필요하다.
    public StoryLine() { }
    public StoryLine(string speaker, string text) { this.speaker = speaker; this.text = text; }   // this.speaker = 필드, speaker = 매개변수
}

/// <summary>말하는 사람 이름과 초상화 그림 한 쌍.</summary>
[Serializable]
public class SpeakerPortrait
{
    public string speaker;   // 대사의 Speaker와 똑같이 적는다 (예: 강두철)
    public Sprite sprite;    // 예) 에셋의 Characters/Roi/Portrait
}

/// <summary>구간 기믹 종류.
/// Speed: 노트 속도 배율 / Hidden: 유령 모드 (화면 끝에서 점점 진해지며 판정선 value칸 앞에서 다 보임)
/// Zoom: 화면 확대(value) + 기울기(tilt) / Rush: 바람 가르는 집중선·흔들림·배경 빨리 흐름 (value = 세기 0~1) / Dark: 배경 밝기 (value = 0~1)</summary>
public enum GimmickType { Speed, Hidden, Zoom, Rush, Dark }

/// <summary>곡 시간 구간 하나의 기믹. 채보는 그대로 두고 시작·끝 초만 적는다.</summary>
[Serializable]
public class GimmickSection
{
    [Tooltip("메모용 이름 (동작에는 안 씀)")]
    public string label;
    public GimmickType type = GimmickType.Speed;
    [Tooltip("시작 시각 (곡 시간, 초)")] public float start;
    [Tooltip("끝 시각 (곡 시간, 초)")] public float end;
    [Tooltip("Speed: 배율 / Hidden: 다 보이는 거리(판정선에서) / Zoom: 확대 배율 / Rush: 세기 / Dark: 밝기")]
    public float value = 1f;
    [Tooltip("Zoom일 때 화면 기울기(도)")] public float tilt;

    public GimmickSection() { }
    public GimmickSection(string label, GimmickType type, float start, float end, float value, float tilt = 0f)
    { this.label = label; this.type = type; this.start = start; this.end = end; this.value = value; this.tilt = tilt; }
}

/// <summary>스테이지 하나의 설정. 곡·채보를 비워두면 MusicPlayer·Composer의 기본값을 쓴다.</summary>
[Serializable]
public class StageSetup
{
    public string title = "STAGE";
    [Tooltip("비워두면 MusicPlayer의 곡")]
    public AudioClip clip;
    [Tooltip("비워두면 Composer의 기본 채보")]
    public TextAsset chart;                      // 채보 편집기에서 내보낸 JSON을 여기에 넣는다.
    [Tooltip("이 스테이지의 배경 층들 (먼 것부터). 비워두면 BackgroundDisplay의 기본 배경")]
    public List<BackgroundLayer> background = new List<BackgroundLayer>();
    public bool isBoss;                          // 보스전이면 보스 등장 연출과 보스 체력바가 나온다.
    public string bossName = "???";
    [Tooltip("보스 그림 (비워두면 임시 도형)")]
    public Sprite bossSprite;
    [Tooltip("보스 등장 컷인 그림 (화면 전체)")]
    public Sprite cutInImage;                    // 한마 바키 짤 같은 등장 컷인을 넣는 칸
    [Tooltip("스테이지 시작 전 대사")]
    public List<StoryLine> storyBefore = new List<StoryLine>();
    [Tooltip("곡 시간 구간별 기믹 (속도 변화·서든·확대·러시·어둡게). ⋮ 메뉴 > \"기믹 추천 배치 채우기\"로 채울 수 있다")]
    public List<GimmickSection> gimmicks = new List<GimmickSection>();
}

/// <summary>게임 전체 흐름(타이틀 → 대사 → 스테이지 → 보스전 → 결과)을 관리한다. 한 씬 안에서 진행한다.</summary>
public class GameFlow : MonoBehaviour
{
    // 진행 단계. 이 값에 따라 Update가 하는 일이 바뀐다.
    enum Phase { Title, Story, BossIntro, Playing, StageClear, GameOver, Result }

    // 지휘할 컴포넌트들. 비워 두면 Awake에서 씬에서 자동으로 찾는다.
    [SerializeField] MusicPlayer music;
    [SerializeField] Composer composer;
    [SerializeField] Judge judge;
    [SerializeField] Referee referee;
    [SerializeField] NoteDisplay noteDisplay;
    [SerializeField] GameHud hud;
    [SerializeField] BossDisplay boss;           // 없어도 된다(보스 연출만 빠짐).
    [SerializeField] DebugOverlay debugOverlay;  // 없어도 된다.
    [SerializeField] BackgroundDisplay background;  // 없어도 된다. 스테이지 배경을 흘린다.
    [SerializeField] TitleLogo titleLogo;           // 없어도 된다. 있으면 타이틀 화면에서만 로고 연출을 하고, 예전 글자 제목은 숨긴다.

    [Header("타이틀")]
    [SerializeField] string gameTitle = "말도 안돼! 내가 너같은 약골이랑 사귀라고?\n무리무리!!!";
    [SerializeField] string subtitle = "~전생에 나라를 구한 나, 다시 한번 그녀에게 사랑을 도전하다~";
    [Tooltip("타이틀 화면 배경 그림 (비워두면 어두운 단색). 16:9 그림이 화면에 맞는다.")]
    [SerializeField] Sprite titleBackground;
    [Tooltip("켜면 타이틀과 대사를 건너뛰고 바로 첫 스테이지 (테스트용)")]
    [SerializeField] bool skipTitleAndStory = false;

    [Header("스테이지 (위에서부터 차례로)")]
    // 기본 스테이지 2개(일반 + 보스). 대사는 아래 DefaultPrologue / DefaultBossIntro에 적힌 시나리오를 쓴다.
    // 주의: 이 값은 컴포넌트를 처음 붙일 때의 기본값이다. 이미 씬에 붙어 있으면 Inspector 값이 우선한다.
    //       이미 붙어 있는 컴포넌트에 시나리오 대사를 넣으려면 컴포넌트 ⋮ 메뉴 > "대사를 시나리오 기본값으로" 를 누른다.
    [SerializeField] List<StageSetup> stages = new List<StageSetup>
    {
        new StageSetup
        {
            title = "STAGE 1",
            storyBefore = DefaultPrologue()   // 프롤로그 (첫 스테이지 시작 전)
        },
        new StageSetup
        {
            title = "BOSS",
            isBoss = true,
            bossName = "히카리",
            storyBefore = DefaultBossIntro()  // 보스전 시작 전
        }
    };

    [Header("엔딩 대사 (보스를 이긴 뒤)")]
    [Tooltip("비워두면 보스를 이긴 뒤 바로 결과 화면")]
    [SerializeField] List<StoryLine> ending = new List<StoryLine>();   // 시나리오상 보스전 피니시 → 바로 결과 화면

    [Header("대화 초상화 (미연시처럼 말하는 사람 그림 표시)")]
    [Tooltip("이름(Speaker)이 같은 대사에서 그 그림을 대화창 왼쪽 위에 띄운다. 해설(이름 없음)이나 목록에 없는 이름이면 숨긴다.")]
    [SerializeField] List<SpeakerPortrait> portraits = new List<SpeakerPortrait>();

    [Header("구간 기믹")]
    [Tooltip("Speed·Zoom·Rush·Dark가 바뀌는 데 걸리는 시간(초)")]
    [SerializeField] float gimmickRamp = 1f;
    [Tooltip("구간이 시작될 때 SPEED UP!! 같은 글자 띄우기")]
    [SerializeField] bool gimmickBanners = true;

    [Header("연출 시간 (초)")]
    [SerializeField] float bossIntroTime = 2.5f;    // "보스 등장!!" 배너를 보여주는 시간. 끝나면 곡 시작
    [Tooltip("보스 등장 컷인 위에 뜨는 글자 (비우면 글자 없이 그림만)")]
    [SerializeField] string bossIntroText = "보스 등장!!";
    [SerializeField] Color bossIntroColor = new Color(1f, 0.3f, 0.3f);
    [SerializeField] float stageClearTime = 2.5f;   // STAGE CLEAR / K.O. 배너 시간
    [SerializeField] float gameOverTime = 2f;       // GAME OVER 배너 시간

    Phase phase;                  // 지금 단계
    int stageIndex;               // 지금 스테이지 번호 (stages 목록의 몇 번째)
    List<StoryLine> story;        // 지금 보여주는 대사 목록
    int storyIndex;               // 지금 대사 줄 번호
    Action afterStory;            // 대사가 끝나면 실행할 일 (함수를 변수에 담아 둔다)
    float timer;                  // 배너 단계에서 남은 시간
    bool retryMode;   // 다시 하기 중이면 대사를 건너뛴다
    readonly List<StageResult> results = new List<StageResult>();   // 클리어한 스테이지들의 결과
    readonly List<string> resultTitles = new List<string>();        // 그 스테이지들의 이름 (results와 같은 순서)
    StageResult failed;           // 실패한 스테이지의 결과 (결과 화면에 표시)

    // ── 타이틀 메뉴 상태 ──
    const string PrefVolume = "Settings.Volume";   // 음량 저장 이름
    bool inSettings;              // 설정 화면인지 (false면 메인 메뉴)
    int menuIndex;                // 선택된 항목 번호
    Vector2 lastMousePos;         // 지난 프레임 마우스 위치 (마우스가 움직였을 때만 선택을 따라가게)

    void Awake()
    {
        // Inspector에서 비워 둔 칸은 씬에서 찾아 채운다. 씬 설정이 조금 틀려도 동작하게 하는 안전장치.
        if (music == null) music = FindAnyObjectByType<MusicPlayer>();
        if (composer == null) composer = FindAnyObjectByType<Composer>();
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (referee == null) referee = FindAnyObjectByType<Referee>();
        if (noteDisplay == null) noteDisplay = FindAnyObjectByType<NoteDisplay>();
        if (hud == null) hud = FindAnyObjectByType<GameHud>();
        if (boss == null) boss = FindAnyObjectByType<BossDisplay>();
        if (debugOverlay == null) debugOverlay = FindAnyObjectByType<DebugOverlay>();
        if (background == null) background = FindAnyObjectByType<BackgroundDisplay>();   // 없어도 된다(배경만 안 나옴)
        if (titleLogo == null) titleLogo = FindAnyObjectByType<TitleLogo>();             // 없어도 된다(글자 제목이 나옴)

        // MusicPlayer.Start에서 자동 재생되지 않게 막는다. Awake는 모든 Start보다 먼저 실행되므로 제때 꺼진다.
        if (music != null) music.PlayOnStart = false;   // 재생은 GameFlow가 시작한다

        // 저장해 둔 음량을 불러온다. AudioListener.volume: 게임 전체 소리 크기(0~1). 저장값이 없으면 1(100%).
        AudioListener.volume = PlayerPrefs.GetFloat(PrefVolume, 1f);
    }

    void OnEnable()
    {
        // 이 6개는 꼭 있어야 진행할 수 있다.
        if (music == null || composer == null || judge == null || referee == null || noteDisplay == null || hud == null)
        {
            Debug.LogError("[GameFlow] MusicPlayer, Composer, Judge, Referee, NoteDisplay, GameHud가 씬에 있어야 합니다.");
            enabled = false;
            return;
        }
        referee.OnStageCleared += HandleCleared;
        referee.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        if (referee != null)
        {
            referee.OnStageCleared -= HandleCleared;
            referee.OnGameOver -= HandleGameOver;
        }
    }

    void Start()
    {
        if (stages.Count == 0) stages.Add(new StageSetup { title = "STAGE 1" });   // 스테이지를 다 지워 버렸어도 하나는 있게
        if (skipTitleAndStory) StartRun(true);   // 테스트용: 바로 첫 스테이지
        else ShowTitle();
    }

    // 매 프레임: 전역 단축키 처리 후, 지금 단계에 맞는 일만 한다.
    void Update()
    {
        HandleGlobalKeys();
        UpdateGimmicks();

        switch (phase)
        {
            case Phase.Title:
                HandleMenu();   // 메뉴 고르기·결정
                break;

            case Phase.Story:
                if (Pressed(Key.Escape)) EndStory();     // Esc → 남은 대사 건너뛰기
                else if (ConfirmPressed())
                {
                    // 글자가 아직 나오는 중이면 한 번에 다 보여주고, 다 나왔으면 다음 줄로.
                    if (hud.IsTyping) hud.FinishTyping();
                    else NextLine();
                }
                break;

            case Phase.BossIntro:
                timer -= Time.deltaTime;
                if (timer <= 0f) PlayStage();   // 등장 연출이 끝나면 곡 시작
                break;

            case Phase.StageClear:
                timer -= Time.deltaTime;
                if (timer <= 0f) AfterClear();  // 배너가 끝나면 다음 스테이지 또는 엔딩
                break;

            case Phase.GameOver:
                timer -= Time.deltaTime;
                if (timer <= 0f) ShowResult(false);
                break;

            case Phase.Result:
                if (Pressed(Key.R))
                {
                    // 클리어했으면 처음부터(대사 생략), 실패했으면 실패한 스테이지만 다시
                    if (lastCleared) StartRun(true);
                    else BeginStage(true);
                }
                else if (Pressed(Key.Enter) || Pressed(Key.NumpadEnter))
                {
                    ShowTitle();
                }
                break;

            // Playing 단계에서는 할 일이 없다. 진행은 Judge·Referee가 하고, 끝나면 Referee 이벤트가 HandleCleared/HandleGameOver를 부른다.
        }
    }

    // ───────────────────────── 진행 ─────────────────────────

    void ShowTitle()
    {
        phase = Phase.Title;
        EndGimmicks();
        music.Stop();
        noteDisplay.ResetForSong();        // 화면에 남은 노트 정리
        hud.SetGameplayVisible(false);
        if (boss != null) boss.Hide();
        if (background != null) background.UseDefault();
        // 로고 그림이 있으면 예전 글자 제목·부제는 비우고 로고 연출을 한다.
        bool logo = titleLogo != null && titleLogo.HasLogo;
        hud.ShowTitle(logo ? "" : gameTitle, logo ? "" : subtitle, "↑↓ 선택     Enter · Space · 클릭 결정");
        if (logo) titleLogo.Show();
        hud.SetTitleBackground(titleBackground);
        inSettings = false;   // 타이틀로 돌아오면 항상 메인 메뉴부터
        menuIndex = 0;
        RefreshMenu();
    }

    // 한 판을 처음부터 시작. skipStory면 대사를 건너뛴다(다시 하기).
    void StartRun(bool skipStory)
    {
        if (titleLogo != null) titleLogo.Hide();   // 타이틀을 떠나면 로고를 숨긴다
        results.Clear();
        resultTitles.Clear();
        stageIndex = 0;
        retryMode = skipStory;   // 이번 판 내내 대사를 건너뛸지 기억
        BeginStage(skipStory);
    }

    // stageIndex번 스테이지를 시작한다(대사가 있으면 대사부터).
    void BeginStage(bool skipStory)
    {
        // 실패 후 다시 하기: 그 스테이지 결과만 다시 기록한다
        // (결과 목록이 지금 스테이지 번호보다 길면, 이 스테이지 이후의 기록을 지운다)
        while (results.Count > stageIndex)
        {
            results.RemoveAt(results.Count - 1);       // 마지막 항목 삭제
            resultTitles.RemoveAt(resultTitles.Count - 1);
        }

        StageSetup s = stages[stageIndex];
        EndGimmicks();
        hud.HideOverlays();
        hud.SetGameplayVisible(false);
        if (boss != null) boss.Hide();
        music.Stop();
        noteDisplay.ResetForSong();
        if (background != null) background.SetStage(s.background);   // 대사도 그 스테이지 배경 위에서 진행한다(미연시 배경 역할).

        if (!skipStory && s.storyBefore != null && s.storyBefore.Count > 0)
            // 람다식 () => StartSong(s): "대사가 끝나면 StartSong(s)를 실행해"라는 할 일을 변수로 넘긴다.
            // s를 람다 안에서 쓰면 람다가 그 값을 기억한다(캡처).
            PlayStory(s.storyBefore, () => StartSong(s));
        else
            StartSong(s);
    }

    // 곡·채보를 준비하고 스테이지를 시작한다. 보스면 등장 연출을 먼저 한다.
    void StartSong(StageSetup s)
    {
        // 순서 중요: 곡·채보를 먼저 바꾸고, 판정·점수를 초기화한 뒤, 화면 노트가 새 채보를 읽게 한다.
        music.SetClip(s.clip);         // null이면 MusicPlayer의 기본 곡
        composer.Load(s.chart);        // null이면 Composer의 기본 채보
        judge.ResetState();
        referee.ResetForStage();
        BeginGimmicks(s.gimmicks);   // 속도 표는 노트가 화면에 오르기 전에 정해야 한다
        noteDisplay.ResetForSong();

        hud.HideOverlays();
        hud.SetGameplayVisible(true);
        hud.SetStageTitle(s.title);
        if (background != null) background.SetStage(s.background);

        if (s.isBoss && boss != null)
        {
            boss.Begin(composer.TotalCount, s.bossName, s.bossSprite);   // 전체 노트 수 = 보스 체력 기준
            hud.ShowBanner(bossIntroText, bossIntroColor, s.cutInImage, bossIntroTime);
            CameraShake.Shake(0.3f, 0.6f);
            phase = Phase.BossIntro;   // 타이머가 끝나면 Update에서 PlayStage
            timer = bossIntroTime;
        }
        else
        {
            if (boss != null) boss.Hide();
            PlayStage();
        }
    }

    void PlayStage()
    {
        music.Play();            // startDelay 동안 카운트다운이 나오고 곡이 시작된다.
        phase = Phase.Playing;
    }

    // Referee: 곡 끝까지 살아남음
    void HandleCleared(StageResult r)
    {
        if (phase != Phase.Playing) return;   // 다른 단계에서 잘못 들어온 이벤트는 무시
        StageSetup s = stages[stageIndex];
        results.Add(r);
        resultTitles.Add(s.title);

        bool bossStage = s.isBoss && boss != null;
        EndGimmicks();
        if (bossStage) boss.Defeat();   // 보스가 날아가는 연출
        hud.ShowBanner(bossStage ? "K.O.!!" : "STAGE CLEAR", new Color(1f, 0.9f, 0.3f), null, stageClearTime);
        phase = Phase.StageClear;
        timer = stageClearTime;
    }

    // 클리어 배너가 끝난 뒤: 다음 스테이지 → 없으면 엔딩 대사 → 결과
    void AfterClear()
    {
        stageIndex++;
        if (stageIndex < stages.Count)
        {
            BeginStage(retryMode);   // 다음 스테이지 (다시 하기 중이면 대사 생략)
        }
        else if (!retryMode && ending.Count > 0)
        {
            hud.SetGameplayVisible(false);
            if (boss != null) boss.Hide();
            PlayStory(ending, () => ShowResult(true));   // 엔딩 대사가 끝나면 클리어 결과 화면
        }
        else
        {
            ShowResult(true);
        }
    }

    // Referee: 체력 0
    void HandleGameOver(StageResult r)
    {
        if (phase != Phase.Playing) return;
        failed = r;   // 결과 화면에 실패한 스테이지 기록도 보여주려고 저장
        EndGimmicks();
        hud.ShowBanner("GAME OVER", new Color(1f, 0.3f, 0.3f), null, gameOverTime);
        CameraShake.Shake(0.3f, 0.5f);
        phase = Phase.GameOver;
        timer = gameOverTime;
    }

    // 마지막 결과가 클리어였는지. 결과 화면에서 R을 눌렀을 때 무엇을 다시 할지 정하는 데 쓴다.
    // (필드 선언은 보통 클래스 위쪽에 모으지만, 이렇게 쓰는 곳 근처에 둘 수도 있다)
    bool lastCleared;

    // 결과 화면: 스테이지별 기록을 모아서 총점·정확도·등급을 계산해 보여준다.
    void ShowResult(bool cleared)
    {
        phase = Phase.Result;
        lastCleared = cleared;
        music.Stop();
        hud.SetGameplayVisible(false);
        if (boss != null) boss.Hide();
        noteDisplay.ResetForSong();

        int p = 0, g = 0, m = 0, total = 0, maxCombo = 0;   // Perfect, Good, Miss 합계, 총점, 최대 콤보
        // StringBuilder: 문자열을 여러 번 이어 붙일 때 쓰는 도구. + 로 계속 붙이면 매번 새 문자열이 만들어져서 비효율적이다.
        var body = new System.Text.StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            StageResult r = results[i];
            // {r.score:N0}: 천 단위 쉼표 (12,345). AppendLine: 붙이고 줄바꿈.
            body.AppendLine($"{resultTitles[i]}   {r.score:N0}점   최대 콤보 {r.maxCombo}   P {r.perfect} / G {r.good} / M {r.miss}");
            p += r.perfect; g += r.good; m += r.miss; total += r.score;
            maxCombo = Math.Max(maxCombo, r.maxCombo);
        }
        if (!cleared)
        {
            // 실패한 스테이지: 점수는 총점에 넣지 않고, 판정 수만 정확도 계산에 넣는다.
            body.AppendLine($"{stages[stageIndex].title}   실패   P {failed.perfect} / G {failed.good} / M {failed.miss}");
            p += failed.perfect; g += failed.good; m += failed.miss;
        }

        double acc = GameMath.Accuracy(p, g, m);
        body.AppendLine();   // 빈 줄
        body.AppendLine($"총점  {total:N0}");
        body.AppendLine($"정확도  {acc * 100.0:F1}%     최대 콤보  {maxCombo}");   // 0.923 → 92.3%

        hud.ShowResult(
            cleared ? "CLEAR!" : "GAME OVER",
            cleared ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 0.35f, 0.35f),
            GameMath.Rank(acc, cleared),
            body.ToString(),
            cleared ? "R: 처음부터 다시 (대사 생략)     Enter: 타이틀로" : "R: 이 스테이지 다시     Enter: 타이틀로");
    }

    // ───────────────────────── 대사 ─────────────────────────

    // 대사 목록을 시작한다. 다 끝나면 then을 실행한다.
    void PlayStory(List<StoryLine> lines, Action then)
    {
        phase = Phase.Story;
        story = lines;
        storyIndex = 0;
        afterStory = then;
        ShowLine();
    }

    void ShowLine()
    {
        StoryLine line = story[storyIndex];
        // 줄마다 넣은 초상화(표정)가 있으면 그것, 없으면 이름으로 찾은 기본 초상화
        Sprite face = line.portrait != null ? line.portrait : PortraitOf(line.speaker);
        hud.ShowStory(line.speaker, line.text, "Space ▶     Esc 건너뛰기", face);
    }

    // 이름에 맞는 초상화를 찾는다. 없으면 null(초상화 숨김).
    Sprite PortraitOf(string speaker)
    {
        if (string.IsNullOrEmpty(speaker)) return null;   // 해설은 초상화 없음
        foreach (SpeakerPortrait p in portraits)
            if (p != null && p.speaker == speaker) return p.sprite;
        return null;
    }

    void NextLine()
    {
        storyIndex++;
        if (storyIndex >= story.Count) EndStory();   // 마지막 줄이었으면 끝
        else ShowLine();
    }

    void EndStory()
    {
        hud.HideOverlays();
        // afterStory를 지역 변수로 옮기고 필드를 먼저 비운 뒤 실행한다.
        // then() 안에서 또 PlayStory를 부르면 afterStory가 새 값으로 바뀌는데, 그걸 실수로 지우지 않게 하려는 순서다.
        Action then = afterStory;
        afterStory = null;
        if (then != null) then();
    }

    // ───────────────────────── 시나리오 대사 ─────────────────────────

    // 프롤로그: 타이틀에서 "게임 시작"을 누르면 STAGE 1 전에 나온다.
    // Speaker가 ""인 줄은 해설(강두철의 1인칭 독백)로, 이름표 없이 나온다.
    // static: 객체 없이도 부를 수 있어서 위의 필드 초기값(stages = ...)에서 바로 쓸 수 있다.
    static List<StoryLine> DefaultPrologue()
    {
        return new List<StoryLine>
        {
            new StoryLine("", "20XX년. 현대 무기가 기영이 박사가 발견한 '기' 앞에서 무력해진 지 어언 40년…"),
            new StoryLine("", "각 나라는 어릴 때부터 학생들을 인간흉기로 키우기 위해, 기존 교육과정을 격투 교육과정으로 바꾼 지 오래다."),
            new StoryLine("", "그런 위대한 기영이 박사의 손녀딸, '히카리'가 우리 학교에 입학했다!!"),
            new StoryLine("", "나는 어릴 적, 그녀에게 차인 전적이 있다."),
            new StoryLine("어린 히카리", "나보다 약한 녀석의 말 따위 듣지 않는다. 꺼져라."),
            new StoryLine("", "그때부터 나는 절치부심하여, 그녀보다 강해지기 위해 온갖 방법을 다 썼다."),
            new StoryLine("", "그리고 마침내, 12년 만에 나는 그녀 앞에 다시 섰다."),
            new StoryLine("강두철", "히카리! 너에게 할 말이 있다!"),
            new StoryLine("히카리", "엉?"),
            new StoryLine("", "우락부락한 대퇴사두근, 당장이라도 터질 듯한 대흉근, 그리고 쩍 벌어진 광배근…"),
            new StoryLine("", "틀림없다. 그녀는 나의 뮤즈다."),
            new StoryLine("강두철", "널 좋아한다!"),
            new StoryLine("히카리", "흠. 나보다 강해야 한다."),
            new StoryLine("강두철", "그래! 알겠다!"),
            new StoryLine("히카리", "그리고 너 말고도 여기 전교생이 똑같은 말을 했지."),
            new StoryLine("강두철", "뭐라고?!"),
            new StoryLine("", "어느 순간, 전교생이 주변에 바글바글했다."),
            new StoryLine("히카리", "여기서 오직 단 한 명만이, 내 상대가 될 수 있다!"),
            new StoryLine("", "배틀로얄이 시작됐다! 모두를 쓰러뜨려라!\nA키: 슈퍼 드래곤 라이징 어퍼컷 (위쪽 노트)   B키: 헥토파스칼 드릴 초전도체 킥 (아래쪽 노트)"),
        };
    }

    // 보스전 전: STAGE 1을 클리어하면 나온다. 이 대사가 끝나면 "보스 등장!!" 컷인 → 보스전.
    static List<StoryLine> DefaultBossIntro()
    {
        return new List<StoryLine>
        {
            new StoryLine("히카리", "전부 쓰러뜨렸군."),
            new StoryLine("", "산처럼 쌓인 전교생의 언덕 위로, 그녀가 모습을 드러냈다."),
            new StoryLine("강두철", "히카리! 이제 약속대로 내 연인이 되어서, 평생 나에게 된장찌개와 계란후라이를 만들어 줘라!"),
            new StoryLine("히카리", "내가 언제 연인이라 했지?"),
            new StoryLine("히카리", "나보다 약한 자의 말은 듣지 않는다. 너는 나보다 약한가, 강한가?"),
            new StoryLine("강두철", "뭣!! 그래 좋다! 나 사나이 강두철, 이번에야말로… 12년 만에 다시금 너의 마음을 뺏어 가 주지!"),
        };
    }

    // [ContextMenu]: Inspector에서 이 컴포넌트의 ⋮ 메뉴(또는 컴포넌트 이름 우클릭)에 항목을 추가한다. Play를 안 해도 실행된다.
    // 이미 씬에 붙어 있는 GameFlow는 코드의 기본 대사가 들어가지 않으므로, 이 메뉴로 대사만 시나리오로 바꾼다.
    // 곡·채보·배경·보스 그림 같은 다른 칸은 건드리지 않는다.
    [ContextMenu("대사를 시나리오 기본값으로")]
    void ApplyScenarioStory()
    {
#if UNITY_EDITOR
        // 바꾸기 전 상태를 기록해서 Ctrl+Z로 되돌릴 수 있게 한다. (에디터 전용 기능이라 #if로 감싼다)
        UnityEditor.Undo.RecordObject(this, "대사를 시나리오 기본값으로");
#endif
        if (stages.Count > 0) stages[0].storyBefore = DefaultPrologue();
        if (stages.Count > 1)
        {
            stages[1].storyBefore = DefaultBossIntro();
            stages[1].bossName = "히카리";
        }
        ending = new List<StoryLine>();
        Debug.Log("[GameFlow] 대사를 시나리오 기본값으로 바꿨습니다. (Ctrl+Z로 되돌리기)");
    }

    // 일반 스테이지(보스 아님)의 첫 번째에는 Road of Resistance, 보스 스테이지에는 Homura용 추천 기믹을 넣는다.
    // 곡·채보·대사는 건드리지 않는다. 채보를 바꾸면 Gimmicks 목록의 시작·끝 초만 고치면 된다.
    [ContextMenu("기믹 추천 배치 채우기")]
    void ApplyGimmickPreset()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "기믹 추천 배치 채우기");
#endif
        bool normalDone = false;
        foreach (StageSetup s in stages)
        {
            if (s.isBoss) s.gimmicks = HomuraPreset();
            else if (!normalDone) { s.gimmicks = RoadPreset(); normalDone = true; }
        }
        Debug.Log("[GameFlow] 기믹 추천 배치를 채웠습니다. (Ctrl+Z로 되돌리기)");
    }

    // ───────────────────────── 구간 기믹 ─────────────────────────
    // 스테이지의 Gimmicks 목록(시작·끝 초)에 따라 곡 시간 구간마다 연출을 바꾼다. 채보는 건드리지 않는다.
    //   속도: NoteDisplay에 "시간별 속도 배율 표"를 넘긴다 → 판정 시각은 그대로, 노트가 빨리/느리게 흐른다.
    //   서든: NoteDisplay.HiddenDistance / 어둡게·러시 배경: BackgroundDisplay / 집중선: ScreenFx / 확대·기울기: 카메라

    List<GimmickSection> activeGimmicks;   // 지금 스테이지의 구간 목록 (없으면 null)
    bool[] gimmickAnnounced;               // 구간마다 시작 글자를 띄웠는지
    double gimmickLastTime;
    float camBaseSize = 5f;
    bool camTouched;                       // 카메라를 건드렸는지 (원래대로 돌려놓아야 하는지)
    float nextRushShake;
    const float GimmickStep = 0.01f;       // 속도 표 간격 (초)

    void BeginGimmicks(List<GimmickSection> list)
    {
        EndGimmicks();
        activeGimmicks = list != null && list.Count > 0 ? list : null;
        gimmickAnnounced = activeGimmicks != null ? new bool[activeGimmicks.Count] : null;
        gimmickLastTime = -999;
        Camera cam = Camera.main;
        if (cam != null) camBaseSize = cam.orthographicSize;
        noteDisplay.SetSpeedCurve(BuildSpeedCurve(), GimmickStep);
    }

    void EndGimmicks()
    {
        activeGimmicks = null;
        if (noteDisplay != null) { noteDisplay.SetSpeedCurve(null, GimmickStep); noteDisplay.HiddenDistance = 0f; }
        if (background != null) { background.SetDim(1f); background.SpeedBoost = 1f; }
        ScreenFx.SetSpeedLines(0f);
        Camera cam = Camera.main;
        if (camTouched && cam != null)
        {
            cam.orthographicSize = camBaseSize;
            cam.transform.rotation = Quaternion.identity;
        }
        camTouched = false;
    }

    // 0.01초마다의 속도 배율 표. Speed 구간이 없으면 null (속도 변화 없음).
    float[] BuildSpeedCurve()
    {
        if (activeGimmicks == null) return null;
        float last = 0f;
        bool any = false;
        foreach (var g in activeGimmicks)
            if (g != null && g.type == GimmickType.Speed) { any = true; last = Mathf.Max(last, g.end); }
        if (!any) return null;
        int n = Mathf.CeilToInt((last + gimmickRamp + 2f) / GimmickStep) + 1;
        var mult = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i * GimmickStep, m = 1f;
            foreach (var g in activeGimmicks)
                if (g != null && g.type == GimmickType.Speed) m += GimmickWeight(g, t) * (g.value - 1f);
            mult[i] = Mathf.Max(0.2f, m);   // 너무 느려지거나 거꾸로 가지 않게
        }
        return mult;
    }

    // 구간의 세기 0~1: 시작부터 gimmickRamp초 동안 0 → 1, 끝부터 gimmickRamp초 동안 1 → 0 (부드러운 S자)
    float GimmickWeight(GimmickSection g, float t)
    {
        float r = Mathf.Max(0.01f, gimmickRamp);
        float up = Mathf.Clamp01((t - g.start) / r);
        float down = 1f - Mathf.Clamp01((t - g.end) / r);
        return Mathf.SmoothStep(0f, 1f, up) * Mathf.SmoothStep(0f, 1f, down);
    }

    void UpdateGimmicks()
    {
        if (activeGimmicks == null || music == null || !music.IsStarted) return;
        double now = music.SongTime;
        if (now < gimmickLastTime - 1.0) Array.Clear(gimmickAnnounced, 0, gimmickAnnounced.Length);   // 곡이 처음부터 다시 시작됨
        gimmickLastTime = now;
        float t = (float)now;

        float hidden = 0f, zoom = 1f, tilt = 0f, rush = 0f, dim = 1f;
        GimmickSection announce = null;
        for (int i = 0; i < activeGimmicks.Count; i++)
        {
            var g = activeGimmicks[i];
            if (g == null) continue;
            if (!gimmickAnnounced[i] && t >= g.start && t < g.end)
            {
                gimmickAnnounced[i] = true;
                // 같은 순간에 여러 기믹이 시작하면 알림은 하나만 (러시 > 블라인드 > 가속)
                if (announce == null || GimmickPriority(g) > GimmickPriority(announce)) announce = g;
            }
            float w = GimmickWeight(g, t);
            if (w <= 0f) continue;
            switch (g.type)
            {
                case GimmickType.Hidden:
                    if (t >= g.start && t < g.end) hidden = hidden > 0f ? Mathf.Min(hidden, g.value) : g.value;
                    break;
                case GimmickType.Zoom: zoom *= Mathf.Lerp(1f, g.value, w); tilt += g.tilt * w; break;
                case GimmickType.Rush: rush = Mathf.Max(rush, Mathf.Clamp01(g.value) * w); break;
                case GimmickType.Dark: dim = Mathf.Min(dim, Mathf.Lerp(1f, g.value, w)); break;
            }
        }

        if (announce != null && Time.time >= nextGimmickBanner)
        {
            AnnounceGimmick(announce);
            nextGimmickBanner = Time.time + 1.2f;   // 알림이 연달아 겹쳐 뜨지 않게
        }
        noteDisplay.HiddenDistance = hidden;
        if (background != null) { background.SetDim(dim); background.SpeedBoost = 1f + rush * 0.8f; }
        ScreenFx.SetSpeedLines(rush * 0.7f);
        if (rush > 0.05f && Time.time >= nextRushShake)
        {
            nextRushShake = Time.time + 0.45f;
            CameraShake.Shake(0.04f * rush, 0.1f);
        }

        // 카메라: 확대·기울기가 있거나 원래대로 돌아가는 중일 때만 건드린다 (보스 피니시 확대와 안 싸우게)
        Camera cam = Camera.main;
        bool want = zoom < 0.999f || Mathf.Abs(tilt) > 0.01f;
        if (cam != null && (want || camTouched))
        {
            cam.orthographicSize = want ? camBaseSize * zoom : camBaseSize;
            cam.transform.rotation = want ? Quaternion.Euler(0f, 0f, tilt) : Quaternion.identity;
            camTouched = want;
        }
    }

    float nextGimmickBanner;

    // 알림 우선순위: 알림이 없는 기믹(확대·어둠)은 0
    static int GimmickPriority(GimmickSection g)
    {
        switch (g.type)
        {
            case GimmickType.Rush: return 3;
            case GimmickType.Hidden: return 2;
            case GimmickType.Speed: return Mathf.Abs(g.value - 1f) > 0.01f ? 1 : 0;
            default: return 0;
        }
    }

    void AnnounceGimmick(GimmickSection g)
    {
        if (!gimmickBanners) return;
        switch (g.type)
        {
            case GimmickType.Speed:
                if (g.value > 1.01f) ScreenFx.Banner("SPEED UP!!", new Color(1f, 0.55f, 0.2f), 110f, 1f);
                else if (g.value < 0.99f) ScreenFx.Banner("SLOW…", new Color(0.5f, 0.8f, 1f), 110f, 1f);
                break;
            case GimmickType.Hidden: ScreenFx.Banner("BLIND!", new Color(0.8f, 0.6f, 1f), 110f, 1f); break;
            case GimmickType.Rush: ScreenFx.Banner("RUSH!!!", new Color(1f, 0.3f, 0.25f), 130f, 1f); break;
        }
    }

    // Road of Resistance (일반전, 약 5분 10초) 추천 기믹. 거의 내내 가속이 오르내리고, 쉬는 구간(1.0배)은 짧게만 둔다.
    static List<GimmickSection> RoadPreset()
    {
        return new List<GimmickSection>
        {
            new GimmickSection("가속 1", GimmickType.Speed, 20f, 45f, 1.15f),
            new GimmickSection("가속 2", GimmickType.Speed, 55f, 75f, 1.25f),
            new GimmickSection("가속 3", GimmickType.Speed, 75f, 95f, 1.35f),
            new GimmickSection("유령 모드", GimmickType.Hidden, 105f, 120f, 7f),
            new GimmickSection("가속 4", GimmickType.Speed, 120f, 135f, 1.3f),
            new GimmickSection("최고 밀도 가속", GimmickType.Speed, 135f, 150f, 1.45f),
            new GimmickSection("최고 밀도 러시", GimmickType.Rush, 135f, 150f, 0.8f),
            new GimmickSection("러시 줌", GimmickType.Zoom, 135f, 150f, 0.9f, 3f),
            new GimmickSection("가속 5", GimmickType.Speed, 160f, 180f, 1.2f),
            new GimmickSection("가속 6", GimmickType.Speed, 180f, 195f, 1.35f),
            new GimmickSection("가속 7", GimmickType.Speed, 195f, 225f, 1.45f),
            new GimmickSection("가속 8", GimmickType.Speed, 225f, 240f, 1.25f),
            new GimmickSection("거대 반장 구간 어둡게", GimmickType.Dark, 240f, 260f, 0.55f),
            new GimmickSection("가속 9", GimmickType.Speed, 255f, 270f, 1.35f),
            new GimmickSection("클라이맥스 가속", GimmickType.Speed, 270f, 285f, 1.6f),
            new GimmickSection("클라이맥스 러시", GimmickType.Rush, 270f, 285f, 1f),
            new GimmickSection("클라이맥스 줌", GimmickType.Zoom, 270f, 285f, 0.88f, -3f),
            new GimmickSection("마무리 가속", GimmickType.Speed, 285f, 300f, 1.3f),
        };
    }

    // Homura (보스전, 약 3분 14초) 추천 기믹. 단계마다 가속이 세지고, 기공파 구간(90~125초)만 1.0배로 숨을 돌린다.
    static List<GimmickSection> HomuraPreset()
    {
        return new List<GimmickSection>
        {
            new GimmickSection("가속 1", GimmickType.Speed, 15f, 45f, 1.15f),
            new GimmickSection("가속 2", GimmickType.Speed, 50f, 75f, 1.25f),
            new GimmickSection("가속 3", GimmickType.Speed, 75f, 90f, 1.35f),
            new GimmickSection("기공파 난사 (어둡게)", GimmickType.Dark, 90f, 125f, 0.5f),
            new GimmickSection("돌진 구간 가속", GimmickType.Speed, 128f, 150f, 1.35f),
            new GimmickSection("유령 모드", GimmickType.Hidden, 150f, 165f, 7f),
            new GimmickSection("유령 구간 가속", GimmickType.Speed, 150f, 165f, 1.2f),
            new GimmickSection("바키식 러시 가속", GimmickType.Speed, 165f, 188f, 1.5f),
            new GimmickSection("바키식 러시", GimmickType.Rush, 165f, 188f, 1f),
            new GimmickSection("러시 줌", GimmickType.Zoom, 165f, 188f, 0.9f, 2f),
        };
    }

    // ───────────────────────── 타이틀 메뉴 ─────────────────────────

    // 지금 메뉴(메인 또는 설정)의 항목 글자. 설정은 현재 값을 글자에 넣어 보여준다.
    string[] MenuItems()
    {
        if (!inSettings) return new[] { "게임 시작", "설정", "종료" };
        int vol = Mathf.RoundToInt(AudioListener.volume * 100f);   // 0~1 → 0~100%
        return new[]
        {
            $"음량   ◁ {vol}% ▷",
            $"입력 보정   ◁ {judge.InputOffsetMs:+0;-0;0} ms ▷",
            "돌아가기"
        };
    }

    void RefreshMenu()
    {
        hud.ShowMenu(MenuItems(), menuIndex);
    }

    // 타이틀 단계에서 매 프레임: 키보드·마우스로 고르고 결정한다.
    void HandleMenu()
    {
        int count = MenuItems().Length;

        // 위아래 이동. (menuIndex - 1 + count) % count: 맨 위에서 위로 가면 맨 아래로 돌아간다(음수 나머지 방지로 count를 더함).
        if (Pressed(Key.UpArrow) || Pressed(Key.W)) { menuIndex = (menuIndex - 1 + count) % count; RefreshMenu(); }
        if (Pressed(Key.DownArrow) || Pressed(Key.S)) { menuIndex = (menuIndex + 1) % count; RefreshMenu(); }

        // 설정 화면: 좌우로 값 조절, Esc로 돌아가기
        if (inSettings)
        {
            if (Pressed(Key.LeftArrow) || Pressed(Key.A)) Adjust(-1);
            if (Pressed(Key.RightArrow) || Pressed(Key.D)) Adjust(+1);
            if (Pressed(Key.Escape)) { inSettings = false; menuIndex = 1; RefreshMenu(); }
        }

        // 마우스: 움직였을 때만 가리킨 항목을 선택한다(가만히 있는 마우스가 키보드 선택을 계속 덮어쓰지 않게).
        var mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 mp = mouse.position.ReadValue();   // 화면 픽셀 좌표 (왼쪽 아래가 0,0)
            int hover = hud.MenuItemAt(mp);
            if (hover >= 0 && hover != menuIndex && mp != lastMousePos) { menuIndex = hover; RefreshMenu(); }
            lastMousePos = mp;
            if (mouse.leftButton.wasPressedThisFrame && hover >= 0) { menuIndex = hover; Activate(); return; }
        }

        if (Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space)) Activate();
    }

    // 선택된 항목을 실행한다.
    void Activate()
    {
        if (!inSettings)
        {
            switch (menuIndex)
            {
                case 0: StartRun(false); break;                                         // 게임 시작 (대사 포함)
                case 1: inSettings = true; menuIndex = 0; RefreshMenu(); break;         // 설정으로
                case 2: Quit(); break;                                                  // 종료
            }
        }
        else
        {
            switch (menuIndex)
            {
                case 0: Adjust(+1, true); break;   // 값 칸에서 결정/클릭 → 한 칸 올림 (음량은 100%에서 0%로 돌아감)
                case 1: Adjust(+1, true); break;
                case 2: inSettings = false; menuIndex = 1; RefreshMenu(); break;   // 돌아가기 (메인의 "설정"에 커서)
            }
        }
    }

    // 설정 값 조절. dir = -1 또는 +1. wrap이면 최대에서 최소로 돌아간다(클릭만으로도 값을 다 고를 수 있게).
    void Adjust(int dir, bool wrap = false)
    {
        if (menuIndex == 0)
        {
            float v = AudioListener.volume + dir * 0.1f;          // 10%씩
            if (wrap && v > 1.001f) v = 0f;                         // 1.001: float 오차 여유
            AudioListener.volume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(PrefVolume, AudioListener.volume); // 저장
            PlayerPrefs.Save();
        }
        else if (menuIndex == 1)
        {
            judge.InputOffsetMs += dir * 5;   // 5ms씩. 범위 제한과 저장은 Judge가 한다.
        }
        RefreshMenu();
    }

    // 게임 종료. 빌드한 게임은 창이 닫히고, 에디터에서는 Play 모드가 꺼진다.
    void Quit()
    {
        // #if: 컴파일할 때 조건에 따라 코드를 넣거나 뺀다. UNITY_EDITOR는 에디터에서만 참.
        // 빌드한 게임에는 UnityEditor가 없어서, 이렇게 나누지 않으면 빌드가 실패한다.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ───────────────────────── 키 ─────────────────────────

    // 어느 단계에서든 동작하는 키: F1 디버그 표시, - / = 입력 보정
    void HandleGlobalKeys()
    {
        if (Pressed(Key.F1) && debugOverlay != null) debugOverlay.Visible = !debugOverlay.Visible;   // ! = 반대로 (켜짐 ↔ 꺼짐)

        int step = 0;
        if (Pressed(Key.Minus) || Pressed(Key.NumpadMinus)) step = -5;
        if (Pressed(Key.Equals) || Pressed(Key.NumpadPlus)) step = 5;   // = 키 (Shift 없이 누르는 + 자리)
        if (step != 0)
        {
            judge.InputOffsetMs += step;   // 프로퍼티의 set이 불려서 범위 제한과 저장까지 된다.
            hud.ShowNotice($"입력 보정 {judge.InputOffsetMs:+0;-0;0} ms   (입력이 늦으면 +)");
            if (phase == Phase.Title) RefreshMenu();   // 설정 화면에 떠 있는 값도 같이 갱신
        }
    }

    // 이번 프레임에 그 키가 막 눌렸는지. kb[key]: Key 값으로 키를 고르는 인덱서.
    static bool Pressed(Key key)
    {
        var kb = Keyboard.current;
        return kb != null && kb[key].wasPressedThisFrame;
    }

    // 확인 입력: Enter, 숫자패드 Enter, Space, 마우스 왼쪽 클릭 중 하나
    // A/B는 넣지 않았다 → 노트 입력 키와 겹치지 않는다.
    static bool ConfirmPressed()
    {
        var mouse = Mouse.current;
        return Pressed(Key.Enter) || Pressed(Key.NumpadEnter) || Pressed(Key.Space)
            || (mouse != null && mouse.leftButton.wasPressedThisFrame);
    }
}
