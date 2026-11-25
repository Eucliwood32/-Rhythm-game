// ============================================================================
// NoteDisplay.cs : 노트를 화면에 그려서 오른쪽에서 왼쪽 판정선으로 흘러오게 하는 컴포넌트
// ----------------------------------------------------------------------------
// 6요소 중 담당: Display (화면 표시)
// 적 노트 모드 (Enemies 목록에 그림이 있으면 켜짐, 뮤즈 대쉬 방식):
//   - 모든 적은 땅(아래 레인 높이)을 달려온다. 위 레인 적은 판정 Jump Lead초 전에 점프 공격 자세로 뛰어올라
//     정확히 판정 순간 위 레인 높이에 도착하고, 아래 레인 적은 도착 직전 낮은 공격 자세로 바뀐다.
//     레인 선 위의 작은 색 원(타이밍 표시)은 그대로 흘러와서 "언제, 어느 키"인지 알려준다.
//   - 보스전: 적이 땅을 달려오지 않고 보스(히카리)가 던진다. 위 레인은 높은 포물선, 아래 레인은 땅에 떨어져 굴러온다.
//   - 발밑 그림자(뛰어오르면 작고 옅어짐)와 달리기·점프·착지 흙먼지로 땅이 있다는 걸 보여준다.
//   - 단노트: 적이 서 있는 자세(Stand)로 다가온다. 맞히면 사라지고, StudentLauncher가 같은 캐릭터를 Hit 그림으로 날린다.
//   - 롱노트: 머리에 적 1명 + 뒤로 막대. 누르는 동안 적이 판정선에 붙어 맞는 자세(Groggy)로 버티고, 끝까지 누르면 날아간다.
//   - 연타: 덩치 큰 적 1명(Barrage Enemy Scale배)이 판정선에 멈춰 서서 칠 때마다 움찔(Groggy)하고, 성공하면 날아간다.
//   - Miss: 적이 도발 자세(Annoying)로 바뀌어 흐릿하게 지나간다.
//   적 그림은 Inspector의 ⋮ 메뉴 > "적 그림 자동 채우기"로 Battle Zone 에셋에서 한 번에 채울 수 있다.
//
// 원 노트 모드 (Enemies 목록이 비어 있을 때):
//   - 단노트: 원
//   - 롱노트: 원 + 뒤로 이어지는 몸통과 끝. 누르고 있는 동안 머리가 판정선에 붙어 있고 몸통이 줄어든다.
//   - 연타: 노란 원 + 노란 줄무늬 구간. 구간 동안 판정선에 붙어 있고 칠 때마다 커졌다 줄어든다.
// 판정 결과(Judge.OnNoteJudged)를 받아서 맞춘 노트는 터뜨리고, 놓친 노트는 흐리게 만든다.
// 터지는 이펙트는 기본으로 코드가 그린 고리·불꽃이고, Hit Effect Prefab 칸에 파티클 프리팹을 넣으면 그것으로 바뀐다.
// 안 하는 일: 판정. 노트가 판정선에 "닿았는지"를 이 스크립트가 정하지 않는다. 그건 Judge가 시간으로 정한다.
//
// 위치 계산의 핵심:
//   x = 판정선 x + (노트 시각 - 지금 곡 시각) × 스크롤 속도
//   예) 노트 5.0초, 지금 4.0초, 속도 8 → 판정선에서 오른쪽으로 8칸. 5.0초가 되는 순간 판정선 위.
//   위치를 매 프레임 조금씩 더해서 움직이지 않고 곡 시간으로 매번 새로 계산한다.
//   그래서 렉이 걸려도 노트가 음악과 어긋나지 않는다. (게임물리 관점: 적분 누적 오차가 없는 방식)
//
// 어느 노트의 판정인지 찾는 방법:
//   Judge가 OnNoteJudged로 "NoteData 객체"를 함께 보내준다. 그 객체를 열쇠로 Dictionary에서 화면 노트를 바로 찾는다.
//   그래서 롱노트 끊김처럼 판정이 추가로 나와도 엉뚱한 노트가 흐려지지 않는다.
//
// 연결:
//   MusicPlayer.SongTime --> 위치 계산,  Composer.GetNotes --> 그릴 노트 목록
//   Judge.OnNoteJudged / OnBarrageHit --> 터뜨리기·흐리게·연타 반응
//   GameFlow --(새 곡 시작 전 ResetForSong)--> NoteDisplay
//   GameHud·PlayerAvatar·StudentLauncher --(JudgeX, CenterY, LaneY, 색상)--> 배치 기준으로 읽어 감
//   StudentLauncher --(HitSpriteOf, RandomHitSprite)--> 날릴 적의 Hit 그림을 물어봄 (다가오던 그 적이 날아가게)
// ============================================================================

using System.Collections;           // IEnumerator (코루틴)
using System.Collections.Generic;   // List, Dictionary
using UnityEngine;

/// <summary>적 캐릭터 한 명의 자세별 그림. Battle Zone 에셋의 캐릭터 폴더 하나에 해당한다.</summary>
[System.Serializable]   // Inspector 목록에서 편집할 수 있게
public class EnemyLook
{
    public string name;       // 캐릭터 이름 (구분용. 동작에는 안 씀)
    public Sprite stand;      // 달려올 때
    public Sprite jump;       // 위 레인: 뛰어올라 덤빌 때 (예: Attack0)
    public Sprite lowAttack;  // 아래 레인: 도착 직전 낮은 공격 (예: Attack2)
    public Sprite hit;        // 맞아서 날아갈 때 (StudentLauncher가 씀)
    public Sprite groggy;     // 롱노트·연타 동안 버틸 때
    public Sprite annoying;   // Miss 때 도발하며 지나갈 때
}

/// <summary>노트를 오른쪽에서 왼쪽 판정선으로 날아오게 그리고, 판정 결과에 맞춰 터뜨리거나 흐리게 한다.</summary>
public class NoteDisplay : MonoBehaviour
{
    [SerializeField] MusicPlayer music;
    [SerializeField] Judge judge;
    [SerializeField] Composer composer;

    [Header("배치")]
    [Tooltip("판정선의 x 위치")]
    [SerializeField] float judgeX = -5f;          // 월드 좌표. 카메라 크기 5 기준으로 화면 왼쪽 쪽
    [Tooltip("두 레인의 가운데 y 위치")]
    [SerializeField] float centerY = -2f;         // 화면 아래쪽에 레인을 둔다(위쪽은 캐릭터·연출 자리)
    [Tooltip("가운데에서 각 레인까지의 거리")]
    [SerializeField] float laneOffset = 1.5f;     // 상단 레인 y = -0.5, 하단 레인 y = -3.5
    [Tooltip("노트가 날아오는 속도 (월드 단위/초)")]
    [SerializeField] float scrollSpeed = 8f;      // 클수록 노트가 빨리 지나가고 간격이 넓어 보인다. 판정 시각에는 영향 없음.
    [Tooltip("켜면 Play할 때 Main Camera를 2D용(Orthographic)으로 맞춘다")]
    [SerializeField] bool setupCamera = true;

    [Header("교체용 프리팹 (비워두면 기본 원). 노트 머리만 바뀐다.")]
    [SerializeField] GameObject upperNotePrefab;  // 그림 담당이 노트 프리팹을 만들면 여기에 넣는다. 연타 노트는 항상 기본 원.
    [SerializeField] GameObject lowerNotePrefab;

    [Header("적 노트 (목록에 그림이 있으면 원 대신 적이 다가온다)")]
    [Tooltip("적 캐릭터 목록. 노트마다 무작위 한 명. ⋮ 메뉴 > \"적 그림 자동 채우기\"로 한 번에 채울 수 있다.")]
    [SerializeField] List<EnemyLook> enemies = new List<EnemyLook>();
    [Tooltip("자동 채우기에서 뺄 캐릭터 폴더 이름 (플레이어로 쓰는 캐릭터)")]
    [SerializeField] string playerCharacter = "Roi";
    [Tooltip("적 그림 한 장 전체의 높이(월드 단위). 512px 그림에서 캐릭터는 약 70%를 차지한다.")]
    [SerializeField] float enemyHeight = 3f;
    [Tooltip("그림을 위로 올리는 정도(월드 단위). 캐릭터 몸통 가운데가 레인 선에 오게 맞춘다.")]
    [SerializeField] float enemyYOffset = 0.4f;
    [Tooltip("연타 노트 적의 크기 배율 (덩치 큰 적)")]
    [SerializeField] float barrageEnemyScale = 1.6f;
    [Tooltip("롱노트를 누르는 동안 적이 흔들리는 세기. 끝에 가까울수록 점점 세게 흔들리다가 끝나면 날아간다. 0이면 안 흔들림")]
    [SerializeField] float holdShake = 1f;
    [Tooltip("달리는 발걸음 빠르기 (들썩임·기울기·흙먼지 주기)")]
    [SerializeField] float enemyMoveSpeed = 7f;
    [Tooltip("위 레인 적이 판정 몇 초 전에 뛰어오를지")]
    [SerializeField] float jumpLead = 0.5f;
    [Tooltip("점프 포물선이 직선보다 더 솟는 높이(월드 단위). 클수록 높이 뛰었다가 내려오며 덤빈다.")]
    [SerializeField] float jumpArc = 1.0f;
    [Tooltip("아래 레인 적이 판정 몇 초 전에 낮은 공격 자세로 바뀔지")]
    [SerializeField] float lowAttackLead = 0.2f;
    [Tooltip("보스전: 위 레인으로 던질 때 포물선이 솟는 높이")]
    [SerializeField] float bossThrowArc = 1.5f;
    [Tooltip("보스 (보스전에 적을 던지는 쪽). 비워두면 씬에서 찾는다.")]
    [SerializeField] BossDisplay boss;

    [Header("교체용 이펙트 프리팹 (비워두면 코드로 그린 고리·불꽃)")]
    [Tooltip("노트를 맞혔을 때 (Perfect·Good, 연타 한 번 칠 때는 0.6배 크기로)")]
    [SerializeField] GameObject hitEffectPrefab;
    [Tooltip("롱노트 완료·연타 성공 때. 비워두면 Hit Effect Prefab을 쓴다.")]
    [SerializeField] GameObject bigHitEffectPrefab;
    [Tooltip("이펙트 프리팹을 몇 초 뒤에 지울지")]
    [SerializeField] float effectLifetime = 1f;

    // public static readonly: 다른 스크립트도 NoteDisplay.UpperColor처럼 읽을 수 있는 고정 색. 게임 전체 색을 여기서 통일한다.
    // 색은 GameColors.cs 한 곳에서 정한다(채보 편집기와 같은 색). 여기서는 이름만 이어 준다.
    public static readonly Color UpperColor = GameColors.LaneUpper;     // 빨강: 상단 레인
    public static readonly Color LowerColor = GameColors.LaneLower;     // 파랑: 하단 레인
    public static readonly Color BarrageColor = GameColors.Barrage;     // 노랑: 연타
    public static readonly Color PerfectColor = GameColors.Perfect;     // 연노랑: Perfect 이펙트

    // 화면에 떠 있는 노트 하나의 정보를 묶는 중첩 클래스.
    class NoteView
    {
        public NoteData data;         // 어떤 노트인지
        public Transform root;        // 노트 전체의 부모 (지울 때 이것만 지우면 자식까지 다 지워진다)
        public Transform head;        // 머리(원)
        public SpriteRenderer body;   // 몸통 (롱노트·연타만, 단노트는 null)
        public Transform tail;        // 끝 표시 (롱노트·연타만)
        public bool held;             // 롱노트를 누르고 있는 중
        public bool missed;           // 놓쳐서 흐려짐
        public bool done;             // 연타 판정이 끝남
        public float pulse;           // 연타 칠 때 커지는 정도 (1에서 시작해 0으로 줄어든다)
        public float baseScale = 1f;  // 머리의 기본 크기 배율 (연타는 크게)
        public float pulseAmount = 0.35f;  // 연타 칠 때 얼마나 커지는지 (적은 덩치가 커서 작게)
        public SpriteRenderer enemy;  // (적 노트 모드) 적 그림. 원 노트면 null
        public EnemyLook look;        // (적 노트 모드) 이 노트에 뽑힌 캐릭터
        public float phase;           // (적 노트 모드) 발걸음 시작 위치. 적마다 달라서 다 같이 똑같이 움직이지 않는다.
        public SpriteRenderer marker; // (적 노트 모드) 레인 선 위 타이밍 표시 원
        public SpriteRenderer shadow; // (적 노트 모드) 발밑 그림자
        public bool jumped, landed;   // 점프·착지 흙먼지를 한 번만 내려고
        public float nextDust;        // 다음 발걸음 흙먼지 시각
    }

    readonly List<NoteData> all = new List<NoteData>();          // 두 레인을 합친 전체 노트 (시간순)
    readonly List<NoteView> active = new List<NoteView>();       // 지금 화면에 있는 노트들
    // Dictionary<열쇠, 값>: 열쇠로 값을 바로 찾는 표. 여기서는 NoteData 객체 → 그 노트의 화면 표시.
    // class는 참조형이라 "같은 객체인지"로 비교된다. 값이 같아도 다른 객체면 다른 열쇠다.
    readonly Dictionary<NoteData, NoteView> views = new Dictionary<NoteData, NoteView>();
    // 노트 → 뽑힌 캐릭터. 화면 노트(views)는 맞으면 바로 지워지지만, 이 표는 곡이 끝날 때까지 남겨 둔다.
    // StudentLauncher가 같은 판정 이벤트에서 "이 노트의 캐릭터"를 물어볼 때, 이벤트 처리 순서와 상관없이 답할 수 있게.
    readonly Dictionary<NoteData, EnemyLook> lookOf = new Dictionary<NoteData, EnemyLook>();

    // 적 노트 모드인지: 쓸 수 있는(Stand 그림이 있는) 적이 하나라도 있으면
    bool EnemyMode
    {
        get
        {
            foreach (EnemyLook e in enemies) if (e != null && e.stand != null) return true;
            return false;
        }
    }

    int next;                // all에서 다음에 화면에 올릴 노트의 번호
    Transform root;          // 이 스크립트가 만드는 모든 오브젝트의 부모. Hierarchy가 지저분해지지 않게 한다.
    bool initialized;        // 판정선·레인 가이드를 이미 만들었는지

    // 다른 스크립트가 배치 기준으로 쓰라고 읽기 전용으로 공개한다. 캐릭터·학생·판정 글자를 판정선 근처에 놓을 때 쓴다.
    public float JudgeX => judgeX;
    public float ScrollSpeed => scrollSpeed;   // 1초에 노트가 움직이는 거리. BackgroundDisplay가 배경을 같은 빠르기로 흘릴 때 쓴다.
    public float CenterY => centerY;
    public float LaneOffset => laneOffset;

    // 레인의 y 위치. 상단은 가운데보다 위(+), 하단은 아래(-).
    public float LaneY(Lane lane)
    {
        return centerY + (lane == Lane.Upper ? laneOffset : -laneOffset);
    }

    public Color LaneColor(Lane lane)
    {
        return lane == Lane.Upper ? UpperColor : LowerColor;
    }

    void Awake()
    {
        // Inspector에서 안 넣었으면 씬에서 찾는다.
        if (music == null) music = FindAnyObjectByType<MusicPlayer>();
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (composer == null) composer = FindAnyObjectByType<Composer>();
    }

    void OnEnable()
    {
        if (music == null || judge == null || composer == null)
        {
            Debug.LogError("[NoteDisplay] MusicPlayer, Judge, Composer를 씬에서 찾지 못했습니다.");
            enabled = false;
            return;
        }
        judge.OnNoteJudged += HandleNoteJudged;   // 판정 (노트·상황 포함 버전)
        judge.OnBarrageHit += HandleBarrageHit;   // 연타 한 번 칠 때마다
    }

    void OnDisable()
    {
        if (judge != null)
        {
            judge.OnNoteJudged -= HandleNoteJudged;
            judge.OnBarrageHit -= HandleBarrageHit;
        }
    }

    void Start()
    {
        if (boss == null) boss = FindAnyObjectByType<BossDisplay>();   // 없어도 된다(보스 던지기만 빠짐)
        ResetForSong();   // GameFlow 없이 테스트할 때도 바로 노트가 나오게 시작할 때 한 번 준비한다.
    }

    /// <summary>화면의 노트를 모두 지우고 Composer의 지금 채보를 다시 읽는다. 새 곡을 시작하기 전에 부른다.</summary>
    public void ResetForSong()
    {
        Init();   // 판정선·레인은 처음 한 번만 만든다.
        foreach (NoteView v in active)
            if (v.root != null) Destroy(v.root.gameObject);   // 이전 곡의 노트를 화면에서 지운다.
        active.Clear();
        views.Clear();
        lookOf.Clear();

        all.Clear();
        // 두 레인의 노트를 한 목록으로 합쳐 시간순 정렬. 화면에 올릴 때는 레인 구분 없이 시간 순서대로 올리면 된다.
        all.AddRange(composer.GetNotes(Lane.Upper));
        all.AddRange(composer.GetNotes(Lane.Lower));
        all.Sort((a, b) => a.time.CompareTo(b.time));
        next = 0;   // 처음 노트부터 다시 올린다.
    }

    // 처음 한 번만: 부모 오브젝트, 카메라 설정, 판정선·레인 가이드
    void Init()
    {
        if (initialized) return;
        initialized = true;
        root = new GameObject("NoteDisplayRoot").transform;
        if (setupCamera) SetupCamera();
        BuildStage();
    }

    void Update()
    {
        if (!music.IsStarted) return;

        double now = music.SongTime;
        float viewRight = ViewRight();   // 화면 오른쪽 끝의 x
        // 노트가 화면 오른쪽 끝 + 1칸에서 판정선까지 오는 데 걸리는 시간(초) = 거리 ÷ 속도.
        float ahead = (viewRight + 1f - judgeX) / scrollSpeed;   // 몇 초 앞의 노트까지 화면에 올릴지

        // 도착까지 ahead초 미만 남은 노트를 차례로 화면에 올린다(미리 다 만들면 수백 개가 쌓여서 낭비).
        while (next < all.Count && all[next].time - now < ahead)
        {
            Spawn(all[next]);
            next++;
        }

        // 위치 갱신. 뒤에서부터 도는 이유: 반복 중에 Release로 목록에서 지워도 아직 볼 번호가 밀리지 않는다.
        for (int i = active.Count - 1; i >= 0; i--)
        {
            NoteView v = active[i];
            float endX = Place(v, now);   // 이번 프레임 위치에 놓고, 꼬리 x를 받는다.
            // 꼬리까지 화면 왼쪽 밖으로 나갔으면 지운다. 누르고 있는 롱노트는 지우지 않는다.
            if (endX < -viewRight - 2f && !v.held) Release(v);
        }
    }

    // 곡 시각 → 화면 x. 위치 계산 공식을 한 곳에 모았다.
    float X(double time, double now)
    {
        return judgeX + (float)((time - now) * scrollSpeed);
    }

    // 노트를 지금 시각에 맞는 위치에 놓는다. 끝(꼬리)의 x를 돌려준다.
    float Place(NoteView v, double now)
    {
        NoteData d = v.data;
        float y = LaneY(d.lane);
        bool hasLength = d.type != NoteType.Tap && d.duration > 0;
        float headX = X(d.time, now);                                     // 머리 위치
        float endX = hasLength ? X(d.time + d.duration, now) : headX;     // 꼬리 위치 (단노트는 머리와 같음)

        // 연타가 진행 중인지: 연타이고, 아직 판정 안 났고, 놓치지 않았고, 시작 시각이 지났다.
        bool barrageLive = d.type == NoteType.Barrage && !v.done && !v.missed && now >= d.time;
        // 롱노트를 누르는 중이거나 연타 진행 중이면 머리가 판정선보다 왼쪽으로 못 가게 붙잡는다.
        // → 머리는 판정선에 멈춰 있고 꼬리만 다가오면서 몸통이 줄어드는 모습이 된다.
        if (v.held || barrageLive) headX = Mathf.Max(judgeX, headX);   // 판정선에 붙인다
        if (hasLength) endX = Mathf.Max(endX, headX);                  // 꼬리가 머리보다 왼쪽으로 가지 않게

        // 연타 반응: pulse를 초당 6씩 줄인다(약 0.17초 만에 1 → 0). Max(0, ...)로 음수 방지.
        v.pulse = Mathf.Max(0f, v.pulse - Time.deltaTime * 6f);
        // 머리 크기: 기본 배율(원 연타 1.15배, 적 연타 1.6배)에 pulse만큼 더 커진다.
        float headScale = v.baseScale * (1f + v.pulse * v.pulseAmount);
        float yHead = y, tilt = 0f;
        if (v.enemy != null) MoveEnemy(v, now, headX, ref yHead, ref tilt, v.held || barrageLive);
        float shakeX = 0f;
        if (v.held && v.enemy != null && d.duration > 0 && holdShake > 0f)
        {
            // 롱노트 버티기: 누른 시간의 비율(0 → 1)의 제곱만큼 흔들림이 커진다 → 처음엔 살짝, 끝에 가까울수록 격하게.
            // 매 프레임 무작위로 위치·기울기를 흔들어서 덜덜 떠는 것처럼 보이게 한다. 끝나면 StudentLauncher가 날린다.
            float k = Mathf.Clamp01((float)((now - d.time) / d.duration));
            float amp = Mathf.Lerp(0.02f, 0.2f, k * k) * holdShake;
            shakeX = Random.Range(-amp, amp);
            yHead += Random.Range(-amp, amp) * 0.6f;
            tilt += Random.Range(-1f, 1f) * amp * 60f;
        }
        v.head.position = new Vector3(headX + shakeX, yHead, 0f);
        v.head.localRotation = Quaternion.Euler(0f, 0f, tilt);
        v.head.localScale = Vector3.one * headScale;
        if (v.marker != null) v.marker.transform.position = new Vector3(headX, y, 0f);   // 타이밍 표시는 늘 레인 선 위

        if (v.body != null)   // 롱노트·연타
        {
            float len = endX - headX;                                   // 몸통 길이 = 꼬리 - 머리
            Vector3 mid = new Vector3((headX + endX) * 0.5f, y, 0f);    // 몸통 가운데 (스프라이트 중심점이 가운데라서)
            v.body.transform.position = mid;
            if (d.type == NoteType.Barrage)
            {
                // 연타 몸통은 Tiled 모드라서 크기를 size로 바꾼다. 늘려도 줄무늬가 늘어나지 않고 반복해서 깔린다.
                v.body.size = new Vector2(Mathf.Max(0.01f, len), 0.95f);   // 0.01: 길이 0이 되면 경고가 날 수 있어서 최소값
            }
            else
            {
                // 롱노트 몸통은 1×1 사각형을 localScale로 늘린다. 높이 0.42 = 머리보다 얇게.
                v.body.transform.localScale = new Vector3(Mathf.Max(0.01f, len), 0.42f, 1f);
            }
            v.tail.position = new Vector3(endX, y, 0f);
        }
        return endX;
    }

    // 땅 높이: 아래 레인 적이 서 있는 높이를 땅으로 삼는다.
    float GroundY => LaneY(Lane.Lower);

    // 적 노트 한 명의 높이·기울기·자세·그림자·흙먼지를 정한다. y와 tilt는 ref로 받아서 고쳐 돌려준다.
    // ref: 함수 안에서 바꾼 값이 부른 쪽 변수에 그대로 반영된다(값을 두 개 돌려주려고 썼다).
    void MoveEnemy(NoteView v, double now, float x, ref float y, ref float tilt, bool pinned)
    {
        NoteData d = v.data;
        float laneY = LaneY(d.lane);
        float remaining = (float)(d.time - now);    // 판정까지 남은 시간(초). 지나면 음수.
        bool upper = d.lane == Lane.Upper;
        bool visible = true;
        float step = Time.time * enemyMoveSpeed + v.phase;   // 발걸음 박자
        bool onGround = true;                                // 땅에 붙어 달리는 중인지 (흙먼지용)
        Sprite pose = null;                                  // 이번 프레임 자세 (null이면 그대로)

        if (pinned)
        {
            // 롱노트·연타로 판정선에 붙잡힌 적: 자기 레인 높이에 고정 (자세는 판정 때 Groggy로 바뀌어 있다)
            y = laneY;
            onGround = !upper;
        }
        else if (boss != null && boss.IsActive)
        {
            // 보스전: 보스가 던진다. x가 보스 위치에서 판정선까지 얼마나 왔는지를 진행률 s(0 → 1)로 쓴다.
            //   x는 지금처럼 곡 시간으로 정해지므로 타이밍은 그대로이고, 높이만 포물선으로 바꾼다.
            Vector3 b = boss.HitPoint;
            float s01 = (b.x - x) / Mathf.Max(0.01f, b.x - judgeX);
            if (s01 < 0f) visible = false;   // 아직 보스 뒤(오른쪽)에 있으면 숨긴다 → 보스 손에서 튀어나오는 것처럼
            float sClamped = Mathf.Max(0f, s01);
            if (upper)
            {
                // 높은 포물선: 직선(보스 높이 → 위 레인)에 4·s·(1-s) 모양의 볼록함을 더한다. s=0, 1에서 0, s=0.5에서 최대.
                y = Mathf.Lerp(b.y, laneY, sClamped) + bossThrowArc * 4f * sClamped * (1f - sClamped);
                if (s01 > 1f) y = Mathf.Max(GroundY, laneY - (s01 - 1f) * 6f);   // 놓쳐서 지나가면 땅으로 떨어진다
                tilt = -sClamped * 360f;   // 날아오며 한 바퀴 돈다
                onGround = false;
            }
            else
            {
                // 아래 레인: 진행률 절반까지는 낮은 포물선으로 땅에 떨어지고, 그다음은 땅을 굴러온다.
                if (sClamped < 0.5f)
                {
                    float u = sClamped / 0.5f;   // 떨어지는 구간 안의 진행률 0 → 1
                    y = Mathf.Lerp(b.y, laneY, u) + 0.8f * 4f * u * (1f - u);
                    onGround = false;
                }
                else
                {
                    y = laneY;
                    if (!v.landed) { v.landed = true; StartCoroutine(Dust(new Vector3(x, FeetY(v), 0f), 1.4f)); }   // 착지 흙먼지
                }
                // 굴러오기: 회전 각도를 이동 거리에 비례시킨다 → 미끄러지지 않고 실제로 구르는 것처럼 보인다.
                tilt = (b.x - x) * 70f;
            }
            pose = v.look.hit;   // 던져진 사람은 맞은 자세
        }
        else if (upper)
        {
            // 위 레인: 땅을 달려오다가 판정 jumpLead초 전에 뛰어오른다.
            if (remaining > jumpLead)
            {
                y = GroundY;
                pose = v.look.stand;
            }
            else
            {
                // 점프 진행률 s: 뛰는 순간 0, 판정 순간 1. 놓치면 1을 넘어 계속 떨어진다.
                float s01 = 1f - remaining / Mathf.Max(0.01f, jumpLead);
                // 포물선(= 중력이 일정한 낙하): 직선(땅 → 위 레인) + 볼록함 4·arc·s·(1-s).
                //   s=1에서 기울기 = (높이 차) - 4·arc 이므로, arc가 높이 차의 1/4보다 크면 판정 순간 이미 내려오는 중 → 내리꽂으며 덤비는 느낌
                y = Mathf.Lerp(GroundY, laneY, s01) + jumpArc * 4f * s01 * (1f - s01);
                y = Mathf.Max(GroundY, y);   // 땅 밑으로는 안 내려간다
                onGround = false;
                pose = v.look.jump != null ? v.look.jump : v.look.stand;
                if (!v.jumped) { v.jumped = true; StartCoroutine(Dust(new Vector3(x, FeetY(v), 0f), 1.2f)); }   // 도약 흙먼지
            }
        }
        else
        {
            // 아래 레인: 땅을 달려오다가 도착 직전 낮은 공격 자세
            y = GroundY;
            pose = remaining > lowAttackLead || v.look.lowAttack == null ? v.look.stand : v.look.lowAttack;
        }

        // 땅을 달리는 중이면 발걸음마다 살짝 들썩이고 좌우로 기울며, 일정 간격으로 흙먼지를 남긴다.
        if (onGround && !pinned && !(boss != null && boss.IsActive) && !v.missed)
        {
            y += Mathf.Abs(Mathf.Sin(step)) * 0.08f;
            tilt = Mathf.Sin(step) * 5f;
            if (Time.time >= v.nextDust && visible)
            {
                v.nextDust = Time.time + Mathf.PI / Mathf.Max(0.1f, enemyMoveSpeed);   // 발걸음 한 번(sin 반주기)마다
                StartCoroutine(Dust(new Vector3(x + 0.5f, FeetY(v), 0f), 0.6f));       // 달려가는 반대쪽(오른쪽) 뒤에
            }
        }

        // 놓친 적은 Fade에서 도발 자세로 바꿔 두었으니 덮어쓰지 않는다. 붙잡힌 적도 Groggy 유지.
        if (!v.missed && !pinned && pose != null) v.enemy.sprite = pose;

        v.enemy.enabled = visible;
        if (v.marker != null) v.marker.enabled = !v.missed;   // 타이밍 표시는 판정 전까지

        // 그림자: 땅(발 높이)에 납작한 타원. 공중에 높이 있을수록 작고 옅게 → 떠 있는 게 아니라 뛰어오른 것으로 보인다.
        if (v.shadow != null)
        {
            float height = Mathf.Max(0f, y - GroundY);       // 땅에서 떨어진 높이
            float k = 1f / (1f + height * 0.35f);             // 높을수록 1보다 작아진다
            v.shadow.enabled = visible;
            v.shadow.transform.position = new Vector3(x, FeetY(v), 0f);
            v.shadow.transform.localScale = new Vector3(enemyHeight * 0.45f * v.baseScale * k, 0.22f * v.baseScale * k, 1f);
            v.shadow.color = new Color(0f, 0f, 0f, (v.missed ? 0.15f : 0.4f) * k);
        }
    }

    // 땅 위에 서 있을 때 이 적의 발 높이. 그림의 발은 그림 아래쪽 끝 근처(높이의 약 48% 아래)에 있다.
    float FeetY(NoteView v)
    {
        return GroundY + (enemyYOffset - enemyHeight * 0.48f) * v.baseScale;
    }

    // 흙먼지: 회색 원이 커지면서 위로 살짝 떠오르고 사라진다.
    IEnumerator Dust(Vector3 pos, float size)
    {
        var sr = ProcSprites.Make("Dust", root, ProcSprites.Circle, new Color(0.75f, 0.7f, 0.62f, 0.5f), 2);
        sr.transform.position = pos;
        const float duration = 0.35f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            sr.transform.position = pos + new Vector3(k * 0.3f, k * 0.25f, 0f);                    // 뒤쪽 위로 떠오름
            sr.transform.localScale = new Vector3(0.6f, 0.4f, 1f) * size * Mathf.Lerp(0.5f, 1.2f, k);   // 납작한 타원이 커짐
            sr.color = new Color(0.75f, 0.7f, 0.62f, 0.5f * (1f - k));
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    // 노트 하나를 화면에 만든다.
    void Spawn(NoteData n)
    {
        bool upper = n.lane == Lane.Upper;
        Color col = n.type == NoteType.Barrage ? BarrageColor : LaneColor(n.lane);   // 연타는 노랑, 나머지는 레인 색

        var view = new NoteView { data = n };
        view.root = new GameObject("Note").transform;   // 이 노트의 부모 오브젝트
        view.root.SetParent(root, false);

        GameObject prefab = upper ? upperNotePrefab : lowerNotePrefab;
        if (n.type == NoteType.Barrage) view.baseScale = 1.15f;   // 원 연타 노트는 조금 크게

        if (EnemyMode)
        {
            // 적 노트: 머리 자리에 빈 오브젝트를 두고 그 자식으로 적 그림을 붙인다.
            // 머리(head)는 위치·크기만 담당하고, 그림 자식은 크기 맞춤·위치 보정·좌우 반전만 담당한다(역할 분리).
            view.look = PickLook();
            lookOf[n] = view.look;
            view.phase = Random.Range(0f, Mathf.PI * 2f);   // 움직임 시작 위치를 적마다 다르게 (0 ~ 2π = 한 주기)
            view.head = new GameObject("Enemy").transform;
            view.head.SetParent(view.root, false);
            view.enemy = ProcSprites.Make("Sprite", view.head, view.look.stand, Color.white, 3);
            view.enemy.flipX = true;   // 에셋 캐릭터는 모두 오른쪽을 본다 → 뒤집어서 플레이어(왼쪽)를 보게
            float h = view.look.stand.bounds.size.y;
            view.enemy.transform.localScale = Vector3.one * (h > 0f ? enemyHeight / h : 1f);
            view.enemy.transform.localPosition = new Vector3(0f, enemyYOffset, 0f);   // 몸통 가운데를 레인 선에 맞춤
            if (n.type == NoteType.Barrage) view.baseScale = barrageEnemyScale;      // 연타 = 덩치 큰 적
            view.pulseAmount = 0.2f;   // 큰 그림은 조금만 커져도 티가 나므로 원보다 작게

            // 타이밍 표시: 레인 선 위에 작은 레인 색 원. 적은 땅을 달리다가 뛰어오르므로, "언제·어느 키"는 이 원으로 읽는다.
            // root의 자식이라 머리(적)와 따로 움직인다. 위치는 아래 Place의 머리 x와 레인 높이로 정해진다.
            Color mc = n.type == NoteType.Barrage ? BarrageColor : LaneColor(n.lane);
            view.marker = ProcSprites.Make("Marker", view.root, ProcSprites.Circle, new Color(mc.r, mc.g, mc.b, 0.85f), 5);
            view.marker.transform.localScale = Vector3.one * 0.45f;
            // 그림자: 적보다 뒤(순서 0)
            view.shadow = ProcSprites.Make("Shadow", view.root, ProcSprites.Circle, new Color(0f, 0f, 0f, 0.4f), 0);
        }
        else if (prefab != null && n.type != NoteType.Barrage)
        {
            view.head = Instantiate(prefab, view.root).transform;   // 프리팹이 있으면 그걸로 머리를 만든다.
        }
        else
        {
            // 기본 머리: 원. sortingOrder 3 (몸통 1, 꼬리 2보다 위)
            view.head = ProcSprites.Make("Head", view.root, ProcSprites.Circle, col, 3).transform;
            if (n.type != NoteType.Tap)
            {
                // 롱노트·연타 머리에는 흰 테두리를 둘러서 단노트와 구분한다. 머리의 자식이라 머리와 같이 움직이고 커진다.
                var ring = ProcSprites.Make("HeadRing", view.head, ProcSprites.Ring, Color.white, 4);
                ring.transform.localScale = Vector3.one * 1.05f;
            }
        }

        if (n.type == NoteType.Long && n.duration > 0)
        {
            // 롱노트: 반투명 사각형 몸통 + 작은 원 꼬리
            view.body = ProcSprites.Make("Body", view.root, ProcSprites.Square, new Color(col.r, col.g, col.b, 0.55f), 1);
            var tail = ProcSprites.Make("Tail", view.root, ProcSprites.Circle, col, 2);
            tail.transform.localScale = Vector3.one * 0.55f;
            view.tail = tail.transform;
        }
        else if (n.type == NoteType.Barrage && n.duration > 0)
        {
            // 연타: 줄무늬 몸통(타일 반복) + 얇은 세로 막대 꼬리
            view.body = ProcSprites.Make("Body", view.root, ProcSprites.Stripes, new Color(col.r, col.g, col.b, 0.75f), 1);
            view.body.drawMode = SpriteDrawMode.Tiled;   // 크기를 늘리면 그림을 늘이지 않고 반복해서 깐다.
            view.body.size = new Vector2(1f, 0.95f);
            var tail = ProcSprites.Make("Tail", view.root, ProcSprites.Square, col, 2);
            tail.transform.localScale = new Vector3(0.12f, 0.95f, 1f);
            view.tail = tail.transform;
        }

        active.Add(view);          // 위치 갱신 목록에 추가
        views[n] = view;           // 판정이 왔을 때 찾을 수 있게 표에 등록
        Place(view, music.SongTime);   // 만든 프레임에 바로 제자리에 놓는다(한 프레임 동안 (0,0)에 보이는 것 방지).
    }

    // 노트를 화면에서 없앤다.
    void Release(NoteView v)
    {
        active.Remove(v);
        views.Remove(v.data);
        if (v.root != null) Destroy(v.root.gameObject);   // 부모를 지우면 머리·몸통·꼬리가 다 지워진다.
    }

    // 놓친 노트를 반투명하게 만든다. 지우지 않고 계속 흘러가게 둬서 놓쳤다는 걸 눈으로 보여준다.
    void Fade(NoteView v)
    {
        v.missed = true;
        SetEnemySprite(v, v.look != null ? v.look.annoying : null);   // 적은 도발 자세로 바꾼 뒤 흐리게
        foreach (var r in v.root.GetComponentsInChildren<SpriteRenderer>())   // 머리·몸통·꼬리·테두리 전부
        {
            Color c = r.color;   // Color는 struct라서 꺼내서 고친 뒤 다시 넣어야 한다.
            c.a = r == v.enemy ? 0.6f : 0.3f;   // 적 그림은 도발하는 모습이 보이도록 덜 흐리게
            r.color = c;
        }
    }

    // 적 그림을 바꾼다. 적 노트가 아니거나 그 그림이 비어 있으면 그대로 둔다.
    static void SetEnemySprite(NoteView v, Sprite s)
    {
        if (v.enemy != null && s != null) v.enemy.sprite = s;
    }

    // Stand 그림이 있는 적 중에서 무작위 한 명
    EnemyLook PickLook()
    {
        var usable = new List<EnemyLook>();
        foreach (EnemyLook e in enemies) if (e != null && e.stand != null) usable.Add(e);
        return usable[Random.Range(0, usable.Count)];   // EnemyMode일 때만 불리므로 최소 1명은 있다.
    }

    /// <summary>이 노트에 뽑힌 적의 Hit 그림. 적 노트가 아니면 null.</summary>
    public Sprite HitSpriteOf(NoteData note)
    {
        EnemyLook look;
        return lookOf.TryGetValue(note, out look) && look != null ? look.hit : null;
    }

    /// <summary>적 목록에서 무작위 Hit 그림 (연타 때 튀어 나가는 작은 적용). 목록이 비면 null.</summary>
    public Sprite RandomHitSprite()
    {
        if (!EnemyMode) return null;
        EnemyLook look = PickLook();
        return look.hit != null ? look.hit : look.stand;
    }

#if UNITY_EDITOR
    // Inspector의 ⋮ 메뉴에 "적 그림 자동 채우기"를 추가한다. Play를 안 해도 실행된다.
    // 프로젝트에서 Characters 폴더 안의 Stand 그림을 모두 찾아, 같은 폴더의 Attack0(점프)·Attack2(낮은 공격)·Hit·Groggy·Annoying과 묶어서 목록을 만든다.
    // #if UNITY_EDITOR: 에디터 전용 기능(AssetDatabase)이라 빌드할 때는 빠지게 한다.
    [ContextMenu("적 그림 자동 채우기 (Battle Zone 에셋)")]
    public void AutoFillEnemies()
    {
        UnityEditor.Undo.RecordObject(this, "적 그림 자동 채우기");   // Ctrl+Z로 되돌릴 수 있게
        enemies.Clear();
        // FindAssets("검색어 t:타입"): 프로젝트에서 이름에 Stand가 들어간 Sprite를 찾아 GUID(고유 번호) 목록을 준다.
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("Stand t:Sprite"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);        // 예) Assets/.../Characters/Amy/Stand.png
            if (!path.Contains("/Characters/") || !path.EndsWith("/Stand.png")) continue;
            string dir = path.Substring(0, path.LastIndexOf('/'));                // 폴더 경로
            string name = dir.Substring(dir.LastIndexOf('/') + 1);                // 폴더 이름 = 캐릭터 이름
            if (name == playerCharacter) continue;                                // 플레이어 캐릭터는 적에서 뺀다

            enemies.Add(new EnemyLook
            {
                name = name,
                stand = LoadSprite(dir + "/Stand.png"),
                jump = LoadSprite(dir + "/Attack0.png"),       // 캐릭터마다 다르니 어색하면 목록에서 Attack1로 바꿔도 된다
                lowAttack = LoadSprite(dir + "/Attack2.png"),
                hit = LoadSprite(dir + "/Hit.png"),
                groggy = LoadSprite(dir + "/Groggy.png"),
                annoying = LoadSprite(dir + "/Annoying.png")
            });
        }
        Debug.Log($"[NoteDisplay] 적 {enemies.Count}명을 채웠습니다. (플레이어 '{playerCharacter}' 제외)");
    }

    static Sprite LoadSprite(string path)
    {
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);   // 없으면 null
    }
#endif

    // Judge가 판정을 보냈을 때. 어떤 상황(kind)이냐에 따라 연출을 다르게 한다.
    void HandleNoteJudged(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        NoteView v;
        // TryGetValue: 열쇠가 있으면 true와 함께 값을 out으로 준다. 없으면 false. (아직 화면에 안 올라온 노트 등)
        if (!views.TryGetValue(note, out v)) return;

        Vector3 pos = new Vector3(judgeX, LaneY(lane), 0f);   // 이펙트는 항상 판정 위치에서 터진다.
        Color burst = result == Judgment.Perfect ? PerfectColor : LaneColor(lane);

        // 성공할 때마다 아주 작은 화면 흔들림(타격감). Perfect가 조금 더 세다. 롱노트 완료·연타 성공은 더 크게.
        if (result != Judgment.Miss)
        {
            bool bigHit = kind == JudgeKind.HoldComplete || kind == JudgeKind.Barrage;
            CameraShake.Shake(bigHit ? 0.12f : (result == Judgment.Perfect ? 0.06f : 0.04f), bigHit ? 0.15f : 0.08f);
        }

        switch (kind)
        {
            case JudgeKind.Hit:
                if (result == Judgment.Miss)
                {
                    Fade(v);   // 놓침 → 흐리게 (적은 도발 자세로)
                }
                else if (note.type == NoteType.Long && note.duration > 0)
                {
                    v.held = true;   // 롱노트 시작 성공 → 지우지 않고 "누르는 중"으로. 머리가 판정선에 붙는다.
                    SetEnemySprite(v, v.look != null ? v.look.groggy : null);   // 적은 맞으면서 버티는 자세
                    StartCoroutine(BurstRoutine(pos, burst, false));
                }
                else
                {
                    StartCoroutine(BurstRoutine(pos, burst, false));   // 단노트 성공 → 터뜨리고 지운다.
                    Release(v);
                }
                break;

            case JudgeKind.HoldBreak:
                v.held = false;   // 롱노트 중간에 뗌 → 붙잡기를 풀어서 다시 흘러가게 하고 흐리게
                Fade(v);
                break;

            case JudgeKind.HoldComplete:
                StartCoroutine(BurstRoutine(pos, PerfectColor, true));   // 롱노트 완료 → 큰 이펙트
                Release(v);
                break;

            case JudgeKind.Barrage:
                v.done = true;   // 연타 판정 끝 → 판정선에 붙잡기 해제
                if (result == Judgment.Miss)
                {
                    Fade(v);
                }
                else
                {
                    StartCoroutine(BurstRoutine(pos, BarrageColor, true));
                    Release(v);
                }
                break;
        }
    }

    // 연타 구간에서 한 번 칠 때마다: 머리를 통 튀게 하고 작은 이펙트
    void HandleBarrageHit(NoteData note, Lane lane, int hits, int need)
    {
        NoteView v;
        if (!views.TryGetValue(note, out v)) return;
        v.pulse = 1f;   // Place에서 점점 줄어든다.
        SetEnemySprite(v, v.look != null ? v.look.groggy : null);   // 처음 맞는 순간부터 움찔하는 자세
        StartCoroutine(BurstRoutine(new Vector3(judgeX, LaneY(lane), 0f), BarrageColor, false, 0.6f));
    }

    // 터지는 이펙트: 퍼지는 고리 + 사방으로 튀는 작은 사각형(불꽃).
    // 코루틴: yield return null에서 멈췄다가 다음 프레임에 이어서 실행된다.
    // float size = 1f: 기본값이 있는 선택 매개변수. 부를 때 생략하면 1.
    IEnumerator BurstRoutine(Vector3 pos, Color color, bool big, float size = 1f)
    {
        // 이펙트 프리팹이 있으면 그것을 띄우고 끝낸다. 큰 이펙트 칸이 비어 있으면 일반 이펙트를 쓴다.
        GameObject prefab = big && bigHitEffectPrefab != null ? bigHitEffectPrefab : hitEffectPrefab;
        if (prefab != null)
        {
            GameObject fx = Instantiate(prefab, pos, Quaternion.identity, root);   // (프리팹, 위치, 회전, 부모)
            fx.transform.localScale = fx.transform.localScale * size;             // 연타 한 번은 0.6배
            Destroy(fx, effectLifetime);   // 파티클이 끝날 즈음 지운다. 파티클 길이에 맞춰 Effect Lifetime을 조절.
            yield break;                   // 코루틴을 여기서 끝낸다(아래 도형 이펙트는 만들지 않음).
        }

        var go = new GameObject("Burst");
        go.transform.SetParent(root, false);
        go.transform.position = pos;
        var ring = ProcSprites.Make("Ring", go.transform, ProcSprites.Ring, color, 5);

        int sparkCount = big ? 12 : 6;                           // 큰 이펙트는 불꽃 12개, 작은 건 6개
        var sparks = new Transform[sparkCount];
        var dirs = new Vector3[sparkCount];                      // 불꽃마다 날아갈 방향 × 속도
        var sparkRenders = new SpriteRenderer[sparkCount];
        for (int i = 0; i < sparkCount; i++)
        {
            // 원을 sparkCount등분한 각도(라디안)에 약간의 무작위를 섞는다. 2π = 360도
            float ang = (i + Random.Range(-0.3f, 0.3f)) / sparkCount * Mathf.PI * 2f;
            // (cos, sin) = 그 각도 방향의 길이 1짜리 벡터. 여기에 무작위 속도(3.5~6.5)를 곱한다.
            dirs[i] = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * Random.Range(3.5f, 6.5f) * (big ? 1.4f : 1f) * size;
            sparkRenders[i] = ProcSprites.Make("Spark", go.transform, ProcSprites.Square, color, 5);
            sparks[i] = sparkRenders[i].transform;
            sparks[i].localScale = Vector3.one * 0.14f * size;
            // 날아가는 방향 + 45도로 돌려서 마름모 모양으로 보이게. Rad2Deg = 라디안 → 도
            sparks[i].localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg + 45f);
        }

        float duration = big ? 0.4f : 0.28f;
        float from = 1.3f * size, to = (big ? 3.4f : 2.4f) * size;   // 고리 시작·끝 크기
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);   // 진행률 0 → 1
            ring.transform.localScale = Vector3.one * Mathf.Lerp(from, to, k);
            ring.color = new Color(color.r, color.g, color.b, 1f - k);   // 점점 투명
            for (int i = 0; i < sparkCount; i++)
            {
                // 이동 거리 = 방향 × 경과 시간 × (1 - k/2). 끝으로 갈수록 느려져서 감속하는 느낌이 난다.
                sparks[i].localPosition = dirs[i] * (k * duration) * (1f - k * 0.5f);
                sparkRenders[i].color = new Color(color.r, color.g, color.b, 1f - k);
            }
            yield return null;
        }
        Destroy(go);
    }

    // 화면 오른쪽 끝의 x. Orthographic 카메라의 orthographicSize(세로 절반) × 가로세로 비율 = 가로 절반 폭.
    float ViewRight()
    {
        Camera c = Camera.main;
        return c != null ? c.orthographicSize * c.aspect : 9f;   // 카메라가 없으면 대략 9로 가정
    }

    // 2D 게임용으로 메인 카메라 설정을 맞춘다.
    void SetupCamera()
    {
        Camera c = Camera.main;
        if (c == null)
        {
            Debug.LogWarning("[NoteDisplay] Main Camera를 찾지 못했습니다. 카메라를 직접 Orthographic으로 설정하세요.");
            return;
        }
        c.orthographic = true;           // 원근감 없는 2D 투영
        c.orthographicSize = 5f;         // 화면 세로 절반 = 5칸
        c.transform.position = new Vector3(0f, 0f, -10f);   // 스프라이트(z = 0)보다 앞에서 바라본다.
        c.transform.rotation = Quaternion.identity;
        c.clearFlags = CameraClearFlags.SolidColor;         // 배경을 단색으로
        c.backgroundColor = new Color(0.07f, 0.07f, 0.1f);
    }

    // 판정선, 레인 가이드, 판정 위치 표시를 만든다.
    void BuildStage()
    {
        float height = laneOffset * 2f + 1.6f;   // 판정선 길이: 두 레인을 다 덮고 위아래로 0.8씩 더
        var line = ProcSprites.Make("JudgeLine", root, ProcSprites.Square, new Color(1f, 1f, 1f, 0.5f), 0);
        line.transform.position = new Vector3(judgeX, centerY, 0f);
        line.transform.localScale = new Vector3(0.05f, height, 1f);   // 폭 0.05의 세로 막대

        foreach (Lane lane in new[] { Lane.Upper, Lane.Lower })
        {
            float y = LaneY(lane);
            Color c = LaneColor(lane);
            // 레인 가이드: 가로 60칸의 아주 얇은 선 (화면보다 길게 해서 끝이 안 보이게)
            var guide = ProcSprites.Make("LaneGuide", root, ProcSprites.Square, new Color(1f, 1f, 1f, 0.15f), 0);
            guide.transform.position = new Vector3(0f, y, 0f);
            guide.transform.localScale = new Vector3(60f, 0.03f, 1f);

            // 판정 위치 고리: 노트가 이 고리에 겹칠 때 치면 된다.
            var target = ProcSprites.Make("Target", root, ProcSprites.Ring, new Color(c.r, c.g, c.b, 0.9f), 1);
            target.transform.position = new Vector3(judgeX, y, 0f);
            target.transform.localScale = Vector3.one * 1.4f;
        }
    }
}
