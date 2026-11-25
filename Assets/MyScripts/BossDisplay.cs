// ============================================================================
// BossDisplay.cs : 보스전의 보스를 화면 오른쪽 위에 보여주는 컴포넌트
// ----------------------------------------------------------------------------
// 6요소 중 담당: Display (화면 연출)
// 동작:
//   - 노트를 맞힐 때마다 HUD의 보스 체력바가 줄어든다.
//   - StudentLauncher가 던진 적이 날아와 맞으면 Flinch()가 불려서 보스가 움찔 흔들린다.
//     (판정 순간이 아니라 "맞는 순간"에 흔들려야 던진 적과 타이밍이 맞는다)
//   - 곡을 끝까지 버티면 GameFlow가 Defeat()를 불러서 보스가 회전하며 날아가 쓰러진다.
//   - 보스 체력 = 1 - (성공한 판정 수 / 전체 노트 수). 계산은 GameMath.BossHealth.
//   주의: 보스 체력은 연출일 뿐이다. 클리어 여부는 Referee(플레이어 체력)가 정한다.
//
// 보스 그림: GameFlow의 스테이지 설정(Boss Sprite)이나 이 컴포넌트의 Default Sprite로 바꾼다.
//   둘 다 비어 있으면 임시 도형(사각형 몸 + 노란 눈 + 입)을 쓴다.
//
// 연결:
//   GameFlow --(Begin, Defeat, Hide)--> BossDisplay
//   Judge.OnNoteJudged --> 맞힐 때마다 체력 감소
//   StudentLauncher --(CanBeHit, HitPoint로 조준 → 맞으면 Flinch)--> BossDisplay
//   BossDisplay --(SetBoss, SetBossHealth)--> GameHud
// ============================================================================

using UnityEngine;

/// <summary>보스전의 보스(화면 오른쪽 위). 맞힐 때마다 흔들리고 체력이 줄며, 곡을 끝까지 버티면 날아가며 쓰러진다.</summary>
public class BossDisplay : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] GameHud hud;

    [Tooltip("보스 그림 (비워두면 임시 도형)")]
    [SerializeField] Sprite defaultSprite;
    [SerializeField] Vector2 position = new Vector2(5.6f, 1.4f);   // 보스가 서 있는 월드 위치 (화면 오른쪽 위)
    [Tooltip("보스 크기 (월드 단위 높이)")]
    [SerializeField] float height = 3.4f;                           // 그림 크기가 제각각이어도 이 높이로 맞춘다.

    Transform root;              // 보스 전체의 부모. 이것을 움직이고 돌리면 몸·눈·입이 같이 움직인다.
    SpriteRenderer main;         // 몸통 (그림을 넣으면 이 칸에 들어간다)
    SpriteRenderer[] extras;     // 임시 얼굴 부위들 (그림이 있으면 숨긴다)
    bool active, defeated;       // 보스전 중인지, 쓰러지는 중인지
    int total, hits;             // 전체 노트 수, 지금까지 성공한 판정 수
    float shake, defeatTime;     // 움찔 세기(1 → 0), 쓰러지기 시작한 뒤 흐른 시간

    public bool IsActive => active;

    /// <summary>지금 공격을 받을 수 있는지 (보스전 중이고 아직 쓰러지지 않음). StudentLauncher가 적을 던질지 정할 때 본다.</summary>
    public bool CanBeHit => active && !defeated;

    /// <summary>던진 적이 겨냥할 지점 (보스가 서 있는 위치 = 그림 가운데).</summary>
    public Vector3 HitPoint => new Vector3(position.x, position.y, 0f);

    /// <summary>맞아서 움찔한다. strength 1이 보통 세기. 던진 적이 도착했을 때 StudentLauncher가 부른다.</summary>
    public void Flinch(float strength)
    {
        if (!CanBeHit) return;
        shake = Mathf.Max(shake, strength);   // 이미 더 세게 흔들리는 중이면 약한 값으로 덮어쓰지 않는다.
    }

    void Awake()
    {
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (hud == null) hud = FindAnyObjectByType<GameHud>();
    }

    void OnEnable()
    {
        // judge가 없어도 에러로 끄지 않는다. 보스가 없는 씬에서도 이 컴포넌트가 문제를 일으키지 않게.
        if (judge != null) judge.OnNoteJudged += HandleNoteJudged;
    }

    void OnDisable()
    {
        if (judge != null) judge.OnNoteJudged -= HandleNoteJudged;
    }

    /// <summary>보스전 시작. totalNotes는 체력 계산용 전체 노트 수.</summary>
    public void Begin(int totalNotes, string bossName, Sprite sprite)
    {
        Build();   // 처음이면 보스 오브젝트를 만든다.
        total = totalNotes;
        hits = 0;
        active = true;
        defeated = false;
        shake = 0f;

        Sprite use = sprite != null ? sprite : defaultSprite;   // 스테이지 그림 → 기본 그림 → 없으면 임시 도형
        bool custom = use != null;                               // 그림이 있는지
        main.sprite = custom ? use : ProcSprites.Square;
        main.color = custom ? Color.white : new Color(0.32f, 0.1f, 0.12f);   // 그림은 원래 색 그대로(흰색 곱하기), 임시 도형은 검붉은색
        foreach (var e in extras) e.gameObject.SetActive(!custom);          // 그림이 있으면 임시 얼굴을 숨긴다.
        FitHeight(custom);

        root.gameObject.SetActive(true);
        root.position = new Vector3(position.x, position.y, 0f);
        root.rotation = Quaternion.identity;   // 지난번 쓰러질 때의 회전을 되돌린다.
        // string.IsNullOrEmpty: null이거나 ""이면 true. 이름이 비어 있으면 "BOSS"로.
        if (hud != null) hud.SetBoss(true, string.IsNullOrEmpty(bossName) ? "BOSS" : bossName);
    }

    /// <summary>보스를 쓰러뜨린다 (날아가는 연출).</summary>
    public void Defeat()
    {
        if (!active) return;
        defeated = true;          // Update에서 날아가는 연출로 바뀐다.
        defeatTime = 0f;
        if (hud != null) hud.SetBossHealth(0f);   // 체력바를 0으로
        CameraShake.Shake(0.4f, 0.6f);            // 큰 흔들림
    }

    // 보스를 숨기고 HUD의 보스 표시도 끈다. (보스전이 아닐 때)
    public void Hide()
    {
        active = false;
        defeated = false;
        if (root != null) root.gameObject.SetActive(false);
        if (hud != null) hud.SetBoss(false, "");
    }

    // 판정이 올 때마다: 보스전 중이고 성공한 판정이면 보스 체력을 깎는다.
    void HandleNoteJudged(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        if (!active || defeated) return;
        // Miss는 제외. 롱노트 완료도 제외 → 롱노트는 시작 판정 때 이미 1번 셌으므로 두 번 세지 않는다.
        if (result == Judgment.Miss || kind == JudgeKind.HoldComplete) return;
        hits++;   // 흔들림은 던진 적이 도착할 때 Flinch에서 시작한다.
        if (hud != null) hud.SetBossHealth(GameMath.BossHealth(hits, total));
    }

    void Update()
    {
        if (!active || root == null) return;
        float dt = Time.deltaTime;

        if (defeated)
        {
            // 쓰러지는 연출: 오른쪽 위로 초당 (9, 7)만큼 날아가면서 초당 720도(2바퀴) 회전
            defeatTime += dt;
            root.position += new Vector3(9f, 7f, 0f) * dt;   // 속도 × 시간 = 이번 프레임 이동 거리
            root.rotation = Quaternion.Euler(0f, 0f, -defeatTime * 720f);   // 음수 = 시계 방향
            if (defeatTime > 2f) root.gameObject.SetActive(false);         // 2초 뒤 숨김 (이미 화면 밖)
            return;
        }

        shake = Mathf.Max(0f, shake - dt * 8f);   // 움찔은 초당 8씩 줄어서 약 0.125초 만에 끝난다.
        // 숨쉬기: 세로 크기가 ±2% 사이로 천천히 오르내린다. sin은 -1~1을 반복하는 파동.
        float breathe = 1f + Mathf.Sin(Time.time * 2.5f) * 0.02f;
        root.localScale = new Vector3(1f, breathe, 1f);
        // 움찔: 좌우로 무작위 떨림 + 오른쪽으로 살짝 밀림(맞아서 뒤로 밀리는 느낌). shake가 0이면 원래 위치.
        root.position = new Vector3(position.x + Random.Range(-1f, 1f) * shake * 0.15f + shake * 0.2f, position.y, 0f);
    }

    // 보스 크기를 height에 맞춘다.
    void FitHeight(bool custom)
    {
        if (custom && main.sprite != null)
        {
            // bounds.size.y: 스프라이트의 실제 월드 높이(픽셀 수 ÷ Pixels Per Unit). 원하는 높이 ÷ 실제 높이 = 배율
            float h = main.sprite.bounds.size.y;
            main.transform.localScale = Vector3.one * (h > 0f ? height / h : 1f);   // 가로세로 같은 배율 → 그림 비율 유지
        }
        else
        {
            // 임시 도형: 1×1 사각형을 폭 = 높이의 75%, 높이 = height로 늘린다.
            main.transform.localScale = new Vector3(height * 0.75f, height, 1f);
        }
    }

    // 보스 오브젝트를 처음 한 번만 만든다.
    void Build()
    {
        if (root != null) return;
        root = new GameObject("Boss").transform;
        main = ProcSprites.Make("BossBody", root, ProcSprites.Square, Color.white, 4);

        // 임시 얼굴 (그림이 없을 때만 보임). sortingOrder 5로 몸통(4) 위에 그린다.
        var eyeL = ProcSprites.Make("EyeL", root, ProcSprites.Circle, new Color(1f, 0.9f, 0.3f), 5);
        eyeL.transform.localPosition = new Vector3(-0.45f, 0.8f, 0f);
        eyeL.transform.localScale = Vector3.one * 0.45f;
        var eyeR = ProcSprites.Make("EyeR", root, ProcSprites.Circle, new Color(1f, 0.9f, 0.3f), 5);
        eyeR.transform.localPosition = new Vector3(0.45f, 0.8f, 0f);
        eyeR.transform.localScale = Vector3.one * 0.45f;
        var mouth = ProcSprites.Make("Mouth", root, ProcSprites.Square, new Color(0.08f, 0.02f, 0.02f), 5);
        mouth.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        mouth.transform.localScale = new Vector3(1.2f, 0.18f, 1f);
        extras = new[] { eyeL, eyeR, mouth };   // new[] { ... }: 타입을 추론해서 배열 생성 (SpriteRenderer[])

        root.gameObject.SetActive(false);   // 보스전 전까지는 숨긴다.
    }
}
