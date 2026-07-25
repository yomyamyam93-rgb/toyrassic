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

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public static ItemDrop Spawn(Kind kind, Vector3 pos, int amount, GameObject visual = null)
    {
        GameObject g;
        if (visual != null) g = visual;
        else if (kind == Kind.Wood)
        {   // 잔가지 — 길쭉하고 비스듬히
            g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(g.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = new Color(0.5f, 0.34f, 0.18f);
            g.GetComponent<MeshRenderer>().material = m;
            g.transform.localScale = new Vector3(0.22f, 0.85f, 0.22f);
            g.transform.rotation = Quaternion.Euler(80f, Random.Range(0f, 360f), 0f);
        }
        else
        {
            g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(g.GetComponent<Collider>());
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.color = kind == Kind.Stone ? new Color(0.62f, 0.60f, 0.55f) : new Color(0.98f, 0.93f, 0.80f);
            g.GetComponent<MeshRenderer>().material = m;
            g.transform.localScale = kind == Kind.Stone
                ? new Vector3(0.75f, 0.55f, 0.7f)      // 조약돌 — 납작 둥글
                : Vector3.one * 0.8f;
        }
        g.name = "드랍_" + kind;
        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
        g.transform.position = pos + Vector3.up * 0.8f;
        var d = g.GetComponent<ItemDrop>();
        if (d == null) d = g.AddComponent<ItemDrop>();
        d.kind = kind; d.amount = amount; d.baseY = pos.y + 0.8f;
        return d;
    }

    void Update()
    {
        bobT += Time.deltaTime;
        transform.Rotate(0f, 80f * Time.deltaTime, 0f, Space.World);
        var p = transform.position;
        p.y = baseY + Mathf.Sin(bobT * 2.4f) * 0.18f + 0.18f;
        transform.position = p;
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

/// E키 줍기 — 근처의 드랍 아이템을 줍는다. 플레이어에 부착
public class PlayerPickup : MonoBehaviour
{
    [Tooltip("줍기 사거리 (m)")] public float reach = 5.5f;
    float cd;

    void Update()
    {
        cd -= Time.deltaTime;
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null) pressed = k.eKey.isPressed;   // 꾹 누르면 연달아 줍기
#else
        pressed = Input.GetKey(KeyCode.E);
#endif
        if (!pressed || cd > 0f) return;

        ItemDrop best = null; float bd = reach;
        foreach (var d in ItemDrop.All)
        {
            if (d == null) continue;
            float dist = Vector3.Distance(
                new Vector3(d.transform.position.x, 0, d.transform.position.z),
                new Vector3(transform.position.x, 0, transform.position.z));
            if (dist < bd) { bd = dist; best = d; }
        }
        if (best == null) return;
        cd = 0.12f;
        best.Collect();
    }
}
