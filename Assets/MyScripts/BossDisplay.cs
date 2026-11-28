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
//   Hit Sprite: 던진 적에 맞는 순간 잠깐 바뀌는 그림 (예: Hikari/Hit/Hit_Impact)
//   Defeat Sprite: 쓰러질 때 그림 (예: Hikari/Boss_Fall). 비워두면 원래 그림 그대로 날아간다.
//
// 보스 패턴 "돌진": 보스전의 연타 노트는 잡몹 대신 보스(히카리)가 직접 달려든다. NoteDisplay가 매 프레임 위치를 알려준다.
//   1) 기 모으기: 노트가 화면에 들어오면 보스가 제자리에서 빨갛게 달아오르며 부들부들 떤다 (예고)
//   2) 돌진: 노트가 보스 위치를 지나면 노트와 같은 속도로 판정선까지 달려든다 → 판정 시각에 정확히 판정선 도착
//   3) 연타: 판정선에 버티고 선 보스를 두드린다. 칠 때마다 피격 그림 + 움찔
//   4) 성공: 크게 튕겨 나가며 제자리로 + 보스 체력 추가 감소 / 실패: 비웃으며 제자리로 걸어 돌아간다
//
// 보스 패턴 "기공파": 보스전의 롱노트는 히카리가 쏘는 빔이 된다 (그림은 NoteDisplay가 그림).
//   빔이 나가는 동안 BeamUpdate()로 보스가 푸르게 빛나며 떨고, 끝까지 막아내면 BeamReflected()로 되맞는다.
//
// 체력별 그림: Health Sprites에 "체력이 이 값 이하면 이 그림"을 넣으면, 체력이 줄 때마다 평소 그림이 바뀐다.
//   (맞은 그림·돌진 그림 등이 끝나면 지금 체력 구간의 그림으로 돌아온다.) ⋮ 메뉴 > "체력별 그림 자동 채우기"로 채울 수 있다.
//
// 쓰러질 때(Defeat): 대난투의 "별이 되는" 연출. 화면 안쪽(오른쪽 위)으로 작아지며 날아가다가 반짝 하고 사라진다.
//
// 연결:
//   GameFlow --(Begin, Defeat, Hide)--> BossDisplay
//   Judge.OnNoteJudged --> 맞힐 때마다 체력 감소
//   StudentLauncher --(CanBeHit, HitPoint로 조준 → 맞으면 Flinch)--> BossDisplay
//   BossDisplay --(SetBoss, SetBossHealth)--> GameHud
// ============================================================================

using System.Collections.Generic;
using UnityEngine;

/// <summary>체력 구간 하나: 보스 체력이 below 이하가 되면 평소 그림을 sprite로 바꾼다.</summary>
[System.Serializable]
public class BossHealthLook
{
    [Range(0f, 1f)] public float below = 0.5f;
    public Sprite sprite;
}

/// <summary>보스전의 보스(화면 오른쪽 위). 맞힐 때마다 흔들리고 체력이 줄며, 곡을 끝까지 버티면 날아가며 쓰러진다.</summary>
public class BossDisplay : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] GameHud hud;

    [Tooltip("보스 그림 (비워두면 임시 도형)")]
    [SerializeField] Sprite defaultSprite;
    [Tooltip("맞는 순간 잠깐 바뀌는 그림 (비워두면 안 바뀜)")]
    [SerializeField] Sprite hitSprite;
    [Tooltip("맞은 그림을 보여주는 시간(초)")]
    [SerializeField] float hitSpriteTime = 0.18f;
    [Tooltip("쓰러질 때 그림 (비워두면 안 바뀜)")]
    [SerializeField] Sprite defeatSprite;

    [Header("보스 패턴: 돌진 (보스전 연타 노트)")]
    [Tooltip("달려들 때 그림 (비워두면 평소 그림)")]
    [SerializeField] Sprite chargeSprite;
    [Tooltip("기 모으는 동안 달아오르는 색")]
    [SerializeField] Color windupColor = new Color(1f, 0.45f, 0.4f, 1f);
    [Tooltip("돌진을 막아냈을 때 추가로 깎이는 보스 체력 (노트 몇 개 분량)")]
    [SerializeField] int chargeBonusHits = 3;
    [Tooltip("제자리로 돌아가는 시간(초)")]
    [SerializeField] float returnTime = 0.6f;

    [Header("보스 패턴: 기공파 (보스전 롱노트)")]
    [Tooltip("기공파를 쏘는 동안 그림 (비워두면 평소 그림)")]
    [SerializeField] Sprite beamSprite;
    [Tooltip("기공파를 쏘는 동안 빛나는 색")]
    [SerializeField] Color beamGlowColor = new Color(0.55f, 0.85f, 1f, 1f);
    [Tooltip("기공파를 되받아쳤을 때 추가로 깎이는 보스 체력 (노트 몇 개 분량)")]
    [SerializeField] int beamBonusHits = 2;

    [Header("체력별 그림")]
    [Tooltip("체력이 below 이하가 되면 평소 그림이 sprite로 바뀐다 (순서 상관없음)")]
    [SerializeField] List<BossHealthLook> healthSprites = new List<BossHealthLook>();

    [Header("쓰러질 때 (별이 되기)")]
    [Tooltip("날아가서 별이 되는 데 걸리는 시간(초)")]
    [SerializeField] float starTime = 1.1f;
    [Tooltip("날아가는 방향과 거리 (보스 위치 기준)")]
    [SerializeField] Vector2 starOffset = new Vector2(2.5f, 4.5f);
    [SerializeField] Vector2 position = new Vector2(5.6f, 1.4f);   // 보스가 서 있는 월드 위치 (화면 오른쪽 위)
    [Tooltip("켜면 보스 그림을 좌우로 뒤집는다 (Y축 기준 반전)")]
    [SerializeField] bool flipX = true;
    [Tooltip("보스 크기 (월드 단위 높이)")]
    [SerializeField] float height = 3.4f;                           // 그림 크기가 제각각이어도 이 높이로 맞춘다.

    Transform root;              // 보스 전체의 부모. 이것을 움직이고 돌리면 몸·눈·입이 같이 움직인다.
    SpriteRenderer main;         // 몸통 (그림을 넣으면 이 칸에 들어간다)
    SpriteRenderer[] extras;     // 임시 얼굴 부위들 (그림이 있으면 숨긴다)
    bool active, defeated;       // 보스전 중인지, 쓰러지는 중인지
    int total, hits;             // 전체 노트 수, 지금까지 성공한 판정 수
    float shake, defeatTime;     // 움찔 세기(1 → 0), 쓰러지기 시작한 뒤 흐른 시간
    Sprite idleSprite;           // 지금 평소 그림 (맞은 그림에서 돌아올 때 씀). 체력 구간에 따라 바뀐다
    Sprite baseSprite;           // 체력이 가득일 때 그림 (스테이지 그림 또는 Default Sprite)

    enum Charge { None, Windup, Charging, Returning }
    Charge charge;               // 돌진 패턴 상태
    float chargeX;               // 돌진 중 보스의 x
    float returnFromX, returnT;  // 돌아가기 시작한 x, 돌아간 시간
    bool chargeWon;              // 돌진을 막아냈는지 (돌아가는 모습이 달라진다)
    float beamTimer;             // 기공파를 쏘는 중이면 > 0 (NoteDisplay가 매 프레임 채운다)
    Vector3 defeatFrom;          // 날아가기 시작한 위치
    bool twinkled;               // 별 반짝임을 냈는지

    float hitTimer;              // 맞은 그림을 보여줄 남은 시간

    public bool IsActive => active;

    /// <summary>보스 체력 0~1 (연출용). 보스전이 아니면 1.</summary>
    public float Health => active && total > 0 ? Mathf.Clamp01(GameMath.BossHealth(hits, total)) : 1f;

    /// <summary>기공파를 쏘는 동안 NoteDisplay가 매 프레임 부른다.</summary>
    public void BeamUpdate()
    {
        if (!CanBeHit) return;
        if (beamTimer <= 0f && charge == Charge.None)
        {
            if (beamSprite != null) SetSprite(beamSprite);
            GameSfx.Play(GameSfx.Sound.Windup, 0.7f);   // 기 모으는 소리
        }
        beamTimer = 0.1f;
    }

    /// <summary>기공파를 끝까지 막아내서 되돌아온 기에 맞았을 때.</summary>
    public void BeamReflected()
    {
        if (!CanBeHit) return;
        shake = 1.5f;
        if (hitSprite != null) { SetSprite(hitSprite); hitTimer = 0.5f; }
        hits += beamBonusHits;
        if (hud != null) hud.SetBossHealth(Health);
        UpdateHealthLook();
        CameraShake.Shake(0.25f, 0.3f);
    }

    /// <summary>돌진 노트가 화면에 있는 동안 NoteDisplay가 매 프레임 부른다. noteX = 노트(머리) x, front = 보스 몸 가운데에서 앞(왼쪽) 끝까지 거리.</summary>
    public void ChargeUpdate(float noteX, float front)
    {
        if (!CanBeHit || charge == Charge.Returning) return;
        float x = noteX + front;                 // 보스 앞 끝이 노트 위치에 오도록
        if (x >= position.x)
        {
            if (charge == Charge.None) GameSfx.Play(GameSfx.Sound.Windup);
            charge = Charge.Windup;              // 아직 노트가 보스보다 오른쪽 → 제자리에서 기 모으기
        }
        else
        {
            if (charge != Charge.Charging && chargeSprite != null) SetSprite(chargeSprite);
            charge = Charge.Charging;
            chargeX = x;
        }
    }

    /// <summary>돌진한 보스를 연타로 한 대 때렸을 때.</summary>
    public void ChargeHit()
    {
        if (charge != Charge.Charging) return;
        shake = 1f;
        if (hitSprite != null) { SetSprite(hitSprite); hitTimer = hitSpriteTime; }
    }

    /// <summary>돌진 끝. won = 연타 성공(막아냄)인지.</summary>
    public void EndCharge(bool won)
    {
        if (charge == Charge.None || charge == Charge.Returning) return;
        chargeWon = won;
        returnFromX = root != null ? root.position.x : position.x;
        returnT = 0f;
        charge = Charge.Returning;
        if (won)
        {
            GameSfx.Play(GameSfx.Sound.BigHit);
            CameraShake.Shake(0.3f, 0.35f);
            if (hitSprite != null) { SetSprite(hitSprite); hitTimer = returnTime; }
            hits += chargeBonusHits;   // 큰 데미지
            if (hud != null) hud.SetBossHealth(Mathf.Max(0f, GameMath.BossHealth(hits, total)));
            UpdateHealthLook();
        }
        else
        {
            hitTimer = 0f;
            SetSprite(idleSprite);
        }
    }

    /// <summary>지금 공격을 받을 수 있는지 (보스전 중이고 아직 쓰러지지 않음). StudentLauncher가 적을 던질지 정할 때 본다.</summary>
    public bool CanBeHit => active && !defeated;

    /// <summary>던진 적이 겨냥할 지점 (보스가 서 있는 위치 = 그림 가운데).</summary>
    public Vector3 HitPoint => new Vector3(position.x, position.y, 0f);

    /// <summary>맞아서 움찔한다. strength 1이 보통 세기. 던진 적이 도착했을 때 StudentLauncher가 부른다.</summary>
    public void Flinch(float strength)
    {
        if (!CanBeHit) return;
        shake = Mathf.Max(shake, strength);   // 이미 더 세게 흔들리는 중이면 약한 값으로 덮어쓰지 않는다.
        if (hitSprite != null && idleSprite != null)
        {
            hitTimer = hitSpriteTime;
            SetSprite(hitSprite);
        }
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
        idleSprite = baseSprite = use;
        hitTimer = 0f;
        charge = Charge.None;
        main.sprite = custom ? use : ProcSprites.Square;
        main.flipX = custom && flipX;   // 그림을 바꿔도 flipX는 SpriteRenderer에 남아 있어서 한 번만 정하면 된다
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
        defeatFrom = root != null ? root.position : (Vector3)position;
        GameSfx.Play(GameSfx.Sound.Whoosh);
        twinkled = false;
        charge = Charge.None;
        if (main != null) main.color = Color.white;
        if (defeatSprite != null && idleSprite != null) SetSprite(defeatSprite);
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
        UpdateHealthLook();
    }

    void Update()
    {
        if (!active || root == null) return;
        float dt = Time.deltaTime;

        if (defeated)
        {
            // 별이 되기: 화면 안쪽(오른쪽 위)으로 빙글빙글 돌며 점점 작아진다 → 멀어지는 것처럼 보임
            defeatTime += dt;
            float k = Mathf.Clamp01(defeatTime / Mathf.Max(0.1f, starTime));
            float ease = 1f - (1f - k) * (1f - k);
            root.position = Vector3.Lerp(defeatFrom, defeatFrom + (Vector3)starOffset, ease);
            root.localScale = Vector3.one * Mathf.Lerp(1f, 0.03f, ease);
            root.rotation = Quaternion.Euler(0f, 0f, -defeatTime * 900f);
            if (k >= 1f && !twinkled)
            {
                twinkled = true;
                StartCoroutine(Twinkle(root.position));   // 사라진 자리에서 반짝!
                GameSfx.Play(GameSfx.Sound.Twinkle);
                root.gameObject.SetActive(false);
            }
            return;
        }

        shake = Mathf.Max(0f, shake - dt * 8f);   // 움찔은 초당 8씩 줄어서 약 0.125초 만에 끝난다.
        if (hitTimer > 0f)
        {
            hitTimer -= dt;
            if (hitTimer <= 0f) SetSprite(charge == Charge.Charging && chargeSprite != null ? chargeSprite : idleSprite);   // 맞은 그림 → 원래 그림
        }
        // 숨쉬기: 세로 크기가 ±2% 사이로 천천히 오르내린다. sin은 -1~1을 반복하는 파동.
        float breathe = 1f + Mathf.Sin(Time.time * 2.5f) * 0.02f;
        root.localScale = new Vector3(1f, breathe, 1f);

        // 돌진 패턴 상태에 따라 기준 위치·기울기·색을 정한다.
        float baseX = position.x, baseY = position.y, angle = 0f, heat = 0f;
        Color glowColor = windupColor;
        if (beamTimer > 0f)
        {
            // 기공파 쏘는 중: 푸르게 빛나며 반동으로 덜덜
            beamTimer -= dt;
            glowColor = beamGlowColor;
            heat = 0.5f + Mathf.Sin(Time.time * 25f) * 0.25f;
            baseX += Random.Range(-1f, 1f) * 0.05f + 0.15f;
            if (beamTimer <= 0f && hitTimer <= 0f && charge == Charge.None) SetSprite(idleSprite);
        }
        switch (charge)
        {
            case Charge.Windup:
                heat = 0.6f + Mathf.Sin(Time.time * 18f) * 0.4f;      // 빨갛게 깜빡이며 달아오름
                baseX += Random.Range(-1f, 1f) * 0.06f;                // 부들부들
                baseY += Random.Range(-1f, 1f) * 0.04f;
                break;
            case Charge.Charging:
                baseX = chargeX;
                angle = 8f;                                            // 앞으로 숙이고 달려드는 느낌
                heat = 0.5f;
                break;
            case Charge.Returning:
                returnT += dt;
                float k = Mathf.Clamp01(returnT / Mathf.Max(0.05f, returnTime));
                float ease = 1f - (1f - k) * (1f - k);                 // 처음 빠르고 끝에 느려짐
                baseX = Mathf.Lerp(returnFromX, position.x, ease);
                if (chargeWon)
                {
                    baseY += Mathf.Sin(k * Mathf.PI) * 1.2f;           // 얻어맞고 붕 떠서 튕겨 나감
                    angle = -Mathf.Sin(k * Mathf.PI) * 25f;            // 뒤로 젖혀짐
                }
                else
                {
                    baseY += Mathf.Abs(Mathf.Sin(k * Mathf.PI * 4f)) * 0.15f;   // 여유롭게 깡총깡총 돌아감
                }
                if (k >= 1f)
                {
                    charge = Charge.None;
                    if (hitTimer <= 0f) SetSprite(idleSprite);
                }
                break;
        }
        Color tint = Color.Lerp(Color.white, glowColor, heat);
        if (main != null && idleSprite != null) main.color = tint;
        root.rotation = Quaternion.Euler(0f, 0f, angle);
        // 움찔: 좌우로 무작위 떨림 + 오른쪽으로 살짝 밀림(맞아서 뒤로 밀리는 느낌). shake가 0이면 원래 위치.
        root.position = new Vector3(baseX + Random.Range(-1f, 1f) * shake * 0.15f + shake * 0.2f, baseY, 0f);
    }

    // 별 반짝임: 보스가 사라진 자리에서 별이 커졌다 작아지며 돈다.
    System.Collections.IEnumerator Twinkle(Vector3 pos)
    {
        var sr = ProcSprites.Make("StarTwinkle", null, ScreenFx.Sparkle, new Color(1f, 0.95f, 0.6f, 1f), 10);
        sr.transform.position = pos;
        const float duration = 0.6f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            sr.transform.localScale = Vector3.one * Mathf.Sin(k * Mathf.PI) * 1.8f;
            sr.transform.rotation = Quaternion.Euler(0f, 0f, k * 180f);
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    // 지금 체력에 맞는 평소 그림을 고른다. 바뀌면 움찔 + 붉은 번쩍임으로 "한 단계 무너졌다"는 느낌을 준다.
    void UpdateHealthLook()
    {
        if (baseSprite == null || defeated) return;   // 임시 도형일 때는 안 바꾼다
        float hp = Health;
        Sprite pick = baseSprite;
        float best = 2f;
        foreach (var h in healthSprites)
            if (h != null && h.sprite != null && hp <= h.below && h.below < best) { best = h.below; pick = h.sprite; }
        if (pick == idleSprite) return;
        idleSprite = pick;
        shake = Mathf.Max(shake, 1.2f);
        ScreenFx.Flash(new Color(1f, 0.3f, 0.25f, 0.35f), 0.2f);
        // 맞은 그림·돌진·기공파 그림이 보이는 중이면 그게 끝날 때 새 그림으로 돌아온다
        if (hitTimer <= 0f && charge == Charge.None && beamTimer <= 0f) SetSprite(idleSprite);
    }

#if UNITY_EDITOR
    // Inspector ⋮ 메뉴: Assets/Arts/Hikari/FullBody 의 그림으로 체력별 그림을 채운다.
    [ContextMenu("체력별 그림 자동 채우기 (Arts/Hikari/FullBody)")]
    void AutoFillHealthSprites()
    {
        const string dir = "Assets/Arts/Hikari/FullBody/";
        UnityEditor.Undo.RecordObject(this, "체력별 그림 자동 채우기");
        healthSprites = new List<BossHealthLook>
        {
            new BossHealthLook { below = 0.7f,  sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dir + "FullBody_03.png") },   // 주먹 쥐고 진지해짐
            new BossHealthLook { below = 0.4f,  sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dir + "FullBody_10.png") },   // 포효
            new BossHealthLook { below = 0.15f, sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(dir + "FullBody_11.png") },   // 피투성이
        };
        UnityEditor.EditorUtility.SetDirty(this);
        int n = 0; foreach (var h in healthSprites) if (h.sprite != null) n++;
        Debug.Log("[BossDisplay] 체력별 그림 " + n + "/3 개를 채웠습니다." + (n < 3 ? " (" + dir + " 를 확인하세요)" : ""));
    }
#endif

    // 보스 그림을 바꾸고 크기를 다시 맞춘다 (그림마다 픽셀 크기가 달라서).
    void SetSprite(Sprite s)
    {
        if (s == null || main == null) return;
        main.sprite = s;
        FitHeight(true);
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
