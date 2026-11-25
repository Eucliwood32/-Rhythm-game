using UnityEngine;

/// <summary>
/// 임시 그래픽용 스프라이트를 코드로 만든다 (원, 링, 사각형, 줄무늬).
/// 아트가 들어오면 각 컴포넌트의 프리팹/스프라이트 칸으로 바꾸면 된다.
/// </summary>
public static class ProcSprites
{
    static Sprite circle, ring, square, stripes;

    /// <summary>지름 1 월드 단위의 원.</summary>
    public static Sprite Circle { get { if (circle == null) circle = MakeCircle(false); return circle; } }

    /// <summary>지름 1 월드 단위의 링.</summary>
    public static Sprite Ring { get { if (ring == null) ring = MakeCircle(true); return ring; } }

    /// <summary>1x1 월드 단위 사각형.</summary>
    public static Sprite Square
    {
        get
        {
            if (square == null)
                square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return square;
        }
    }

    /// <summary>대각선 줄무늬 (타일로 깔아 쓰는 용도, 0.5 월드 단위 반복).</summary>
    public static Sprite Stripes
    {
        get
        {
            if (stripes == null)
            {
                const int size = 32;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        px[y * size + x] = ((x + y) % 16) < 7 ? new Color32(255, 255, 255, 230) : new Color32(255, 255, 255, 60);
                tex.SetPixels32(px);
                tex.Apply();
                stripes = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size * 2f,
                    0, SpriteMeshType.FullRect);
            }
            return stripes;
        }
    }

    /// <summary>스프라이트 하나짜리 오브젝트를 만든다.</summary>
    public static SpriteRenderer Make(string name, Transform parent, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    static Sprite MakeCircle(bool isRing)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color32[size * size];
        float center = (size - 1) / 2f;
        float half = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / half;
                float edge = Mathf.Clamp01((1f - d) * half);
                float a = isRing ? edge * Mathf.Clamp01((d - 0.8f) * half) : edge;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
