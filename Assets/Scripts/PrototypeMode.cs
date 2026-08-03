using UnityEngine;

/// 프로토타입 모드 — **월드만 먼저 본다.**
///
/// ★왜 (2026-08-03 사용자): "기존에 펫 소환 Q·E·R 이런 단축키 같은 거, 코어 다 삭제,
///   스폰되는 펫도 우선 삭제하고 월드 먼저 어떻게 제작되는지 테스트해봐야 할 듯."
///
/// ★지우지 않고 **끈다.** 새 기획(영웅 펫 한 마리 · 좀보이드 템포)이 자리를 잡으면
///   그때 실제로 걷어낸다. 지금 지우면 되살릴 때 다시 짜야 하고, 무엇이 왜 있었는지도
///   같이 사라진다. 체크를 풀면 옛 시스템이 그대로 돌아온다.
[DefaultExecutionOrder(-500)]
public class PrototypeMode : MonoBehaviour
{
    [Header("월드만 보기 — 체크한 것을 잠근다")]
    [Tooltip("야생 펫이 안 나온다 (시작 지급 펫도 없음)")]
    public bool 펫스폰끄기 = true;
    [Tooltip("Q·E·R 부대 배치 · C 회수 · Space 구르기")]
    public bool 부대조작끄기 = true;
    [Tooltip("부화터·둥지·알 시스템")]
    public bool 거점끄기 = true;
    [Tooltip("F5~F11 실험대")]
    public bool 실험대끄기 = true;
    [Tooltip("잔디·땅 루팅 (평지엔 데이터가 없다)")]
    public bool 환경끄기 = true;

    void Awake()
    {
        int off = 0;

        if (펫스폰끄기)
        {
            // cap 0 = 야생을 한 마리도 안 채운다. Start 의 즉시 채우기도 같이 막힌다
            foreach (var s in FindObjectsByType<PetSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                s.cap = 0;
                s.startSpecies = new string[0];   // 시험 키트 지급 중단
                off++;
            }
        }

        if (부대조작끄기)
        {
            off += Kill<SkillSystem>();
            off += Kill<PetCommand>();
            off += Kill<SquadHUD>();
        }

        if (거점끄기)
        {
            off += Kill<HatcherySite>();
            off += Kill<HatcheryUI>();
            off += Kill<NestSite>();
        }

        if (실험대끄기) off += Kill<StressTest>();

        if (환경끄기)
        {
            off += Kill<GrassManager>();
            off += Kill<ScatterSpawner>();
        }

        Debug.Log($"[프로토타입] 옛 시스템 {off}개 잠금 — 월드만 봅니다. " +
                  "되살리려면 이 컴포넌트의 체크를 풀거나 오브젝트를 끄세요.");
    }

    static int Kill<T>() where T : MonoBehaviour
    {
        int n = 0;
        foreach (var c in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c != null && c.enabled) { c.enabled = false; n++; }
        return n;
    }
}
