# 기여 안내

## 개발 환경

- Unity **6000.5.1f1** 사용. 다른 버전으로 프로젝트를 저장하지 않습니다.
- 강의에서 배포한 Template Project를 유지합니다.
- `Assets/Scenes/SampleScene.unity` 파일은 메인 씬입니다. 이름을 바꾸거나 삭제하지 않습니다.
- `SampleScene`의 `Main Camera`는 삭제하지 않습니다.
- Unity에서 생성하는 `.meta` 파일은 대응하는 에셋과 함께 커밋합니다. `Library/` 등 생성 파일은 커밋하지 않습니다.

## 과제 준수사항

- 직접 작성한 C# 스크립트: `Assets/MyScripts/`
- 외부에서 가져와 수정한 스크립트: `Assets/OtherScripts/` (출처 및 이용 조건 확인)
- 유료 에셋·리소스 사용 금지.
- 기존 게임 프로젝트를 가져와 수정하는 방식 금지.
- 별도의 개인 과제 코드는 이 팀 저장소에 올리지 않습니다.

위 규정과 다른 해석이 필요한 경우 강의자료와 교수자 안내를 우선합니다.

## 작업 방식

1. 작업 전 `main`을 최신 상태로 맞추고 기능별 브랜치를 만듭니다.
2. 변경 범위를 작게 유지합니다. 같은 씬이나 프리팹을 동시에 편집할 때는 먼저 조율합니다.
3. 변경 사항을 Unity에서 확인한 뒤 푸시합니다.
4. PR에는 작업 내용과 확인 결과를 간단히 적습니다. **PR 생성과 리뷰는 현재 의무가 아닙니다.**

`main` 브랜치는 강제 푸시 및 삭제가 차단되어 있습니다.

## 문서 관리

- `README.md`: 프로젝트 소개와 실행 방법
- `docs/GDD.md`: 게임 설계 및 확정된 기획
- `CONTRIBUTING.md`: 작업 절차와 과제 준수사항

기획이나 작업 방식이 바뀌면 해당 문서를 수정합니다. 단순 수정 내역은 커밋 또는 PR에 남깁니다. 기존 문서로 충분하면 새 문서를 만들지 않습니다.

## Unity 파일 충돌

Unity 6000.5의 Smart Merge(`UnityYAMLMerge`)를 Git 병합 도구로 사용할 수 있습니다.

Windows PowerShell에서 저장소 루트를 기준으로:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\docs\setup-unity-merge.ps1
```

충돌이 발생하면 해당 파일에 `git mergetool -- <파일 경로>`를 실행합니다. 병합된 씬·프리팹은 Unity에서 확인합니다.

참고: [Unity Smart Merge](https://docs.unity3d.com/6000.5/Documentation/Manual/SmartMerge.html)
