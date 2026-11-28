// ============================================================================
// TitleLogo.cs : 타이틀 화면의 제목 로고 연출
// ----------------------------------------------------------------------------
// 1) 하얀 실루엣이 크게 나타나며 제자리로 줄어든다 (통 튀는 느낌)
// 2) 하얀 실루엣 위로 원래 색 로고가 스르르 떠오르고, 실루엣은 사라진다
// 3) 그 순간 반짝이가 사방으로 터지고, 그 뒤로는 로고 위에서 반짝이가 계속 반짝인다
// 4) 로고는 천천히 위아래로 둥실거린다
//
// 넣는 법: 빈 오브젝트(이름 TitleLogo)에 이 컴포넌트를 붙이고
//   Logo = Arts/UI/Title_Logo, Silhouette = Arts/UI/Title_Silhouette 를 넣는다.
//   자기 Canvas를 따로 만들어서 화면 위쪽 가운데에 띄운다.
// 다른 스크립트에서: Show() = 처음부터 다시 연출, Hide() = 숨김
// 씬에 GameFlow가 있으면 GameFlow가 타이틀 화면에서만 Show()를 부른다 (Play On Start는 무시됨).
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TitleLogo : MonoBehaviour
{
    [Header("그림")]
    [SerializeField] Sprite logo;
    [Tooltip("로고와 같은 모양의 하얀 그림")]
    [SerializeField] Sprite silhouette;

    [Header("배치 (1920×1080 화면 기준 픽셀)")]
    [SerializeField] Vector2 position = new Vector2(0f, -40f);   // 화면 위쪽 가운데에서 아래로
    [Tooltip("로고 폭. 900이면 메뉴(게임 시작/설정/종료)와 안 겹친다")]
    [SerializeField] float width = 900f;
    [Tooltip("Canvas 그리는 순서. 타이틀 메뉴보다 위에 그리려면 크게")]
    [SerializeField] int sortingOrder = 50;
    [SerializeField] bool playOnStart = true;

    [Header("연출 시간 (초)")]
    [SerializeField] float popTime = 0.45f;      // 실루엣이 튀어나오는 시간
    [SerializeField] float revealTime = 0.8f;    // 실루엣 → 색 로고로 바뀌는 시간

    [Header("반짝이")]
    [SerializeField] int burstCount = 18;              // 로고가 나타나는 순간 터지는 개수
    [SerializeField] float twinkleInterval = 0.12f;    // 그 뒤로 몇 초마다 하나씩 반짝일지
    [SerializeField] Color sparkleColor = new Color(1f, 0.97f, 0.8f, 1f);
    [SerializeField] float bobAmount = 8f;             // 둥실거리는 높이(픽셀)

    RectTransform holder;     // 로고 묶음 (둥실거리기·크기 연출을 여기에)
    Image logoImg, silImg;
    RectTransform sparkleLayer;
    Sprite sparkleSprite;
    Coroutine running;
    float logoHeight;
    readonly List<GameObject> sparkles = new List<GameObject>();

    /// <summary>로고 그림이 들어 있는지. GameFlow가 예전 글자 제목을 숨길지 정할 때 본다.</summary>
    public bool HasLogo => logo != null;

    void Start()
    {
        // GameFlow가 있으면 GameFlow가 타이틀 화면에서만 Show/Hide를 부르므로 여기서는 아무것도 안 한다.
        if (playOnStart && FindAnyObjectByType<GameFlow>() == null) Show();
    }

    /// <summary>처음부터 연출을 다시 한다.</summary>
    public void Show()
    {
        Build();
        gameObject.SetActive(true);
        holder.gameObject.SetActive(true);
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Play());
    }

    /// <summary>로고를 숨긴다.</summary>
    public void Hide()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        foreach (var s in sparkles) if (s != null) Destroy(s);
        sparkles.Clear();
        if (holder != null) holder.gameObject.SetActive(false);
    }

    IEnumerator Play()
    {
        SetAlpha(logoImg, 0f);
        SetAlpha(silImg, 0f);
        holder.localScale = Vector3.one * 1.3f;

        // 1) 하얀 실루엣이 커다랗게 나타나며 제자리로 (튕기듯 살짝 작아졌다 돌아옴 = Ease Out Back)
        for (float t = 0f; t < popTime; t += Time.unscaledDeltaTime)
        {
            float k = t / popTime;
            SetAlpha(silImg, Mathf.Clamp01(k * 2f));
            holder.localScale = Vector3.one * Mathf.LerpUnclamped(1.3f, 1f, EaseOutBack(k));
            yield return null;
        }
        holder.localScale = Vector3.one;

        // 2) 반짝이 터지기 + 실루엣 위로 색 로고가 스르르
        for (int i = 0; i < burstCount; i++) StartCoroutine(Sparkle(true));
        for (float t = 0f; t < revealTime; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / revealTime);
            SetAlpha(logoImg, k);
            SetAlpha(silImg, 1f - k);
            yield return null;
        }
        SetAlpha(logoImg, 1f);
        SetAlpha(silImg, 0f);

        // 3) 계속: 둥실거리기 + 가끔 반짝
        float next = 0f, start = Time.unscaledTime;
        while (true)
        {
            float time = Time.unscaledTime - start;
            holder.anchoredPosition = position + new Vector2(0f, Mathf.Sin(time * 1.6f) * bobAmount);
            if (time >= next)
            {
                next = time + twinkleInterval * Random.Range(0.6f, 1.4f);
                StartCoroutine(Sparkle(false));
            }
            yield return null;
        }
    }

    // 반짝이 하나: 커졌다 작아지며 돈다. burst면 로고 가운데에서 바깥으로 튀어 나간다.
    IEnumerator Sparkle(bool burst)
    {
        var go = new GameObject("Sparkle", typeof(RectTransform), typeof(Image));
        sparkles.Add(go);
        var rt = (RectTransform)go.transform;
        rt.SetParent(sparkleLayer, false);
        var img = go.GetComponent<Image>();
        img.sprite = sparkleSprite;
        img.raycastTarget = false;

        Vector2 half = new Vector2(width, logoHeight) * 0.5f;
        Vector2 from, to;
        if (burst)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            from = Vector2.Scale(dir, half) * 0.3f;
            to = Vector2.Scale(dir, half) * Random.Range(0.9f, 1.25f);
        }
        else
        {
            from = to = new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y)) * 0.9f;
        }
        float size = burst ? Random.Range(40f, 90f) : Random.Range(25f, 60f);
        float life = burst ? Random.Range(0.5f, 0.8f) : Random.Range(0.4f, 0.7f);
        float spin = Random.Range(-180f, 180f);

        for (float t = 0f; t < life; t += Time.unscaledDeltaTime)
        {
            float k = t / life;
            float s = Mathf.Sin(k * Mathf.PI);   // 0 → 1 → 0
            rt.anchoredPosition = Vector2.Lerp(from, to, 1f - (1f - k) * (1f - k));
            rt.sizeDelta = Vector2.one * size * s;
            rt.localRotation = Quaternion.Euler(0f, 0f, spin * k);
            img.color = new Color(sparkleColor.r, sparkleColor.g, sparkleColor.b, s);
            yield return null;
        }
        sparkles.Remove(go);
        Destroy(go);
    }

    void Build()
    {
        if (holder != null) return;

        var canvasGo = new GameObject("TitleLogoCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;   // 해상도가 달라도 같은 비율로
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        logoHeight = logo != null ? width * logo.rect.height / logo.rect.width : width * 0.6f;

        holder = NewRect("Holder", canvasGo.transform);
        holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 1f);   // 화면 위쪽 가운데 기준
        holder.pivot = new Vector2(0.5f, 1f);
        holder.sizeDelta = new Vector2(width, logoHeight);
        holder.anchoredPosition = position;

        silImg = NewImage("Silhouette", holder, silhouette);
        logoImg = NewImage("Logo", holder, logo);

        sparkleLayer = NewRect("Sparkles", holder);
        sparkleLayer.anchorMin = sparkleLayer.anchorMax = new Vector2(0.5f, 0.5f);
        sparkleLayer.sizeDelta = Vector2.zero;

        sparkleSprite = MakeSparkleSprite();
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, RectTransform parent, Sprite sprite)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = Vector2.zero;   // 부모(Holder)를 꽉 채움
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;     // 메뉴 버튼 클릭을 막지 않게
        return img;
    }

    static void SetAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    // 튕기듯 넘쳤다가 돌아오는 곡선 (0 → 1, 중간에 1을 살짝 넘음)
    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        x = Mathf.Clamp01(x) - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    // 네 갈래 반짝이 그림을 코드로 만든다 (64×64, 가운데가 밝고 십자 모양으로 뻗음).
    static Sprite MakeSparkleSprite()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / n * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                // 오목한 네 갈래 별: |x|^0.5 + |y|^0.5 < 1 모양 + 가운데 둥근 빛
                float star = 1f - (Mathf.Sqrt(dx) + Mathf.Sqrt(dy));
                float glow = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f);
                float a = Mathf.Clamp01(Mathf.Max(star * 3f, glow * 0.8f));
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }
}
