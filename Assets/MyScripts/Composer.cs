using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 채보(어느 시각에 어느 레인을 눌러야 하는지)를 보관하고, 레인별로 시간순 노트 목록을 제공한다.
/// 채보 JSON 파일이 지정되면 그것을 쓰고, 비어 있으면 아래 테스트 노트를 쓴다.
/// GameFlow는 스테이지마다 Load()로 채보를 바꾼다.
/// </summary>
public class Composer : MonoBehaviour
{
    [Tooltip("기본 채보 JSON 파일. 비워두면 아래 테스트 노트를 사용한다.")]
    [SerializeField] TextAsset chartJson;

    [Header("테스트 노트 (Inspector에서 직접 수정 가능)")]
    [SerializeField] List<NoteData> testNotes = new List<NoteData>
    {
        new NoteData { time = 3.0,  lane = Lane.Upper },
        new NoteData { time = 4.0,  lane = Lane.Lower },
        new NoteData { time = 5.0,  lane = Lane.Upper },
        new NoteData { time = 6.0,  lane = Lane.Lower },
        new NoteData { time = 7.0,  lane = Lane.Upper },
        new NoteData { time = 7.5,  lane = Lane.Lower },
        new NoteData { time = 8.0,  lane = Lane.Upper },
        new NoteData { time = 8.0,  lane = Lane.Lower },   // 동시치기 테스트
    };

    readonly List<NoteData>[] laneNotes = { new List<NoteData>(), new List<NoteData>() };
    bool built;

    void Awake()
    {
        EnsureBuilt();
    }

    /// <summary>해당 레인의 노트를 시간순으로 돌려준다.</summary>
    public IReadOnlyList<NoteData> GetNotes(Lane lane)
    {
        EnsureBuilt();
        return laneNotes[(int)lane];
    }

    /// <summary>두 레인을 합친 노트 수.</summary>
    public int TotalCount
    {
        get { EnsureBuilt(); return laneNotes[0].Count + laneNotes[1].Count; }
    }

    /// <summary>
    /// 채보를 다시 읽는다. json이 null이면 Inspector의 기본 채보(없으면 테스트 노트)를 쓴다.
    /// </summary>
    public void Load(TextAsset json)
    {
        built = true;
        laneNotes[0].Clear();
        laneNotes[1].Clear();

        TextAsset use = json != null ? json : chartJson;
        List<NoteData> source = testNotes;

        if (use != null)
        {
            ChartData chart = null;
            try
            {
                chart = JsonUtility.FromJson<ChartData>(use.text);
            }
            catch (System.Exception)
            {
                chart = null;
            }

            if (chart != null && chart.notes != null)
            {
                source = chart.notes;
            }
            else
            {
                Debug.LogError($"[Composer] 채보 JSON '{use.name}'을 읽지 못했습니다. 테스트 노트를 사용합니다.");
            }
        }

        foreach (var note in source)
        {
            // 원본(테스트 노트, JSON)과 분리해서 게임 중 변경이 원본에 남지 않게 복사한다.
            laneNotes[(int)note.lane].Add(note.Clone());
        }
        foreach (var list in laneNotes)
        {
            ChartMath.SortNotes(list);
        }

        Debug.Log($"[Composer] 노트 로드 완료: Upper {laneNotes[0].Count}개 / Lower {laneNotes[1].Count}개");
    }

    void EnsureBuilt()
    {
        if (!built) Load(null);
    }
}
