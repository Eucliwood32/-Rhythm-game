using UnityEngine;

/// <summary>
/// 화면 흔들림. 어디서든 CameraShake.Shake(세기, 시간)으로 부른다.
/// Main Camera에 자동으로 붙고, 흔들림이 끝나면 원래 위치로 돌아간다.
/// </summary>
public class CameraShake : MonoBehaviour
{
    Vector3 basePosition;
    float strength;
    float duration;
    float timeLeft;
    bool shaking;

    public static void Shake(float shakeStrength, float shakeDuration)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        var shaker = cam.GetComponent<CameraShake>();
        if (shaker == null) shaker = cam.gameObject.AddComponent<CameraShake>();
        shaker.Begin(shakeStrength, shakeDuration);
    }

    void Begin(float s, float d)
    {
        if (!shaking) basePosition = transform.position;
        // 더 센 흔들림이 우선
        if (!shaking || s >= strength * (timeLeft / Mathf.Max(0.0001f, duration)))
        {
            strength = s;
            duration = d;
            timeLeft = d;
        }
        shaking = true;
    }

    void LateUpdate()
    {
        if (!shaking) return;

        timeLeft -= Time.unscaledDeltaTime;
        if (timeLeft <= 0f)
        {
            transform.position = basePosition;
            shaking = false;
            return;
        }

        float k = strength * (timeLeft / duration);
        transform.position = basePosition + new Vector3(Random.Range(-k, k), Random.Range(-k, k), 0f);
    }
}
