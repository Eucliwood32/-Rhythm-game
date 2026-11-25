// ============================================================================
// GameHud.cs : 게임 화면의 모든 UI(글자, 체력바, 대화창, 결과 화면)를 코드로 만들고 보여주는 컴포넌트
// ----------------------------------------------------------------------------
// 6요소 중 담당: Display (화면 표시 - UI)
// Play를 누르면 Canvas를 코드로 만든다. 씬에 UI를 따로 만들어 둘 필요가 없다.
//   - 플레이 중: 체력바, 점수, 콤보, 판정 글자(PERFECT/GOOD/MISS), 타격 글자(퍽! POW!), 연타 횟수, 카운트다운, 보스 체력, 기술 이름 외침
//   - 화면 전환: 타이틀(배경 그림 + 시작/설정/종료 메뉴), 스토리 대화창, 큰 배너(STAGE CLEAR 등), 결과 화면, 작은 알림
//   메뉴는 글자만 보여준다. 어떤 항목이 선택됐는지, 눌렀을 때 무엇을 할지는 GameFlow가 정한다.
// 무엇을 언제 보여줄지는 GameFlow가 정한다. 이 클래스는 규칙을 모르고 "보여 달라는 것"만 보여준다.
//
// Unity UI(UGUI) 기본 개념:
//   - Canvas: UI를 그리는 판. ScreenSpaceOverlay면 카메라와 상관없이 화면 맨 위에 덮어 그린다.
//   - RectTransform: UI 전용 Transform. 위치를 "앵커(anchor) + 피벗(pivot) + 위치 + 크기"로 정한다.
//       앵커 = 부모 사각형의 어느 지점에 붙을지 (0,0 = 왼쪽 아래, 1,1 = 오른쪽 위, 0.5,0.5 = 가운데)
//       피벗 = 자기 사각형의 어느 지점을 기준점으로 삼을지
//       anchoredPosition = 앵커에서 피벗까지의 거리(픽셀), sizeDelta = 크기
//   - CanvasScaler: 기준 해상도(1920×1080)로 배치해 두면 실제 화면 크기에 맞게 전체를 늘리거나 줄인다.
//   - Image(색 사각형), Text(글자), Outline(글자 테두리)
//
// 연결:
//   GameFlow --(ShowTitle, ShowStory, ShowBanner, ShowResult, SetGameplayVisible ...)--> GameHud
//   Referee.OnHealthChanged / OnScoreChanged / OnComboChanged --> 체력바·점수·콤보
//   Judge.OnNoteJudged / OnBarrageHit --> 판정 글자·연타 횟수
//   BossDisplay --(SetBoss, SetBossHealth)--> 보스 체력바,  PlayerAvatar --(ShowShout)--> 기술 이름
//   MusicPlayer.SongTime --> 카운트다운
// ============================================================================

using UnityEngine;
using UnityEngine.UI;   // Canvas UI 컴포넌트(Image, Text, CanvasScaler, Outline)

/// <summary>게임 화면 UI. Play를 누르면 코드로 Canvas를 만든다. 무엇을 보여줄지는 GameFlow가 정한다.</summary>
public class GameHud : MonoBehaviour
{
    [SerializeField] MusicPlayer music;
    [SerializeField] Judge judge;
    [SerializeField] Referee referee;
    [SerializeField] NoteDisplay noteDisplay;

    [Tooltip("UI 글꼴. 비워두면 OS의 한글 글꼴(맑은 고딕 등)을 쓴다.")]
    [SerializeField] Font font;

    [Tooltip("대화 글자가 나오는 속도 (초당 글자 수)")]
    [SerializeField] float typingSpeed = 40f;

    // 판정 글자 색
    static readonly Color PerfectCol = new Color(1f, 0.9f, 0.3f);    // 노랑
    static readonly Color GoodCol = new Color(0.45f, 0.9f, 1f);      // 하늘색
    static readonly Color MissCol = new Color(0.75f, 0.75f, 0.8f);   // 회색

    // ── 코드로 만든 UI 부품을 기억해 두는 칸들 (Build에서 채운다) ──
    RectTransform canvasRect;                                                            // Canvas의 사각형 (월드 → UI 좌표 변환에 필요)
    GameObject gameplay, bossGroup, titlePanel, storyPanel, resultPanel, bannerGroup;    // 화면 묶음들. SetActive로 통째로 켜고 끈다.
    RectTransform hpFill, bossFill;                                                      // 체력바 안쪽(채워지는 부분)
    Image hpFillImage;
    Text scoreText, stageText, comboText, comboLabel, countdownText, shoutText, noticeText;
    Text bossName, titleText, subtitleText, titleHint;
    Image titleBg;                                   // 타이틀 배경 그림
    readonly Text[] menuItems = new Text[4];         // 메뉴 글자 칸 (최대 4줄)
    Text storyName, storyLine, storyHint;
    Image storyPortrait;                             // 대화 초상화 (말하는 사람 그림)
    Text resultHeading, resultRank, resultBody, resultHint;
    Text bannerText;
    Image bannerImage;
    readonly Text[] popups = new Text[2];            // 레인별 판정 글자 (0 = 위, 1 = 아래)
    readonly float[] popupTimer = new float[2];      // 레인별 판정 글자 남은 시간

    // 타격 글자(퍽! POW! 등): 여러 개가 겹쳐 뜰 수 있게 6개를 미리 만들어 돌려 쓴다(오브젝트 풀).
    // 매번 새로 만들고 지우면 연타 때 오브젝트가 계속 생겼다 없어져서 낭비가 크다.
    static readonly string[] ImpactWords = { "퍽!", "쾅!", "POW!", "BAM!", "WHAM!" };
    readonly Text[] impacts = new Text[6];
    readonly float[] impactTimer = new float[6];
    int impactNext;                                  // 다음에 쓸 칸 번호 (돌아가며 쓴다)
    const float ImpactTime = 0.35f;                  // 타격 글자가 떠 있는 시간

    // 애니메이션용 값. 대부분 "1에서 시작해 0으로 줄어드는" 타이머 방식이다.
    float comboPop, shoutTimer, noticeTimer, bannerTimer, hpFlash, healFlash;
    string storyFull = "";   // 지금 대사 전체
    float storyShown;        // 지금까지 보여준 글자 수 (소수로 세다가 정수로 잘라서 쓴다)
    bool built;              // UI를 이미 만들었는지

    /// <summary>대화 글자가 아직 한 글자씩 나오는 중인지. GameFlow가 "확인 키 → 글자 다 보이기 / 다음 대사"를 구분할 때 쓴다.</summary>
    public bool IsTyping => storyPanel != null && storyPanel.activeSelf && storyShown < storyFull.Length;

    void Awake()
    {
        if (music == null) music = FindAnyObjectByType<MusicPlayer>();
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (referee == null) referee = FindAnyObjectByType<Referee>();
        if (noteDisplay == null) noteDisplay = FindAnyObjectByType<NoteDisplay>();
        Build();   // GameFlow가 Start에서 바로 ShowTitle 등을 부르므로 Awake에서 미리 만들어 둔다.
    }

    void OnEnable()
    {
        // 없어도 에러로 끄지 않는다. 연결된 것만 구독한다.
        if (judge != null)
        {
            judge.OnNoteJudged += HandleNoteJudged;
            judge.OnBarrageHit += HandleBarrageHit;
        }
        if (referee != null)
        {
            referee.OnHealthChanged += HandleHealth;
            referee.OnScoreChanged += HandleScore;
            referee.OnComboChanged += HandleCombo;
        }
    }

    void OnDisable()
    {
        if (judge != null)
        {
            judge.OnNoteJudged -= HandleNoteJudged;
            judge.OnBarrageHit -= HandleBarrageHit;
        }
        if (referee != null)
        {
            referee.OnHealthChanged -= HandleHealth;
            referee.OnScoreChanged -= HandleScore;
            referee.OnComboChanged -= HandleCombo;
        }
    }

    // ───────────────────────── GameFlow가 부르는 함수 ─────────────────────────

    // 플레이 중 UI(체력·점수·콤보 등)를 켜고 끈다. 끌 때는 보스 체력바도 같이 끈다.
    public void SetGameplayVisible(bool visible)
    {
        gameplay.SetActive(visible);
        if (!visible) bossGroup.SetActive(false);
    }

    // 화면 위쪽 가운데 스테이지 이름
    public void SetStageTitle(string title)
    {
        stageText.text = title;
    }

    // 타이틀 화면. 다른 패널을 먼저 다 숨긴다.
    public void ShowTitle(string title, string subtitle, string hint)
    {
        HideOverlays();
        titlePanel.SetActive(true);
        titleText.text = title;
        subtitleText.text = subtitle;
        titleHint.text = hint;
    }

    // 대화 한 줄을 시작한다. 글자는 Update에서 한 글자씩 나타난다(타자 효과).
    // portrait: 말하는 사람 초상화. null이면 숨긴다. (= null: 생략 가능한 매개변수)
    public void ShowStory(string speaker, string line, string hint, Sprite portrait = null)
    {
        storyPortrait.sprite = portrait;
        storyPortrait.gameObject.SetActive(portrait != null);
        storyPanel.SetActive(true);
        storyName.text = speaker;
        // 이름 상자(storyName의 부모)는 화자가 있을 때만 보인다. 해설처럼 화자가 비어 있으면 숨긴다.
        storyName.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(speaker));
        storyFull = line ?? "";   // ?? : line이 null이면 "" (null이면 .Length에서 에러가 나므로)
        storyShown = 0f;
        storyLine.text = "";
        storyHint.text = hint;
    }

    /// <summary>나오는 중인 대화 글자를 한 번에 다 보여준다.</summary>
    public void FinishTyping()
    {
        storyShown = storyFull.Length;
        storyLine.text = storyFull;
    }

    // 결과 화면: 제목(색 지정), 등급 큰 글자, 본문, 안내 문구
    public void ShowResult(string heading, Color headingColor, string rank, string body, string hint)
    {
        HideOverlays();
        resultPanel.SetActive(true);
        resultHeading.text = heading;
        resultHeading.color = headingColor;
        resultRank.text = rank;
        resultBody.text = body;
        resultHint.text = hint;
    }

    // 화면 전체를 덮는 패널 3개(타이틀·대화·결과)를 숨긴다.
    public void HideOverlays()
    {
        titlePanel.SetActive(false);
        storyPanel.SetActive(false);
        resultPanel.SetActive(false);
    }

    /// <summary>화면 가운데 큰 글자. image가 있으면 화면 전체에 깔린다(보스 컷인 등). duration초 뒤 사라진다.</summary>
    public void ShowBanner(string text, Color color, Sprite image, float duration)
    {
        bannerGroup.SetActive(true);
        bannerText.text = text;
        bannerText.color = color;
        bannerImage.sprite = image;
        bannerImage.gameObject.SetActive(image != null);   // 그림이 없으면 이미지 칸을 숨긴다(흰 사각형이 보이지 않게).
        bannerTimer = duration;
        bannerGroup.transform.localScale = Vector3.one * 1.6f;   // 1.6배 크기에서 시작해 Update에서 1배로 줄어든다(쾅 하고 박히는 느낌).
    }

    // 보스 체력바를 켜고 끈다. 켤 때 이름을 쓰고 체력을 가득 채운다.
    public void SetBoss(bool visible, string name)
    {
        bossGroup.SetActive(visible);
        bossName.text = name;
        SetBossHealth(1f);
    }

    // 보스 체력바 비율(0~1). 안쪽 막대의 오른쪽 앵커를 비율 위치로 옮겨서 길이를 바꾼다.
    // 앵커를 쓰는 이유: 크기를 픽셀로 계산하지 않아도 부모(배경 막대) 폭의 몇 %인지로 바로 정해진다.
    public void SetBossHealth(float ratio)
    {
        bossFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
    }

    /// <summary>기술 이름 외침. 화면 가운데에 1.2초 동안 크게 뜬다.</summary>
    public void ShowShout(string text, Color color)
    {
        shoutText.text = text;
        shoutText.color = color;
        shoutTimer = 1.2f;
    }

    /// <summary>타이틀 배경 그림. null이면 숨기고 어두운 단색 배경만 보인다.</summary>
    public void SetTitleBackground(Sprite sprite)
    {
        titleBg.sprite = sprite;
        titleBg.gameObject.SetActive(sprite != null);
    }

    /// <summary>타이틀 메뉴 글자를 바꾼다. selected번 항목은 노란색과 ▶ 표시로 강조한다.</summary>
    public void ShowMenu(string[] items, int selected)
    {
        for (int i = 0; i < menuItems.Length; i++)
        {
            bool used = i < items.Length;               // 항목 수보다 많은 칸은 숨긴다.
            menuItems[i].gameObject.SetActive(used);
            if (!used) continue;
            bool on = i == selected;
            menuItems[i].text = on ? "▶  " + items[i] + "  ◀" : items[i];
            menuItems[i].color = on ? PerfectCol : new Color(1f, 1f, 1f, 0.75f);
            menuItems[i].transform.localScale = Vector3.one * (on ? 1.1f : 1f);   // 선택된 항목은 살짝 크게
        }
    }

    /// <summary>화면 좌표(마우스 위치)에 있는 메뉴 항목 번호. 없으면 -1.</summary>
    public int MenuItemAt(Vector2 screenPos)
    {
        for (int i = 0; i < menuItems.Length; i++)
        {
            if (!menuItems[i].gameObject.activeInHierarchy) continue;   // 숨긴 칸이나 타이틀이 꺼져 있으면 건너뜀
            // 화면 좌표가 이 글자 상자 사각형 안에 있는지. Overlay Canvas라서 카메라 인자는 null.
            if (RectTransformUtility.RectangleContainsScreenPoint(menuItems[i].rectTransform, screenPos, null)) return i;
        }
        return -1;
    }

    /// <summary>오른쪽 아래 작은 알림 (입력 보정 값 등). 2초 동안 보인다.</summary>
    public void ShowNotice(string text)
    {
        noticeText.text = text;
        noticeTimer = 2f;
    }

    // ───────────────────────── 이벤트 ─────────────────────────

    // 체력이 바뀔 때: 체력바 길이와 색, 깎였으면 배경을 빨갛게 번쩍
    void HandleHealth(float hp, float max)
    {
        float ratio = max > 0 ? hp / max : 0f;
        float before = hpFill.anchorMax.x;   // 바뀌기 전 비율 (깎였는지 비교용)
        hpFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        hpFillImage.color = ratio > 0.3f ? new Color(0.35f, 0.9f, 0.45f) : new Color(1f, 0.35f, 0.3f);   // 30% 이하면 빨강(위험)
        if (ratio < before - 0.001f) hpFlash = 1f;   // 줄었으면 번쩍. 0.001은 float 오차 여유.
        else if (ratio > before + 0.001f) healFlash = 1f;   // 늘었으면(Perfect 회복) 초록으로 번쩍
    }

    void HandleScore(int score)
    {
        // ToString("0000000"): 7자리로, 빈자리는 0으로 채운다. 예) 4520 → "0004520"
        scoreText.text = "SCORE  " + score.ToString("0000000");
    }

    // 콤보: 3 이상일 때만 보인다(1~2 콤보는 의미가 적어서). 바뀔 때마다 숫자가 살짝 튀어오른다.
    void HandleCombo(int combo)
    {
        bool show = combo >= 3;
        comboText.gameObject.SetActive(show);
        comboLabel.gameObject.SetActive(show);
        comboText.text = combo.ToString();
        if (show) comboPop = 1f;
    }

    // 판정 글자: 해당 레인 판정선 근처에 PERFECT / GOOD / MISS
    void HandleNoteJudged(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        // 타격 글자: 성공일 때만. 롱노트 완료·연타 성공은 크게.
        if (result != Judgment.Miss) ShowImpact(lane, kind == JudgeKind.HoldComplete || kind == JudgeKind.Barrage);
        if (kind == JudgeKind.HoldComplete) return;   // 롱노트 완료는 판정 글자 없음
        // 삼항 연산자를 이어 붙여 세 갈래로 고른다.
        string text = result == Judgment.Perfect ? "PERFECT" : result == Judgment.Good ? "GOOD" : "MISS";
        Color col = result == Judgment.Perfect ? PerfectCol : result == Judgment.Good ? GoodCol : MissCol;
        ShowPopup(lane, text, col);
    }

    // 연타 중에는 "연타 5 / 12"처럼 진행 횟수를 같은 자리에 보여준다.
    void HandleBarrageHit(NoteData note, Lane lane, int hits, int need)
    {
        ShowPopup(lane, "연타 " + hits, GameColors.Barrage);
        if (hits % 3 == 1) ShowImpact(lane, false);   // 연타는 칠 때마다 띄우면 너무 많아서 3번에 1번만
    }

    // 맞은 자리 근처에 만화풍 타격 글자를 띄운다. 칸 6개를 차례로 돌려 쓴다.
    void ShowImpact(Lane lane, bool big)
    {
        Camera cam = Camera.main;
        if (cam == null || noteDisplay == null) return;
        int i = impactNext;
        impactNext = (impactNext + 1) % impacts.Length;   // 다음 칸 (끝에 가면 0으로)

        Text t = impacts[i];
        t.text = ImpactWords[Random.Range(0, ImpactWords.Length)];
        t.fontSize = big ? 84 : 58;
        t.color = big ? PerfectCol : Color.white;
        // 맞은 자리(판정선 오른쪽, 레인 높이)에서 조금씩 흩어진 곳. 판정 글자(PERFECT)와 겹치지 않게 아래쪽에.
        Vector3 world = new Vector3(noteDisplay.JudgeX + 0.8f + Random.Range(-0.3f, 0.6f), noteDisplay.LaneY(lane) - 0.2f + Random.Range(-0.3f, 0.3f), 0f);
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, cam.WorldToScreenPoint(world), null, out local);
        t.rectTransform.anchoredPosition = local;
        t.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-18f, 18f));   // 삐딱하게 (만화 느낌)
        impactTimer[i] = ImpactTime;
        t.gameObject.SetActive(true);
    }

    void ShowPopup(Lane lane, string text, Color color)
    {
        int i = (int)lane;
        popups[i].text = text;
        popups[i].color = color;
        popupTimer[i] = 0.5f;   // 0.5초 동안 보인다.
        popups[i].gameObject.SetActive(true);
        PlacePopup(i, lane);
    }

    // 판정 글자를 월드 좌표의 판정선 근처에 맞춰 UI 위치로 옮긴다.
    void PlacePopup(int i, Lane lane)
    {
        Camera cam = Camera.main;
        if (cam == null || noteDisplay == null) return;
        // 1) 놓고 싶은 월드 위치: 판정선 오른쪽 1.4칸, 레인보다 0.75칸 위
        Vector3 world = new Vector3(noteDisplay.JudgeX + 1.4f, noteDisplay.LaneY(lane) + 0.75f, 0f);
        Vector2 local;
        // 2) 월드 → 화면 픽셀(WorldToScreenPoint) → Canvas 안 좌표(ScreenPointToLocalPointInRectangle)
        //    Overlay Canvas라서 카메라 인자는 null. 결과는 out local로 받는다.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, cam.WorldToScreenPoint(world), null, out local);
        popups[i].rectTransform.anchoredPosition = local;   // 3) 가운데 앵커 기준 위치로 넣는다.
    }

    // ───────────────────────── 매 프레임 ─────────────────────────

    // UI 애니메이션. 모든 타이머는 unscaledDeltaTime(일시정지·슬로모션 영향 없음)으로 줄인다.
    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // 판정 글자: 처음 30% 동안 커졌다가 원래 크기로, 후반 절반 동안 투명해진다.
        for (int i = 0; i < 2; i++)
        {
            if (popupTimer[i] <= 0f) continue;
            popupTimer[i] -= dt;
            float k = Mathf.Clamp01(popupTimer[i] / 0.5f);   // 남은 비율 1 → 0
            // k가 0.7보다 클 때만(처음 0.15초) 크기가 1 + 최대 0.45배
            popups[i].transform.localScale = Vector3.one * (1f + Mathf.Max(0f, k - 0.7f) * 1.5f);
            // 알파 = k × 2를 0~1로 자름 → k가 0.5 아래로 내려가면서부터 투명해진다. (한 줄에 문장 세 개)
            Color c = popups[i].color; c.a = Mathf.Clamp01(k * 2f); popups[i].color = c;
            if (popupTimer[i] <= 0f) popups[i].gameObject.SetActive(false);
        }

        // 콤보 숫자: 바뀔 때 1.25배로 튀었다가 빠르게(초당 6) 원래 크기로
        // 타격 글자: 처음에 1.5배로 튀어나와 원래 크기로 줄고, 후반에 투명해진다.
        for (int i = 0; i < impacts.Length; i++)
        {
            if (impactTimer[i] <= 0f) continue;
            impactTimer[i] -= dt;
            float k = Mathf.Clamp01(impactTimer[i] / ImpactTime);   // 남은 비율 1 → 0
            impacts[i].transform.localScale = Vector3.one * (1f + Mathf.Max(0f, k - 0.7f) * 1.7f);
            Color c = impacts[i].color; c.a = Mathf.Clamp01(k * 2.5f); impacts[i].color = c;
            if (impactTimer[i] <= 0f) impacts[i].gameObject.SetActive(false);
        }

        comboPop = Mathf.Max(0f, comboPop - dt * 6f);
        comboText.transform.localScale = Vector3.one * (1f + comboPop * 0.25f);

        // 체력바 배경: 피해를 입으면 검정 → 빨강으로 번쩍였다가 돌아온다. hpFill.parent = 배경 막대(HpBg)
        hpFlash = Mathf.Max(0f, hpFlash - dt * 4f);
        healFlash = Mathf.Max(0f, healFlash - dt * 5f);
        Color frame = Color.Lerp(new Color(0f, 0f, 0f, 0.6f), new Color(0.3f, 1f, 0.5f, 0.8f), healFlash);   // 회복: 초록
        hpFill.parent.GetComponent<Image>().color = Color.Lerp(frame, new Color(1f, 0.2f, 0.2f, 0.8f), hpFlash);   // 피해(빨강)가 우선

        // 기술 이름: 처음 15% 동안 크게(최대 1.45배) 등장, 마지막 1/3 동안 투명해진다.
        if (shoutTimer > 0f)
        {
            shoutTimer -= dt;
            float k = Mathf.Clamp01(shoutTimer / 1.2f);
            shoutText.transform.localScale = Vector3.one * (1f + Mathf.Max(0f, k - 0.85f) * 3f);
            Color c = shoutText.color; c.a = Mathf.Clamp01(k * 3f); shoutText.color = c;
        }
        shoutText.gameObject.SetActive(shoutTimer > 0f);

        // 알림: 시간이 남아 있는 동안만 보인다.
        if (noticeTimer > 0f) noticeTimer -= dt;
        noticeText.gameObject.SetActive(noticeTimer > 0f);

        // 배너: 1.6배에서 1배로 부드럽게 줄어든다(지수 감쇠: fps와 상관없이 같은 빠르기). 시간이 다 되면 숨김.
        if (bannerTimer > 0f)
        {
            bannerTimer -= dt;
            bannerGroup.transform.localScale = Vector3.Lerp(bannerGroup.transform.localScale, Vector3.one, 1f - Mathf.Exp(-dt * 14f));
            if (bannerTimer <= 0f) bannerGroup.SetActive(false);
        }

        // 대화 타자 효과: 초당 typingSpeed 글자씩 늘리고, 앞에서부터 그만큼 잘라서 보여준다.
        if (storyPanel.activeSelf && storyShown < storyFull.Length)
        {
            storyShown = Mathf.Min(storyFull.Length, storyShown + typingSpeed * dt);
            storyLine.text = storyFull.Substring(0, (int)storyShown);   // Substring(시작, 길이)
        }

        // 카운트다운: 플레이 화면이고 곡이 예약된 상태일 때 곡 시간으로 READY / 3 / 2 / 1 / GO!
        if (gameplay.activeSelf && music != null && music.IsStarted)
        {
            string cd = GameMath.Countdown(music.SongTime);
            countdownText.text = cd;
            countdownText.gameObject.SetActive(cd != "");   // 빈 문자열이면 숨김
        }
        else
        {
            countdownText.gameObject.SetActive(false);
        }
    }

    // ───────────────────────── UI 만들기 ─────────────────────────

    // Canvas와 모든 UI 부품을 코드로 만든다. 처음 한 번만.
    // 아래 Label/Box 호출의 인자 순서: (부모, 이름, [글자, 크기, 색, 정렬 | 색], 앵커, 피벗, 위치, 크기)
    // 위치·크기는 기준 해상도 1920×1080 픽셀 기준이다. y는 위로 갈수록 +, 앵커가 위쪽(y=1)이면 아래로 내려가려면 음수.
    void Build()
    {
        if (built) return;
        built = true;

        // 글꼴이 비어 있으면 OS에 설치된 한글 글꼴을 순서대로 찾아 쓴다(윈도우 → 맥 → 리눅스 → 영문 기본).
        // Unity 기본 글꼴은 한글이 없어서 글자가 안 보이기 때문이다. 32 = 기본 글자 크기
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "AppleGothic", "Noto Sans CJK KR", "NanumGothic", "Arial" }, 32);

        // Canvas: 이 오브젝트(GameHud)의 자식으로 만든다.
        var canvasGo = new GameObject("GameHudCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();   // Canvas를 붙이면 RectTransform이 자동으로 생긴다.
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;   // 화면 맨 위에 덮어 그리기
        canvas.sortingOrder = 10;                            // 다른 Canvas가 있어도 그 위에
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;   // 화면 크기에 맞춰 크기 조절
        scaler.referenceResolution = new Vector2(1920, 1080);             // 이 해상도 기준으로 배치
        scaler.matchWidthOrHeight = 0.5f;                                  // 가로·세로 비율이 다를 때 가로와 세로를 반반 기준으로
        canvasRect = canvasGo.GetComponent<RectTransform>();

        Transform c = canvasGo.transform;

        // 플레이 중 UI
        gameplay = Group("Gameplay", c);
        Transform g = gameplay.transform;

        // 왼쪽 위: "HP" 글자 + 체력바(검은 배경 + 초록 막대). 안쪽 막대는 배경 안에서 사방 3픽셀 여백을 두고 꽉 채운다.
        Label(g, "HpLabel", "HP", 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -40), new Vector2(80, 40));
        var hpBg = Box(g, "HpBg", new Color(0, 0, 0, 0.6f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -44), new Vector2(520, 32));
        hpFillImage = Box(hpBg.transform, "HpFill", new Color(0.35f, 0.9f, 0.45f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        hpFill = hpFillImage.rectTransform;
        // anchorMin (0,0) ~ anchorMax (1,1) = 부모 전체로 늘림. offsetMin/Max = 왼쪽 아래 / 오른쪽 위 여백
        hpFill.anchorMin = Vector2.zero; hpFill.anchorMax = Vector2.one; hpFill.offsetMin = new Vector2(3, 3); hpFill.offsetMax = new Vector2(-3, -3);

        // 오른쪽 위: 점수 / 위쪽 가운데: 스테이지 이름
        scoreText = Label(g, "Score", "SCORE  0000000", 40, Color.white, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -36), new Vector2(700, 50));
        stageText = Label(g, "Stage", "", 28, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(900, 40));

        // 가운데 위쪽: 콤보 숫자(크게) + "COMBO" 글자. 처음엔 숨김.
        comboText = Label(g, "Combo", "", 110, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 260), new Vector2(600, 130));
        comboLabel = Label(g, "ComboLabel", "COMBO", 34, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(400, 50));
        comboText.gameObject.SetActive(false);
        comboLabel.gameObject.SetActive(false);

        // 가운데: 카운트다운(아주 크게), 기술 이름 외침(굵은 기울임)
        countdownText = Label(g, "Countdown", "", 150, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(900, 200));
        shoutText = Label(g, "Shout", "", 64, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1700, 100));
        shoutText.fontStyle = FontStyle.BoldAndItalic;
        shoutText.gameObject.SetActive(false);

        // 판정 글자 2개(레인별). 위치는 판정할 때마다 PlacePopup이 정한다.
        for (int i = 0; i < 2; i++)
        {
            popups[i] = Label(g, "Popup" + i, "", 44, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 60));
            popups[i].fontStyle = FontStyle.Bold;
            popups[i].gameObject.SetActive(false);
        }

        // 보스 체력바: 위쪽 가운데, 이름 + 빨간 막대. 보스전에서만 켠다.
        bossGroup = Group("Boss", g);
        bossName = Label(bossGroup.transform, "BossName", "BOSS", 30, new Color(1f, 0.6f, 0.6f), TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(800, 40));
        var bossBg = Box(bossGroup.transform, "BossBg", new Color(0, 0, 0, 0.6f), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(800, 24));
        var bossFillImg = Box(bossBg.transform, "BossFill", new Color(0.9f, 0.2f, 0.25f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        bossFill = bossFillImg.rectTransform;
        bossFill.anchorMin = Vector2.zero; bossFill.anchorMax = Vector2.one; bossFill.offsetMin = new Vector2(3, 3); bossFill.offsetMax = new Vector2(-3, -3);
        bossGroup.SetActive(false);
        gameplay.SetActive(false);   // 플레이 UI 전체도 처음엔 숨긴다(타이틀부터 시작).

        // 큰 배너 (STAGE CLEAR, GAME OVER, 보스 등장)
        // 그림(컷인)은 화면 전체로 늘리고, 비율은 유지한다(preserveAspect). 글자는 그 위 가운데.
        bannerGroup = Group("Banner", c);
        bannerImage = Box(bannerGroup.transform, "BannerImage", Color.white, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        Stretch(bannerImage.rectTransform);
        bannerImage.preserveAspect = true;
        bannerText = Label(bannerGroup.transform, "BannerText", "", 140, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1900, 300));
        bannerText.fontStyle = FontStyle.Bold;
        bannerGroup.SetActive(false);

        // 타이틀: 화면 전체 어두운 배경 + 배경 그림 + 제목 + 부제 + 메뉴 + 안내
        titlePanel = Group("Title", c);
        Stretch(Box(titlePanel.transform, "Bg", new Color(0.05f, 0.05f, 0.09f, 0.96f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero).rectTransform);
        // 배경 그림: 화면 전체로 늘린다. 같은 부모 안에서는 나중에 만든 것이 위에 그려지므로, 단색 배경 위·글자 아래에 온다.
        titleBg = Box(titlePanel.transform, "BgImage", Color.white, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        Stretch(titleBg.rectTransform);
        titleBg.gameObject.SetActive(false);
        titleText = Label(titlePanel.transform, "TitleText", "", 66, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1800, 260));
        titleText.fontStyle = FontStyle.Bold;
        subtitleText = Label(titlePanel.transform, "Subtitle", "", 38, new Color(1f, 0.8f, 0.85f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(1800, 80));
        titleHint = Label(titlePanel.transform, "Hint", "", 28, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(1200, 50));
        // 메뉴 글자 칸 4개: 부제 아래로 70픽셀 간격. 글자 상자 폭 700 = 마우스로 고르기 쉬운 넓이
        for (int i = 0; i < menuItems.Length; i++)
        {
            menuItems[i] = Label(titlePanel.transform, "Menu" + i, "", 46, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -170 - i * 70), new Vector2(700, 62));
            menuItems[i].fontStyle = FontStyle.Bold;
            menuItems[i].gameObject.SetActive(false);
        }
        titlePanel.SetActive(false);

        // 스토리 대화창: 화면을 살짝 어둡게 + 아래쪽 대화 상자 + 왼쪽 위 이름표 + 대사 + 오른쪽 아래 안내
        storyPanel = Group("Story", c);
        Stretch(Box(storyPanel.transform, "Dim", new Color(0, 0, 0, 0.45f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero).rectTransform);
        // 초상화: 대화 상자보다 먼저 만들어서 상자 뒤에 그려진다 → 아랫부분이 상자에 가려져 미연시처럼 상자 위로 상반신이 보인다.
        // 왼쪽 아래 모서리 기준(앵커·피벗 0,0), 크기 620×620. preserveAspect: 그림 비율 유지
        storyPortrait = Box(storyPanel.transform, "Portrait", Color.white, new Vector2(0, 0), new Vector2(0, 0), new Vector2(150, 220), new Vector2(620, 620));
        storyPortrait.preserveAspect = true;
        storyPortrait.gameObject.SetActive(false);
        var storyBox = Box(storyPanel.transform, "Box", new Color(0.08f, 0.08f, 0.14f, 0.95f), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(1700, 300));
        // 이름표: 대화 상자 왼쪽 위 모서리(앵커 0,1)에 아래쪽을 기준점(피벗 0,0)으로 붙여서 상자 위로 튀어나오게 한다.
        var nameBox = Box(storyBox.transform, "NameBox", new Color(0.85f, 0.3f, 0.45f), new Vector2(0, 1), new Vector2(0, 0), new Vector2(30, 0), new Vector2(320, 64));
        storyName = Label(nameBox.transform, "Name", "", 36, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320, 64));
        storyName.fontStyle = FontStyle.Bold;
        storyLine = Label(storyBox.transform, "Line", "", 40, Color.white, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(50, -40), new Vector2(1600, 200));
        storyLine.horizontalOverflow = HorizontalWrapMode.Wrap;   // 대사는 상자 폭에서 자동 줄바꿈
        storyLine.lineSpacing = 1.2f;
        storyHint = Label(storyBox.transform, "Hint", "", 26, new Color(1, 1, 1, 0.6f), TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 20), new Vector2(800, 40));
        storyPanel.SetActive(false);

        // 결과 화면: 어두운 배경 + 위쪽 제목 + 왼쪽 큰 등급 글자 + 오른쪽 본문 + 아래 안내
        resultPanel = Group("Result", c);
        Stretch(Box(resultPanel.transform, "Bg", new Color(0.04f, 0.04f, 0.08f, 0.93f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero).rectTransform);
        resultHeading = Label(resultPanel.transform, "Heading", "", 90, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(1600, 130));
        resultHeading.fontStyle = FontStyle.Bold;
        resultRank = Label(resultPanel.transform, "Rank", "", 220, PerfectCol, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-560, 0), new Vector2(400, 300));
        resultRank.fontStyle = FontStyle.Bold;
        resultBody = Label(resultPanel.transform, "Body", "", 34, Color.white, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(160, 0), new Vector2(1150, 560));
        resultBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        resultBody.lineSpacing = 1.25f;
        resultHint = Label(resultPanel.transform, "Hint", "", 32, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(1600, 60));
        resultPanel.SetActive(false);

        // 오른쪽 아래 작은 알림 (Canvas 바로 아래라 어느 화면에서든 보인다)
        // 타격 글자 6칸 (플레이 UI 묶음 안). 처음엔 숨김.
        for (int i = 0; i < impacts.Length; i++)
        {
            impacts[i] = Label(g, "Impact" + i, "", 58, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 100));
            impacts[i].fontStyle = FontStyle.BoldAndItalic;
            impacts[i].gameObject.SetActive(false);
        }

        noticeText = Label(c, "Notice", "", 28, Color.white, TextAnchor.MiddleRight, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 30), new Vector2(800, 44));
        noticeText.gameObject.SetActive(false);

        HandleScore(0);   // 점수 글자를 0으로 초기화
    }

    // 부모 전체를 덮는 빈 묶음 오브젝트. 이 아래에 부품을 넣고 묶음째 켜고 끈다.
    GameObject Group(string name, Transform parent)
    {
        // new GameObject(이름, 붙일 컴포넌트 타입들): UI 오브젝트는 Transform 대신 RectTransform이 필요해서 처음부터 붙여 만든다.
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        return go;
    }

    // 부모 사각형을 꽉 채우게 만든다(앵커 0~1, 여백 0).
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // 앵커 한 점에 붙이고 피벗·위치·크기를 정한다. (anchorMin = anchorMax면 늘어나지 않고 고정 크기)
    static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // 색 사각형(Image) 하나를 만든다.
    Image Box(Transform parent, string name, Color color, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;        // sprite가 없으면 흰 사각형에 이 색이 칠해진다.
        img.raycastTarget = false;   // 마우스 클릭을 가로채지 않게 한다(이 게임 UI는 클릭할 버튼이 없다).
        Place(img.rectTransform, anchor, pivot, pos, size);
        return img;
    }

    // 글자(Text) 하나를 만든다. 검은 테두리(Outline)를 붙여서 어떤 배경 위에서도 읽히게 한다.
    Text Label(Transform parent, string name, string text, int size, Color color, TextAnchor align,
        Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        // Overflow: 상자보다 글자가 길어도 잘리지 않고 넘쳐서 보인다(기본은 줄바꿈·잘림).
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0, 0, 0, 0.85f);
        outline.effectDistance = new Vector2(2, -2);   // 테두리 두께(픽셀)
        Place(t.rectTransform, anchor, pivot, pos, box);
        return t;
    }
}
