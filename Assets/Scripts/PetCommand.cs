using UnityEngine;

/// 펫 편성 — 밖에 내놓을 수 있는 마릿수 등 '군단' 관련 설정을 들고 있다.
///
/// ★2026-07-28 로 두 번 비워졌다.
///   ① 지휘(E 소집 / R 돌격) — 펫 행동을 걷어내면서 같이 삭제
///   ② 탑승 — 사용자 판단으로 폐기 ("메리트가 없었다")
///   펫은 이제 타는 것이 아니라, 무기로 던져서 그 자리에 소환하는 것이다.
///   앞으로 R = 펫 스왑(3종 순환)이 여기에 들어온다.
public class PetCommand : MonoBehaviour
{
    public static PetCommand I;

    [Header("군단")]
    [Tooltip("동시에 밖에 내놓을 수 있는 펫 수 — 펫 보관함이 쓴다")]
    public int maxParty = 4;

    void Awake() { I = this; }
}
