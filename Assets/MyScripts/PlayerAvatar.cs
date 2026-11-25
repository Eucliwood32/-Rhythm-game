// ============================================================================
// PlayerAvatar.cs : 주인공 강두철을 판정선 왼쪽에 그리고, 판정에 맞춰 자세를 바꾸는 컴포넌트
// ----------------------------------------------------------------------------
// 6요소 중 담당: Display (화면 연출)
// 동작:
//   - 위쪽(A) 성공: 위 레인 높이로 뛰어올라 어퍼컷
//   - 아래쪽(B) 성공: 킥
//   - Miss: 맞아서 뒤로 휘청 + 빨갛게 번쩍 + 화면 흔들림
//   - 롱노트를 누르는 동안은 자세를 유지한다.
//   - 콤보가 일정 수(shoutEvery)의 배수가 될 때마다 기술 이름을 외친다(GameHud에 큰 글자).
//
// 그리는 방식이 두 가지다:
//   1) 그림 모드: Stand Sprite 칸에 그림을 넣으면 켜진다. 자세마다 그림 한 장을 통째로 바꿔 끼운다.
//      "2D Characters Sprites In Battle Zone" 에셋처럼 자세별 그림(Stand, Attack0~2, Hit)이 따로 있는 에셋에 맞춘 방식.
//      공격 그림은 여러 장을 넣으면 칠 때마다 무작위로 골라서 덜 단조롭게 보인다.
//   2) 도형 모드: Stand Sprite가 비어 있으면 예전처럼 코드로 그린 임시 도형(관절로 팔다리를 휘두름)을 쓴다.
//
// 두 모드 공통: 몸 전체의 높이는 목표 높이로 부드럽게 따라가게(지수 감쇠) 해서 뚝뚝 끊기지 않게 한다.
//
// 연결:
//   Judge.OnNoteJudged / OnBarrageHit --> 자세 바꾸기,  Judge.HoldingNote --> 롱노트 자세 유지
//   Referee.OnComboChanged --> 기술 외치기 → GameHud.ShowShout
//   NoteDisplay --> 판정선·레인 위치와 레인 색
// ============================================================================

using UnityEngine;

/// <summary>주인공 강두철. 판정에 맞춰 어퍼컷·킥·피격 자세를 취하고, 콤보마다 기술 이름을 외친다. 자세별 그림을 넣으면 그림 모드로 그린다.</summary>
public class PlayerAvatar : MonoBehaviour
{
    // 자세 4가지. 이 파일 안에서만 쓰는 enum이라 클래스 안에 넣었다.
    enum Pose { Idle, Uppercut, Kick, Hurt }

    [SerializeField] Judge judge;
    [SerializeField] Referee referee;
    [SerializeField] NoteDisplay noteDisplay;
    [SerializeField] GameHud hud;

    [Header("그림 모드 (Stand Sprite를 넣으면 켜짐)")]
    [Tooltip("⋮ 메뉴 > \"캐릭터 그림 자동 채우기\"가 찾을 Battle Zone 에셋 캐릭터 폴더 이름")]
    [SerializeField] string characterName = "Roi";
    [Tooltip("기본 자세 그림. 예) Characters/Roi/Stand")]
    [SerializeField] Sprite standSprite;
    [Tooltip("위쪽(A) 공격 그림들. 여러 장이면 칠 때마다 무작위. 예) Attack0, Attack1")]
    [SerializeField] Sprite[] upperAttackSprites;
    [Tooltip("아래쪽(B) 공격 그림들. 예) Attack2")]
    [SerializeField] Sprite[] lowerAttackSprites;
    [Tooltip("Miss 때 맞는 그림. 예) Hit")]
    [SerializeField] Sprite hitSprite;
    [Tooltip("그림 한 장 전체의 높이(월드 단위). 에셋 그림은 512px 정사각형이고 캐릭터가 그 높이의 약 70%를 차지한다.")]
    [SerializeField] float spriteHeight = 4f;
    [Tooltip("그림 순서. 노트(1~5)보다 작게 두어야 판정선 근처 노트를 가리지 않는다.")]
    [SerializeField] int spriteOrder = -5;

    [Header("도형 모드")]
    [Tooltip("몸통 그림 (도형 모드에서 몸통만 바꾼다. 비워두면 임시 도형)")]
    [SerializeField] Sprite bodySprite;

    [Tooltip("판정선에서 왼쪽으로 떨어진 거리")]
    [SerializeField] float distanceFromJudge = 1.5f;

    [Header("기술 이름")]
    [SerializeField] string upperMoveName = "슈퍼 드래곤 라이징 어퍼컷!!";
    [SerializeField] string lowerMoveName = "헥토파스칼 드릴 초전도체 킥!!";
    [Tooltip("콤보가 이 수의 배수가 될 때마다 기술 이름을 외친다")]
    [SerializeField] int shoutEvery = 10;

    const float PoseTime = 0.18f;   // 공격·피격 자세를 유지하는 시간(초). 지나면 Idle로 돌아간다.

    Transform root, arm, leg;      // 몸 전체, (도형 모드) 팔 관절, 다리 관절
    SpriteRenderer spriteBody;     // (그림 모드) 그림을 바꿔 끼우는 SpriteRenderer 하나
    Sprite attackSprite;           // (그림 모드) 이번 공격에 고른 그림
    SpriteRenderer[] renders;      // 몸의 모든 부위 (피격 때 빨갛게 칠하려고)
    Color[] baseColors;            // 각 부위의 원래 색 (빨간색에서 돌아올 때 필요)
    Pose pose = Pose.Idle;         // 지금 자세
    Lane poseLane;                 // 지금 자세를 만든 레인 (롱노트 유지 확인용)
    float poseTimer, hurtFlash;    // 자세 남은 시간, 빨간 번쩍임 세기(1 → 0)
    Lane lastHitLane = Lane.Upper; // 마지막으로 성공한 레인 (외칠 기술 이름을 고를 때)
    float currentY;                // 지금 몸 높이 (목표 높이로 부드럽게 따라간다)

    // 그림 모드인지: 기본 자세 그림이 있으면 그림 모드
    bool SpriteMode => standSprite != null;

    void Awake()
    {
        // Inspector에서 안 넣었으면 씬에서 찾는다.
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (referee == null) referee = FindAnyObjectByType<Referee>();
        if (noteDisplay == null) noteDisplay = FindAnyObjectByType<NoteDisplay>();
        if (hud == null) hud = FindAnyObjectByType<GameHud>();
    }

    void OnEnable()
    {
        if (judge == null || noteDisplay == null)   // 이 둘은 필수. referee·hud는 없으면 외치기만 안 된다.
        {
            Debug.LogError("[PlayerAvatar] Judge와 NoteDisplay를 찾지 못했습니다.");
            enabled = false;
            return;
        }
        judge.OnNoteJudged += HandleNoteJudged;
        judge.OnBarrageHit += HandleBarrageHit;
        if (referee != null) referee.OnComboChanged += HandleCombo;
    }

    void OnDisable()
    {
        if (judge != null)
        {
            judge.OnNoteJudged -= HandleNoteJudged;
            judge.OnBarrageHit -= HandleBarrageHit;
        }
        if (referee != null) referee.OnComboChanged -= HandleCombo;
    }

    void Start()
    {
        // NoteDisplay 위치값이 필요해서 Awake가 아니라 Start에서 몸을 만든다.
        if (SpriteMode) BuildSprite();
        else Build();
    }

    void HandleNoteJudged(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        if (kind == JudgeKind.HoldComplete) return;   // 롱노트 완료는 새 동작 없이 그대로
        if (result == Judgment.Miss)
        {
            SetPose(Pose.Hurt, lane);
            hurtFlash = 1f;                     // 빨간 번쩍임 시작
            CameraShake.Shake(0.12f, 0.18f);    // 작은 흔들림
            return;
        }
        lastHitLane = lane;
        SetPose(lane == Lane.Upper ? Pose.Uppercut : Pose.Kick, lane);   // 위 = 어퍼컷, 아래 = 킥
    }

    // 연타 한 번 칠 때마다 같은 공격 동작을 다시 시작 → 연타하는 만큼 주먹·발이 계속 나간다.
    void HandleBarrageHit(NoteData note, Lane lane, int hits, int need)
    {
        lastHitLane = lane;
        SetPose(lane == Lane.Upper ? Pose.Uppercut : Pose.Kick, lane);
    }

    // 콤보가 바뀔 때: shoutEvery의 배수면 마지막 성공 레인의 기술 이름을 그 레인 색으로 외친다.
    void HandleCombo(int combo)
    {
        if (hud == null || !GameMath.IsShoutCombo(combo, shoutEvery)) return;
        hud.ShowShout(lastHitLane == Lane.Upper ? upperMoveName : lowerMoveName,
            lastHitLane == Lane.Upper ? GameColors.LaneUpper : GameColors.LaneLower);
    }

    void SetPose(Pose p, Lane lane)
    {
        pose = p;
        poseLane = lane;
        poseTimer = PoseTime;   // 자세 시간을 다시 채운다.
        // 그림 모드: 공격할 때마다 그 레인의 공격 그림 중 하나를 무작위로 고른다.
        if (p == Pose.Uppercut) attackSprite = Pick(upperAttackSprites);
        else if (p == Pose.Kick) attackSprite = Pick(lowerAttackSprites);
    }

    // 배열에서 하나를 무작위로 고른다. 비어 있으면 null.
    static Sprite Pick(Sprite[] list)
    {
        if (list == null || list.Length == 0) return null;
        return list[Random.Range(0, list.Length)];   // int 버전 Random.Range는 끝 값을 포함하지 않는다 → 0 ~ Length-1
    }

    void Update()
    {
        if (root == null) return;   // 아직 몸을 안 만들었으면 대기
        float dt = Time.deltaTime;

        // 롱노트를 누르는 동안은 자세 유지: 그 레인에 누르고 있는 롱노트가 있고, 지금 공격 자세이면 타이머를 줄이지 않는다.
        bool holding = judge.HoldingNote(poseLane) != null && (pose == Pose.Uppercut || pose == Pose.Kick);
        if (!holding) poseTimer -= dt;
        if (poseTimer <= 0f) pose = Pose.Idle;

        // 기본(Idle) 값. 각도는 도 단위, 0도 = 오른쪽, 90도 = 위, -90도 = 아래.
        float targetY = noteDisplay.CenterY;
        float armAngle = -70f, armLength = 0.75f, legAngle = -90f, legLength = 0.8f, lean = 0f, xShift = 0f;

        switch (pose)
        {
            case Pose.Uppercut:
                targetY = noteDisplay.LaneY(Lane.Upper) - 0.1f;   // 위 레인 높이로 점프
                armAngle = 65f; armLength = 1.3f; lean = -12f;    // (도형 모드) 팔을 위로 길게, 몸은 뒤로 살짝 젖힘
                break;
            case Pose.Kick:
                // 도형 모드는 아래 레인 쪽으로 몸을 낮춘다. 그림 모드는 킥 그림 자체가 낮은 자세라서 서 있는 높이 그대로 둔다.
                targetY = SpriteMode ? noteDisplay.CenterY : noteDisplay.LaneY(Lane.Lower) + 0.4f;
                legAngle = 5f; legLength = 1.35f; lean = 15f;     // (도형 모드) 다리를 거의 수평으로 길게 뻗음
                break;
            case Pose.Hurt:
                lean = 22f; xShift = -0.35f;                      // 뒤로 휘청하며 왼쪽으로 밀림
                break;
        }

        // 그림 모드에서는 그림이 이미 동작을 담고 있으므로, 몸 기울이기는 절반만 쓴다(과하게 기울면 어색하다).
        if (SpriteMode) lean *= 0.5f;

        // Idle일 때만 위아래로 살짝 들썩인다(대기 모션).
        float idleBob = pose == Pose.Idle ? Mathf.Sin(Time.time * 6f) * 0.05f : 0f;
        // 지수 감쇠로 목표 높이를 따라간다: 1 - e^(-28·dt)는 프레임 속도와 상관없이 일정한 빠르기로 따라가게 하는 비율.
        currentY = Mathf.Lerp(currentY, targetY, 1f - Mathf.Exp(-dt * 28f));
        root.position = new Vector3(noteDisplay.JudgeX - distanceFromJudge + xShift, currentY + idleBob, 0f);
        root.rotation = Quaternion.Euler(0f, 0f, lean);   // z축 회전 = 2D 기울기

        if (SpriteMode)
        {
            // 그림 모드: 자세에 맞는 그림으로 바꿔 끼운다. 그 자세 그림이 비어 있으면 기본 그림을 쓴다.
            Sprite s = standSprite;
            if ((pose == Pose.Uppercut || pose == Pose.Kick) && attackSprite != null) s = attackSprite;
            else if (pose == Pose.Hurt && hitSprite != null) s = hitSprite;
            spriteBody.sprite = s;
        }
        else
        {
            // 도형 모드: 관절을 돌리고 x방향으로 늘린다. 팔·다리 그림이 관절 오른쪽에 붙어 있어서 관절 기준으로 휘두르고 늘어난다.
            arm.localRotation = Quaternion.Euler(0f, 0f, armAngle);
            arm.localScale = new Vector3(armLength, 1f, 1f);
            leg.localRotation = Quaternion.Euler(0f, 0f, legAngle);
            leg.localScale = new Vector3(legLength, 1f, 1f);
        }

        // 빨간 번쩍임: 초당 5씩 줄어서 약 0.2초. 원래 색과 빨강 사이를 hurtFlash 비율로 섞는다.
        hurtFlash = Mathf.Max(0f, hurtFlash - dt * 5f);
        for (int i = 0; i < renders.Length; i++)
            renders[i].color = Color.Lerp(baseColors[i], new Color(1f, 0.25f, 0.25f), hurtFlash);
    }

#if UNITY_EDITOR
    // Inspector의 ⋮ 메뉴에 추가: characterName 폴더에서 Stand, Attack0·1(위), Attack2(아래), Hit을 찾아 칸을 채운다.
    // 캐릭터마다 어느 공격이 높고 낮은지 다르므로, 채운 뒤 그림을 보고 위·아래 칸을 바꿔도 된다.
    [ContextMenu("캐릭터 그림 자동 채우기 (Battle Zone 에셋)")]
    public void AutoFillSprites()
    {
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("Stand t:Sprite"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("/Characters/" + characterName + "/Stand.png")) continue;   // 그 캐릭터 폴더의 Stand만
            string dir = path.Substring(0, path.LastIndexOf('/'));

            UnityEditor.Undo.RecordObject(this, "캐릭터 그림 자동 채우기");   // Ctrl+Z로 되돌릴 수 있게
            standSprite = Load(dir + "/Stand.png");
            upperAttackSprites = new[] { Load(dir + "/Attack0.png"), Load(dir + "/Attack1.png") };
            lowerAttackSprites = new[] { Load(dir + "/Attack2.png") };
            hitSprite = Load(dir + "/Hit.png");
            Debug.Log($"[PlayerAvatar] '{characterName}' 그림을 채웠습니다: {dir}");
            return;
        }
        Debug.LogWarning($"[PlayerAvatar] Characters/{characterName}/Stand.png 를 찾지 못했습니다. Character Name을 확인하세요.");
    }

    static Sprite Load(string path)
    {
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
#endif

    // 그림 모드: SpriteRenderer 하나만 만든다.
    void BuildSprite()
    {
        root = new GameObject("PlayerAvatar").transform;
        currentY = noteDisplay.CenterY;

        spriteBody = ProcSprites.Make("Sprite", root, standSprite, Color.white, spriteOrder);
        // 그림 크기 맞추기: bounds.size.y = 그림의 월드 높이(픽셀 ÷ Pixels Per Unit). 512px, PPU 100이면 5.12.
        // 원하는 높이 ÷ 실제 높이 = 배율. 모든 자세 그림이 같은 512px이라 처음 한 번만 계산하면 된다.
        float h = standSprite.bounds.size.y;
        spriteBody.transform.localScale = Vector3.one * (h > 0f ? spriteHeight / h : 1f);

        renders = new[] { spriteBody };
        baseColors = new[] { Color.white };
    }

    // 도형 모드: 임시 몸을 조립한다. 부모(root) 아래에 다리 관절, 몸통, 머리, 머리띠, 팔 관절.
    void Build()
    {
        root = new GameObject("PlayerAvatar").transform;
        currentY = noteDisplay.CenterY;

        // 다리 (엉덩이 기준으로 회전)
        // 관절(LegPivot)을 엉덩이 위치에 두고, 다리 그림은 관절에서 오른쪽으로 0.5만큼 떨어뜨려 붙인다.
        // → 관절을 돌리면 다리가 엉덩이를 축으로 돈다. (그림 중심을 축으로 돌면 엉뚱하게 돈다)
        leg = new GameObject("LegPivot").transform;
        leg.SetParent(root, false);
        leg.localPosition = new Vector3(0.1f, -0.55f, 0f);
        var legSr = ProcSprites.Make("Leg", leg, ProcSprites.Square, new Color(0.15f, 0.15f, 0.2f), 5);
        legSr.transform.localPosition = new Vector3(0.5f, 0f, 0f);
        legSr.transform.localScale = new Vector3(1f, 0.28f, 1f);

        // 몸통
        SpriteRenderer body;   // if/else 두 갈래에서 값을 넣으므로 선언만 먼저 해 둔다.
        if (bodySprite != null)
        {
            body = ProcSprites.Make("Body", root, bodySprite, Color.white, 6);   // 그림 원래 색 그대로
        }
        else
        {
            body = ProcSprites.Make("Body", root, ProcSprites.Square, new Color(0.95f, 0.95f, 0.98f), 6);   // 흰 도복
            body.transform.localScale = new Vector3(0.85f, 1.25f, 1f);
        }

        var head = ProcSprites.Make("Head", root, ProcSprites.Circle, new Color(1f, 0.85f, 0.7f), 7);
        head.transform.localScale = Vector3.one * 0.7f;
        head.transform.localPosition = new Vector3(0.05f, 0.95f, 0f);

        var band = ProcSprites.Make("Headband", root, ProcSprites.Square, new Color(0.9f, 0.15f, 0.2f), 8);   // 빨간 머리띠
        band.transform.localScale = new Vector3(0.72f, 0.12f, 1f);
        band.transform.localPosition = new Vector3(0.05f, 1.08f, 0f);

        // 팔 (어깨 기준으로 회전). 다리와 같은 관절 방식. 끝에 주먹(원)을 단다.
        arm = new GameObject("ArmPivot").transform;
        arm.SetParent(root, false);
        arm.localPosition = new Vector3(0.25f, 0.35f, 0f);
        var armSr = ProcSprites.Make("Arm", arm, ProcSprites.Square, new Color(1f, 0.85f, 0.7f), 9);
        armSr.transform.localPosition = new Vector3(0.45f, 0f, 0f);
        armSr.transform.localScale = new Vector3(0.9f, 0.24f, 1f);
        var fist = ProcSprites.Make("Fist", arm, ProcSprites.Circle, new Color(1f, 0.8f, 0.62f), 10);
        fist.transform.localPosition = new Vector3(0.92f, 0f, 0f);
        fist.transform.localScale = Vector3.one * 0.34f;

        // 모든 부위와 원래 색을 기억해 둔다(피격 번쩍임에서 원래 색으로 돌아오기 위해).
        renders = root.GetComponentsInChildren<SpriteRenderer>();
        baseColors = new Color[renders.Length];
        for (int i = 0; i < renders.Length; i++) baseColors[i] = renders[i].color;
    }
}
