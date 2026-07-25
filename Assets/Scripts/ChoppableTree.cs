using System.Collections.Generic;
using UnityEngine;

/// 맞아서 '깨어난' 나무/바위 — 지형 인스턴스에서 실체화되어 타격 리액션을 한다.
/// 맞으면 반짝(플래시) + 통통통(감쇠 진동), 다 맞으면 조각이 통! 퍼지고 먼지와 함께 사라짐.
public class ChoppableTree : MonoBehaviour
{
    public static readonly List<ChoppableTree> All = new List<ChoppableTree>();

    bool isRock; int hitsLeft; int pieces;
    Vector3 baseScale;
    float bounceT;
    readonly List<Material> mats = new List<Material>();
    readonly List<Color> baseCols = new List<Color>();
    readonly List<string> colProps = new List<string>();

    public bool IsRock => isRock;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void Init(bool rock, int hits, int dropPieces)
    {
        isRock = rock; hitsLeft = hits; pieces = dropPieces;
        baseScale = transform.localScale;
        foreach (var r in GetComponentsInChildren<Renderer>())
            foreach (var m in r.materials)   // 인스턴스 — 곧 사라질 오브젝트라 OK
            {
                string prop = m.HasProperty("_BaseColor") ? "_BaseColor"
                            : m.HasProperty("_Color") ? "_Color" : null;
                if (prop == null) continue;
                mats.Add(m); colProps.Add(prop); baseCols.Add(m.GetColor(prop));
            }
    }

    /// 한 대 맞음 — 스윙 절정 타이밍에 호출됨
    public void Hit()
    {
        hitsLeft--;
        bounceT = 1f;
        var wp = transform.position;
        if (isRock)
            FX.Burst(wp + Vector3.up * 1.5f, new Color(0.72f, 0.70f, 0.65f, 0.95f), 12, 0.35f, 3.8f);
        else
        {
            FX.Burst(wp + Vector3.up * 4f, new Color(0.45f, 0.72f, 0.30f, 0.9f), 14, 0.45f, 4.5f);
            FX.Burst(wp + Vector3.up * 1.3f, new Color(0.55f, 0.38f, 0.20f, 0.9f), 8, 0.3f, 3f);
        }
        if (hitsLeft <= 0) Break();
    }

    void Update()
    {
        if (bounceT <= 0f) return;
        bounceT = Mathf.Max(0f, bounceT - Time.deltaTime * 2.4f);
        float k = bounceT;
        // 통통통 — 감쇠 진동 스쿼시
        float wob = Mathf.Sin((1f - k) * 24f) * 0.07f * k;
        transform.localScale = new Vector3(
            baseScale.x * (1f - wob), baseScale.y * (1f + wob * 1.5f), baseScale.z * (1f - wob));
        // 반짝반짝 — 진동 박자에 맞춰 하얗게
        float flash = Mathf.Abs(Mathf.Sin((1f - k) * 24f)) * k;
        for (int i = 0; i < mats.Count; i++)
            if (mats[i] != null)
                mats[i].SetColor(colProps[i], Color.Lerp(baseCols[i], Color.white * 1.7f, flash * 0.75f));
        if (bounceT <= 0f)
        {   // 원상 복구
            transform.localScale = baseScale;
            for (int i = 0; i < mats.Count; i++)
                if (mats[i] != null) mats[i].SetColor(colProps[i], baseCols[i]);
        }
    }

    /// 와르르 — 조각이 통! 퍼져나가고 먼지와 함께 사라짐
    void Break()
    {
        var wp = transform.position;
        var kind = isRock ? ItemDrop.Kind.Stone : ItemDrop.Kind.Wood;
        for (int j = 0; j < pieces; j++)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            var target = wp + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * Random.Range(2.2f, 4.5f);
            ItemDrop.Spawn(kind, target, 1, null, wp + Vector3.up * 2f);   // 중심에서 통! 하고 튐
        }
        // 먼지 + 뭉게 연기 (사라짐 연출)
        FX.Burst(wp + Vector3.up * 1f, new Color(0.75f, 0.68f, 0.58f, 0.9f), 26, 0.6f, 5f, 0.6f);
        FX.Burst(wp + Vector3.up * 2.5f, new Color(0.82f, 0.80f, 0.74f, 0.7f), 16, 1.0f, 2.2f, 0.95f);
        if (!isRock)
            FX.Burst(wp + Vector3.up * 4.5f, new Color(0.45f, 0.72f, 0.30f, 1f), 26, 0.55f, 6.5f);
        FollowCam.Shake(0.22f);
        TreeBlocker.Rebuild();
        Destroy(gameObject);
    }
}
