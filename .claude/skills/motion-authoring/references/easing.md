# 이징 카탈로그 — 어떤 느낌에 어떤 곡선인가

SKILL.md 의 기본 4종(Ease In / Out / In-Out / 등속)은 **밋밋함을 면하는 최소선**이다.
"쫀득하다 / 시원하다 / 묵직하다" 는 느낌은 대부분 아래 **오버슈트 계열**에서 나온다.

참고: [easings.net](https://easings.net/) · [Febucci 게임 애니메이션 이징 가이드](https://blog.febucci.com/2018/08/easing-functions/)

---

## 고르는 법 — 느낌에서 곡선으로

먼저 "무엇처럼 보이고 싶은가"를 정하고 표에서 찾는다. 이름부터 고르면 안 맞는다.

| 원하는 느낌 | 곡선 | 어디에 |
|---|---|---|
| **탁 붙는다, 야무지다** | `OutBack` | 등장·착지·UI 튀어나옴 · **가장 자주 쓴다** |
| **탱탱 튄다, 장난스럽다** | `OutElastic` | 젤리·고무 펫·보상 획득 |
| **통통 구른다** | `OutBounce` | 떨어진 물건·드랍 아이템 |
| **묵직하게 내리꽂힌다** | `InQuart` / `InExpo` | 내려찍기·낙하·강타 |
| **총알처럼 튀어나간다** | `OutExpo` | 발사·돌진 시작 |
| **부드럽게 미끄러진다** | `InOutCubic` | 카메라·UI 이동 |
| **힘을 모은다 (뜸)** | `OutQuad` + 정지 구간 | 예비동작 |
| **둥글게 휘어 나간다** | `OutCirc` | 곡선 궤적·회피 |

**In / Out / InOut 의 뜻** — 초보자가 가장 많이 헷갈린다:
- `In` = **시작이 느리고 끝이 빠름** (가속) → 무언가를 향해 *떨어질* 때
- `Out` = **시작이 빠르고 끝이 느림** (감속) → 무언가에 *도착할* 때
- `InOut` = 양쪽 다 느림 → 이동

**대부분의 동작은 `Out` 이다.** 사람 눈은 "빠르게 튀어나와 천천히 자리 잡는" 것을 자연스럽게 읽는다.

---

## 오버슈트 3종 — 여기가 핵심

### OutBack — 지나쳤다 되돌아온다 (야무짐)

가장 활용도가 높다. 목표를 살짝 넘었다가 돌아오면서 **"탁"** 하고 붙는 느낌이 난다.
등장·착지·버튼·아이콘 — 뭐든 여기서 시작해 보면 대체로 맞는다.

```csharp
// s = 되돌아오는 세기. 1.70158 이 표준, 크면 더 과장된다 (2~4 도 게임에선 흔하다)
static float OutBack(float k, float s = 1.70158f)
{
    k -= 1f;
    return k * k * ((s + 1f) * k + s) + 1f;
}
```

### OutElastic — 탱탱하게 여러 번 떤다 (장난스러움)

고무·젤리 느낌. **과하면 싸구려로 보인다** — 중요한 순간에만, 짧게 쓴다.

```csharp
// p = 떨림 주기(작을수록 촘촘), 0.3 이 표준
static float OutElastic(float k, float p = 0.3f)
{
    if (k <= 0f) return 0f;
    if (k >= 1f) return 1f;
    return Mathf.Pow(2f, -10f * k) * Mathf.Sin((k - p * 0.25f) * (2f * Mathf.PI) / p) + 1f;
}
```

### OutBounce — 바닥에 통통 튄다 (무게감)

떨어지는 것에만 쓴다. 옆으로 움직이는 데 쓰면 이상하다.

```csharp
static float OutBounce(float k)
{
    const float n = 7.5625f, d = 2.75f;
    if (k < 1f / d)       return n * k * k;
    if (k < 2f / d)       { k -= 1.5f / d;   return n * k * k + 0.75f; }
    if (k < 2.5f / d)     { k -= 2.25f / d;  return n * k * k + 0.9375f; }
    k -= 2.625f / d;      return n * k * k + 0.984375f;
}
```

---

## 세기 단계 — 같은 계열에서 강도만 바꾼다

`Quad → Cubic → Quart → Quint → Expo` 순으로 **점점 극단적**이 된다.
"좀 더 세게" 라는 피드백을 받으면 다른 계열로 갈아타지 말고 **한 칸 올려라.**

```csharp
static float OutQuad (float k) { k = 1f - k; return 1f - k * k; }
static float OutCubic(float k) { k = 1f - k; return 1f - k * k * k; }
static float OutQuart(float k) { k = 1f - k; return 1f - k * k * k * k; }
static float OutQuint(float k) { k = 1f - k; return 1f - k * k * k * k * k; }
static float OutExpo (float k) => k >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * k);
static float InQuad  (float k) => k * k;
static float InCubic (float k) => k * k * k;
static float InQuart (float k) => k * k * k * k;
static float InExpo  (float k) => k <= 0f ? 0f : Mathf.Pow(2f, 10f * (k - 1f));
static float OutCirc (float k) { k -= 1f; return Mathf.Sqrt(1f - k * k); }
static float InOutCubic(float k) =>
    k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) * 0.5f;
```

---

## 3막에 실제로 붙이는 조합

SKILL.md 의 3막(예비 → 본동작 → 여운)에 위 곡선을 얹으면 이렇게 된다.
**여운에 `OutBack` 을 쓰는 것이 밋밋함을 없애는 가장 빠른 한 수다.**

| 동작 | 예비 | 본동작 | 여운 |
|---|---|---|---|
| 칼 휘두르기 (잽쌈) | `OutQuad` | `InCubic` | `OutBack` s=2 |
| 도끼 내려찍기 (묵직) | `OutQuad` 길게 | `InQuart` | `OutBack` s=3 + 스쿼시 |
| 펫 등장 (퐁) | 웅크림 유지 | `OutExpo` 로 솟음 | `OutBack` s=3 착지 |
| 던지기 | `OutQuad` 젖힘 | `InExpo` 채기 | `OutBack` 되돌아옴 |
| 죽음 (힘 빠짐) | — | `InQuad` 천천히 | `OutQuad` 정착 |
| 피격 움찔 | — | `OutExpo` 즉시 | `OutElastic` 파르르 |
| 획득·보상 | — | `OutBack` | `OutElastic` 짧게 |

---

## Unity 애니메이션 창에서 오버슈트 만들기

코드가 아니라 클립으로 저작할 때는 **키를 하나 더 찍어서** 오버슈트를 만든다.
탄젠트만으로는 목표를 지나칠 수 없다 — 지나친 값을 가진 키가 실제로 있어야 한다.

```
목표값 ─────────────╮   ╭──── K4 (목표)
                    ╰─╮ │
K2 (목표 ×1.2) ───────╳─╯      ← 이 키가 오버슈트
K3 (목표 ×0.95) ───────╰       ← 살짝 반대로 튐
```

- **K2** 도착 키: In 탄젠트 = Flat (감속하며 도착)
- **K3** 되돌아온 키: 양쪽 자동 (부드럽게 통과)
- **K4** 정지 키: In 탄젠트 = Flat

`OutBack` 세기를 키우고 싶으면 K2 의 값을 더 키운다 (1.2 → 1.35).
`OutElastic` 처럼 여러 번 떨게 하려면 K2·K3 쌍을 한 번 더 반복하되 **진폭을 절반씩** 줄인다.

---

## 흔한 실수

**여운에 `Linear` 를 쓴다** — 가장 흔하고 가장 치명적이다. 살아있는 것은 등속으로 멈추지 않는다.

**모든 곳에 `OutElastic`** — 처음엔 재밌지만 금방 싸구려로 보인다. 한 화면에 하나면 충분하다.

**`InOut` 남용** — 양쪽이 다 느려서 흐물흐물해진다. 이동에만 쓰고 타격에는 쓰지 않는다.

**곡선만 바꾸고 시간 배분은 그대로** — 이징은 3막 구조 위에서만 효과가 있다.
본동작이 전체의 절반을 먹고 있으면 어떤 곡선을 써도 느려 보인다. **구조를 먼저 고쳐라.**
