// ============================================================================
// ScreenFx.cs : 화면 전체에 덮는 연출 모음 (섬광, 가장자리 빨간 맥박, 대사 말풍선 글자, 큰 글자, 화면 금)
// ----------------------------------------------------------------------------
// 씬에 붙일 필요 없다. 처음 부를 때 스스로 오브젝트와 Canvas를 만든다.
// 시간은 Time.unscaledDeltaTime으로 흐른다 → 슬로우모션(Time.timeScale) 중에도 제 속도로 움직인다.
// 쓰는 곳: BossPhaseFx(대사·HP 연출·피니시), NoteDisplay(기공파 맞음), StudentLauncher(화면 박치기)
// ============================================================================

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ScreenFx : MonoBehaviour
{
    static ScreenFx inst;
    static Font font;
    static Sprite sparkle, crack, vignette, heart, speedLines;

    RectTransform canvasRect;
    Image flashImg, edgeImg, feverImg, speedImg;
    float nextFeverSparkle, speedStrength, nextSpeedFlip;
    bool fever;
    Color edgeColor;
    float edgeStrength, flashTime, flashLeft;
    Color flashColor;

    /// <summary>글자에 쓸 글꼴 (한글 글꼴을 넣는다). 안 넣으면 Unity 기본 글꼴.</summary>
    public static void SetFont(Font f) { if (f != null) font = f; }

    static ScreenFx I
    {
        get
        {
            if (inst == null) inst = new GameObject("ScreenFx").AddComponent<ScreenFx>().Build();
            return inst;
        }
    }

    // ───────────────────────── 공개 기능 ─────────────────────────

    /// <summary>화면 전체가 color로 번쩍였다가 time초 동안 사라진다.</summary>
    public static void Flash(Color color, float time)
    {
        var s = I;
        s.flashColor = color;
        s.flashTime = s.flashLeft = Mathf.Max(0.01f, time);
    }

    /// <summary>화면 가장자리가 color로 맥박 친다. strength 0이면 끔.</summary>
    public static void SetEdge(Color color, float strength)
    {
        var s = I;
        s.edgeColor = color;
        s.edgeStrength = Mathf.Clamp01(strength);
    }

    /// <summary>월드 위치(예: 보스 머리 위) 근처에 대사 글자가 통 튀어나왔다가 사라진다.</summary>
    public static void Say(string text, Vector3 worldPos, Color color, float size = 64f, float time = 1.6f)
    {
        var s = I;
        Camera cam = Camera.main;
        Vector2 screen = cam != null ? (Vector2)cam.WorldToScreenPoint(worldPos) : new Vector2(Screen.width * 0.7f, Screen.height * 0.8f);
        s.StartCoroutine(s.PopText(text, screen, color, size, time, true));
    }

    /// <summary>화면 가운데에 큰 글자가 쾅 찍혔다가 사라진다.</summary>
    public static void Banner(string text, Color color, float size = 160f, float time = 1.4f)
    {
        var s = I;
        s.StartCoroutine(s.PopText(text, new Vector2(Screen.width * 0.5f, Screen.height * 0.55f), color, size, time, false));
    }

    /// <summary>화면(유리)에 금이 간다. screenPos는 픽셀 좌표.</summary>
    public static void Crack(Vector2 screenPos, float time = 1.2f)
    {
        var s = I;
        s.StartCoroutine(s.CrackRoutine(screenPos, time));
    }

    /// <summary>바람을 가르는 집중선 (화면 가장자리에서 가운데로 모이는 흰 선). strength 0이면 끔. 러시 기믹이 쓴다.</summary>
    public static void SetSpeedLines(float strength) { I.speedStrength = Mathf.Clamp01(strength); }

    /// <summary>피버 타임 화면 연출 (금색 가장자리 + 반짝이) 켜기/끄기.</summary>
    public static void SetFever(bool on) { I.fever = on; }

    /// <summary>하트 그림 (64×64, 분홍). 하트 학생 머리 위에 쓴다.</summary>
    public static Sprite Heart
    {
        get
        {
            if (heart == null) heart = MakeHeart();
            return heart;
        }
    }

    /// <summary>네 갈래 반짝이 그림 (64×64, 흰색). 다른 연출에서도 쓴다.</summary>
    public static Sprite Sparkle
    {
        get
        {
            if (sparkle == null) sparkle = MakeSparkle();
            return sparkle;
        }
    }

    // ───────────────────────── 내부 ─────────────────────────

    ScreenFx Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)transform;

        if (vignette == null) vignette = MakeVignette();
        if (speedLines == null) speedLines = MakeSpeedLines();
        speedImg = FullImage("SpeedLines", speedLines);
        // 집중선은 화면보다 크게 (돌려도 모서리가 비지 않게)
        speedImg.rectTransform.offsetMin = new Vector2(-300f, -300f);
        speedImg.rectTransform.offsetMax = new Vector2(300f, 300f);
        speedImg.color = new Color(1f, 1f, 1f, 0f);
        feverImg = FullImage("Fever", vignette);
        edgeImg = FullImage("Edge", vignette);
        flashImg = FullImage("Flash", null);
        edgeImg.color = flashImg.color = feverImg.color = new Color(0f, 0f, 0f, 0f);
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return this;
    }

    Image FullImage(string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;   // 버튼 클릭을 막지 않게
        return img;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (flashLeft > 0f)
        {
            flashLeft -= dt;
            float a = Mathf.Clamp01(flashLeft / flashTime);
            flashImg.color = new Color(flashColor.r, flashColor.g, flashColor.b, flashColor.a * a);
        }
        else flashImg.color = new Color(0f, 0f, 0f, 0f);

        // 집중선: 0.05초마다 무작위로 돌려서 선이 지글지글 흐르는 것처럼
        if (speedStrength > 0f)
        {
            if (Time.unscaledTime >= nextSpeedFlip)
            {
                nextSpeedFlip = Time.unscaledTime + 0.05f;
                speedImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            }
            speedImg.color = new Color(1f, 1f, 1f, speedStrength * Random.Range(0.55f, 0.85f));
        }
        else speedImg.color = new Color(1f, 1f, 1f, 0f);

        // 피버: 금색 가장자리가 무지개처럼 색이 돌고, 화면 여기저기서 반짝
        if (fever)
        {
            Color rainbow = Color.HSVToRGB(Mathf.Repeat(Time.unscaledTime * 0.5f, 1f), 0.55f, 1f);
            feverImg.color = new Color(rainbow.r, rainbow.g, rainbow.b, 0.45f + 0.2f * Mathf.Sin(Time.unscaledTime * 8f));
            if (Time.unscaledTime >= nextFeverSparkle)
            {
                nextFeverSparkle = Time.unscaledTime + 0.08f;
                StartCoroutine(FeverSparkle());
            }
        }
        else feverImg.color = new Color(0f, 0f, 0f, 0f);

        // 가장자리 맥박: 0.55~1.0 사이로 초당 약 1.3번 두근거림
        float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
        edgeImg.color = new Color(edgeColor.r, edgeColor.g, edgeColor.b, edgeStrength * pulse);
    }

    IEnumerator PopText(string text, Vector2 screen, Color color, float size, float time, bool speech)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text), typeof(Outline));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.sizeDelta = new Vector2(1600f, size * 2f);
        rt.position = screen;   // Overlay Canvas에서는 transform.position이 곧 화면 픽셀 좌표
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = Mathf.RoundToInt(size);
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        var o = go.GetComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.9f);
        o.effectDistance = new Vector2(4f, -4f);

        float pop = 0.18f;
        for (float e = 0f; e < time; e += Time.unscaledDeltaTime)
        {
            float k = e / time;
            // 처음 0.18초: 1.6배에서 1배로 쾅 / 끝 25%: 흐려짐 / 말풍선은 살짝 떠오름
            float scale = e < pop ? Mathf.Lerp(1.6f, 1f, e / pop) : 1f;
            rt.localScale = Vector3.one * scale;
            float a = k > 0.75f ? 1f - (k - 0.75f) / 0.25f : 1f;
            t.color = new Color(color.r, color.g, color.b, a);
            o.effectColor = new Color(0f, 0f, 0f, 0.9f * a);
            if (speech) rt.position = screen + new Vector2(Random.Range(-1f, 1f) * (e < pop ? 6f : 0f), e * 30f);
            yield return null;
        }
        Destroy(go);
    }

    IEnumerator FeverSparkle()
    {
        var go = new GameObject("FeverSparkle", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.position = new Vector2(Random.Range(0f, Screen.width), Random.Range(0f, Screen.height));
        var img = go.GetComponent<Image>();
        img.sprite = Sparkle;
        img.raycastTarget = false;
        float size = Random.Range(30f, 70f), life = 0.5f;
        for (float e = 0f; e < life; e += Time.unscaledDeltaTime)
        {
            float k = Mathf.Sin(e / life * Mathf.PI);
            rt.sizeDelta = Vector2.one * size * k;
            img.color = new Color(1f, 0.95f, 0.6f, k);
            yield return null;
        }
        Destroy(go);
    }

    IEnumerator CrackRoutine(Vector2 screenPos, float time)
    {
        if (crack == null) crack = MakeCrack();
        var go = new GameObject("Crack", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(transform, false);
        rt.sizeDelta = new Vector2(1000f, 1000f);
        rt.position = screenPos;
        rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        var img = go.GetComponent<Image>();
        img.sprite = crack;
        img.raycastTarget = false;
        for (float e = 0f; e < time; e += Time.unscaledDeltaTime)
        {
            float k = e / time;
            rt.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(e / 0.06f));   // 쩍 하고 퍼짐
            img.color = new Color(1f, 1f, 1f, k > 0.6f ? 1f - (k - 0.6f) / 0.4f : 1f);
            yield return null;
        }
        Destroy(go);
    }

    // ───────────────────────── 그림 만들기 ─────────────────────────

    static Sprite MakeSparkle()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / n * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                float star = 1f - (Mathf.Sqrt(dx) + Mathf.Sqrt(dy));
                float glow = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f);
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Max(star * 3f, glow * 0.8f)));
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 64f);
    }

    // 하트: 두 원 + 아래 뾰족한 삼각형 모양을 픽셀마다 계산 (|x|, y로 하트 방정식)
    static Sprite MakeHeart()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fx = ((x + 0.5f) / n * 2f - 1f) * 1.25f, fy = ((y + 0.5f) / n * 2f - 1f) * 1.25f + 0.15f;
                // 하트 곡선: (x² + y² - 1)³ - x²·y³ ≤ 0 이면 안쪽
                float a = fx * fx + fy * fy - 1f;
                float v = a * a * a - fx * fx * fy * fy * fy;
                bool inside = v <= 0f;
                bool edge = v <= 0.06f && !inside;
                Color c = inside ? new Color(1f, 0.3f, 0.55f, 1f) : edge ? new Color(1f, 1f, 1f, 1f) : new Color(0f, 0f, 0f, 0f);
                if (inside && fx < -0.25f && fy > 0.35f && fy < 0.7f) c = new Color(1f, 0.75f, 0.85f, 1f);   // 반짝이는 하이라이트
                px[y * n + x] = c;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 64f);
    }

    // 집중선 그림: 가운데는 비우고(노트가 보이게) 바깥으로 갈수록 진해지는 가는 흰 선들
    static Sprite MakeSpeedLines()
    {
        const int n = 256;   // 화면 크기로 늘려 쓰므로 작아도 된다 (만드는 시간 절약)
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        var rnd = new System.Random(11);
        int lines = 60;
        float[] ang = new float[lines], width = new float[lines];
        for (int i = 0; i < lines; i++) { ang[i] = (float)rnd.NextDouble() * Mathf.PI * 2f; width[i] = 0.004f + (float)rnd.NextDouble() * 0.012f; }
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < 0.45f) continue;
                float a = Mathf.Atan2(dy, dx), best = 0f;
                for (int i = 0; i < lines; i++)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, ang[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    float w = width[i] * (r + 0.3f);
                    if (d < w) best = Mathf.Max(best, 1f - d / w);
                }
                float fade = Mathf.Clamp01((r - 0.45f) / 0.5f);
                px[y * n + x] = new Color(1f, 1f, 1f, best * fade);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    // 가장자리만 진하고 가운데는 투명한 그림 (화면 크기로 늘려서 쓴다)
    static Sprite MakeVignette()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float ex = Mathf.Min(x, n - 1 - x) / (n * 0.5f), ey = Mathf.Min(y, n - 1 - y) / (n * 0.5f);
                float d = Mathf.Min(ex, ey);                         // 가장자리까지 거리 (0 = 가장자리)
                float a = Mathf.Clamp01(1f - d / 0.35f);
                px[y * n + x] = new Color(1f, 1f, 1f, a * a);
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    // 유리 금 그림: 가운데에서 뻗는 굵은 금 + 갈라지는 잔금 + 가운데 원형 금
    const int CrackSize = 512;

    static Sprite MakeCrack()
    {
        int n = CrackSize;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        var rnd = new System.Random(7);

        float c0 = n * 0.5f;
        int rays = 11;
        for (int i = 0; i < rays; i++)
        {
            float ang = (i + R(rnd) * 0.6f) / rays * Mathf.PI * 2f;
            float x = c0, y = c0, w = 3.2f;
            float step = n * 0.06f;
            while (x > 0 && y > 0 && x < n && y < n)
            {
                ang += (R(rnd) - 0.5f) * 0.5f;                                // 지그재그
                float nx = x + Mathf.Cos(ang) * step, ny = y + Mathf.Sin(ang) * step;
                Line(px, n, x, y, nx, ny, w);
                if (R(rnd) < 0.35f)                                           // 잔금
                {
                    float ba = ang + (R(rnd) < 0.5f ? 1f : -1f) * (0.5f + R(rnd) * 0.6f);
                    Line(px, n, nx, ny, nx + Mathf.Cos(ba) * step * 0.8f, ny + Mathf.Sin(ba) * step * 0.8f, w * 0.6f);
                }
                x = nx; y = ny;
                w = Mathf.Max(1.2f, w * 0.9f);
            }
        }
        // 가운데 깨진 원
        for (int ring = 0; ring < 2; ring++)
        {
            float r = n * (0.07f + ring * 0.07f);
            int seg = 14 + ring * 6;
            for (int i = 0; i < seg; i++)
            {
                if (R(rnd) < 0.25f) continue;
                float a0 = (float)i / seg * Mathf.PI * 2f, a1 = (float)(i + 1) / seg * Mathf.PI * 2f;
                float r0 = r * (0.9f + R(rnd) * 0.2f), r1 = r * (0.9f + R(rnd) * 0.2f);
                Line(px, n, c0 + Mathf.Cos(a0) * r0, c0 + Mathf.Sin(a0) * r0, c0 + Mathf.Cos(a1) * r1, c0 + Mathf.Sin(a1) * r1, 1.8f);
            }
        }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
    }

    static float R(System.Random rnd) { return (float)rnd.NextDouble(); }

    // 선 하나: 어두운 테두리 + 하얀 금
    static void Line(Color[] px, int n, float x0, float y0, float x1, float y1, float w)
    {
        float len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
        for (float s = 0f; s <= len; s += 0.7f)
        {
            float t = s / Mathf.Max(1f, len);
            float x = Mathf.Lerp(x0, x1, t), y = Mathf.Lerp(y0, y1, t);
            Dot(px, n, x, y, w + 1.2f, new Color(0.05f, 0.05f, 0.08f, 0.75f));
            Dot(px, n, x, y, w * 0.5f, new Color(1f, 1f, 1f, 0.95f));
        }
    }

    // 점 하나 (원). 더 진한 색만 덮어쓴다 → 하얀 금이 어두운 테두리에 안 가려짐
    static void Dot(Color[] px, int n, float cx, float cy, float r, Color c)
    {
        int r0 = Mathf.CeilToInt(r);
        for (int y = -r0; y <= r0; y++)
            for (int x = -r0; x <= r0; x++)
            {
                int qx = (int)cx + x, qy = (int)cy + y;
                if (qx < 0 || qy < 0 || qx >= n || qy >= n || x * x + y * y > r * r) continue;
                if (c.a >= px[qy * n + qx].a) px[qy * n + qx] = c;
            }
    }
}
