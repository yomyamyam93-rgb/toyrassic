using UnityEngine;

/// 좌상단 '내 펫' 현황 (이름·레벨·체력·경험치) + 토스트 — 한 마리 키우기 HUD.
public class SquadHUD : MonoBehaviour
{
    static string toast; static float toastT;
    public static void Toast(string msg) { toast = msg; toastT = 3.2f; }

    void Update() { toastT -= Time.deltaTime; }

    void OnGUI()
    {
        var pet = BlueprintPickup.MyPet();
        var head = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };

        if (pet == null)
            Shadowed(new Rect(14, 10, 900, 32), "펫 없음 — 야생을 격파하고 설계도를 주워보자!", head, new Color(1f, 0.75f, 0.6f));
        else
        {
            Shadowed(new Rect(14, 10, 900, 32), $"{pet.name}   Lv.{pet.level}", head, Color.white);
            // 체력 바
            Bar(new Rect(16, 44, 240, 14), pet.maxHp > 0 ? pet.hp / pet.maxHp : 0f,
                new Color(0.35f, 0.9f, 0.4f), $"{Mathf.CeilToInt(pet.hp)}/{Mathf.CeilToInt(pet.maxHp)}");
            // 경험치 바
            float need = 25f + 20f * (pet.level - 1);
            Bar(new Rect(16, 62, 240, 10), Mathf.Clamp01(pet.xp / need),
                new Color(1f, 0.85f, 0.25f), null);
        }

        if (toastT > 0f && !string.IsNullOrEmpty(toast))
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 27, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Shadowed(new Rect(0, Screen.height * 0.20f, Screen.width, 44), toast, big, new Color(1f, 0.9f, 0.4f));
        }
    }

    void Bar(Rect r, float f, Color c, string label)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = c; GUI.DrawTexture(new Rect(r.x + 1, r.y + 1, (r.width - 2) * f, r.height - 2), Texture2D.whiteTexture);
        GUI.color = Color.white;
        if (label != null)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = Color.white;
            GUI.Label(r, label, st);
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
