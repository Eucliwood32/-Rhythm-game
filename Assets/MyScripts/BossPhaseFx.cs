// ============================================================================
// BossPhaseFx.cs : 보스전 분위기 연출 (채보와 상관없이 체력·콤보에 따라 바뀜)
// ----------------------------------------------------------------------------
//   - 보스 HP 50% 이하: 배경이 더 붉게 달아오름
//   - 보스 HP 30% 이하: 배경 더 붉게 + 화면 가장자리가 빨갛게 두근거림
//   - 마지막 노트를 맞히면 피니시: 하얀 섬광 + 슬로우모션 + 화면 확대 + "FINISH!!"
//     (그 뒤 곡이 끝나면 GameFlow가 BossDisplay.Defeat를 불러 히카리가 날아가 별이 된다)
//
// 넣는 법: Game 오브젝트에 이 컴포넌트를 붙인다. 나머지 칸은 비워두면 씬에서 찾는다.
//   Font 칸에는 GameHud와 같은 한글 글꼴을 넣으면 화면 글자가 같은 글꼴로 나온다.
// ============================================================================

using System.Collections;
using UnityEngine;

public class BossPhaseFx : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] NoteDisplay noteDisplay;
    [SerializeField] BossDisplay boss;
    [SerializeField] BackgroundDisplay background;
    [Tooltip("화면 글자 글꼴 (한글). 비워두면 기본 글꼴")]
    [SerializeField] Font font;

    [Header("체력 구간")]
    [Range(0f, 1f)] [SerializeField] float phase1 = 0.5f;
    [Range(0f, 1f)] [SerializeField] float phase2 = 0.3f;
    [Tooltip("HP 50% 이하일 때 배경 색 (곱해짐)")]
    [SerializeField] Color phase1Tint = new Color(1f, 0.75f, 0.65f, 1f);
    [Tooltip("HP 30% 이하일 때 배경 색 (곱해짐)")]
    [SerializeField] Color phase2Tint = new Color(1f, 0.5f, 0.42f, 1f);
    [SerializeField] Color edgeColor = new Color(1f, 0.1f, 0.05f, 1f);
    [Range(0f, 1f)] [SerializeField] float edgeStrength = 0.55f;

    [Header("피니시 (마지막 노트)")]
    [Tooltip("슬로우모션 배속 (0.25 = 4배 느리게)")]
    [SerializeField] float slowScale = 0.25f;
    [Tooltip("슬로우모션 시간 (실제 초)")]
    [SerializeField] float slowTime = 1.2f;
    [Tooltip("화면 확대 정도 (0.8 = 20% 확대)")]
    [SerializeField] float zoom = 0.8f;
    [SerializeField] string finishText = "FINISH!!";

    bool wasActive;
    int phase;                 // 0, 1(50% 이하), 2(30% 이하)
    Color tintFrom = Color.white, tintTo = Color.white;
    float tintT = 1f;
    bool finishing;

    void Awake()
    {
        if (judge == null) judge = FindAnyObjectByType<Judge>();
        if (noteDisplay == null) noteDisplay = FindAnyObjectByType<NoteDisplay>();
        if (boss == null) boss = FindAnyObjectByType<BossDisplay>();
        if (background == null) background = FindAnyObjectByType<BackgroundDisplay>();
        ScreenFx.SetFont(font);
    }

    void OnEnable() { if (judge != null) judge.OnNoteJudged += HandleNoteJudged; }

    void OnDisable()
    {
        if (judge != null) judge.OnNoteJudged -= HandleNoteJudged;
        Time.timeScale = 1f;   // 혹시 슬로우모션 중에 꺼져도 원래 속도로
    }

    void HandleNoteJudged(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        if (boss == null || !boss.IsActive) return;

        bool fail = result == Judgment.Miss || kind == JudgeKind.HoldBreak;

        // 마지막 노트를 끝까지 성공했을 때 피니시 (롱노트는 끝까지 누른 순간, 연타는 연타 판정 순간)
        bool finalJudge = note.type == NoteType.Long ? kind == JudgeKind.HoldComplete
                        : note.type == NoteType.Barrage ? kind == JudgeKind.Barrage
                        : kind == JudgeKind.Hit;
        if (!fail && finalJudge && !finishing && noteDisplay != null && noteDisplay.IsLastNote(note))
            StartCoroutine(Finish());
    }

    void Update()
    {
        bool active = boss != null && boss.IsActive;
        if (active && !wasActive) ResetPhase();          // 보스전 시작
        if (!active && wasActive) EndPhase();            // 보스전 끝
        wasActive = active;

        if (active)
        {
            float hp = boss.Health;
            if (phase < 1 && hp <= phase1) { phase = 1; TintTo(phase1Tint); GameSfx.Play(GameSfx.Sound.PhaseChange); }
            if (phase < 2 && hp <= phase2)
            {
                phase = 2;
                TintTo(phase2Tint);
                ScreenFx.SetEdge(edgeColor, edgeStrength);
                CameraShake.Shake(0.15f, 0.3f);
                GameSfx.Play(GameSfx.Sound.PhaseChange);
            }
        }

        if (tintT < 1f && background != null)
        {
            tintT = Mathf.Min(1f, tintT + Time.deltaTime);   // 1초에 걸쳐 바뀜
            background.SetTintMultiplier(Color.Lerp(tintFrom, tintTo, tintT));
        }
    }

    void ResetPhase()
    {
        phase = 0;
        finishing = false;
        ScreenFx.SetEdge(edgeColor, 0f);
        if (background != null) background.SetTintMultiplier(Color.white);
        tintFrom = tintTo = Color.white;
        tintT = 1f;
    }

    void EndPhase()
    {
        ScreenFx.SetEdge(edgeColor, 0f);
        if (background != null) background.SetTintMultiplier(Color.white);
        tintT = 1f;
    }

    void TintTo(Color c)
    {
        tintFrom = Color.Lerp(tintFrom, tintTo, tintT);
        tintTo = c;
        tintT = 0f;
    }

    IEnumerator Finish()
    {
        finishing = true;
        Camera cam = Camera.main;
        float baseSize = cam != null ? cam.orthographicSize : 5f;

        ScreenFx.Flash(new Color(1f, 1f, 1f, 0.9f), 0.25f);
        GameSfx.Play(GameSfx.Sound.Finish);
        ScreenFx.SetEdge(edgeColor, 0f);
        CameraShake.Shake(0.35f, 0.4f);
        Time.timeScale = slowScale;

        // 슬로우모션 동안 화면 확대 (실제 시간 기준으로 진행)
        for (float t = 0f; t < slowTime; t += Time.unscaledDeltaTime)
        {
            float k = t / slowTime;
            float z = k < 0.2f ? Mathf.Lerp(1f, zoom, k / 0.2f)            // 빠르게 확대
                    : k > 0.8f ? Mathf.Lerp(zoom, 1f, (k - 0.8f) / 0.2f)   // 끝에 원래대로
                    : zoom;
            if (cam != null) cam.orthographicSize = baseSize * z;
            yield return null;
        }
        if (cam != null) cam.orthographicSize = baseSize;
        Time.timeScale = 1f;
        ScreenFx.Banner(finishText, new Color(1f, 0.85f, 0.2f, 1f));
    }
}
