using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>배경 한 겹.</summary>
[Serializable]
public class BackgroundLayer
{
    [Tooltip("배경 그림. 끝없이 이어지려면 왼쪽 끝과 오른쪽 끝이 자연스럽게 이어지는 그림이어야 한다.")]
    public Sprite sprite;
    [Tooltip("노트 속도에 대한 흐르는 속도 비율. 0 = 멈춤(하늘 등 먼 배경), 1 = 노트와 같은 속도(바닥)")]
    [Range(0f, 1.5f)] public float speed = 0.3f;
    [Tooltip("켜면 화면 높이에 맞춰 크기를 정한다. 끄면 아래 Scale을 그대로 쓴다.")]
    public bool fitScreenHeight = true;
    [Tooltip("Fit Screen Height를 껐을 때의 크기 배율")]
    public float scale = 1f;
    [Tooltip("화면 가운데 기준으로 위아래 위치 (월드 단위)")]
    public float yOffset;
    [Tooltip("색 (투명도로 흐리게 할 수 있다)")]
    public Color tint = Color.white;
}

/// <summary>
/// 뮤즈 대쉬식 스크롤 배경. 노트가 흐르는 속도에 맞춰 배경이 오른쪽에서 왼쪽으로 흐르고,
/// 여러 겹을 서로 다른 속도로 흘려 시차(원근감)를 만든다.
/// 목록의 위쪽 겹이 가장 뒤에 그려진다(먼 배경부터 넣는다).
/// 곡이 재생 중이면 곡 시간에 맞춰 흐르고, 타이틀·대사 중에는 천천히 흐른다.
/// GameFlow 스테이지에 배경을 넣으면 그 스테이지 동안 그 배경으로 바뀐다.
/// </summary>
public class BackgroundDisplay : MonoBehaviour
{
    [SerializeField] MusicPlayer music;
    [SerializeField] NoteDisplay noteDisplay;

    [Tooltip("기본 배경 (뒤 → 앞 순서). 스테이지에 배경이 없으면 이것을 쓴다.")]
    [SerializeField] List<BackgroundLayer> layers = new List<BackgroundLayer>();

    [Tooltip("곡이 재생 중이 아닐 때(타이틀·대사·결과) 흐르는 빠르기. 0이면 멈춘다.")]
    [Range(0f, 1f)] [SerializeField] float idleSpeed = 0.15f;

    [Tooltip("가장 뒤 겹의 Order in Layer. 다음 겹은 1씩 앞으로.")]
    [SerializeField] int sortingOrderBack = -100;

    class Built
    {
        public BackgroundLayer layer;
        public Transform root;
        public readonly List<Transform> tiles = new List<Transform>();
        public readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();   // 색을 한꺼번에 바꿀 때 씀
        public float tileWidth;
    }

    readonly List<Built> built = new List<Built>();
    List<BackgroundLayer> current;
    double distance;          // 지금까지 흐른 거리 (노트 속도 1배 기준, 월드 단위)
    double lastSongTime;
    bool wasPlaying;
    float builtViewWidth, builtViewHeight;
    Transform container;
    Color tintMultiplier = Color.white;   // 모든 겹에 곱하는 색 (BossPhaseFx가 보스 HP에 따라 붉게 바꿈)

    float dim = 1f;   // 밝기 (StageGimmicks의 Dark 기믹). 1이면 원래 밝기

    /// <summary>배경이 흐르는 빠르기 배율 (StageGimmicks의 Rush 기믹). 1이면 노트와 같은 빠르기.</summary>
    public float SpeedBoost { get; set; } = 1f;

    /// <summary>모든 배경 겹의 색에 c를 곱한다. Color.white면 원래 색.</summary>
    public void SetTintMultiplier(Color c)
    {
        tintMultiplier = c;
        ApplyColors();
    }

    /// <summary>배경 밝기 (0~1). Dark 기믹이 쓴다. HP 연출 색(SetTintMultiplier)과 따로 곱해진다.</summary>
    public void SetDim(float d)
    {
        d = Mathf.Clamp01(d);
        if (Mathf.Abs(d - dim) < 0.001f) return;
        dim = d;
        ApplyColors();
    }

    Color Multiplier => new Color(tintMultiplier.r * dim, tintMultiplier.g * dim, tintMultiplier.b * dim, tintMultiplier.a);

    void ApplyColors()
    {
        Color m = Multiplier;
        foreach (Built b in built)
        {
            Color baseTint = b.layer.tint.a <= 0f ? Color.white : b.layer.tint;
            foreach (SpriteRenderer r in b.renderers) if (r != null) r.color = baseTint * m;
        }
    }

    void Awake()
    {
        if (music == null) music = FindAnyObjectByType<MusicPlayer>();
        if (noteDisplay == null) noteDisplay = FindAnyObjectByType<NoteDisplay>();
    }

    void Start()
    {
        if (current == null) UseDefault();
    }

    /// <summary>기본 배경으로 돌아간다.</summary>
    public void UseDefault()
    {
        SetLayers(layers);
    }

    /// <summary>스테이지 배경을 쓴다. 비어 있으면 기본 배경.</summary>
    public void SetStage(List<BackgroundLayer> stageLayers)
    {
        SetLayers(stageLayers != null && stageLayers.Count > 0 ? stageLayers : layers);
    }

    void SetLayers(List<BackgroundLayer> list)
    {
        if (current != null && ReferenceEquals(current, list)) return;   // 같은 배경이면 그대로 (깜빡임 없음)
        current = list;
        Rebuild();
    }

    void Update()
    {
        if (current == null) return;

        float viewW, viewH;
        CameraUtil.ViewSize(out viewW, out viewH);
        if (Mathf.Abs(viewW - builtViewWidth) > 0.01f || Mathf.Abs(viewH - builtViewHeight) > 0.01f) Rebuild();

        // 흐른 거리: 재생 중이면 곡 시간 변화만큼(노트와 정확히 같이), 아니면 천천히
        float speed = noteDisplay != null ? noteDisplay.ScrollSpeed : 8f;
        bool playing = music != null && music.IsStarted;
        if (playing)
        {
            double t = music.SongTime;
            double d = t - lastSongTime;
            if (!wasPlaying || d < 0 || d > 0.5) d = Time.deltaTime;   // 곡이 새로 시작했거나 위치가 튀면 한 프레임만큼
            distance += d * speed * SpeedBoost;
            lastSongTime = t;
        }
        else
        {
            distance += Time.deltaTime * speed * idleSpeed;
        }
        wasPlaying = playing;

        Vector3 cam = CameraUtil.Position();
        foreach (Built b in built)
        {
            if (b.tileWidth <= 0f) continue;
            float shift = (float)Repeat(distance * b.layer.speed, b.tileWidth);
            float left = cam.x - viewW / 2f - shift;   // 첫 타일의 왼쪽 끝 (화면 왼쪽 끝에서 shift만큼 밖)
            for (int i = 0; i < b.tiles.Count; i++)
                b.tiles[i].position = new Vector3(left + (i + 0.5f) * b.tileWidth, cam.y + b.layer.yOffset, 0f);
        }
    }

    void Rebuild()
    {
        if (container == null) container = new GameObject("Background").transform;
        foreach (Built b in built)
            if (b.root != null) Destroy(b.root.gameObject);
        built.Clear();

        float viewW, viewH;
        CameraUtil.ViewSize(out viewW, out viewH);
        builtViewWidth = viewW;
        builtViewHeight = viewH;
        if (current == null) return;

        for (int li = 0; li < current.Count; li++)
        {
            BackgroundLayer layer = current[li];
            if (layer == null || layer.sprite == null) continue;

            Vector3 size = layer.sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f) continue;
            float s = layer.fitScreenHeight ? viewH / size.y : (layer.scale > 0f ? layer.scale : 1f);
            // Inspector의 + 버튼으로 칸을 추가하면 Tint가 (0,0,0,0) = 완전 투명, Scale이 0으로 들어간다.
            // 그대로면 배경이 안 보이므로, 투명도 0인 Tint는 흰색(원래 색)으로 친다.
            Color tint = layer.tint.a <= 0f ? Color.white : layer.tint;

            var b = new Built { layer = layer, tileWidth = size.x * s };
            b.root = new GameObject("BackgroundLayer" + li).transform;
            b.root.SetParent(container, false);

            int count = Mathf.CeilToInt(viewW / b.tileWidth) + 2;   // 화면을 덮고 양옆에 한 장씩 여유
            for (int i = 0; i < count; i++)
            {
                var sr = ProcSprites.Make("Tile" + i, b.root, layer.sprite, tint * Multiplier, sortingOrderBack + li);
                b.renderers.Add(sr);
                sr.transform.localScale = new Vector3(s, s, 1f);
                b.tiles.Add(sr.transform);
            }
            built.Add(b);
        }
    }

    static double Repeat(double v, double length)
    {
        double r = v % length;
        return r < 0 ? r + length : r;
    }

}
