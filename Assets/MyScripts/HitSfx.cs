// ============================================================================
// HitSfx.cs : 판정에 맞춰 타격음(효과음)을 내는 컴포넌트
// ----------------------------------------------------------------------------
// 6요소 중 담당: Display (소리 연출)
// 하는 일: Perfect·Good·Miss·연타·롱노트 완료마다 정해 둔 효과음을 한 번 재생한다.
//   칸이 비어 있는 경우는 소리를 내지 않는다. (예: Good 칸이 비면 Perfect 소리를 대신 쓴다)
//
// 왜 MusicPlayer의 AudioSource와 따로 두나?
//   MusicPlayer는 곡 하나를 dspTime으로 정확히 재생하는 데만 쓴다. 효과음을 같은 AudioSource에서 내면
//   곡 재생에 간섭할 수 있어서, 이 컴포넌트가 자기 AudioSource를 따로 쓴다.
//
// PlayOneShot: 같은 AudioSource에서 소리를 겹쳐서 재생할 수 있다(연타처럼 빠르게 여러 번 칠 때 앞 소리가 끊기지 않음).
//
// 사용법: Game 오브젝트에 Add Component → HitSfx, 각 칸에 효과음 파일(.wav, .ogg)을 넣는다.
// 연결: Judge.OnNoteJudged / OnBarrageHit --> 소리 재생
// ============================================================================

using UnityEngine;

/// <summary>판정에 맞춰 타격음을 낸다. 칸이 비어 있으면 그 소리는 생략한다.</summary>
[RequireComponent(typeof(AudioSource))]
public class HitSfx : MonoBehaviour
{
    [SerializeField] Judge judge;

    [Header("효과음 (비워두면 소리 없음)")]
    [SerializeField] AudioClip perfectClip;      // Perfect 타격음 (가장 시원한 소리)
    [Tooltip("비워두면 Perfect 소리를 쓴다")]
    [SerializeField] AudioClip goodClip;
    [SerializeField] AudioClip missClip;         // 헛방·맞는 소리
    [Tooltip("연타 한 번 칠 때. 비워두면 Perfect 소리를 쓴다")]
    [SerializeField] AudioClip barrageClip;
    [Tooltip("롱노트 완료·연타 성공 같은 큰 타격. 비워두면 Perfect 소리를 쓴다")]
    [SerializeField] AudioClip bigHitClip;

    [Header("소리 조절")]
    [Range(0f, 1f)]   // [Range]: Inspector에서 0~1 슬라이더로 보인다.
    [SerializeField] float volume = 0.8f;
    [Tooltip("재생할 때마다 음높이를 이만큼 무작위로 흔든다. 같은 소리가 반복돼도 덜 기계적으로 들린다. (0.05 = ±5%)")]
    [SerializeField] float pitchJitter = 0.05f;

    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D 소리 (거리와 상관없이 같은 크기)
        if (judge == null) judge = FindAnyObjectByType<Judge>();
    }

    void OnEnable()
    {
        if (judge == null) return;   // 판정이 없으면 소리 낼 일도 없다. 에러로 끄지는 않는다.
        judge.OnNoteJudged += HandleNoteJudged;
        judge.OnBarrageHit += HandleBarrageHit;
    }

    void OnDisable()
    {
        if (judge == null) return;
        judge.OnNoteJudged -= HandleNoteJudged;
        judge.OnBarrageHit -= HandleBarrageHit;
    }

    void HandleNoteJudged(NoteData note, Judgment result, Lane lane, double errorMs, JudgeKind kind)
    {
        if (result == Judgment.Miss) { Play(missClip); return; }
        if (kind == JudgeKind.HoldComplete || kind == JudgeKind.Barrage) { Play(bigHitClip != null ? bigHitClip : perfectClip); return; }
        if (result == Judgment.Perfect) Play(perfectClip);
        else Play(goodClip != null ? goodClip : perfectClip);
    }

    void HandleBarrageHit(NoteData note, Lane lane, int hits, int need)
    {
        Play(barrageClip != null ? barrageClip : perfectClip);
    }

    void Play(AudioClip clip)
    {
        if (clip == null) return;
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);   // 음높이를 살짝 흔든다.
        source.PlayOneShot(clip, volume);                               // 겹쳐서 재생 가능
    }
}
