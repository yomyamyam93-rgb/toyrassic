using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// 스킬칸 툴팁 — 마우스를 올리면 칸 위에 뜨는 설명.
///
/// ★왜 만들었나 (2026-07-28 사용자): 스킬칸에 "1번 트리케라 ×10 · 흩뿌리기" 같은
///   긴 글자를 우겨넣고 있었는데, 54px 칸에서는 그게 아무것도 안 읽힌다.
///   칸에는 키 글자만 크게 남기고 자세한 건 전부 여기로 내렸다.
///   화면은 조용해지고, 알고 싶을 때만 마우스를 올리면 된다.
///
/// 툴팁은 하나만 만들어 네 칸이 돌려 쓴다 — 어차피 한 번에 하나만 뜬다.
public class SkillTooltip : MonoBehaviour
{
    RectTransform rt;
    Text text;
    RectTransform anchorTo;

    public static SkillTooltip Create(Transform canvas, Font font, UIStyle st)
    {
        var go = new GameObject("SkillTooltip", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(canvas, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);

        var border = go.AddComponent<Image>();
        border.sprite = st != null ? st.Round() : null;
        border.type = Image.Type.Sliced;
        border.color = st != null ? st.panelBorder : new Color(0.627f, 0.553f, 0.455f);
        border.raycastTarget = false;   // 툴팁이 마우스를 가로채면 칸에서 벗어난 걸로 읽혀 깜빡인다

        float bw = st != null ? st.borderWidth : 3f;
        var inner = new GameObject("in", typeof(RectTransform)).GetComponent<RectTransform>();
        inner.SetParent(rt, false);
        inner.anchorMin = Vector2.zero; inner.anchorMax = Vector2.one;
        inner.offsetMin = new Vector2(bw, bw); inner.offsetMax = new Vector2(-bw, -bw);
        var bgi = inner.gameObject.AddComponent<Image>();
        bgi.sprite = st != null ? st.Round() : null;
        bgi.type = Image.Type.Sliced;
        bgi.color = st != null ? st.panelBg : new Color(0.945f, 0.914f, 0.859f);
        bgi.raycastTarget = false;

        var t = new GameObject("t", typeof(RectTransform)).AddComponent<Text>();
        t.transform.SetParent(inner, false);
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(10f, 8f); trt.offsetMax = new Vector2(-10f, -8f);
        t.font = font; t.fontSize = 15;
        t.alignment = TextAnchor.UpperLeft;
        t.color = st != null ? st.textMain : new Color(0.23f, 0.2f, 0.18f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;

        var tip = go.AddComponent<SkillTooltip>();
        tip.rt = rt; tip.text = t;
        go.SetActive(false);
        return tip;
    }

    public void Show(RectTransform slot, string body)
    {
        anchorTo = slot;
        if (text.text != body) text.text = body;
        gameObject.SetActive(true);
        Place();
    }

    public void Hide(RectTransform slot)
    {
        // 다른 칸으로 옮겨간 뒤에 옛 칸의 '나감' 이 오면 새 툴팁을 지우게 된다
        if (anchorTo != slot) return;
        gameObject.SetActive(false);
    }

    void LateUpdate() { Place(); }

    void Place()
    {
        if (anchorTo == null) return;
        // 글자에 맞춰 상자 크기를 잡는다 (줄 수가 스킬마다 다르다)
        var pref = new Vector2(text.preferredWidth, text.preferredHeight);
        rt.sizeDelta = new Vector2(Mathf.Max(200f, pref.x + 20f), pref.y + 16f);

        // 칸 바로 위에 띄운다. 부모 좌표계로 바꿔서 놓아야 해상도가 바뀌어도 안 어긋난다.
        var parent = (RectTransform)rt.parent;
        var world = anchorTo.TransformPoint(new Vector3(0f, anchorTo.rect.height * 0.5f + 10f, 0f));
        var local = parent.InverseTransformPoint(world);
        rt.anchoredPosition = new Vector2(local.x, local.y - parent.rect.height * -0f);

        // 화면 밖으로 나가지 않게 좌우만 가둔다
        float half = rt.sizeDelta.x * 0.5f;
        float lim = parent.rect.width * 0.5f - half - 8f;
        var p = rt.anchoredPosition;
        p.x = Mathf.Clamp(p.x, -lim, lim);
        rt.anchoredPosition = p;
    }
}

/// 스킬칸에 붙어 마우스가 올라왔는지 본다
public class SkillHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public SkillTooltip tip;
    public SkillSystem owner;
    public int hud;

    RectTransform Rt => (RectTransform)transform;
    bool inside;

    public void OnPointerEnter(PointerEventData e)
    {
        inside = true;
        if (tip != null && owner != null) tip.Show(Rt, owner.TipFor(hud));
    }

    public void OnPointerExit(PointerEventData e)
    {
        inside = false;
        if (tip != null) tip.Hide(Rt);
    }

    /// 쿨타임 초가 툴팁 안에서도 흘러야 한다 — 올려둔 채 기다릴 때 멈춰 있으면 고장으로 보인다
    void Update()
    {
        if (inside && tip != null && owner != null) tip.Show(Rt, owner.TipFor(hud));
    }
}
