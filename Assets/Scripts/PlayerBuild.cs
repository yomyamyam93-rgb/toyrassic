using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// B키로 부화기 설치 — 재료(나무·돌) 필요. 플레이어에 부착.
/// ※씬에 붙는 컴포넌트라 반드시 파일명=클래스명 (다른 파일에 넣으면 Missing 됨)
public class PlayerBuild : MonoBehaviour
{
    [Tooltip("부화기 건설 비용")] public int costWood = 20, costStone = 12;

    void Update()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null) pressed = k.bKey.wasPressedThisFrame;
#else
        pressed = Input.GetKeyDown(KeyCode.B);
#endif
        if (!pressed) return;
        if (Incubator.Active != null) { SquadHUD.Toast("부화기는 이미 있다 — 알을 가져가자"); return; }
        if (Stock.Wood < costWood || Stock.Stone < costStone)
        {
            SquadHUD.Toast($"재료 부족!  부화기 = 나무 {costWood}·돌 {costStone}  (지금: 나무 {Stock.Wood}·돌 {Stock.Stone})");
            return;
        }
        Stock.Wood -= costWood; Stock.Stone -= costStone;
        Place(transform);
    }

    /// 부화기 설치 (비용 차감은 호출자가) — B키·제작 창 공용
    public static void Place(Transform player)
    {
        var pos = player.position + player.forward * 8f;
        var terr = Terrain.activeTerrain;
        if (terr != null) pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
        var go = new GameObject("부화기");
        go.transform.position = pos;
        var inc = go.AddComponent<Incubator>();
        inc.spawner = Object.FindFirstObjectByType<PetSpawner>();
        SquadHUD.Toast("부화기 설치! 알을 가지고 다가가면 품기 시작");
        FX.Burst(pos + Vector3.up, new Color(0.9f, 0.85f, 0.7f, 0.9f), 16, 0.4f, 3f);
    }
}
