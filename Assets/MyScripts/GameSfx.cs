// ============================================================================
// GameSfx.cs : 연출 효과음 (기공파, 돌진, 화면 박치기, 피니시, 별 반짝 …)
// ----------------------------------------------------------------------------
// 노트를 맞힐 때 나는 기본 타격음은 HitSfx가 맡고, 이 컴포넌트는 보스 패턴·연출 소리를 맡는다.
// 다른 스크립트는 GameSfx.Play(GameSfx.Sound.Finish) 처럼 부르기만 한다. 씬에 GameSfx가 없으면 조용히 넘어간다.
//
// 넣는 법:
//   1) 효과음 파일(wav)을 Assets/Audio/Sfx/ 에 넣는다.
//   2) Hierarchy의 Sfx 오브젝트(없으면 빈 오브젝트)에 이 컴포넌트를 붙인다.
//   3) 컴포넌트 ⋮ 메뉴 > "효과음 자동 채우기" → 파일 이름이 같은 칸에 들어간다. 직접 끌어다 넣어도 된다.
// AudioSource를 여러 개 만들어 돌려 쓰므로 소리가 겹쳐도 앞 소리가 끊기지 않는다.
// ============================================================================

using UnityEngine;

public class GameSfx : MonoBehaviour
{
    public enum Sound { BarrageHit, BigHit, Whoosh, ScreenCrash, Windup, BeamReflect, PhaseChange, Finish, Twinkle }

    [Header("효과음 (비워두면 그 소리만 안 남)")]
    [SerializeField] AudioClip barrageHit;    // Barrage_Hit: 연타 한 대
    [SerializeField] AudioClip bigHit;        // Big_Hit: 돌진 막아냄·기공파에 맞음
    [SerializeField] AudioClip whoosh;        // Whoosh: 화면 쪽으로 날아옴·보스가 날아감
    [SerializeField] AudioClip screenCrash;   // Screen_Crash: 화면 박치기 쾅+유리
    [SerializeField] AudioClip windup;        // Windup: 돌진·기공파 전 기 모으기
    [SerializeField] AudioClip beamReflect;   // Beam_Reflect: 기공파 되받아치기
    [SerializeField] AudioClip beamLoop;      // Beam_Loop: 기공파를 막는 동안 반복
    [SerializeField] AudioClip phaseChange;   // Phase_Change: 보스 HP 50%·30%
    [SerializeField] AudioClip finish;        // Finish: 마지막 노트 피니시
    [SerializeField] AudioClip twinkle;       // Twinkle: 히카리가 별이 됨

    [Header("음량")]
    [Range(0f, 1f)] [SerializeField] float volume = 0.8f;
    [Tooltip("같은 소리가 반복될 때 음높이를 살짝씩 다르게 (0이면 항상 같음)")]
    [Range(0f, 0.3f)] [SerializeField] float pitchJitter = 0.08f;

    static GameSfx inst;
    static bool searched;
    AudioSource[] pool;
    AudioSource loopSource;
    int next;

    void Awake()
    {
        inst = this;
        pool = new AudioSource[8];
        for (int i = 0; i < pool.Length; i++) pool[i] = NewSource(false);
        loopSource = NewSource(true);
    }

    void OnDestroy() { if (inst == this) { inst = null; searched = false; } }

    AudioSource NewSource(bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = 0f;   // 2D 소리 (거리와 상관없이 같은 크기)
        return src;
    }

    static GameSfx I
    {
        get
        {
            if (inst == null && !searched) { searched = true; inst = FindAnyObjectByType<GameSfx>(); }
            return inst;
        }
    }

    /// <summary>효과음 하나를 낸다. vol은 이 소리만의 음량 배율.</summary>
    public static void Play(Sound s, float vol = 1f)
    {
        var g = I;
        if (g == null) return;
        AudioClip clip = g.ClipOf(s);
        if (clip == null) return;
        AudioSource src = g.pool[g.next];
        g.next = (g.next + 1) % g.pool.Length;   // 돌려 쓰기
        src.pitch = 1f + Random.Range(-g.pitchJitter, g.pitchJitter);
        src.PlayOneShot(clip, g.volume * vol);
    }

    /// <summary>기공파 반복음 켜기/끄기.</summary>
    public static void BeamLoop(bool on)
    {
        var g = I;
        if (g == null || g.beamLoop == null) return;
        if (on && !g.loopSource.isPlaying)
        {
            g.loopSource.clip = g.beamLoop;
            g.loopSource.volume = g.volume * 0.7f;
            g.loopSource.Play();
        }
        else if (!on && g.loopSource.isPlaying) g.loopSource.Stop();
    }

    AudioClip ClipOf(Sound s)
    {
        switch (s)
        {
            case Sound.BarrageHit: return barrageHit;
            case Sound.BigHit: return bigHit;
            case Sound.Whoosh: return whoosh;
            case Sound.ScreenCrash: return screenCrash;
            case Sound.Windup: return windup;
            case Sound.BeamReflect: return beamReflect;
            case Sound.PhaseChange: return phaseChange;
            case Sound.Finish: return finish;
            case Sound.Twinkle: return twinkle;
        }
        return null;
    }

    // 에디터 전용 필드도 #if 밖에 둔다 (안에 두면 빌드할 때 저장 형식이 달라져 경고가 난다)
    [Tooltip("자동 채우기가 찾는 폴더")]
    [SerializeField] string sfxFolder = "Assets/Audio/Sfx";

#if UNITY_EDITOR
    [ContextMenu("효과음 자동 채우기 (Audio/Sfx)")]
    void AutoFill()
    {
        UnityEditor.Undo.RecordObject(this, "효과음 자동 채우기");
        barrageHit = Load("Barrage_Hit");
        bigHit = Load("Big_Hit");
        whoosh = Load("Whoosh");
        screenCrash = Load("Screen_Crash");
        windup = Load("Windup");
        beamReflect = Load("Beam_Reflect");
        beamLoop = Load("Beam_Loop");
        phaseChange = Load("Phase_Change");
        finish = Load("Finish");
        twinkle = Load("Twinkle");
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[GameSfx] 효과음을 채웠습니다. 빈 칸은 " + sfxFolder + " 에 같은 이름의 wav가 없는 것입니다.");
    }

    AudioClip Load(string name)
    {
        return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(sfxFolder + "/" + name + ".wav");
    }
#endif
}
