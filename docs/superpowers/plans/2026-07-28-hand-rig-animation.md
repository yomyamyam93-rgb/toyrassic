# HandRig 도입 및 손·무기 모션 애니메이션 전환 — 구현 계획

> **작업자에게:** 이 계획은 `superpowers:executing-plans` 로 단계별 실행한다.
> 체크박스(`- [ ]`)로 진행을 추적한다.

**목표:** 손·무기 스윙을 인스펙터 숫자가 아니라 유니티 애니메이션 창에서 저작할 수 있게 만든다.

**구조:** 찌그러지지 않는 `HandRig` 를 플레이어의 형제로 두고, 손·활을 그 밑의 실존 자식으로
옮긴다. 그 뒤 `Animator` 가 손 트랜스폼을 소유하고, 코드는 신호만 보낸다.

**설계 문서:** `docs/superpowers/specs/2026-07-28-hand-rig-animation-design.md`

## 전역 제약

- Unity 6000.3.20f1 · URP · **새 Input System 전용** (`Input.GetKey` 금지)
- 코드는 AI 가 파일을 직접 수정한다. 사용자에게 붙여넣기 시키지 않는다.
- **MCP 는 ①씬 배치 ②콘솔 에러 읽기 에만 쓴다.** 스크린샷 금지. 씬 계층 전체 덤프 금지.
- 크기·거리·위치 리터럴에는 `WorldScale.K` 를 곱한다. **배율·각도·시간·HP 에는 곱하지 않는다.**
- 현재 실측 기준값: 플레이어 키 **0.42m**, `localScale` **0.2239**, 손 월드지름 **0.1259**
- 이 프로젝트엔 테스트 프레임워크가 없다. 검증은 **①MCP 좌표 실측 비교 ②유니티에서 눈으로** 둘 다 한다.

## ★소유권 표 — 이 작업에서 버그가 날 수 있는 유일한 지점

충돌은 "누가 트랜스폼에 값을 쓰는가" 하나뿐이다. 아래 경계를 어기면 코드와 클립이
매 프레임 같은 값을 서로 덮어써서 무기가 떨린다.

| 대상 | 소유자 | 비고 |
|---|---|---|
| `HandRig` 위치·회전·크기 | **코드** (`HandRig.cs`) | `applyRootMotion=false` 필수 |
| `HandL` · `HandR` · `Bow` 의 위치·회전 | **Animator (클립)** | Task 6 이후. 코드는 손 뗀다 |
| `HandL` · `HandR` 의 크기 | 코드 (`handRadius`) | 클립에 크기 키프레임을 넣지 않는다 |
| 손·활의 **자식** (무기 모델, 시위, 화살촉) | **코드** | 부모/자식으로 층이 갈려 안 밟는다 |
| 무기 활성/비활성 | 코드 | 트랜스폼이 아니라 SetActive |

**클립 저작 규칙: `HandL` · `HandR` · `Bow` 이 셋만 키프레임을 찍는다. 그 밑은 절대 안 찍는다.**
손의 자식(무기 모델)에 키프레임이 들어가면 `PlayerBow` 의 모델 정렬 코드와 정면 충돌한다.

### 클립은 무기가 아니라 손을 움직인다

무기는 손의 자식이므로 **클립 하나가 모든 무기에 적용된다.** 새 무기를 추가할 때
애니메이션은 만들지 않는다. 무기별 차이는 `gripPos`/`gripEuler`(손 안에서의 자리),
`scale`(크기), `style`(세로/가로 중 어느 클립을 쓸지)에서만 낸다.

## 회귀 기준선 (Task 0 에서 측정, 이후 매 태스크 비교)

`HandL`/`HandR`/`Bow` 의 **플레이어 기준 상대 위치**와 **월드 스케일**. Task 1~4 는
구조만 바꾸는 작업이므로 이 값이 변하면 안 된다.

## 파일 구조

| 파일 | 책임 |
|---|---|
| `Assets/Scripts/HandRig.cs` (신규) | 리그 루트의 위치·회전·크기만 매 프레임 갱신. 그 외 아무것도 안 함 |
| `Assets/Scripts/HandRigEvents.cs` (신규, Task 7) | 애니메이션 이벤트 수신 → 게임 로직으로 중계 |
| `Assets/Scripts/BlobMotion.cs` (수정) | `baseScale` 읽기 접근자 노출 |
| `Assets/Scripts/PlayerBow.cs` (수정) | 손 배치 코드를 로컬 좌표로 → 최종 제거. 나머지 유지 |
| `Assets/Animation/HandRig.controller` (신규) | 상태 4개 |
| `Assets/Animation/Carry|Aim|Swing_Vertical|Swing_Horizontal.anim` (신규) | 클립 |

---

## Task 0: 회귀 기준선 측정

**Files:** 없음 (측정만)

**Interfaces:**
- Produces: 기준선 수치. Task 1~4 의 합격 판정에 쓰인다.

- [ ] **Step 1: 플레이 모드 진입**

`unity_play_mode` action=play

- [ ] **Step 2: 손·활의 플레이어 기준 상대 위치와 크기를 잰다**

`unity_execute_code` 로 실행:

```csharp
if (!Application.isPlaying) return "플레이 모드 아님";
var pb = UnityEngine.Object.FindFirstObjectByType<PlayerBow>();
var t = pb.transform;
var sb = new System.Text.StringBuilder();
foreach (var n in new[] { "HandL", "HandR", "Bow" })
{
    var c = t.Find(n);
    if (c == null) { sb.Append(n + " 없음\n"); continue; }
    var rel = t.InverseTransformPoint(c.position);
    sb.Append(n + " 상대위치=" + rel.ToString("F4")
           + " 월드스케일=" + c.lossyScale.x.ToString("F4") + "\n");
}
return sb.ToString();
```

- [ ] **Step 3: 결과를 이 문서 아래에 적어 둔다**

`## 기준선 실측값` 절을 만들어 그대로 붙여넣는다. Task 1~4 는 매번 이 값과 비교한다.

- [ ] **Step 4: 플레이 정지**

`unity_play_mode` action=stop

- [ ] **Step 5: 커밋**

```bash
git add docs/superpowers/plans/2026-07-28-hand-rig-animation.md
git commit -m "계획: 손 리그 전환 회귀 기준선 기록"
```

---

## Task 1: BlobMotion.baseScale 노출 + HandRig 생성

**Files:**
- Modify: `Assets/Scripts/BlobMotion.cs:33`
- Create: `Assets/Scripts/HandRig.cs`

**Interfaces:**
- Consumes: `PlayerBow.AimDir` (이미 public), `BlobMotion` 컴포넌트
- Produces: `HandRig` 컴포넌트. `public static HandRig I`, `public Transform HandL, HandR, BowRoot`

- [ ] **Step 1: `baseScale` 을 읽을 수 있게 한다**

`BlobMotion.cs:33` 의 `Vector3 baseScale;` 를 다음으로 교체:

```csharp
    Vector3 baseScale;
    /// ★찌그러지기 전 원래 크기. HandRig 이 이걸 써야 손이 스쿼시를 안 먹는다 (2026-07-28)
    public Vector3 BaseScale => baseScale;
```

- [ ] **Step 2: HandRig.cs 를 만든다**

```csharp
using UnityEngine;

/// 손·무기 전용 리그 루트 — 플레이어의 '형제'다. 자식이 아니다.
///
/// ★왜 자식이 아닌가 (2026-07-28): BlobMotion 이 플레이어 트랜스폼의 localScale 을
///   비균등하게 찌그러뜨리고(스쿼시&스트레치) localRotation 으로 기울인다. 손을 자식으로
///   넣으면 손이 같이 찌그러진다. 예전 코드가 손을 월드 좌표로 계산한 것도 같은 이유였다.
///
/// 하는 일은 셋뿐이다 — 위치·회전·크기. 손을 어떻게 움직일지는 여기서 정하지 않는다.
public class HandRig : MonoBehaviour
{
    public static HandRig I;

    [Tooltip("따라다닐 플레이어")] public Transform player;

    [HideInInspector] public Transform HandL, HandR, BowRoot;

    BlobMotion blob;
    PlayerBow bow;

    void Awake() { I = this; }

    void OnEnable() { I = this; }

    void LateUpdate()
    {
        if (player == null)
        {
            var p = GameObject.Find("Player");
            if (p == null) return;
            player = p.transform;
        }
        if (blob == null) blob = player.GetComponent<BlobMotion>();
        if (bow == null) bow = player.GetComponent<PlayerBow>();

        // 위치 — 통통 튐(hop)은 따라간다. 예전 손도 그랬다.
        transform.position = player.position;

        // 회전 — 조준 프레임. 예전 코드의 LookRotation(aimDir) 을 실제 오브젝트로 꺼낸 것.
        var fwd = bow != null ? bow.AimDir : player.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        // 크기 — ★찌그러지기 전 크기. 이래야 이 밑의 로컬 값이 예전 '플레이어 로컬'과
        //   단위가 같고, WorldScale.K 누락이 이 공간 안에서는 생길 수 없다.
        transform.localScale = blob != null ? blob.BaseScale : player.localScale;
    }
}
```

- [ ] **Step 3: 컴파일 에러 확인**

`unity_get_compilation_errors` severity=error → 0건이어야 한다.

- [ ] **Step 4: 씬에 HandRig 을 만들고 배치**

`unity_execute_code`:

```csharp
if (Application.isPlaying) return "플레이 중 — 정지 후 다시";
var player = GameObject.Find("Player");
if (player == null) return "Player 없음";
var existing = GameObject.Find("HandRig");
if (existing != null) return "이미 있음";
var go = new GameObject("HandRig");
go.transform.SetParent(player.transform.parent, false);   // ★형제로
var rig = go.AddComponent<HandRig>();
rig.player = player.transform;
UnityEditor.Undo.RegisterCreatedObjectUndo(go, "HandRig 생성");
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
bool ok = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(go.scene);
return "HandRig 생성 · 부모=" + (go.transform.parent != null ? go.transform.parent.name : "(루트)") + " 저장=" + ok;
```

- [ ] **Step 5: 디스크 저장 확인**

```bash
grep -c "HandRig" Assets/Scenes/SampleScene.unity
```

Expected: 1 이상

- [ ] **Step 6: 플레이해서 HandRig 이 제대로 따라오는지 확인**

플레이 후 `unity_execute_code`:

```csharp
if (!Application.isPlaying) return "플레이 모드 아님";
var r = UnityEngine.Object.FindFirstObjectByType<HandRig>();
var p = GameObject.Find("Player").transform;
return "리그위치-플레이어위치 = " + (r.transform.position - p.position).ToString("F4")
     + "\n리그 크기 = " + r.transform.localScale.ToString("F4")
     + "\n플레이어 크기(찌그러진 현재) = " + p.localScale.ToString("F4");
```

Expected: 위치 차이 ≈ (0,0,0) · 리그 크기 ≈ (0.2239, 0.2239, 0.2239) **고정**
(플레이어 크기는 매 프레임 달라져야 정상 — 그게 스쿼시다. 리그는 안 변해야 한다.)

**손은 아직 예전 그대로다. 화면은 아무것도 안 변한다. 그게 맞다.**

- [ ] **Step 7: 커밋**

```bash
git add Assets/Scripts/HandRig.cs Assets/Scripts/BlobMotion.cs Assets/Scenes/SampleScene.unity
git commit -m "손 리그 루트 도입 — 스쿼시를 안 먹는 조준 프레임"
```

---

## Task 2: 손·활을 HandRig 자식으로 옮긴다 (배치는 아직 월드 좌표)

**Files:**
- Modify: `Assets/Scripts/PlayerBow.cs:477`, `:481-482`

**Interfaces:**
- Consumes: `HandRig.I`
- Produces: `HandRig.I.HandL/HandR/BowRoot` 가 채워진다

- [ ] **Step 1: `Build()` 에서 부모를 HandRig 으로 바꾼다**

`PlayerBow.cs:477` 부근, 손 생성 루프 안의 `g.transform.SetParent(transform, false);` 를
찾아 다음으로 교체:

```csharp
            // ★부모 = HandRig (2026-07-28). 플레이어의 자식이면 BlobMotion 의
            //   비균등 스쿼시를 그대로 먹어 손이 찌그러진다.
            var rigT = HandRig.I != null ? HandRig.I.transform : transform;
            g.transform.SetParent(rigT, false);
```

`bowRoot.SetParent(transform, false);` (481-482행) 도 같은 `rigT` 를 쓰도록 교체:

```csharp
        bowRoot = new GameObject("Bow").transform;
        bowRoot.SetParent(HandRig.I != null ? HandRig.I.transform : transform, false);
```

- [ ] **Step 2: 생성 직후 HandRig 에 참조를 넘긴다**

`Build()` 의 손 루프가 끝난 직후 (`if (side < 0) handL = ...; else handR = ...;` 루프 종료 후)
다음을 추가:

```csharp
        if (HandRig.I != null) { HandRig.I.HandL = handL; HandRig.I.HandR = handR; }
```

`bowRoot` 생성 직후에도:

```csharp
        if (HandRig.I != null) HandRig.I.BowRoot = bowRoot;
```

- [ ] **Step 3: 컴파일 에러 확인**

`unity_get_compilation_errors` severity=error → 0건

- [ ] **Step 4: 회귀 확인 — Task 0 기준선과 비교**

배치 코드는 여전히 **월드 좌표**(`handL.position = ...`)를 쓰므로 부모가 바뀌어도
월드 결과는 같아야 한다. 플레이 후:

```csharp
if (!Application.isPlaying) return "플레이 모드 아님";
var pb = UnityEngine.Object.FindFirstObjectByType<PlayerBow>();
var t = pb.transform;
var rig = UnityEngine.Object.FindFirstObjectByType<HandRig>().transform;
var sb = new System.Text.StringBuilder();
foreach (var n in new[] { "HandL", "HandR", "Bow" })
{
    var c = rig.Find(n);
    if (c == null) { sb.Append(n + " ★HandRig 밑에 없음\n"); continue; }
    sb.Append(n + " 상대위치=" + t.InverseTransformPoint(c.position).ToString("F4")
           + " 월드스케일=" + c.lossyScale.x.ToString("F4") + "\n");
}
return sb.ToString();
```

Expected: **Task 0 기준선과 소수점 3자리까지 일치.** 손 크기(월드스케일)도 같아야 한다.

> 크기가 달라지면 `handRadius` 가 예전엔 플레이어(찌그러지는) 밑, 지금은 리그(안 찌그러지는)
> 밑이라 그렇다. **이건 의도한 개선이다** — 예전엔 손 크기가 몸 스쿼시에 따라 미세하게
> 흔들렸다. 값이 0.1259 근처면 통과로 본다.

- [ ] **Step 5: 유니티에서 눈으로 확인**

플레이해서 손·활이 예전과 같은 자리에 있는지 본다. **사용자에게 확인받는다.**

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/PlayerBow.cs
git commit -m "손·활을 HandRig 자식으로 — 배치는 아직 월드 좌표"
```

---

## Task 3: 배치 코드를 로컬 좌표로 전환 — ★안전 지점

**Files:**
- Modify: `Assets/Scripts/PlayerBow.cs:824-873`, `:987`, `:1012`, `:1034`, `:1046`

**Interfaces:**
- Consumes: `HandRig.I.transform` (조준 프레임)
- Produces: 없음 (동작 동일, 좌표계만 변경)

**이 태스크가 계획 전체의 안전 지점이다.** 여기까지 화면 결과가 같지 않으면 Task 4 로
가지 않는다. 되돌리고 원인을 먼저 잡는다.

- [ ] **Step 1: 월드 좌표 대입을 로컬 좌표 대입으로 바꾼다**

지금 코드는 `transform.position + right*a + fwd*b + up*c` 형태로 월드 좌표를 만든다.
`right`/`fwd` 는 조준 기준 축이고, `HandRig` 의 회전이 정확히 그 축이다.
따라서 **`(a, c, b)` 를 그대로 `localPosition` 에 넣으면 같은 결과**가 된다
(리그 로컬: x=옆, y=높이, z=앞).

`handL.position = Vector3.Lerp(handL.position, drawing ? aimL : idleL, k);` 를
다음으로 교체:

```csharp
        var rigT = HandRig.I != null ? HandRig.I.transform : transform;
        handL.localPosition = Vector3.Lerp(handL.localPosition,
            rigT.InverseTransformPoint(drawing ? aimL : idleL), k);
```

`handR.position = Vector3.Lerp(...)` 도 같은 방식으로:

```csharp
        handR.localPosition = Vector3.Lerp(handR.localPosition,
            rigT.InverseTransformPoint(drawing ? aimR : idleR), drawing ? 22f * Time.deltaTime : k);
```

`bowRoot.position = handL.position;` →

```csharp
        bowRoot.localPosition = handL.localPosition;
```

스윙 부분 (`handR.position = transform.position + frame * Vector3.LerpUnclamped(sPos, ePos, p) * S;`) →

```csharp
                    // 리그 회전이 곧 frame 이므로 로컬에 그대로 넣는다
                    handR.localPosition = Vector3.LerpUnclamped(sPos, ePos, p);
```

> `* S` 가 사라진 것에 주의. 리그의 크기가 이미 `baseScale` 이라 로컬 값에 자동으로
> 적용된다. **이게 이 구조로 옮기는 이유 중 하나다.**

- [ ] **Step 2: 회전 대입도 로컬로 바꾼다**

`bowRoot.rotation = Quaternion.Slerp(bowRoot.rotation, Quaternion.LookRotation(fwd, up), ...)` →
리그가 이미 `fwd` 를 보고 있으므로:

```csharp
            bowRoot.localRotation = Quaternion.Slerp(bowRoot.localRotation, Quaternion.identity, 18f * Time.deltaTime);
```

휴대 자세 (`rest`) 는:

```csharp
            var rest = Quaternion.Euler(carryEuler + new Vector3(0f, 0f, sway));
            bowRoot.localRotation = Quaternion.Slerp(bowRoot.localRotation, rest, 6f * Time.deltaTime);
            bowRoot.localPosition += rest * bowCarryPos;
```

스윙 회전 (`handR.rotation = frame * Quaternion.LookRotation(aimV, upV)`) →

```csharp
                    handR.localRotation = aimV.sqrMagnitude > 1e-6f && upV.sqrMagnitude > 1e-6f
                        ? Quaternion.LookRotation(aimV.normalized, upV)
                        : Quaternion.Slerp(q0, q1, rk);
```

휴대 회전 (`handR.rotation = Slerp(handR.rotation, LookRotation(fwd, up), ...)`) →

```csharp
                    handR.localRotation = Quaternion.Slerp(handR.localRotation, Quaternion.identity, 10f * Time.deltaTime);
```

`else handR.rotation = Quaternion.identity;` →

```csharp
            else handR.localRotation = Quaternion.identity;
```

- [ ] **Step 3: 컴파일 에러 확인**

`unity_get_compilation_errors` severity=error → 0건

- [ ] **Step 4: 회귀 확인 — Task 0 기준선과 비교**

Task 2 Step 4 와 같은 코드를 실행한다. Expected: **기준선과 일치.**

- [ ] **Step 5: 스윙까지 눈으로 확인 — ★사용자 확인 필수**

사용자에게 요청한다:
1. 도끼를 들고 휘둘러 본다 (세로)
2. 칼을 들고 휘둘러 본다 (가로)
3. 활을 들고 조준해 본다
4. 걸어다니며 손이 몸에 붙어 있는지 본다

**하나라도 예전과 다르면 여기서 멈추고 원인을 잡는다.**

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/PlayerBow.cs
git commit -m "손 배치를 리그 로컬 좌표로 전환 — 동작 동일, WorldScale.K 곱셈 제거"
```

---

## Task 4: 손·활을 에디터 실존 오브젝트로 만든다

**Files:**
- Modify: `Assets/Scripts/PlayerBow.cs` (`Build()` 의 손·활 생성부)

**Interfaces:**
- Consumes: 씬에 미리 만들어 둔 `HandRig/HandL`, `HandRig/HandR`, `HandRig/Bow`
- Produces: 애니메이션 창이 붙을 수 있는 실존 트랜스폼

**이 태스크가 목적 그 자체다.** 여기까지 와야 애니메이션 창에 클립을 만들 수 있다.

- [ ] **Step 1: 현재 런타임 생성 결과를 씬에 그대로 굽는다**

플레이 중에 `HandL`/`HandR`/`Bow` 의 구성(메시·머티리얼·외곽선)을 확인한 뒤,
에디트 모드에서 같은 구조를 만든다. `unity_execute_code` 로:

```csharp
if (Application.isPlaying) return "플레이 중 — 정지 후 다시";
var rig = GameObject.Find("HandRig");
if (rig == null) return "HandRig 없음";
var pb = UnityEngine.Object.FindFirstObjectByType<PlayerBow>();
foreach (var n in new[] { "HandL", "HandR" })
{
    if (rig.transform.Find(n) != null) continue;
    var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
    Object.DestroyImmediate(g.GetComponent<Collider>());
    g.name = n;
    g.transform.SetParent(rig.transform, false);
    g.transform.localScale = Vector3.one * pb.handRadius * 2f;
}
if (rig.transform.Find("Bow") == null)
{
    var b = new GameObject("Bow");
    b.transform.SetParent(rig.transform, false);
}
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(rig.scene);
return "생성 완료";
```

- [ ] **Step 2: `Build()` 가 새로 만드는 대신 찾아 쓰게 한다**

손 생성 루프를 다음으로 교체 (머티리얼·외곽선 적용은 유지):

```csharp
        // ★손은 이제 씬에 실존한다 (2026-07-28). 런타임 생성이면 에디터에 없어서
        //   애니메이션 창을 붙일 수 없다 — 그게 숫자를 타이핑할 수밖에 없던 원인이었다.
        var rigT = HandRig.I != null ? HandRig.I.transform : transform;
        foreach (var (n, side) in new[] { ("HandL", -1f), ("HandR", 1f) })
        {
            var found = rigT.Find(n);
            if (found == null) { Debug.LogError($"[PlayerBow] 씬에 HandRig/{n} 이 없다"); continue; }
            var g = found.gameObject;
            var mr = g.GetComponent<MeshRenderer>();
            if (mr != null) mr.material = Unlit(handColor);
            var mf = g.GetComponent<MeshFilter>();
            if (mf != null) AddOutline(g, mf.sharedMesh);
            if (side < 0) handL = g.transform; else handR = g.transform;
        }
        if (HandRig.I != null) { HandRig.I.HandL = handL; HandRig.I.HandR = handR; }
```

`bowRoot` 도 동일하게 `rigT.Find("Bow")` 로 찾아 쓰게 바꾼다.

- [ ] **Step 3: 컴파일 에러 확인 + 콘솔 에러 확인**

`unity_get_compilation_errors` severity=error → 0건
플레이 후 `unity_console_log` type=error → `[PlayerBow] 씬에 HandRig/... 이 없다` 가 없어야 한다.

- [ ] **Step 4: 회귀 확인**

Task 2 Step 4 와 같은 코드. Expected: 기준선과 일치.

- [ ] **Step 5: ★목표 달성 확인 — 플레이를 안 눌러도 손이 보인다**

에디트 모드(플레이 정지 상태)에서 씬 뷰에 `HandRig/HandL`, `HandRig/HandR` 가 보이는지
사용자에게 확인받는다. **이게 이 계획의 핵심 성과다.**

- [ ] **Step 6: 커밋**

```bash
git add Assets/Scripts/PlayerBow.cs Assets/Scenes/SampleScene.unity
git commit -m "손·활을 씬 실존 오브젝트로 — 애니메이션 창 저작 가능해짐"
```

---

## Task 5: Animator 와 빈 클립 4개를 만든다

**Files:**
- Create: `Assets/Animation/HandRig.controller`
- Create: `Assets/Animation/Carry.anim`, `Aim.anim`, `Swing_Vertical.anim`, `Swing_Horizontal.anim`

**Interfaces:**
- Produces: 파라미터 `SwingV`(Trigger) · `SwingH`(Trigger) · `Draw01`(Float) · `HasWeapon`(Bool)

- [ ] **Step 1: 컨트롤러와 클립을 만든다**

```csharp
if (Application.isPlaying) return "플레이 중 — 정지 후 다시";
System.IO.Directory.CreateDirectory(Application.dataPath + "/Animation");
var ctrl = UnityEditor.Animations.AnimatorController
    .CreateAnimatorControllerAtPath("Assets/Animation/HandRig.controller");
ctrl.AddParameter("SwingV", UnityEngine.AnimatorControllerParameterType.Trigger);
ctrl.AddParameter("SwingH", UnityEngine.AnimatorControllerParameterType.Trigger);
ctrl.AddParameter("Draw01", UnityEngine.AnimatorControllerParameterType.Float);
ctrl.AddParameter("HasWeapon", UnityEngine.AnimatorControllerParameterType.Bool);
var sm = ctrl.layers[0].stateMachine;
foreach (var n in new[] { "Carry", "Aim", "Swing_Vertical", "Swing_Horizontal" })
{
    var clip = new AnimationClip { name = n };
    UnityEditor.AssetDatabase.CreateAsset(clip, "Assets/Animation/" + n + ".anim");
    var st = sm.AddState(n);
    st.motion = clip;
    if (n == "Carry") sm.defaultState = st;
}
UnityEditor.AssetDatabase.SaveAssets();
return "컨트롤러·클립 4개 생성";
```

- [ ] **Step 2: HandRig 에 Animator 를 붙이고 컨트롤러를 연결**

```csharp
var rig = GameObject.Find("HandRig");
var an = rig.GetComponent<Animator>();
if (an == null) an = rig.AddComponent<Animator>();
an.runtimeAnimatorController = UnityEditor.AssetDatabase
    .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>("Assets/Animation/HandRig.controller");
an.applyRootMotion = false;   // ★리그 위치는 코드가 정한다. 루트 모션이 켜지면 싸운다
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(rig.scene);
return "Animator 연결 · applyRootMotion=" + an.applyRootMotion;
```

- [ ] **Step 3: 애니메이션 창이 열리는지 사용자에게 확인받는다**

사용자에게: 씬에서 `HandRig` 선택 → `Ctrl+6` → **왼쪽 위에 클립 드롭다운이 생겼고
`Carry`/`Aim`/`Swing_Vertical`/`Swing_Horizontal` 이 보이는지** 확인.

**여기서 드롭다운이 보이면 원래 질문("드롭다운이 없어")이 해결된 것이다.**

- [ ] **Step 4: 커밋**

```bash
git add Assets/Animation Assets/Scenes/SampleScene.unity
git commit -m "HandRig 애니메이터와 빈 클립 4개"
```

---

## Task 6: 배치 코드를 제거하고 Animator 에 넘긴다

**Files:**
- Modify: `Assets/Scripts/PlayerBow.cs` (손·활 배치부 제거, 신호 송신 추가)

**Interfaces:**
- Consumes: Task 5 의 파라미터 이름
- Produces: 없음

- [ ] **Step 1: 사용자가 클립 4개를 저작할 때까지 기다린다**

**빈 클립 상태로 이 태스크를 진행하면 손이 원점에 붙어버린다.**
사용자가 최소한 `Carry` 와 `Swing_Vertical` 을 채운 뒤에 시작한다.

- [ ] **Step 2: 손·활 배치 코드를 제거한다**

`PlayerBow.LateUpdate` 에서 다음을 삭제:
`handL.localPosition`/`handR.localPosition`/`handR.localRotation`/`bowRoot.localPosition`/
`bowRoot.localRotation` 대입 전부 (Task 3 에서 바꾼 그 줄들).

**남긴다:** 시위(`bowString.SetPosition`), 화살촉(`nockArrow`), 무기 모델 정렬
(`rig.inst.localRotation/localPosition/localScale`), 잔상, 에임 라인, 발사·타격.

- [ ] **Step 3: 애니메이터에 신호를 보낸다**

`LateUpdate` 안, 장비 비주얼 갱신 근처에 추가:

```csharp
        var anim = HandRig.I != null ? HandRig.I.GetComponent<Animator>() : null;
        if (anim != null)
        {
            anim.SetBool("HasWeapon", gearV != GearKind.None);
            anim.SetFloat("Draw01", pull);          // 당김 0~1 — Aim 클립 재생 위치
        }
```

스윙 시작 지점(`gather.SwingT` 가 0 에서 양수로 바뀌는 순간)에서:

```csharp
                    if (prevSwingT <= 0f && gather.SwingT > 0f && anim != null)
                        anim.SetTrigger(setup.style == SwingStyle.Horizontal ? "SwingH" : "SwingV");
```

- [ ] **Step 4: 쓰이지 않게 된 인스펙터 값을 제거한다**

`handSide`, `handUp`, `swingStartPos`, `swingEndPos`, `swingStartEuler`, `swingEndEuler`,
`hSwingStartPos`, `hSwingEndPos`, `hSwingStartEuler`, `hSwingEndEuler`, `bowAimHandL`,
`bowAimHandR`, `swingCurve`, `backswingExtra`, `carrySway`, `bowCarryPos`, `toolCarrySway`,
`toolCarrySwaySpeed` 및 `WeaponDef` 의 `aimHandL`, `aimHandR`, `handOffsetL`, `handOffsetR`,
`carryPos`, `carryEuler`, `carrySway`, `carrySwaySpeed`.

**`handRadius`, `bowSize`, `toolLength`, `scale`, `gripPos`, `gripEuler`, `modelScale`,
`modelEuler`, `modelPos`, `trail*`, `impactPop*`, `shot*` 는 남긴다.**

- [ ] **Step 5: 컴파일·콘솔 에러 확인**

`unity_get_compilation_errors` → 0건. 플레이 후 `unity_console_log` type=error → 0건.

- [ ] **Step 6: 눈으로 확인 — 사용자 확인 필수**

클립대로 손이 움직이는지, 코드와 싸우지 않는지(떨림·순간이동 없음) 확인.

- [ ] **Step 7: 커밋**

```bash
git add Assets/Scripts/PlayerBow.cs Assets/Scenes/SampleScene.unity
git commit -m "손 배치 코드 제거 — Animator 가 손 트랜스폼을 소유"
```

---

## Task 7: 타격 시점을 애니메이션 이벤트로 옮긴다

**Files:**
- Create: `Assets/Scripts/HandRigEvents.cs`
- Modify: `Assets/Scripts/PlayerGather.cs` (`impactDelay` 기반 타이밍 제거)

**Interfaces:**
- Consumes: `PlayerGather.I`
- Produces: `HandRigEvents.OnImpact()` — 클립에서 부를 함수

- [ ] **Step 1: 중계 컴포넌트를 만든다**

```csharp
using UnityEngine;

/// 애니메이션 이벤트 수신 → 게임 로직으로 중계.
/// ★애니메이션 이벤트가 부르는 함수는 Animator 와 같은 GameObject 위에 있어야 한다.
///   그래서 PlayerGather 에 직접 못 걸고 여기를 거친다.
public class HandRigEvents : MonoBehaviour
{
    /// 무기가 눈에 보이게 닿는 프레임에서 부른다.
    /// 예전엔 impactDelay=0.24초 라는 짐작한 숫자였다 — 모션을 바꾸면 매번 어긋났다.
    public void OnImpact()
    {
        if (PlayerGather.I != null) PlayerGather.I.AnimImpact();
    }
}
```

- [ ] **Step 2: `PlayerGather` 에 이벤트용 진입점을 만든다**

`PlayerGather` 의 기존 `DoImpact()` 를 감싸는 public 메서드를 추가:

```csharp
    /// 애니메이션 이벤트에서 부르는 타격 진입점 (2026-07-28)
    public void AnimImpact() => DoImpact();
```

- [ ] **Step 3: HandRig 에 컴포넌트를 붙인다**

```csharp
var rig = GameObject.Find("HandRig");
if (rig.GetComponent<HandRigEvents>() == null) rig.AddComponent<HandRigEvents>();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(rig.scene);
return "HandRigEvents 부착";
```

- [ ] **Step 4: 사용자가 클립에 이벤트 마커를 찍는다**

사용자에게 안내: 애니메이션 창에서 `Swing_Vertical` 선택 → 무기가 닿아 보이는 프레임으로
이동 → 타임라인 위 이벤트 줄 우클릭 → `Add Animation Event` → 함수 `OnImpact` 선택.
`Swing_Horizontal` 도 동일.

- [ ] **Step 5: `impactDelay` 기반 타이밍을 끈다**

`PlayerGather` 에서 `impactDelay` 로 `DoImpact()` 를 부르던 경로를 제거한다.
`impactDelay` 필드와 `ImpactAt01` 은 무기 팝(`impactPop`) 연출이 참조하므로 남기되,
타격 호출은 이벤트만 쓰게 한다.

- [ ] **Step 6: 눈으로 확인**

도끼가 나무에 닿는 순간에 판정·이펙트가 들어가는지 확인. **사용자 확인 필수.**

- [ ] **Step 7: 커밋**

```bash
git add Assets/Scripts/HandRigEvents.cs Assets/Scripts/PlayerGather.cs Assets/Scenes/SampleScene.unity
git commit -m "타격 시점을 애니메이션 이벤트로 — impactDelay 짐작 제거"
```

---

## 중단 지점

- **Task 3 종료 시점**: 구조는 새 것, 동작은 예전 그대로. 여기서 멈춰도 프로젝트는 멀쩡하다.
- **Task 5 종료 시점**: 애니메이션 창이 열린다. 클립 저작을 며칠 나눠 해도 된다.
- **Task 6 은 클립이 채워진 뒤에만 시작한다.** 빈 클립으로 진행하면 손이 원점에 붙는다.
