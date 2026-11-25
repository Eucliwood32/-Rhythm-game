# 게임물리와 상호작용 팀 프로젝트

Unity 6 기반 게임 제작 텀 프로젝트. 게임 제목은 미정이며, 2D 횡스크롤 리듬게임을 잠정안으로 검토 중입니다.

- **엔진:** Unity 6000.5.1f1
- **대상 플랫폼:** Windows 64-bit
- **프로젝트 기반:** 강의에서 제공한 Template Project

## 실행

1. 저장소를 클론합니다.
2. Unity Hub에서 **6000.5.1f1** 버전으로 프로젝트 폴더를 엽니다.
3. `Assets/Scenes/SampleScene.unity` 파일을 열어 작업합니다.

## 문서

- [게임 기획서 (GDD v0.1)](docs/GDD.md)
- [기여 안내 및 과제 준수사항](CONTRIBUTING.md)

## 검사

`main`에 푸시하거나 PR을 열면 GitHub Actions가 프로젝트의 기본 구조를 검사합니다.

로컬에서는 `python scripts/validate_project.py`로 같은 검사를 실행할 수 있습니다. Unity 컴파일이나 실행 테스트는 포함되지 않습니다.
