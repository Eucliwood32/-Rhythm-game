// ============================================================================
// StudentLauncher.cs : 노트를 맞히면 적(잡몹)이 물리로 날아가는 연출. 보스전에서는 적을 보스에게 던진다.
// ----------------------------------------------------------------------------
// 6요소 중 담당: Display (화면 연출). 게임물리 수업과 직접 연결되는 부분이다.
//
// 일반 스테이지:
//   - 위쪽(A, 어퍼컷): 거의 위로 솟구침 / 아래쪽(B, 킥): 오른쪽으로 날아감
//   - Perfect는 더 세게. 롱노트는 끝까지 눌렀을 때, 연타는 칠 때마다 작은 적 + 끝나면 큰 적
// 보스전 (BossDisplay.CanBeHit가 true일 때):
//   - 적을 화면 밖으로 날리지 않고, 포물선으로 보스에게 던진다. 도착하면 보스가 움찔하고 적은 튕겨 나간다.
//
// 물리 핵심 (Rigidbody2D):
//   - 날리는 순간 AddForce(..., ForceMode2D.Impulse)로 "충격량"을 한 번 준다.
//     충격량에 질량을 곱해 넣으므로, 질량과 상관없이 정한 속도로 튀어 나간다.
//   - 그 뒤로는 중력이 매 물리 프레임 아래로 당긴다 → 포물선 궤적.
//   - 보스에게 던질 때는 "T초 뒤에 목표 지점에 도착하는 초기 속도"를 등가속도 운동 공식으로 역산한다(ThrowAtBoss 참고).
//   주의: 판정에는 물리를 전혀 쓰지 않는다. 적은 순수한 연출이다.
//
// 적 그림 (위에서부터 우선):
//   1) Student Prefab 칸에 프리팹이 있으면 그것
//   2) NoteDisplay가 적 노트 모드면 "다가오던 그 적"의 Hit 그림 (연타 중 작은 적은 무작위 캐릭터)
//   3) Enemy Sprites 칸에 그림이 있으면 그중 무작위 한 장
//   4) 다 없으면 코드로 그린 임시 학생 도형
//
// 화면 박치기 (대난투 스크린 KO): 일반 스테이지에서 Perfect로 날린 적 중 가끔(Screen Ko Chance),
//   적이 화면 안쪽으로 날아갔다가 카메라 쪽으로 커다랗게 날아와 화면 한가운데에 쾅 부딪히고,
//   화면에 금이 간 채 미끄러져 내려간다. 2D에서 크기(가까워짐)와 위치만으로 3D처럼 보이게 하는 연출.
//
// 타격 섬광: 날리는 순간 맞은 자리에 하얀 원이 0.08초 번쩍인다(보스에게 맞을 때도).
//   캐릭터 그림 자체를 하얗게 만들려면 전용 셰이더가 필요해서, 대신 맞은 자리를 번쩍이게 했다.
//
// 연결: Judge.OnNoteJudged / OnBarrageHit --> 적 날리기,  NoteDisplay --> 판정선·레인 위치
//       BossDisplay --> 보스전인지(CanBeHit), 조준 지점(HitPoint), 맞았을 때 움찔(Flinch)
// ============================================================================

using System.Collections;           // IEnumerator (코루틴)
using System.Collections.Generic;   // Queue
using UnityEngine;

/// <summary>노트를 맞히면 적이 Rigidbody2D 물리로 날아간다. 보스전에서는 적을 포물선으로 보스에게 던진다.</summary>
public class StudentLauncher : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] NoteDisplay noteDisplay;
    [SerializeField] BossDisplay boss;              // 없으면 보스 던지기 없이 항상 날리기만 한다.
    [SerializeField] Referee referee;               // 피버 중인지 보려고. 없어도 된다.
    [Tooltip("피버 타임 중 날아가는 힘 배율")]
    [SerializeField] float feverLaunch = 1.35f;

    [Tooltip("날아갈 적 프리팹. 비워두면 아래 Enemy Sprites, 그것도 비면 임시 도형")]
    [SerializeField] GameObject studentPrefab;

    [Header("적 그림 (프리팹이 없을 때)")]
    [Tooltip("적 그림들. 날릴 때마다 무작위 한 장. 예) 각 캐릭터 폴더의 Hit.png")]
    [SerializeField] Sprite[] enemySprites;
    [Tooltip("그림 한 장 전체의 높이(월드 단위). 512px 그림에서 캐릭터는 약 70%를 차지한다.")]
    [SerializeField] float enemyHeight = 3f;
    [Tooltip("켜면 그림을 좌우 반전. 에셋 캐릭터는 모두 오른쪽을 보고 있어서, 플레이어 쪽(왼쪽)을 보게 하려면 켠다.")]
    [SerializeField] bool flipEnemies = true;
    [Tooltip("그림 순서. 다가오는 적 노트(3)보다 크게 두어 날아가는 적이 위에 보이게 한다.")]
    [SerializeField] int enemyOrder = 4;

    [Header("물리")]
    [Tooltip("어퍼컷(위쪽 레인) 힘")]
    [SerializeField] float uppercutForce = 14f;    // 사실상 "초기 속도(월드 단위/초)". 아래 Launch 참고.
    [Tooltip("킥(아래쪽 레인) 힘")]
    [SerializeField] float kickForce = 15f;
    [Tooltip("Perfect일 때 힘 배율")]
    [SerializeField] float perfectBonus = 1.25f;   // Perfect면 25% 더 세게
    [Tooltip("중력 배율 (클수록 빨리 떨어짐)")]
    [SerializeField] float gravityScale = 1.6f;    // 1이면 Physics2D 기본 중력(-9.81).
    [Tooltip("회전 속도 최대값 (도/초)")]
    [SerializeField] float maxSpin = 900f;
    [Tooltip("학생끼리 부딪히게 할지")]
    [SerializeField] bool studentsCollide = false; // 켜면 Collider를 붙여서 서로 튕긴다. 많아지면 무거워서 기본은 끔.

    [Header("화면 박치기 (대난투 스크린 KO)")]
    [Tooltip("Perfect로 날린 적이 화면에 부딪힐 확률 (0 = 안 함, 0.1 = 10%)")]
    [Range(0f, 1f)] [SerializeField] float screenKoChance = 0.1f;
    [Tooltip("한 번 나온 뒤 다음까지 최소 몇 초 (너무 자주 나오면 노트를 가린다)")]
    [SerializeField] float screenKoCooldown = 6f;
    [Tooltip("화면에 부딪힐 때 크기 배율 (적 원래 크기 기준)")]
    [SerializeField] float screenKoScale = 4.5f;

    [Header("보스전")]
    [Tooltip("던진 적이 보스에게 도착하기까지 걸리는 시간(초). 짧을수록 빠르고 낮게 날아간다.")]
    [SerializeField] float throwTime = 0.45f;

    [Header("정리")]
    [SerializeField] float lifetime = 3f;          // 날아간 적은 3초 뒤 자동 삭제
    [SerializeField] int maxStudents = 40;
    [Tooltip("켜면 연타 구간에서 칠 때마다 작은 적이 하나씩 튀어 나간다. 끄면 연타가 끝났을 때 큰 적 하나만 날아간다.")]
    [SerializeField] bool launchOnBarrageHit = false;         // 화면에 동시에 있는 최대 수. 넘으면 오래된 것부터 지운다.

    // 임시 도형용 교복 색 4가지
    static readonly Color[] Uniforms =
    {
        new Color(0.16f, 0.2f, 0.38f), new Color(0.2f, 0.2f, 0.24f), new Color(0.35f, 0.18f, 0.2f), new Color(0.18f, 0.3f, 0.25f)
    };

    // 살아 있는 적들. Queue(선입선출)라서 개수 초과 때 오래된 것부터 꺼내 지우기 쉽다.
    readonly Queue<GameObject> alive = new Queue<GameObject>();
    float nextScreenKo;   // 다음 화면 박치기가 가능한 시각

    void Awake()
    {
        // Inspector에서 안 넣었으면 씬에서 자동으로 찾는다.
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (noteDisplay == null) noteDisplay = FindAnyObjectByType<NoteDisplay>();
        if (boss == null) boss = FindAnyObjectByType<BossDisplay>();
        if (referee == null) referee = FindAnyObjectByType<Referee>();
    }

    void OnEnable()
    {
        if (judge == null || noteDisplay == null)
        {
            Debug.LogError("[StudentLauncher] Judge와 NoteDisplay를 찾지 못했습니다.");
            enabled = false;
            return;
        }
        judge.OnNoteJudged += HandleNoteJudged;
        judge.OnBarrageHit += HandleBarrageHit;
    }

    void OnDisable()
    {
        if (judge != null)
        {
            judge.OnNoteJudged -= HandleNoteJudged;
            judge.OnBarrageHit -= HandleBarrageHit;
        }
    }

    // 판정이 올 때마다: 성공한 판정이면 상황에 맞는 크기로 적을 날린다.
    void HandleNoteJudged(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        if (result == Judgment.Miss) return;   // 놓치면 아무도 안 날아간다.
        if (noteDisplay.IsBossSpecial(note)) return;   // 보스 패턴 노트(돌진·기공파): 날릴 잡몹이 없다 (보스가 직접 맞음)

        switch (kind)
        {
            // noteDisplay.HitSpriteOf(note): 이 노트로 다가오던 적의 Hit 그림 → 그 적이 그대로 날아가는 것처럼 보인다.
            case JudgeKind.Hit:
                if (note.type == NoteType.Long && note.duration > 0) return;   // 롱노트는 끝까지 눌렀을 때 날린다
                Launch(lane, result == Judgment.Perfect, 1f, noteDisplay.HitSpriteOf(note));     // 단노트: 보통 크기
                break;
            case JudgeKind.HoldComplete:
                Launch(lane, true, 1.3f, noteDisplay.HitSpriteOf(note));                         // 롱노트 완료: 1.3배
                break;
            case JudgeKind.Barrage:
                Launch(lane, result == Judgment.Perfect, 1.7f, noteDisplay.HitSpriteOf(note));   // 연타 마무리: 1.7배 큰 적
                break;
        }
    }

    // 연타 구간에서 한 번 칠 때마다 작은 적(0.55배)
    void HandleBarrageHit(NoteData note, Lane lane, int hits, int need)
    {
        if (!launchOnBarrageHit) return;   // 기본은 끔: 연타 중에는 큰 적이 판정선에서 움찔하기만 한다.
        if (noteDisplay.IsBossSpecial(note)) return;
        Launch(lane, false, 0.55f, noteDisplay.RandomHitSprite());   // 무작위 캐릭터의 작은 적
    }

    /// <summary>적 하나를 날린다(보스전이면 보스에게 던진다). scale은 크기 배율, sprite는 쓸 그림(null이면 자동 선택).</summary>
    public void Launch(Lane lane, bool perfect, float scale, Sprite sprite = null)
    {
        // 판정선 살짝 오른쪽, 해당 레인 높이에서 나온다.
        Vector3 pos = new Vector3(noteDisplay.JudgeX + 0.4f, noteDisplay.LaneY(lane), 0f);
        StartCoroutine(Flash(pos, (perfect ? 2.4f : 1.9f) * Mathf.Sqrt(scale)));   // 타격 섬광. 큰 적일수록 크게(제곱근이라 너무 커지지는 않음)
        GameObject go = MakeEnemy(pos, sprite);
        go.transform.localScale = go.transform.localScale * scale;   // 원래 크기에 배율을 곱한다.

        // Rigidbody2D: 이 오브젝트를 2D 물리 엔진이 움직이게 한다. 없으면 붙인다.
        var rb = go.GetComponent<Rigidbody2D>();
        if (rb == null) rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = gravityScale;
        if (studentsCollide && go.GetComponent<Collider2D>() == null)
            go.AddComponent<CircleCollider2D>().radius = 0.45f;

        if (boss != null && boss.CanBeHit)
        {
            ThrowAtBoss(go, rb, perfect);   // 보스전: 보스에게 던진다.
        }
        else if (perfect && scale <= 1.01f && Time.time >= nextScreenKo && Random.value < screenKoChance)
        {
            // 화면 박치기: 물리 대신 직접 움직인다 (simulated = false → 물리 엔진이 이 물체를 무시)
            nextScreenKo = Time.time + screenKoCooldown;
            rb.simulated = false;
            StartCoroutine(ScreenKO(go));
            return;   // 아래 회전·자동 삭제 예약은 안 함 (코루틴이 끝나면 지운다)
        }
        else
        {
            // 일반: 레인에 따라 방향을 정해 화면 밖으로 날린다.
            //   어퍼컷: x 0.15~0.45, y 1 → 거의 위쪽 / 킥: x 1, y 0.25~0.6 → 오른쪽, 약간 위
            Vector2 dir = lane == Lane.Upper
                ? new Vector2(Random.Range(0.15f, 0.45f), 1f)
                : new Vector2(1f, Random.Range(0.25f, 0.6f));
            dir.Normalize();   // 길이를 1로. 방향만 남기고 세기는 force로 정한다.
            float force = (lane == Lane.Upper ? uppercutForce : kickForce) * (perfect ? perfectBonus : 1f) * Random.Range(0.9f, 1.1f)
                * (referee != null && referee.IsFever ? feverLaunch : 1f);   // 피버 중에는 더 멀리
            // Impulse: 순간적으로 속도를 바꾼다. 속도 변화 = 충격량 ÷ 질량 → 질량을 곱해 넣었으니 속도 변화 = dir × force.
            rb.AddForce(dir * force * rb.mass, ForceMode2D.Impulse);
        }
        rb.angularVelocity = Random.Range(-maxSpin, maxSpin);   // 회전 속도(도/초)

        Destroy(go, lifetime);   // lifetime초 뒤 자동 삭제 예약
        alive.Enqueue(go);
        while (alive.Count > maxStudents)   // 너무 많으면 오래된 것부터 지운다.
        {
            GameObject old = alive.Dequeue();
            if (old != null) Destroy(old);
        }
    }

    // 보스에게 던지기: throwTime초 뒤에 정확히 보스에 도착하는 초기 속도를 계산해서 준다.
    void ThrowAtBoss(GameObject go, Rigidbody2D rb, bool perfect)
    {
        Vector2 start = go.transform.position;
        // 보스 몸 안에서 조금씩 다른 곳을 맞게 무작위로 흩뜨린다.
        Vector2 target = (Vector2)boss.HitPoint + new Vector2(Random.Range(-0.4f, 0.4f), Random.Range(-0.6f, 0.6f));
        float t = Mathf.Max(0.1f, throwTime);   // 0이면 나눗셈이 안 되므로 최소 0.1초

        // 이 물체에 실제로 걸리는 중력 가속도 g (음수, 아래 방향) = 전역 중력 × 이 물체의 중력 배율
        float g = Physics2D.gravity.y * rb.gravityScale;
        // 등가속도 운동: 위치 = 시작 + v0·t + ½·g·t²  →  v0 = (도착 - 시작 - ½·g·t²) / t
        //   x방향은 중력이 없으므로 v0x = Δx / t
        //   y방향은 중력만큼 미리 더 위로 던져야 한다: v0y = (Δy - ½·g·t²) / t   (g가 음수라 -½gt²는 양수)
        Vector2 v0 = new Vector2(
            (target.x - start.x) / t,
            (target.y - start.y - 0.5f * g * t * t) / t);
        rb.AddForce(v0 * rb.mass, ForceMode2D.Impulse);   // 질량을 곱해서 정확히 v0 속도가 되게 한다.

        StartCoroutine(HitBossAfter(rb, t, perfect));
    }

    // t초 뒤(도착 시점)에 보스를 움찔하게 하고, 적은 왼쪽 위로 튕겨 나가게 한다.
    IEnumerator HitBossAfter(Rigidbody2D rb, float t, bool perfect)
    {
        yield return new WaitForSeconds(t);   // 코루틴을 t초 동안 멈춘다. (게임 시간 기준)
        if (rb == null) yield break;          // 그 사이 지워졌으면(개수 초과 등) 끝

        if (boss != null) boss.Flinch(perfect ? 1f : 0.6f);
        StartCoroutine(Flash(rb.transform.position, perfect ? 3f : 2.2f));   // 보스에게 맞은 자리에서 번쩍
        CameraShake.Shake(perfect ? 0.12f : 0.07f, 0.12f);
        // 튕겨 나가기: 속도를 직접 바꾼다. 왼쪽(-x)으로 3~6, 위로 4~8.
        // linearVelocity는 Unity 6에서 쓰는 이름이다(예전 버전의 velocity).
        rb.linearVelocity = new Vector2(-Random.Range(3f, 6f), Random.Range(4f, 8f));
    }

    // 화면 박치기: ① 화면 안쪽으로 작아지며 날아감 → ② 카메라 쪽으로 커지며 화면 한가운데로 → ③ 쾅! 금 → ④ 미끄러져 내려감
    IEnumerator ScreenKO(GameObject go)
    {
        Transform tr = go.transform;
        Vector3 start = tr.position;
        Vector3 baseScale = tr.localScale;
        foreach (var r in go.GetComponentsInChildren<SpriteRenderer>()) r.sortingOrder = 60;   // 노트·레인보다 위
        Camera cam = Camera.main;

        // ① 멀어짐: 오른쪽 위로 날아가며 작아지고 빙글빙글
        Vector3 far = start + new Vector3(3f, 3.5f, 0f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            if (tr == null) yield break;
            float k = t / 0.3f;
            tr.position = Vector3.Lerp(start, far, 1f - (1f - k) * (1f - k));
            tr.localScale = baseScale * Mathf.Lerp(1f, 0.45f, k);
            tr.rotation = Quaternion.Euler(0f, 0f, -k * 540f);
            yield return null;
        }

        GameSfx.Play(GameSfx.Sound.Whoosh);
        // ② 다가옴: 화면 한가운데로 오면서 급격히 커짐 (k³: 처음엔 천천히, 마지막에 확 → 카메라에 꽂히는 느낌)
        Vector3 center = cam != null ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f) : Vector3.zero;
        for (float t = 0f; t < 0.28f; t += Time.deltaTime)
        {
            if (tr == null) yield break;
            float k = t / 0.28f;
            float k3 = k * k * k;
            tr.position = Vector3.Lerp(far, center, k);
            tr.localScale = baseScale * Mathf.Lerp(0.45f, screenKoScale, k3);
            tr.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-540f, -720f, k));   // 마지막엔 똑바로(-720 = 두 바퀴)
            yield return null;
        }
        if (tr == null) yield break;

        // ③ 쾅: 납작하게 눌리고 화면에 금, 섬광, 흔들림
        tr.position = center;
        tr.rotation = Quaternion.identity;
        tr.localScale = new Vector3(baseScale.x * screenKoScale * 1.12f, baseScale.y * screenKoScale * 0.9f, 1f);
        Vector2 screenPos = cam != null ? (Vector2)cam.WorldToScreenPoint(center) : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        ScreenFx.Crack(screenPos, 1.4f);
        GameSfx.Play(GameSfx.Sound.ScreenCrash);
        ScreenFx.Flash(new Color(1f, 1f, 1f, 0.5f), 0.12f);
        CameraShake.Shake(0.3f, 0.3f);
        yield return new WaitForSeconds(0.08f);
        if (tr == null) yield break;
        tr.localScale = baseScale * screenKoScale;
        yield return new WaitForSeconds(0.35f);   // 화면에 붙어 있는 시간

        // ④ 주르륵: 점점 빨라지며 아래로 미끄러지고, 살짝 기울며 흐려짐
        var renders = go.GetComponentsInChildren<SpriteRenderer>();
        for (float t = 0f; t < 0.9f; t += Time.deltaTime)
        {
            if (tr == null) yield break;
            float k = t / 0.9f;
            tr.position = center + new Vector3(0f, -k * k * 9f, 0f);
            tr.rotation = Quaternion.Euler(0f, 0f, k * 12f);
            foreach (var r in renders) if (r != null) { Color c = r.color; c.a = 1f - Mathf.Max(0f, k - 0.6f) / 0.4f; r.color = c; }
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    // 타격 섬광: 하얀 원이 커지면서 0.08초 만에 사라진다. 아주 짧아야 "번쩍"으로 보인다.
    IEnumerator Flash(Vector3 pos, float size)
    {
        var sr = ProcSprites.Make("Flash", null, ProcSprites.Circle, Color.white, 6);   // 날아가는 적(4)보다 위
        sr.transform.position = pos;
        const float duration = 0.08f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);                               // 0 → 1
            sr.transform.localScale = Vector3.one * size * Mathf.Lerp(0.6f, 1f, k);   // 60% → 100% 크기로 커지고
            sr.color = new Color(1f, 1f, 1f, 0.9f * (1f - k));                  // 불투명 → 투명
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    // 적 오브젝트 하나를 만든다. 프리팹 → 넘겨받은 그림 → Enemy Sprites → 임시 도형 순서로 고른다.
    GameObject MakeEnemy(Vector3 pos, Sprite given)
    {
        if (studentPrefab != null) return Instantiate(studentPrefab, pos, Quaternion.identity);

        // 넘겨받은 그림이 있으면 그것, 없으면 Enemy Sprites에서 무작위 한 장
        Sprite sprite = given != null ? given
            : (enemySprites != null && enemySprites.Length > 0 ? enemySprites[Random.Range(0, enemySprites.Length)] : null);
        if (sprite != null)   // 고른 칸이 비어 있으면 아래 임시 도형으로 넘어간다.
        {
            var go = new GameObject("Enemy");
            go.transform.position = pos;
            var sr = ProcSprites.Make("Sprite", go.transform, sprite, Color.white, enemyOrder);
            sr.flipX = flipEnemies;   // flipX: 그림을 좌우로 뒤집는다(Transform 크기를 음수로 하지 않아도 된다).
            float h = sprite.bounds.size.y;
            // 그림 크기를 enemyHeight에 맞춘다. 그림 자식에 배율을 주고, 부모(go)는 1로 둬서 Launch의 scale 배율이 따로 곱해지게 한다.
            sr.transform.localScale = Vector3.one * (h > 0f ? enemyHeight / h : 1f);
            return go;
        }
        return MakeStudent(pos);
    }

    // 임시 학생 도형을 만든다: 교복 몸통 + 얼굴 + 머리카락 + X자 눈.
    GameObject MakeStudent(Vector3 pos)
    {
        var go = new GameObject("Student");
        go.transform.position = pos;
        Color uniform = Uniforms[Random.Range(0, Uniforms.Length)];

        var body = ProcSprites.Make("Body", go.transform, ProcSprites.Square, uniform, 6);
        body.transform.localScale = new Vector3(0.55f, 0.7f, 1f);
        body.transform.localPosition = new Vector3(0f, -0.1f, 0f);

        var head = ProcSprites.Make("Head", go.transform, ProcSprites.Circle, new Color(1f, 0.85f, 0.7f), 7);
        head.transform.localScale = Vector3.one * 0.48f;
        head.transform.localPosition = new Vector3(0f, 0.48f, 0f);

        var hair = ProcSprites.Make("Hair", go.transform, ProcSprites.Square, new Color(0.12f, 0.1f, 0.1f), 8);
        hair.transform.localScale = new Vector3(0.46f, 0.14f, 1f);
        hair.transform.localPosition = new Vector3(0f, 0.66f, 0f);

        // 맞아서 X 눈: 눈 2개(i) × 막대 2개(k, +45도와 -45도로 겹쳐서 X)
        for (int i = 0; i < 2; i++)
        {
            float ex = i == 0 ? -0.09f : 0.09f;
            for (int k = 0; k < 2; k++)
            {
                var eye = ProcSprites.Make("Eye", go.transform, ProcSprites.Square, Color.black, 9);
                eye.transform.localScale = new Vector3(0.09f, 0.02f, 1f);
                eye.transform.localPosition = new Vector3(ex, 0.5f, 0f);
                eye.transform.localRotation = Quaternion.Euler(0f, 0f, k == 0 ? 45f : -45f);
            }
        }
        return go;
    }
}
