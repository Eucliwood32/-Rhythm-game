using UnityEngine;

/// <summary>게임 카메라에 보이는 범위 계산 (NoteDisplay, BackgroundDisplay가 함께 쓴다).</summary>
public static class CameraUtil
{
    /// <summary>Main Camera에 보이는 폭과 높이(월드 단위). 카메라가 없으면 16:9, 높이 10으로 친다.</summary>
    public static void ViewSize(out float width, out float height)
    {
        Camera c = Camera.main;
        float size = c != null && c.orthographic ? c.orthographicSize : 5f;
        float aspect = c != null ? c.aspect : 16f / 9f;
        height = size * 2f;
        width = height * aspect;
    }

    /// <summary>화면 가운데에서 오른쪽 끝까지의 거리.</summary>
    public static float HalfWidth()
    {
        float w, h;
        ViewSize(out w, out h);
        return w / 2f;
    }

    public static Vector3 Position()
    {
        Camera c = Camera.main;
        return c != null ? c.transform.position : Vector3.zero;
    }
}
