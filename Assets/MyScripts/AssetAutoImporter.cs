// Assets/Editor/AssetAutoImporter.cs
// 에셋을 정해진 폴더에 넣기만 하면 임포트 설정·스프라이트 자르기를 자동으로 해주는 스크립트.
// MyScripts 폴더에 둬도 됨: 전체가 #if UNITY_EDITOR로 감싸져 있어서 빌드에는 안 들어감.
// 추가 패키지 필요 없음.
//
// 폴더 구조 (ROOT 아래 첫 폴더 이름으로 종류를 판단):
//   Assets/Arts/Backgrounds/ → 배경 (Max 4096, Repeat, 무압축)
//   Assets/Arts/Hikari/      → 일러스트 (Bilinear, 투명도 처리)
//   Assets/Arts/UI/          → UI (DialogueBox는 9-slice 테두리 자동)
//   Assets/Arts/Enemies/     → 도트 스프라이트 시트 (Point, 무압축, 정사각 프레임 자동 자르기, 피벗 = 캐릭터 몸 가운데)
//                             예: Assets/Arts/Enemies/Girl_1/Walk.png ~ Girl_3, Assets/Arts/Enemies/Minion_1/Run.png ~ Minion_3
//
// 메뉴: Tools > 에셋 자동 설정 > 전체 다시 적용  (설정값 바꾼 뒤 한 번 누르면 전부 재적용)

using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

#pragma warning disable 618 // spritesheet API가 최신 버전에서 "구버전" 경고를 띄우지만 정상 동작함

public class AssetAutoImporter : AssetPostprocessor
{
    // ───────── 팀원이 바꿔도 되는 값 ─────────
    const string ROOT = "Assets/Arts/";

    // 배경: 카메라 Orthographic Size 5(화면 높이 10유닛)에 높이 1088px이 꽉 차도록
    const float BG_PPU = 108.8f;
    const float ILLUST_PPU = 100f;
    const float UI_PPU = 100f;
    // 도트: 128px 프레임 기준. 캐릭터 실제 키가 ~70px이라 64면 약 1.1유닛
    const float PIXEL_PPU = 64f;

    // 대화창 9-slice 테두리 (왼쪽, 아래, 오른쪽, 위) px
    static readonly Vector4 DIALOGUE_BORDER = new Vector4(60, 55, 85, 125);

    // 적 스프라이트 피벗. 128px 프레임에서 캐릭터는 아래쪽 ~72px만 차지하고, 몸 가운데가 아래에서 약 27% 높이.
    // 피벗을 몸 가운데에 두면 레인 선에 맞추기 쉽고, 날아갈 때 몸 가운데를 축으로 돈다.
    static readonly Vector2 ENEMY_PIVOT = new Vector2(0.5f, 0.27f);
    // ────────────────────────────────────────

    static bool forceReslice;

    enum Kind { None, Background, Illustration, UI, PixelSheet }

    static Kind GetKind(string path)
    {
        path = path.Replace('\\', '/');
        if (!path.StartsWith(ROOT)) return Kind.None;
        string first = path.Substring(ROOT.Length).Split('/')[0];
        switch (first)
        {
            case "Backgrounds": return Kind.Background;
            case "Hikari": return Kind.Illustration;
            case "UI": return Kind.UI;
            case "Enemies": return Kind.PixelSheet;
            default: return Kind.None;
        }
    }

    void OnPreprocessTexture()
    {
        Kind kind = GetKind(assetPath);
        if (kind == Kind.None) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.sRGBTexture = true;

        switch (kind)
        {
            case Kind.Background:
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = BG_PPU;
                ti.wrapMode = TextureWrapMode.Repeat;   // 루프 스크롤용
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 4096;               // 기본 2048이면 흐려짐
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                SetFullRect(ti); // 타일링/UV 스크롤 시 필요
                break;

            case Kind.Illustration:
                ti.spriteImportMode = SpriteImportMode.Single;
                // 큰 원본(1500~2500px)을 화면에 작게 그리면 밉맵 없이는 계단·자글거림이 생긴다 → 일러스트만 밉맵 켬
                ti.mipmapEnabled = true;
                ti.spritePixelsPerUnit = ILLUST_PPU;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 4096;
                ti.textureCompression = TextureImporterCompression.Uncompressed;   // 압축하면 머리카락 선이 뭉개짐
                break;

            case Kind.UI:
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = UI_PPU;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Bilinear;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                SetFullRect(ti); // 9-slice에 필요
                if (Path.GetFileNameWithoutExtension(assetPath).Contains("DialogueBox"))
                    ti.spriteBorder = DIALOGUE_BORDER;
                break;

            case Kind.PixelSheet:
                if (!assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) return; // PSD 등은 무시
                ti.spriteImportMode = SpriteImportMode.Multiple;
                ti.spritePixelsPerUnit = PIXEL_PPU;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Point;       // 도트 안 뭉개지게
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                SliceStrip(ti);
                break;
        }
    }

    // spriteMeshType은 TextureImporter에 직접 없어서 설정 묶음(TextureImporterSettings)으로 바꿔야 함
    static void SetFullRect(TextureImporter ti)
    {
        var st = new TextureImporterSettings();
        ti.ReadTextureSettings(st);
        st.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(st);
    }

    // 가로 한 줄 시트를 정사각 프레임(높이 = 프레임 크기)으로 자름. Book.png(64px)도 자동 대응.
    void SliceStrip(TextureImporter ti)
    {
        if (!TryReadPngSize(assetPath, out int w, out int h) || h <= 0) return;
        int frame = h;
        int count = w / frame;
        if (count <= 0) return;

        var existing = ti.spritesheet;
        // 이미 같은 개수·같은 피벗으로 잘려 있으면 건드리지 않음 → 애니메이션/프리팹 참조가 안 깨짐
        if (!forceReslice && existing != null && existing.Length == count
            && existing[0].alignment == (int)SpriteAlignment.Custom
            && Vector2.Distance(existing[0].pivot, ENEMY_PIVOT) < 0.001f) return;

        string baseName = Path.GetFileNameWithoutExtension(assetPath);
        var metas = new SpriteMetaData[count];
        for (int i = 0; i < count; i++)
        {
            metas[i] = new SpriteMetaData
            {
                name = $"{baseName}_{i}",   // 이름이 같으면 Unity가 기존 참조를 유지함
                rect = new Rect(i * frame, 0, frame, frame),
                alignment = (int)SpriteAlignment.Custom,
                pivot = ENEMY_PIVOT
            };
        }
        ti.spritesheet = metas;
    }

    // PNG 헤더(IHDR)에서 가로·세로를 직접 읽음 (임포트 전이라 텍스처 크기를 아직 모름)
    static bool TryReadPngSize(string path, out int w, out int h)
    {
        w = h = 0;
        if (!path.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) return false;
        using (var fs = File.OpenRead(path))
        {
            var b = new byte[24];
            if (fs.Read(b, 0, 24) < 24) return false;
            w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return true;
        }
    }

    [MenuItem("Tools/에셋 자동 설정/전체 다시 적용")]
    static void ReapplyAll()
    {
        ReimportUnderRoot(false);
    }

    [MenuItem("Tools/에셋 자동 설정/적 스프라이트 강제로 다시 자르기 (참조 끊길 수 있음)")]
    static void ForceResliceAll()
    {
        if (!EditorUtility.DisplayDialog("강제 다시 자르기",
            "적 스프라이트를 전부 새로 자릅니다.\n프레임 개수가 바뀐 시트는 기존 애니메이션 참조가 끊길 수 있어요.", "진행", "취소"))
            return;
        ReimportUnderRoot(true);
    }

    static void ReimportUnderRoot(bool reslice)
    {
        string folder = ROOT.TrimEnd('/');
        if (!AssetDatabase.IsValidFolder(folder))
        {
            Debug.LogWarning($"[AssetAutoImporter] {folder} 폴더가 없음");
            return;
        }
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
        forceReslice = reslice;
        try
        {
            AssetDatabase.StartAssetEditing();
            foreach (var g in guids)
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            forceReslice = false;
        }
        Debug.Log($"[AssetAutoImporter] {guids.Length}개 텍스처 설정 적용 완료");
    }
}
#endif
