using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 땅에 떨어진 아이템 — 둥둥 떠서 회전, E로 줍는다.
public class ItemDrop : MonoBehaviour
{
    public enum Kind { Wood, Stone, Egg }
    public Kind kind;
    public int amount = 1;

    public static readonly List<ItemDrop> All = new List<ItemDrop>();
    float bobT;
    float baseY;
    readonly List<GameObject> outlines = new List<GameObject>();
    bool highlighted;
    static Transform player;
    static Material sHullWhite, sMask; static bool matsInit;
    Transform beam;   // 빛기둥 비콘 — 멀리서도 아이템이 확 보이게

    static Texture2D beamTex;
    static Texture2D BeamTex()
    {
        if (beamTex != null) return beamTex;
        int w = 16, h = 64;
        beamTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fade = Mathf.Pow(1f - y / (h - 1f), 1.6f);                 // 위로 갈수록 옅게
                float edge = Mathf.Pow(Mathf.Sin(Mathf.PI * x / (w - 1f)), 0.7f); // 가장자리 부드럽게
                beamTex.SetPixel(x, y, new Color(1f, 1f, 1f, fade * edge));
            }
        beamTex.Apply();
        return beamTex;
    }

    void MakeBeam()
    {
        var c = kind == Kind.Wood ? new Color(0.65f, 1.7f, 0.5f, 0.6f)
              : kind == Kind.Stone ? new Color(1.3f, 1.3f, 1.5f, 0.6f)
              : new Color(1.9f, 1.55f, 0.5f, 0.7f);
        beam = new GameObject("beam").transform;
        for (int i = 0; i < 2; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(q.GetComponent<Collider>());
            q.SetParent(beam, false);
            q.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            q.localScale = new Vector3(1.1f, 6.5f, 1f);
            q.localPosition = Vector3.up * 3.25f;
            var mr = q.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default"));
            mr.material.mainTexture = BeamTex();
            mr.material.color = c;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    static void InitMats()
    {
        if (matsInit) return; matsInit = true;
        var sp = Object.FindFirstObjectByType<PetSpawner>();
        if (sp != null && sp.outlineHull != null)
        {
            sHullWhite = new Material(sp.outlineHull);            // 흰색 강조 라인
            sHullWhite.SetColor("_OutlineColor", Color.white);
            sMask = sp.outlineMask;
        }
    }

    public static ItemDrop Spawn(Kind kind, Vector3 pos, int amount, GameObject visual = null)
    {
        GameObject g;
        if (visual != null) g = visual;
        else if (kind == Kind.Wood)
        {   // 잔가지 — 길쭉하고 비스듬히, 눈에 띄는 크기
            g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(g.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = new Color(0.52f, 0.35f, 0.18f);
            g.GetComponent<MeshRenderer>().material = m;
            g.transform.localScale = new Vector3(0.95f, 3.4f, 0.95f);   // 2.5배 — 확실히 보이게
            g.transform.rotation = Quaternion.Euler(78f, Random.Range(0f, 360f), 0f);
        }
        else
        {
            g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = kind == Kind.Stone ? new Color(0.62f, 0.60f, 0.55f) : new Color(0.98f, 0.93f, 0.80f);
            g.GetComponent<MeshRenderer>().material = m;
            g.transform.localScale = kind == Kind.Stone
                ? new Vector3(3.2f, 2.3f, 3.0f)        // 조약돌 — 2.5배, 확실히 보이게
                : Vector3.one * 0.8f;
        }
        g.name = "드랍_" + kind;
        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
        g.transform.position = pos + Vector3.up * 0.8f;
        var d = g.GetComponent<ItemDrop>();
        if (d == null) d = g.AddComponent<ItemDrop>();
        d.kind = kind; d.amount = amount; d.baseY = pos.y + 0.8f;
        if (visual == null) d.MakeOutlines();   // 근접 하이라이트용 (알 비주얼은 자체 외곽선 있음)
        d.MakeBeam();                           // 멀리서도 보이는 빛기둥
        return d;
    }

    void OnDestroy() { if (beam != null) Destroy(beam.gameObject); }

    /// 외곽선 준비 — 평소 꺼두고 가까이 가면 켠다 (주울 수 있다는 신호)
    void MakeOutlines()
    {
        InitMats();
        if (sHullWhite == null || sMask == null) return;
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        foreach (var pair in new[] { ("Outline", sHullWhite), ("OutlineMask", sMask) })
        {
            var o = new GameObject(pair.Item1);
            o.transform.SetParent(transform, false);
            o.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = o.AddComponent<MeshRenderer>();
            mr.sharedMaterial = pair.Item2;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            o.SetActive(false);
            outlines.Add(o);
        }
    }

    void Update()
    {
        bobT += Time.deltaTime;   // 회전 없음 — 둥실둥실만
        var p = transform.position;
        p.y = baseY + Mathf.Sin(bobT * 2.4f) * 0.18f + 0.18f;
        transform.position = p;
        if (beam != null) beam.position = new Vector3(p.x, baseY - 0.8f, p.z);

        // 근접 하이라이트 — 줍기 사거리 안이면 테두리 반짝 + 살짝 커짐
        if (player == null) { var pl = GameObject.Find("Player"); if (pl != null) player = pl.transform; }
        if (player != null && outlines.Count > 0)
        {
            float dist = Vector3.Distance(
                new Vector3(p.x, 0, p.z), new Vector3(player.position.x, 0, player.position.z));
            bool near = dist < 6.5f;
            if (near != highlighted)
            {
                highlighted = near;
                foreach (var o in outlines) if (o != null) o.SetActive(near);
                transform.localScale *= near ? 1.15f : 1f / 1.15f;
            }
        }
    }

    public void Collect()
    {
        string label;
        Color c;
        switch (kind)
        {
            case Kind.Wood: Stock.Wood += amount; label = $"+{amount} 나무"; c = new Color(0.55f, 0.95f, 0.4f); break;
            case Kind.Stone: Stock.Stone += amount; label = $"+{amount} 돌"; c = new Color(0.87f, 0.87f, 0.87f); break;
            default: NestSite.EggCount += amount; label = "알 획득!"; c = new Color(1f, 0.9f, 0.5f); break;
        }
        FX.PopText(transform.position + Vector3.up * 1.2f, label, c, 1.6f);
        FX.Burst(transform.position, new Color(1.6f, 1.4f, 0.7f, 0.9f), 10, 0.2f, 2f);
        Destroy(gameObject);
    }
}

