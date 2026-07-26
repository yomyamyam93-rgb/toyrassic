using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// E키 줍기 — 근처의 드랍 아이템(잔가지·조약돌·알)을 줍는다. 플레이어에 부착.
/// ※씬에 붙는 컴포넌트라 반드시 파일명=클래스명 (다른 파일에 넣으면 Missing 됨)
public class PlayerPickup : MonoBehaviour
{
    [Tooltip("줍기 사거리 (m)")] public float reach = 6.5f;
    float cd;

    void Update()
    {
        cd -= Time.deltaTime;
        if (BuildSystem.IsBuilding || MenuUI.IsOpen || PetNameUI.IsOpen) return;   // 창·건축 중엔 줍기 잠금
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null) pressed = k.fKey.isPressed;   // F = 상호작용(줍기), 꾹 누르면 연달아
#else
        pressed = Input.GetKey(KeyCode.F);
#endif
        if (!pressed || cd > 0f) return;

        ItemDrop best = null; float bd = reach;
        foreach (var d in ItemDrop.All)
        {
            if (d == null || d.Collecting) continue;   // 이미 빨려가는 중이면 스킵
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
