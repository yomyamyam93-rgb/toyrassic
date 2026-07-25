using UnityEngine;

/// 화면 좌상단 군단 현황 + 획득 토스트 — 수집 프로토타입용 간이 HUD.
public class SquadHUD : MonoBehaviour
{
    static string toast; static float toastT;
    public static void Toast(string msg) { toast = msg; toastT = 3.2f; }

    void Update() { toastT -= Time.deltaTime; }

    void OnGUI()
    {
        int cap = BlueprintPickup.SupplyCap;
        int sup = BlueprintPickup.SquadSupply();
        var names = new System.Text.StringBuilder();
        int n = 0;
        foreach (var u in PetUnit.All)
            if (u.Alive && u.team == PetUnit.Team.Player)
                names.Append(n++ == 0 ? "" : "  ·  ").Append(u.name);

        var head = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        Shadowed(new Rect(14, 10, 900, 32), $"군단 {n}마리  —  인구수 {sup}/{cap}", head, Color.white);
        var small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        Shadowed(new Rect(14, 42, 1400, 24), names.ToString(), small, new Color(0.9f, 0.95f, 1f));

        if (toastT > 0f && !string.IsNullOrEmpty(toast))
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Shadowed(new Rect(0, Screen.height * 0.20f, Screen.width, 44), toast, big, new Color(1f, 0.9f, 0.4f));
        }
    }

    void Shadowed(Rect r, string s, GUIStyle st, Color c)
    {
        st.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), s, st);
        st.normal.textColor = c;
        GUI.Label(r, s, st);
    }
}
