using UnityEngine;

/// 머리 위에 올려둔 펫 — 지금 무기 칸에 묶인 펫이 무엇인지 몸으로 보여준다.
///
/// ★왜 머리 위인가 (2026-07-28 사용자): 하단 글자만으로는 "뭘 던지는지" 가 안 읽힌다.
///   장착한 물건처럼 몸에 얹혀 있어야 캐릭터와 **하나의 객체로** 인식된다.
///
/// ★그래서 플레이어의 '자식'으로 붙인다. BlobMotion 이 몸을 찌그러뜨리면 이것도 같이
///   찌그러진다 — 손·무기(HandRig)는 그게 문제라 형제로 뺐지만, 여기서는 정반대다.
///   같이 눌리고 같이 튀어야 '얹혀 있는 것' 으로 보인다.
///
/// 아웃라인도 원본 펫에서 그대로 복사한다 (원본이 Outline/OutlineMask 자식을 갖고 있다).
[DefaultExecutionOrder(900)]
public class PetHeadDisplay : MonoBehaviour
{
    public static PetHeadDisplay I;

    [Tooltip("캐릭터 키 대비 미니 펫 크기 (0.7 = 키의 70%)")]
    public float sizeRatio = 0.7f;
    [Tooltip("머리 꼭대기에서 얼마나 더 띄우나 (캐릭터 키 대비)")]
    public float gapRatio = 0.12f;
    [Tooltip("둥실둥실 뜨는 높이 (캐릭터 키 대비, 0 = 고정)")]
    public float bobRatio = 0.04f;
    [Tooltip("둥실거리는 빠르기")] public float bobSpeed = 2.2f;
    // ★펫이 뒤를 보고 있었다 (2026-07-28). 모델의 앞면이 캐릭터 정면과 반대였다.
    //   빙글빙글 돌리던 것도 뺐다 — 방향이 계속 바뀌면 '어느 쪽을 보는지' 가 안 읽힌다.
    //   장착물처럼 캐릭터와 같은 곳을 봐야 한 몸으로 보인다.
    [Tooltip("바라보는 방향 보정 (°) — 뒤를 보고 있으면 180")]
    public float yawOffset = 180f;

    PetUnit shown;          // 지금 올려둔 펫 (바뀌면 다시 만든다)
    Transform mini;
    float baseLocalY, localScale;
    bool hidden;

    void Awake() { I = this; }

    /// 던지는 동안 잠깐 감춘다 — 머리 위의 것이 날아가는 것처럼 보이게
    public void Hide() { hidden = true; if (mini != null) mini.gameObject.SetActive(false); }
    public void Show() { hidden = false; if (mini != null) mini.gameObject.SetActive(true); }

    /// 투척이 출발하는 자리 — 머리 위 펫이 있던 그 지점
    public Vector3 HeadPoint =>
        mini != null ? mini.position : transform.position + Vector3.up * PlayerHeight() * 0.9f;

    void LateUpdate()
    {
        var want = PetCommand.Selected;
        if (want != shown) Rebuild(want);
        if (mini == null || hidden) return;

        // 둥실둥실 — 얹혀 있는 게 아니라 살짝 떠 있는 느낌
        float bob = bobRatio > 0f ? Mathf.Sin(Time.time * bobSpeed) * bobRatio * PlayerHeight() : 0f;
        var lp = mini.localPosition;
        mini.localPosition = new Vector3(lp.x, baseLocalY + bob / Mathf.Max(1e-4f, transform.lossyScale.y), lp.z);
        // 캐릭터와 같은 곳을 본다 (부모가 이미 돌아가므로 로컬 회전은 보정값만)
        mini.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
    }

    float PlayerHeight()
    {
        var r = GetComponentInChildren<MeshRenderer>();
        return r != null ? r.bounds.size.y : 0.42f;
    }

    void Rebuild(PetUnit pet)
    {
        shown = pet;
        if (mini != null) { Destroy(mini.gameObject); mini = null; }
        if (pet == null) return;

        var src = pet.transform;
        var srcMf = src.GetComponent<MeshFilter>();
        var srcMr = src.GetComponent<MeshRenderer>();
        if (srcMf == null || srcMr == null) return;

        var root = new GameObject("HeadPet_" + pet.name).transform;
        root.SetParent(transform, false);

        // 본체 + 아웃라인 자식들을 그대로 베낀다 (원본이 Outline/OutlineMask 를 갖고 있다)
        Copy(srcMf.sharedMesh, srcMr.sharedMaterial, root, "body");
        foreach (var nm in new[] { "Outline", "OutlineMask" })
        {
            var c = src.Find(nm);
            if (c == null) continue;
            var cmf = c.GetComponent<MeshFilter>();
            var cmr = c.GetComponent<MeshRenderer>();
            if (cmf != null && cmr != null) Copy(cmf.sharedMesh, cmr.sharedMaterial, root, nm);
        }

        // 크기 — 캐릭터 키의 sizeRatio 만큼. 부모가 이미 스케일을 갖고 있으므로 나눠 준다
        // ★본체는 비활성이라 renderer.bounds 를 못 쓴다 (0 이 나온다). 만들 때 재 둔 body 를 쓴다.
        float petH = Mathf.Max(0.01f, pet.body > 0.01f ? pet.body : srcMr.bounds.size.y);
        float ph = PlayerHeight();
        float wantWorld = ph * sizeRatio;
        float parentS = Mathf.Max(1e-4f, transform.lossyScale.y);
        localScale = (wantWorld / petH) * (src.lossyScale.y / Mathf.Max(1e-4f, parentS));
        root.localScale = Vector3.one * localScale;

        // 머리 꼭대기 위로
        var pr = GetComponentInChildren<MeshRenderer>();
        float topWorld = pr != null ? pr.bounds.max.y - transform.position.y : ph * 0.5f;
        baseLocalY = (topWorld + ph * gapRatio) / parentS;
        root.localPosition = new Vector3(0f, baseLocalY, 0f);
        root.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

        mini = root;
        if (hidden) root.gameObject.SetActive(false);
    }

    void Copy(Mesh mesh, Material mat, Transform parent, string name)
    {
        if (mesh == null || mat == null) return;
        var g = new GameObject(name).transform;
        g.SetParent(parent, false);
        g.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = g.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
