using System;
using System.Collections.Generic;

public enum NoteType
{
    Tap,      // 단노트
    Long,     // 롱노트
    Barrage   // 연타 구간
}

/// <summary>채보에 들어가는 노트 하나.</summary>
[Serializable]
public class NoteData
{
    public double time;      // 판정선에 닿아야 하는 곡 시간(초)
    public Lane lane;        // Upper(A키) / Lower(B키)
    public NoteType type;    // 단노트 / 롱노트 / 연타
    public double duration;  // 롱노트·연타 길이(초). 단노트는 0
    public int count;        // 연타: 필요한 입력 횟수 (0이면 Judge가 길이로 계산)

    /// <summary>같은 값을 가진 새 노트. (필드가 늘면 여기만 고친다)</summary>
    public NoteData Clone()
    {
        return new NoteData { time = time, lane = lane, type = type, duration = duration, count = count };
    }

    /// <summary>정렬 기준: 시간순, 같은 시각이면 A 레인 먼저.</summary>
    public static int Compare(NoteData a, NoteData b)
    {
        int c = a.time.CompareTo(b.time);
        return c != 0 ? c : a.lane.CompareTo(b.lane);
    }
}

/// <summary>채보 계산 도우미.</summary>
public static class ChartMath
{
    /// <summary>시각을 1ms 단위로 반올림한다.</summary>
    public static double RoundMs(double t)
    {
        return Math.Round(t * 1000.0) / 1000.0;
    }

    /// <summary>노트 목록을 시간순(같은 시각이면 A 먼저)으로 정렬한다.</summary>
    public static void SortNotes(List<NoteData> notes)
    {
        notes.Sort(NoteData.Compare);
    }
}

/// <summary>채보 파일(JSON) 전체. JsonUtility로 읽고 쓴다.</summary>
[Serializable]
public class ChartData
{
    public List<NoteData> notes = new List<NoteData>();
}
