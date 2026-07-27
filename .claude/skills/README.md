# 이 폴더는 뭔가요

토이라기 프로젝트 **전용** 클로드 스킬입니다. 이 폴더(`toyrassic`)에서 클로드를 열 때만 적용되고,
다른 프로젝트(월세앱·홈페이지 등) 작업에는 끼어들지 않습니다.

## 출처

[Nice-Wolf-Studio/unity-claude-skills](https://github.com/Nice-Wolf-Studio/unity-claude-skills) · MIT 라이선스
(라이선스 전문: `LICENSE-unity-claude-skills.txt`)

원본 35개 중 **7개만** 골라 넣었습니다. 마크다운 문서뿐이고 실행되는 코드는 없습니다.

## 왜 이 7개인가 — 우리가 실제로 반복한 버그 기준

커밋 301개를 훑어 "같은 종류를 또 고친" 사례를 세어 고른 것입니다.

| 스킬 | 우리 문제 |
|---|---|
| `unity-3d-math` | 무기 축·손 위치 오류(22회), 타격 판정 각도(8회). "Vector3.Angle은 항상 양수", 블렌더=오른손/유니티=왼손 좌표계 |
| `unity-data-driven` | 씬에 저장된 옛 값이 새 설정을 덮어씀(7회). ScriptableObject가 근본 해법 |
| `unity-lifecycle` | Awake/OnEnable 초기화 순서, fake-null, 지형이 Awake 때 없어서 늦게 다시 잡는 문제 |
| `unity-input-correctness` | 새 Input System 전용 프로젝트. IsPressed vs WasPressedThisFrame 혼동 |
| `unity-state-machines` | `PetUnit.cs` 1,306줄 통짜 → `docs/전투v4` 1단계가 이걸 쪼개는 것 |
| `unity-editor-tools` | 커스텀 인스펙터 10개, 에디터 도구 15개 보유 |
| `unity-performance` | 타격 순간 GC 스파이크(4회 연속 커밋) |

## 일부러 뺀 것

- **`unity-ui-patterns`** — "UI Toolkit 전용"인데 우리는 UGUI를 코드로 생성한다(`docs/UI_가이드.md` §9). 정면충돌
- **`unity-scene-assets`** — Addressables를 권장하는데 우리는 `Resources.Load` 쓰는 한 씬짜리. 과잉
- 2D·XR·멀티플레이·ECS/DOTS·물리 등 21개 — 우리 프로젝트와 무관
  (특히 **물리는 이 프로젝트가 아예 안 쓴다** — Rigidbody·Collider 0개, 전부 손으로 계산)

## 주의

이 스킬들은 Unity 6.3 기준이고 2026-03 이후 갱신이 없습니다. 검증된 대형 프로젝트가 아니라
소규모 스튜디오 문서이니, 여기 적힌 내용과 이 저장소의 `CLAUDE.md`가 충돌하면 **`CLAUDE.md`가 이깁니다.**
