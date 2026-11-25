using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 채보 편집기가 다루는 채보 프로젝트 한 개. 곡과 노트를 함께 담는다.
/// 게임(Composer)은 이 파일이 아니라 편집기에서 내보낸 JSON을 읽는다.
/// Project 창에서 우클릭 > Create > Rhythm > 채보 프로젝트 로도 만들 수 있다.
/// </summary>
[CreateAssetMenu(menuName = "Rhythm/채보 프로젝트", fileName = "NewChart")]
public class ChartProject : ScriptableObject
{
    [Tooltip("이 채보의 곡")]
    public AudioClip clip;

    [Tooltip("BPM 구간 (시간순). 첫 구간의 시작 시각이 곡의 첫 박이다.")]
    public List<TempoPoint> tempo = new List<TempoPoint> { new TempoPoint { time = 0, bpm = 120 } };

    [Tooltip("노트 목록 (시간순)")]
    public List<NoteData> notes = new List<NoteData>();
}
