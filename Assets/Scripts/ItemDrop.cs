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
    bool collecting; float flySpd;
    /// 줍는 중 (빨려가는 중) — 중복 줍기 방지
    public bool Collecting => collecting;
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
        beam = new GameObject("beam").transform;
        beam.SetParent(SceneBuckets.Drops);
        for (int i = 0; i < 2; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(q.GetComponent<Collider>());
            q.SetParent(beam, false);
            q.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.material = new Material(Shader.Find("Sprites/Default"));
            mr.material.mainTexture = BeamTex();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        ApplyBeamSettings();
    }

    /// 빛기둥 설정 반영 — DropDisplayManager 값 변경 시 즉시 호출됨
    public void ApplyBeamSettings()
    {
        if (beam == null) return;
        var set = DropDisplayManager.I;
        bool on = set == null || set.beamOn;
        float h = set != null ? set.beamHeight : 6.5f;
        float w = set != null ? set.beamWidth : 1.1f;
        var c = set != null ? set.BeamColor(kind)
              : new Color(1.5f, 1.4f, 0.8f, 0.6f);
        beam.gameObject.SetActive(on);
        foreach (Transform q in beam)
        {
            q.localScale = new Vector3(w, h, 1f);
            q.localPosition = Vector3.up * h * 0.5f;
            var mr = q.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = c;
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
        var set = DropDisplayManager.I;   // 표시 설정 (없으면 기본값)
        if (visual != null) g = visual;
        else if (kind == Kind.Wood)
        {   // 잔가지 — 길쭉하고 비스듬히
            g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(g.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = new Color(0.52f, 0.35f, 0.18f);
            g.GetComponent<MeshRenderer>().material = m;
            float len = set != null ? set.stickLength : 3.4f;
            float th = set != null ? set.stickThick : 0.95f;
            g.transform.localScale = new Vector3(th, len, th);
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
                ? (set != null ? set.pebbleScale : new Vector3(3.2f, 2.3f, 3.0f))
                : Vector3.one * 0.8f;
        }
        g.name = "드랍_" + kind;
        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
        g.transform.position = pos + Vector3.up * 0.8f;
        var d = g.GetComponent<ItemDrop>();
        if (d == null) d = g.AddComponent<ItemDrop>();
        d.kind = kind; d.amount = amount; d.baseY = pos.y + 0.8f;
        g.transform.SetParent(SceneBuckets.Drops);   // 하이라키 정리
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
        // 줍기 연출 — 쓔웅 하고 플레이어 몸쪽으로 가속하며 빨려 들어감
        if (collecting)
        {
            if (player == null) { Award(); return; }
            flySpd += 85f * Time.deltaTime;                          // 가속
            var target = player.position + Vector3.up * 2.0f;
            transform.position = Vector3.MoveTowards(transform.position, target, flySpd * Time.deltaTime);
            transform.localScale = Vector3.MoveTowards(transform.localScale, transform.localScale * 0.3f, 3.5f * Time.deltaTime);
            if (Vector3.Distance(transform.position, target) < 1.2f) Award();
            return;
        }

        var set = DropDisplayManager.I;
        float amp = set != null ? set.bobAmp : 0.18f;
        float spd = set != null ? set.bobSpeed : 2.4f;
        bobT += Time.deltaTime;   // 회전 없음 — 둥실둥실만
        var p = transform.position;
        p.y = baseY + Mathf.Sin(bobT * spd) * amp + amp;
        transform.position = p;
        if (beam != null) beam.position = new Vector3(p.x, baseY - 0.8f, p.z);

        // 근접 하이라이트 — 줍기 사거리 안이면 테두리 반짝 + 살짝 커짐
        if (player == null) { var pl = GameObject.Find("Player"); if (pl != null) player = pl.transform; }
        if (player != null && outlines.Count > 0)
        {
            float hd = set != null ? set.highlightDist : 6.5f;
            float hs = set != null ? set.highlightScale : 1.15f;
            float dist = Vector3.Distance(
                new Vector3(p.x, 0, p.z), new Vector3(player.position.x, 0, player.position.z));
            bool near = dist < hd;
            if (near != highlighted)
            {
                highlighted = near;
                foreach (var o in outlines) if (o != null) o.SetActive(near);
                transform.localScale *= near ? hs : 1f / hs;
            }
        }
    }

    /// 줍기 시작 — 빨려가는 연출 후 도착 시 획득
    public void Collect()
    {
        if (collecting) return;
        collecting = true;
        flySpd = 6f;
        foreach (var o in outlines) if (o != null) o.SetActive(false);
        if (beam != null) beam.gameObject.SetActive(false);
    }

    /// 도착 — 실제 획득 + "+n 이름" 팝업
    void Award()
    {
        string label;
        Color c;
        switch (kind)
        {
            case Kind.Wood: Stock.Wood += amount; label = $"+{amount} 나뭇가지"; c = new Color(0.55f, 0.95f, 0.4f); break;
            case Kind.Stone: Stock.Stone += amount; label = $"+{amount} 돌"; c = new Color(0.9f, 0.9f, 0.9f); break;
            default: NestSite.EggCount += amount; label = $"+{amount} 알"; c = new Color(1f, 0.9f, 0.5f); break;
        }
        var pos = player != null ? player.position + Vector3.up * 3.5f : transform.position;
        FX.PopText(pos, label, c, 1.7f);
        FX.Burst(transform.position, new Color(1.6f, 1.4f, 0.7f, 0.9f), 8, 0.18f, 1.8f);
        Destroy(gameObject);
    }
}

