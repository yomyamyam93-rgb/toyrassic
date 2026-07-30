# 노드판 + 레벨 20 구현 계획 (알 원정 1단계)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans
> to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Tab 메뉴에 바퀴형 노드판(~44노드)이 뜨고, 레벨업 포인트로 인접 노드를
찍으면 펫·캐릭터 수치가 실제로 변한다. 렙 20까지 실제 플레이로 테스트 가능.

**Architecture:** `NodeMods`(정적 배수 묶음)를 기존 스탯 계산 지점들이 곱해 쓴다.
노드는 씬에 실존하는 UI 버튼(`NodeButton`) — 그래프(이웃 목록)는 인스펙터에서
잇는다. 모양은 데이터라 나중에 포이처럼 자란다. 행동 노드 4개(연쇄 소환·도발
맥동·사기 진작·관통 화살)는 **이 계획에 없다** — 자리만 만들고 2단계 계획에서.

**Tech Stack:** Unity 6000.3.20f1 · uGUI · 새 Input System. 테스트 프레임워크
없음 — 검증은 컴파일 + 유니티에서 눈으로 (프로젝트 원칙).

## Global Constraints

- 새 미터 상수엔 `* WorldScale.K` (CLAUDE.md).
- 코드에 종·노드 이름 문자열 분기 금지 — 분류는 인스펙터 드롭다운.
- Tools 메뉴는 3개 유지 — 노드판 초기 배치는 MCP `unity_execute_code` 1회로.
- 금지 노드 (무조건부 이속+ 등, node-catalog §0) 는 만들지 않는다.
- 검증 기준: "유니티 열고 눈으로 보이는 결과".

---

### Task 1: NodeMods — 배수 묶음과 소비 지점

**Files:**
- Create: `Assets/Scripts/NodeBoard.cs` (NodeMods + NodeEffect enum)
- Modify: `Assets/Scripts/PetUnit.cs` (스탯 계산 3곳), `Assets/Scripts/SkillSystem.cs:294` 부근(throwBudget), `Assets/Scripts/PlayerGather.cs` (캐릭터 피해·차징)

**Interfaces:**
- Produces: `static class NodeMods` — `petDmg, petHp, petAtkSpeed, meleeArm,
  rangedRange, charDmg, chargeSpeed, throwBudgetMul` (전부 float, 기본 1f) ·
  `petDmgFlat` 류는 없음(배수만) · `noKiting`(bool, 기본 false) ·
  `critChance`(float 0), `critMul`(2f) · `Reset()`.

- [ ] **Step 1: NodeBoard.cs 에 NodeMods 작성**

```csharp
/// 노드판이 켠 효과의 집합 — 게임 코드는 여기 숫자만 읽는다 (노드를 모른다).
public static class NodeMods
{
    public static float petDmg = 1f, petHp = 1f, petAtkSpeed = 1f;
    public static float meleeArm = 1f, rangedRange = 1f;
    public static float charDmg = 1f, chargeSpeed = 1f, throwBudgetMul = 1f;
    public static float critChance = 0f, critMul = 2f;
    public static bool noKiting = false;          // 거점 포격 — 카이팅 포기
    public static void Reset()
    {
        petDmg = petHp = petAtkSpeed = meleeArm = rangedRange = 1f;
        charDmg = chargeSpeed = throwBudgetMul = 1f;
        critChance = 0f; critMul = 2f; noKiting = false;
    }
}
```

- [ ] **Step 2: 소비 지점 연결** — 각 자리에 배수 한 번씩:
  - `PetUnit.cs` 피해 주는 자리(`Strike`의 dmg 계산, `PatternDmg` 곱하는 곳):
    `dmg *= NodeMods.petDmg;` + 치명타: `if (Random.value < NodeMods.critChance) dmg *= NodeMods.critMul;`
    ★내 편(`team == Team.Player`)일 때만 — 야생에 노드가 걸리면 안 된다.
  - 체력 자리(`maxHp = vit * HpPerVit` 3곳 중 **스폰 초기화 지점**): 내 편이면 `* NodeMods.petHp`
  - 공속(`647줄 주석` 간격 계산): 내 편이면 간격 `/ NodeMods.petAtkSpeed`
  - `TierArm` 소비 지점: 내 편 근접이면 `* NodeMods.meleeArm`
  - `shootReach` 소비 지점: 내 편 원거리면 `* NodeMods.rangedRange`
  - 카이팅 판정(`KitingPattern`/kite 로직): `if (NodeMods.noKiting && team == Team.Player) 카이팅 안 함`
  - `SkillSystem.cs` 투척 마릿수: `CountFor(Mathf.RoundToInt(throwBudget * NodeMods.throwBudgetMul), ...)`
  - `PlayerGather.cs` 무기 피해에 `* NodeMods.charDmg`, 차징 시간에 `/ NodeMods.chargeSpeed`

- [ ] **Step 3: 컴파일 확인** — MCP `unity_get_compilation_errors` 0개.
      기본값 전부 1이므로 **게임이 그대로여야 한다** (F5 한 판으로 확인).
- [ ] **Step 4: Commit** — `노드 1/6 — NodeMods 배수 묶음, 소비 지점 연결 (기본값 무해)`

### Task 2: PlayerLevel — 노드 포인트 + 어려운 곡선

**Files:**
- Modify: `Assets/Scripts/PlayerLevel.cs`

**Interfaces:**
- Consumes: 기존 `PlayerLevel.Points`, `Gain()`, 레벨업 토스트.
- Produces: `PlayerLevel.NodePoints`(int) — 레벨업마다 +1. 기존 스탯 `Points`/`Spend`
  는 **잠근다** (노드 트리가 스탯을 대체 — 스탯 중간 노드가 그 자리).

- [ ] **Step 1:** `NodePoints` 추가, 레벨업 시 `NodePoints++`. 기존 `Points` 적립 중단
      (필드는 남김 — 저장 호환). 토스트 문구를 `"레벨 업! Lv.{n} — 노드 포인트 {p} (Tab → 노드)"` 로.
- [ ] **Step 2:** XP 곡선 조임 — 목표: 밴드 1 사냥 페이스로 렙 5까지 ~15분,
      렙 20 은 원정 완주 즈음. 기존 지수 곡선의 성장률 상수를 인스펙터로 노출하고
      기본값을 한 단계 올린다 (정확한 값은 플레이 측정 후 — 상수 위치만 만들어 둠).
- [ ] **Step 3:** 컴파일 + 야생 하나 잡아 토스트 확인.
- [ ] **Step 4: Commit** — `노드 2/6 — 레벨업이 노드 포인트를 준다 (스탯 직접 분배 잠금)`

### Task 3: NodeButton — 그래프 규칙과 저장

**Files:**
- Modify: `Assets/Scripts/NodeBoard.cs` (NodeButton, NodeBoardUI 추가)

**Interfaces:**
- Produces: `class NodeButton : MonoBehaviour` — 인스펙터: `id`(string),
  `효과`(NodeEffect 드롭다운), `수치`(float), `이웃`(NodeButton[]), `시작노드`(bool).
  `public bool Picked`. `class NodeBoardUI` — 판 루트, `RebuildMods()`(찍힌 노드
  전부 순회해 NodeMods.Reset 후 재적용), PlayerPrefs `"nodes"` 에 찍힌 id CSV 저장/복원.

- [ ] **Step 1: NodeEffect enum + NodeButton**

```csharp
public enum NodeEffect
{   // ★순서 바꾸지 말 것 — 씬에 정수로 저장된다. 새 효과는 뒤에 붙인다
    없음,            // 자리만 (행동 노드 자리표시)
    펫피해, 펫체력, 펫공속, 근접팔, 원거리사거리,
    캐릭힘, 캐릭민첩, 캐릭체력, 차징속도, 캐릭피해,
    투척예산, 치명타확률, 카이팅포기,
}
```

  NodeButton.OnClick: `NodePoints > 0` && (시작노드 || 이웃 중 Picked 존재) 면
  Picked=true, NodePoints--, `NodeBoardUI.RebuildMods()`. 버튼 색: 안 찍힘=회색 ·
  찍을 수 있음=흰 테두리 · 찍힘=금색 (UIStyle 팔레트 재사용).
- [ ] **Step 2: RebuildMods** — switch(효과) 로 NodeMods 에 곱/더하기. 캐릭 스탯
      3종은 기존 `PlayerLevel.Spend` 가 하던 적용 함수를 직접 호출.
- [ ] **Step 3:** 저장/복원 — PlayerPrefs CSV, `Start()` 에서 복원 후 RebuildMods.
- [ ] **Step 4:** 컴파일 확인 + Commit — `노드 3/6 — 그래프 찍기 규칙(인접)과 저장`

### Task 4: 노드판 UI — 바퀴 배치 (씬에 실존)

**Files:**
- Modify: `Assets/Scripts/MenuUI.cs` (탭에 "노드" 판 추가)
- Scene: MenuUI 캔버스 밑에 `NodeBoard` 판 + 버튼 44개 (MCP 1회 실행으로 생성)

- [ ] **Step 1:** MenuUI 에 노드 탭 추가 (인벤/스탯/제작과 같은 문법 — 기존 탭 코드
      패턴 그대로). 스탯 탭의 포인트 분배 UI 는 숨긴다 (Task 2 와 짝).
- [ ] **Step 2:** MCP `unity_execute_code` 로 바퀴 생성 — 안링 12(반지름 140px) ·
      스포크 8 · 밖링 16(280px) · 중형 4(360px) · 키스톤 4(420px), 이웃 배선까지
      코드로. 생성 후 씬 저장. **이후 수정은 에디터에서 손으로** (씬 실존 원칙).
- [ ] **Step 3:** 노드 효과·수치 배정 (node-catalog §2 표 그대로):
      안링 = 캐릭힘/민첩/체력 + 펫 수치 소폭(+8%) 12개 ·
      밖링 = 구간별 계열 소노드(+10~15%) 16개 ·
      중형 4 = `없음`(자리표시: 연쇄 소환·도발 맥동·사기 진작·관통 화살, 이름만) ·
      키스톤 4 = Task 5.
- [ ] **Step 4:** 눈 확인 — Tab → 노드, 바퀴가 보이고 인접 규칙대로 찍힌다.
      찍으면 피해 숫자가 커진다 (쇼케이스 허수아비로 전후 비교).
- [ ] **Step 5: Commit** — `노드 4/6 — 바퀴판 44노드 씬 배치, Tab 통합`

### Task 5: 키스톤 4 (수치형)

**Files:**
- Modify: `Assets/Scripts/NodeBoard.cs` (RebuildMods 의 키스톤 조합)

키스톤은 NodeEffect 조합으로 표현 (별도 코드 최소):
- **끝없는 무리** = 투척예산 ×1.6 + 펫피해 ×0.75
- **왕의 소수** = 투척예산 ×0.5 + 펫피해 ×1.7 + 펫체력 ×1.7
- **거점 포격** = 원거리사거리 ×1.25 + 펫피해 ×1.5(원거리만*) + 카이팅포기
  (*프로토타입은 전 펫 피해로 단순화 — 원거리 전용 분리는 2단계)
- **우두머리 사냥** = 캐릭피해 ×1.6 + 치명타확률 0.15 + 투척예산 ×0.7

- [ ] **Step 1:** 키스톤용 `키스톤` NodeEffect 값 4개 추가(enum 뒤에) + RebuildMods 조합.
- [ ] **Step 2:** 눈 확인 — 끝없는 무리 찍고 Q 투척 마릿수 증가 확인 ·
      왕의 소수 찍고 마릿수 절반+튼튼함 확인.
- [ ] **Step 3: Commit** — `노드 5/6 — 키스톤 4종 (수치 조합)`

### Task 6: 측정 두 판 — 캐릭터↔펫 간극

**Files:**
- Modify: `Assets/Scripts/StressTest.cs`

- [ ] **Step 1:** F8(야생만) 변형 — Shift+F8: 낙오 M **1마리**만 소환.
      한 번 더 누르면 3마리. (기존 F8 문법 재사용)
- [ ] **Step 2:** 측정 — 목표: 1마리엔 잔여 30~50% 승 / 3마리엔 패.
      어긋나면 조절 손잡이는 **야생 쪽이 아니라 캐릭터 무기 수치** (펫 스탯은
      리그로 잡은 값이라 건드리지 않는다 — "캐릭터를 펫에 맞춘다").
- [ ] **Step 3:** 결과를 CLAUDE.md 밸런스 절에 기록 + Commit — `노드 6/6 — 간극 측정 두 판`

---

## Self-Review 메모

- 스펙 커버리지: 노드판·레벨 20·간극 측정 = 이 계획 / 행동 노드 4종·밴드 스폰·
  둥지·부화터 = 후속 계획 (의도된 분할).
- 타입 일관성: NodeMods 필드명은 Task 1 정의를 3·5 가 그대로 쓴다.
- 실행자는 각 소비 지점의 정확한 줄을 grep 으로 찾는다 (기호명 명시됨:
  `PatternDmg`·`TierArm`·`shootReach`·`KitingPattern`·`CountFor`).
