using UnityEngine;

/// 손·무기 전용 리그 루트 — 플레이어의 '형제'다. 자식이 아니다.
///
/// ★왜 자식이 아닌가 (2026-07-28): BlobMotion 이 플레이어 트랜스폼의 localScale 을
///   비균등하게 찌그러뜨리고(스쿼시&스트레치) localRotation 으로 기울인다. 손을 자식으로
///   넣으면 손이 같이 찌그러진다. 예전 코드가 손을 월드 좌표로 계산한 것도 같은 이유였다.
///
/// ★이게 있어야 하는 진짜 이유: 손이 에디터에 실존해야 애니메이션 창으로 모션을 만들 수
///   있다. 예전엔 런타임 생성이라 에디터에 손이 없었고, 그래서 자세를 인스펙터 숫자로
///   타이핑할 수밖에 없었다.
///
/// 하는 일은 셋뿐이다 — 위치·회전·크기. 손을 어떻게 움직일지는 여기서 정하지 않는다.
///
/// ★실행 순서를 맨 뒤로 미뤄야 한다 (2026-07-28). BlobMotion 도 LateUpdate 에서
///   발높이를 보정하는데, 둘 다 기본 순위(0)면 어느 쪽이 먼저 돌지 정해지지 않는다.
///   실제로 리그가 먼저 돌아 보정 전 위치를 읽었고 몸보다 0.21343m 아래에 붙었다
///   (리그 y 가 BlobMotion.GroundY 와 정확히 같았다 = 발높이를 올리기 전 값).
///
/// ★아래 [DefaultExecutionOrder] 만으로는 안 먹었다. 실제로 순서를 정하는 것은
///   ProjectSettings/Script Execution Order 의 값(1000)이다. 어트리뷰트는 의도를
///   코드에 남겨두는 용도이고, 순서를 바꾸려면 프로젝트 설정을 함께 고쳐야 한다.
///
/// 손은 이 리그의 '자식'이라 리그가 나중에 움직여도 함께 따라가므로 늦어도 문제없다.
/// 단, 손 위치는 반드시 '리그 로컬 값'으로 써야 한다 — 월드 좌표를 리그 기준으로
/// 역변환하면 한 프레임 묵은 리그를 쓰게 되어 미세하게 어긋난다.
/// ★에디터에서도 따라가야 한다 (2026-07-28 사용자 — "캐릭터 위치 옮겼는데 손이 따라오질 않네").
///   리그가 플레이어의 '형제'라, LateUpdate 가 안 도는 편집 모드에서는 캐릭터만 옮기면
///   손이 제자리에 남는다. ExecuteAlways 로 편집 중에도 붙어 다니게 한다.
///   (손의 로컬 자세는 안 건드린다 — 그건 클립·사람 몫이다. 리그 본체만 따라간다)
[ExecuteAlways]
[DefaultExecutionOrder(1000)]
public class HandRig : MonoBehaviour
{
    public static HandRig I;

    [Tooltip("따라다닐 플레이어 (비워두면 이름으로 찾는다)")] public Transform player;

    [HideInInspector] public Transform HandL, HandR, BowRoot;

    BlobMotion blob;

    // ★Awake 에서도 한 번 맞춘다 (2026-07-28). 씬의 리그는 월드 원점에 있고 손은
    //   PlayerBow.Start 에서 이 밑에 생긴다. 안 맞춰두면 손이 원점에서 태어나 섬
    //   반대편(4.6km)에서 날아오는 게 보인다 — 예전엔 플레이어 자식이라 없던 현상이다.
    void Awake() { I = this; Sync(); }
    void OnEnable() { I = this; }

    void LateUpdate() => Sync();

    void Sync()
    {
        if (player == null)
        {
            var p = GameObject.Find("Player");
            if (p == null) return;
            player = p.transform;
        }
        if (blob == null) blob = player.GetComponent<BlobMotion>();

        // ★편집 모드에서는 매 프레임 값을 써 넣으면 씬이 계속 '저장 안 됨' 으로 더러워진다.
        //   실제로 달라졌을 때만 쓴다.
        bool editing = !Application.isPlaying;
        if (editing
            && (transform.position - player.position).sqrMagnitude < 1e-8f
            && Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, player.eulerAngles.y)) < 0.01f)
            return;

        // 위치 — 통통 튐(hop)은 따라간다. 예전 손도 그랬다.
        transform.position = player.position;

        // 회전 — 몸의 좌우 회전(yaw)만. 기울임(lean)과 스쿼시는 안 가져온다.
        //
        // ★조준 방향이 아니라 몸 방향인 이유 (2026-07-28): 손은 몸에 달린 것이고,
        //   무엇보다 타격 판정이 이미 transform.forward 를 쓴다(PlayerGather.DoImpact).
        //   기준을 맞춰야 "눈에는 맞았는데 판정은 빗나감" 이 생기지 않는다.
        //   몸은 어차피 FaceTowards 로 조준을 따라 도므로 스윙은 여전히 마우스 쪽으로
        //   나간다 (실측: 정지 상태에서 조준-몸 각도차 0.0도).
        transform.rotation = Quaternion.Euler(0f, player.eulerAngles.y, 0f);

        // 크기 — ★찌그러지기 전 크기. 이래야 이 밑의 로컬 값이 예전 '플레이어 로컬'과
        //   단위가 같고, WorldScale.K 누락이 이 공간 안에서는 생길 수 없다.
        // ★편집 모드에서는 BlobMotion.Awake 가 안 돌아 BaseScale 이 0 이다.
        //   그대로 쓰면 손이 크기 0 이 되어 사라진다 — 그때는 플레이어 크기를 쓴다.
        var bs = blob != null ? blob.BaseScale : Vector3.zero;
        transform.localScale = bs.sqrMagnitude > 1e-8f ? bs : player.localScale;
    }
}
