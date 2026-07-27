using UnityEngine;

/// 애니메이션 이벤트 수신 → 게임 로직으로 중계.
///
/// ★왜 별도 컴포넌트인가: 애니메이션 이벤트가 부를 수 있는 함수는 Animator 와
///   '같은 GameObject' 위에 있어야 한다. PlayerGather·PlayerBow 는 Player 에 붙어
///   있으므로 직접 걸 수 없다. 그래서 HandRig 에 이걸 두고 넘긴다.
///
/// ★쓰는 법: 애니메이션 창 타임라인 위쪽 이벤트 줄을 우클릭 → Add Animation Event
///   → 아래 함수 중 하나를 고른다. 무기가 눈에 보이게 닿는 프레임에 OnImpact 를 찍는다.
///
/// ★왜 이렇게 하나: 예전엔 타격 시점이 impactDelay = 0.24초 라는 짐작한 숫자였다.
///   모션을 조금만 바꿔도 눈과 판정이 어긋났다. 이벤트는 '본 그 프레임'에 박히고,
///   공속을 올려도 클립 안의 비율이라 알아서 따라간다.
public class HandRigEvents : MonoBehaviour
{
    PlayerGather gather;
    PlayerBow bow;

    void Awake()
    {
        var p = GameObject.Find("Player");
        if (p == null) return;
        gather = p.GetComponent<PlayerGather>();
        bow = p.GetComponent<PlayerBow>();
    }

    /// ★타격 판정 — 무기가 눈에 보이게 닿는 프레임에 찍는다.
    public void OnImpact()
    {
        if (gather != null) gather.AnimImpact();
    }

    /// 칼날 잔상 켜기 — 휘두르기 시작하는 프레임
    public void OnTrailOn() { if (bow != null) bow.SetTrail(true); }

    /// 칼날 잔상 끄기 — 휘두르기가 끝나는 프레임
    public void OnTrailOff() { if (bow != null) bow.SetTrail(false); }

    /// 칼끝에서 불꽃 터뜨리기 — 타격 프레임에 OnImpact 와 같이 찍으면 된다
    public void OnHitFx()
    {
        var at = bow != null ? bow.WeaponTip() : transform.position;
        FX.Burst(at, new Color(2.2f, 1.9f, 1.1f, 0.95f), 12,
                 0.12f * WorldScale.K, 1.4f * WorldScale.K, 0.25f);
    }

    /// 화면 흔들기 — 세기는 이벤트의 Float 칸에 넣는다 (0.2 정도가 무난)
    public void OnShake(float amp)
    {
        FollowCam.Shake(Mathf.Max(0.01f, amp) * WorldScale.K);
    }
}
