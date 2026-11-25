using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 메뉴 Tools > 게임 씬 구성.
/// 게임에 필요한 오브젝트와 컴포넌트를 지금 열린 씬에 만들고, 서로의 연결 칸을 채운 뒤 씬을 저장한다.
/// 이미 있는 오브젝트·컴포넌트·연결은 그대로 두고 빠진 것만 채우므로 여러 번 눌러도 된다.
/// 적·주인공 그림(Battle Zone 에셋)도 칸이 비어 있으면 자동 채우기로 넣는다.
/// </summary>
public class GameSceneSetup : EditorWindow
{
    // 오브젝트 이름과 거기에 붙일 컴포넌트. 컴포넌트가 씬 어딘가에 이미 있으면 그 오브젝트를 그대로 쓴다.
    static readonly (string name, Type[] components)[] Layout =
    {
        ("Game", new[] { typeof(MusicPlayer), typeof(RhythmInput), typeof(Composer), typeof(Judge), typeof(Referee), typeof(GameFlow) }),
        ("Notes", new[] { typeof(NoteDisplay) }),
        ("Background", new[] { typeof(BackgroundDisplay) }),
        ("Player", new[] { typeof(PlayerAvatar) }),
        ("Boss", new[] { typeof(BossDisplay) }),
        ("Students", new[] { typeof(StudentLauncher) }),
        ("HUD", new[] { typeof(GameHud) }),
        ("Debug", new[] { typeof(DebugOverlay) }),
        // 효과음은 Game과 다른 오브젝트에 둔다. 같은 오브젝트면 곡과 AudioSource를 같이 써서, 타격음의 음높이 흔들기가 곡에도 걸린다.
        ("Sfx", new[] { typeof(HitSfx) }),
    };

    AudioClip clip;
    TextAsset chart;
    bool saveScene = true;
    Vector2 scroll;

    [MenuItem("Tools/게임 씬 구성")]
    static void Open()
    {
        var w = GetWindow<GameSceneSetup>("게임 씬 구성");
        w.minSize = new Vector2(360, 360);
        w.ReadCurrentFiles();
    }

    void OnFocus()
    {
        ReadCurrentFiles();
    }

    /// <summary>씬에 이미 들어 있는 곡·채보를 칸에 보여준다.</summary>
    void ReadCurrentFiles()
    {
        var music = FindAnyObjectByType<MusicPlayer>();
        if (music != null && clip == null)
        {
            clip = new SerializedObject(music).FindProperty("clip")?.objectReferenceValue as AudioClip;
        }
        var composer = FindAnyObjectByType<Composer>();
        if (composer != null && chart == null)
        {
            chart = new SerializedObject(composer).FindProperty("chartJson")?.objectReferenceValue as TextAsset;
        }
        Repaint();
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("지금 열린 씬: " + EditorSceneManager.GetActiveScene().name, EditorStyles.boldLabel);
        GUILayout.Space(4);

        EditorGUILayout.LabelField("오브젝트", EditorStyles.boldLabel);
        foreach (var entry in Layout)
        {
            int have = 0;
            foreach (Type t in entry.components)
            {
                if (FindAnyObjectByType(t) != null) have++;
            }
            string state = have == entry.components.Length ? "있음" : have == 0 ? "없음 → 만듦" : "일부 없음 → 채움";
            EditorGUILayout.LabelField("  " + entry.name, state);
        }
        if (Camera.main == null)
        {
            EditorGUILayout.HelpBox("Main Camera가 없습니다. 태그가 MainCamera인 카메라가 있어야 노트와 화면이 보입니다.", MessageType.Warning);
        }

        GUILayout.Space(8);
        EditorGUILayout.LabelField("파일 (비워 두면 지금 값 유지)", EditorStyles.boldLabel);
        clip = (AudioClip)EditorGUILayout.ObjectField("기본 곡", clip, typeof(AudioClip), false);
        chart = (TextAsset)EditorGUILayout.ObjectField("기본 채보 JSON", chart, typeof(TextAsset), false);
        EditorGUILayout.HelpBox("스테이지별 곡·채보는 Game 오브젝트의 GameFlow › Stages에서 넣습니다. 스테이지 칸이 비어 있으면 여기 넣은 기본 곡·채보를 씁니다.", MessageType.None);

        GUILayout.Space(8);
        saveScene = EditorGUILayout.ToggleLeft("끝나면 씬 저장", saveScene);

        GUILayout.Space(4);
        if (GUILayout.Button("구성하기", GUILayout.Height(32)))
        {
            string report = Build(clip, chart, saveScene);
            ShowNotification(new GUIContent("구성 완료"));
            Debug.Log("[게임 씬 구성]\n" + report);
        }

        EditorGUILayout.EndScrollView();
    }

    /// <summary>오브젝트를 만들고 연결한다. 한 번의 Ctrl+Z로 모두 되돌릴 수 있다. 무엇을 했는지 글로 돌려준다.</summary>
    public static string Build(AudioClip clip, TextAsset chart, bool save)
    {
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("게임 씬 구성");
        var log = new List<string>();

        // 1. 오브젝트와 컴포넌트
        var all = new List<Component>();
        foreach (var entry in Layout)
        {
            GameObject go = null;
            foreach (Type t in entry.components)
            {
                var existing = FindAnyObjectByType(t) as Component;
                if (existing != null) { go = existing.gameObject; break; }
            }
            if (go == null)
            {
                go = GameObject.Find(entry.name);
            }
            if (go == null)
            {
                go = new GameObject(entry.name);
                Undo.RegisterCreatedObjectUndo(go, "게임 씬 구성");
                log.Add("만듦: " + entry.name);
            }

            foreach (Type t in entry.components)
            {
                var c = FindAnyObjectByType(t) as Component;
                if (c == null)
                {
                    c = Undo.AddComponent(go, t);
                    log.Add("붙임: " + go.name + " › " + t.Name);
                }
                all.Add(c);
            }
        }

        // 2. 비어 있는 연결 칸 채우기 (칸의 타입 이름으로 씬의 컴포넌트를 찾는다)
        var byType = new Dictionary<string, Component>();
        foreach (Component c in all) byType[c.GetType().Name] = c;

        foreach (Component c in all)
        {
            var so = new SerializedObject(c);
            SerializedProperty p = so.GetIterator();
            bool changed = false;
            while (p.NextVisible(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (p.objectReferenceValue != null) continue;
                string typeName = RefTypeName(p.type);
                if (typeName == null || !byType.TryGetValue(typeName, out Component target)) continue;
                if (target == c) continue;
                p.objectReferenceValue = target;
                changed = true;
                log.Add("연결: " + c.GetType().Name + "." + p.name + " ← " + typeName);
            }
            if (changed) so.ApplyModifiedProperties();
        }

        // 3. 그림 (칸이 비어 있을 때만 자동 채우기. 직접 넣은 그림은 건드리지 않는다)
        if (byType.TryGetValue("NoteDisplay", out Component nd))
        {
            var so = new SerializedObject(nd);
            SerializedProperty list = so.FindProperty("enemies");
            if (list != null && list.arraySize == 0)
            {
                ((NoteDisplay)nd).AutoFillEnemies();
                int count = new SerializedObject(nd).FindProperty("enemies").arraySize;
                log.Add(count > 0 ? "그림: 적 " + count + "명 채움" : "그림: 적 그림을 찾지 못함 (Battle Zone 에셋의 Characters 폴더 확인)");
            }
        }
        if (byType.TryGetValue("PlayerAvatar", out Component pa))
        {
            SerializedProperty stand = new SerializedObject(pa).FindProperty("standSprite");
            if (stand != null && stand.objectReferenceValue == null)
            {
                ((PlayerAvatar)pa).AutoFillSprites();
                bool ok = new SerializedObject(pa).FindProperty("standSprite").objectReferenceValue != null;
                log.Add(ok ? "그림: 주인공 채움" : "그림: 주인공 그림을 찾지 못함 (PlayerAvatar의 Character Name 확인)");
            }
        }

        // 4. 기본 곡·채보
        if (clip != null) SetField(byType, "MusicPlayer", "clip", clip, log);
        if (chart != null) SetField(byType, "Composer", "chartJson", chart, log);

        Undo.CollapseUndoOperations(group);

        // 5. 저장
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        if (save)
        {
            bool saved = string.IsNullOrEmpty(scene.path)
                ? EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()
                : EditorSceneManager.SaveScene(scene);
            log.Add(saved ? "씬 저장함: " + scene.path : "씬을 저장하지 않음 (Ctrl+S로 저장하세요)");
        }

        if (log.Count == 0) log.Add("빠진 것이 없어서 바꾼 것 없음");
        return string.Join("\n", log);
    }

    static void SetField(Dictionary<string, Component> byType, string component, string field, UnityEngine.Object value, List<string> log)
    {
        if (!byType.TryGetValue(component, out Component c)) return;
        var so = new SerializedObject(c);
        SerializedProperty p = so.FindProperty(field);
        if (p == null || p.objectReferenceValue == value) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedProperties();
        log.Add("파일: " + component + "." + field + " ← " + value.name);
    }

    /// <summary>"PPtr&lt;$Judge&gt;" 같은 칸 타입 이름에서 "Judge"를 꺼낸다.</summary>
    static string RefTypeName(string propertyType)
    {
        if (string.IsNullOrEmpty(propertyType)) return null;
        int start = propertyType.IndexOf('$');
        int end = propertyType.LastIndexOf('>');
        if (start < 0 || end <= start) return null;
        return propertyType.Substring(start + 1, end - start - 1);
    }
}
