using System.Collections.Generic;
using UnityEngine;

/// 땅에 떨어진 아이템 — 인벤토리 아이콘 그대로(빌보드 스프라이트) 둥실둥실.
/// E로 주우면 쓔웅 하고 플레이어에게 빨려간 뒤 획득.
public class ItemDrop : MonoBehaviour
{
    public enum Kind { Wood, Stone, Egg }
    public Kind kind;
    public int amount = 1;

    public static readonly List<ItemDrop> All = new List<ItemDrop>();
    float bobT, baseY;
    bool highlighted;
    bool collecting; float flySpd;
    public bool Collecting => collecting;

    SpriteRenderer body, underlay;   // 본체 아이콘 + 근접 하이라이트(흰 밑판)
    Transform beam;
    static Transform player;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }
    void OnDestroy() { if (beam != null) Destroy(beam.gameObject); }

    /// 알처럼 등급이 있는 아이템은 여기에 실제 아이템 이름이 들어온다 (비면 기본값)
    public string itemId;

    static string IconId(Kind k) => k == Kind.Wood ? "나뭇가지" : k == Kind.Stone ? "돌" : "알";

    // 부서질 때 통! 하고 퍼져나가는 연출
    bool popping; float popT; Vector3 popStart, popTarget;

    public static ItemDrop Spawn(Kind kind, Vector3 pos, int amount, GameObject legacyVisual = null, Vector3? popFrom = null)
    {
        if (legacyVisual != null) Object.Destroy(legacyVisual);   // 3D 비주얼은 안 씀 (아이콘 통일)
        var set = DropDisplayManager.I;

        var g = new GameObject("드랍_" + kind);
        g.transform.SetParent(SceneBuckets.Drops);
        var d = g.AddComponent<ItemDrop>();
        d.kind = kind; d.amount = amount;

        // 본체 = 인벤토리 아이콘 스프라이트 (빌보드)
        d.body = g.AddComponent<SpriteRenderer>();
        d.body.sprite = ItemDB.Icon(IconId(kind));
        float size = set != null ? set.iconSize : 2.4f;
        g.transform.localScale = Vector3.one * size;

        // 근접 하이라이트 — 같은 아이콘의 흰 실루엣이 살짝 크게 뒤에
        var u = new GameObject("hl");
        u.transform.SetParent(g.transform, false);
        u.transform.localScale = Vector3.one * 1.14f;
        d.underlay = u.AddComponent<SpriteRenderer>();
        d.underlay.sprite = d.body.sprite;
        d.underlay.color = Color.white;
        d.underlay.sortingOrder = -1;
        u.SetActive(false);

        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
        d.baseY = pos.y + 1.1f;
        if (popFrom.HasValue)
        {   // 통! — 중심에서 포물선으로 튀어나감
            d.popping = true; d.popStart = popFrom.Value; d.popTarget = pos;
            g.transform.position = popFrom.Value;
        }
        else g.transform.position = pos + Vector3.up * 1.1f;
        d.MakeBeam();
        return d;
    }

    // ── 빛기둥 비콘 ──
    static Texture2D beamTex;
    static Texture2D BeamTex()
    {
        if (beamTex != null) return beamTex;
        int w = 16, h = 64;
        beamTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fade = Mathf.Pow(1f - y / (h - 1f), 1.6f);
                float edge = Mathf.Pow(Mathf.Sin(Mathf.PI * x / (w - 1f)), 0.7f);
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
            mr.sortingOrder = -5;   // 아이콘(0)·하이라이트(-1)보다 뒤 — 빛기둥이 아이템을 안 가림
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
        var c = set != null ? set.BeamColor(kind) : new Color(1.5f, 1.4f, 0.8f, 0.6f);
        beam.gameObject.SetActive(on && !collecting);
        foreach (Transform q in beam)
        {
            q.localScale = new Vector3(w, h, 1f);
            q.localPosition = Vector3.up * h * 0.5f;
            var mr = q.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = c;
        }
    }

    void Update()
    {
        // 줍기 연출 — 쓔웅 하고 플레이어 몸쪽으로 가속하며 빨려 들어감
        if (collecting)
        {
            if (player == null) { Award(); return; }
            flySpd += 85f * Time.deltaTime;
            var target = player.position + Vector3.up * 2.0f;
            transform.position = Vector3.MoveTowards(transform.position, target, flySpd * Time.deltaTime);
            transform.localScale = Vector3.MoveTowards(transform.localScale, transform.localScale * 0.3f, 4f * Time.deltaTime);
            if (Vector3.Distance(transform.position, target) < 1.2f) Award();
            return;
        }

        // 통! 튀어나가는 중 — 포물선 후 정착
        if (popping)
        {
            popT += Time.deltaTime / 0.45f;
            float t = Mathf.Clamp01(popT);
            float ease = 1f - Mathf.Pow(1f - t, 2f);   // 감속하며 도착
            var horiz = Vector3.Lerp(new Vector3(popStart.x, 0, popStart.z),
                                     new Vector3(popTarget.x, 0, popTarget.z), ease);
            float y = Mathf.Lerp(popStart.y, baseY, t) + Mathf.Sin(t * Mathf.PI) * 1.7f;
            transform.position = new Vector3(horiz.x, y, horiz.z);
            if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
            if (beam != null) beam.position = new Vector3(horiz.x, baseY - 1.1f, horiz.z);
            if (t >= 1f) popping = false;
            return;
        }

        var set = DropDisplayManager.I;
        float amp = set != null ? set.bobAmp : 0.18f;
        float spd = set != null ? set.bobSpeed : 2.4f;
        bobT += Time.deltaTime;
        var p = transform.position;
        p.y = baseY + Mathf.Sin(bobT * spd) * amp + amp;
        transform.position = p;
        if (beam != null) beam.position = new Vector3(p.x, baseY - 1.1f, p.z);

        // 빌보드 — 항상 카메라를 향함
        if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;

        // 근접 하이라이트 (흰 실루엣) = 줍기 가능 신호
        if (player == null) { var pl = GameObject.Find("Player"); if (pl != null) player = pl.transform; }
        if (player != null && underlay != null)
        {
            float hd = set != null ? set.highlightDist : 6.5f;
            float hs = set != null ? set.highlightScale : 1.15f;
            float dist = Vector3.Distance(
                new Vector3(p.x, 0, p.z), new Vector3(player.position.x, 0, player.position.z));
            bool near = dist < hd;
            if (near != highlighted)
            {
                highlighted = near;
                underlay.gameObject.SetActive(near);
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
        if (underlay != null) underlay.gameObject.SetActive(false);
        if (beam != null) beam.gameObject.SetActive(false);
    }

    /// 도착 — 실제 획득 + "+n 이름" 팝업
    void Award()
    {
        string label;
        Color c;
        switch (kind)
        {
            case Kind.Wood: Inv.Add("나뭇가지", amount); label = $"+{amount} 나뭇가지"; c = new Color(0.55f, 0.95f, 0.4f); break;
            case Kind.Stone: Inv.Add("돌", amount); label = $"+{amount} 돌"; c = new Color(0.9f, 0.9f, 0.9f); break;
            // ★알은 등급이 있다 — 어느 등급인지는 떨어뜨린 둥지가 정해준다
            default:
                string id = string.IsNullOrEmpty(itemId) ? "알" : itemId;
                Inv.Add(id, amount); label = $"+{amount} {id}"; c = new Color(1f, 0.9f, 0.5f); break;
        }
        var pos = player != null ? player.position + Vector3.up * 3.5f : transform.position;
        FX.PopText(pos, label, c, 1.7f);
        FX.Burst(transform.position, new Color(1.6f, 1.4f, 0.7f, 0.9f), 8, 0.18f, 1.8f);
        Destroy(gameObject);
    }
}
