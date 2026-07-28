using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// 펫 지휘 — 여러 마리를 데리고 다니며 싸운다.
///
///   E : 주변의 내 펫을 소집 (바짝 붙어 따라온다, 최대 인원까지)
///   R : 조준한 지점으로 돌격 명령 (거기서부터 알아서 싸운다)
///   펫을 클릭 : 그 펫에 탑승 / 다시 클릭하면 내림
///
/// ★따라다니기는 '소집 중'일 때만이라 길찾기가 필요 없다. 그냥 주인 쪽으로
///   붙기만 하면 되고, 놓으면 그 자리에서 싸운다.
public class PetCommand : MonoBehaviour
{
    public static PetCommand I;

    // ★지휘(E 소집 / R 돌격)는 펫 행동과 함께 삭제됨 (2026-07-28).
    //   펫이 스스로 움직이지 않으니 불러도 따라올 수가 없다. 행동을 다시 만든 뒤
    //   여기에 다시 붙인다. 지금 이 컴포넌트가 하는 일은 '탑승' 하나뿐이다.

    [Header("군단")]
    [Tooltip("동시에 밖에 내놓을 수 있는 펫 수 (탄 펫 포함) — 펫 보관함이 쓴다")]
    public int maxParty = 4;

    [Header("탑승 보정 — 탄 펫은 두 몫을 한다")]
    [Tooltip("탄 펫의 공격력 배수")] public float mountDamageMul = 1.5f;
    [Tooltip("탄 펫의 체력 배수")] public float mountHpMul = 1.6f;
    [Tooltip("탄 펫의 공격 속도 배수")] public float mountAtkSpeedMul = 1.25f;

    /// 타고 있는 펫 (없으면 null)
    public static PetUnit Mount;

    void Awake() { I = this; Mount = null; }

    Camera cam;

    void Update()
    {
        // ★탑승은 핫바가 정한다 — 펫 창에서 0번 칸에 끌어다 넣으면 그 펫을 탄다.
        //   무기를 든 채로도 타야 하므로 키·클릭을 쓰지 않는다 (공격과 안 겹침).
        SyncMountFromHotbar();
    }

    /// 핫바 탑승 칸에 올려둔 펫과 실제 탑승을 맞춘다
    void SyncMountFromHotbar()
    {
        // ★탄 펫이 쓰러지면 탑승도 끝 — 칸을 비우고 내린다 (PlayerMove 가 퐁 떨어뜨린다)
        if (Mount != null && !Mount.Alive)
        {
            Hotbar.MountPet = null;
            Mount = null;
            if (Hotbar.I != null) Hotbar.I.RefreshMountSlot();
        }
        var want = Hotbar.MountPet;
        if (want != null && !want.Alive) { Hotbar.MountPet = null; want = null; }
        if (want == Mount) return;
        if (want == null) Dismount();
        else MountOn(want);
    }

    // ── 클릭해서 탑승 ───────────────────────────────────────
    /// 화면 좌표로 펫을 골라 탄다 / 이미 타고 있으면 내린다
    public bool ToggleMountAt(Vector2 screenPos, Camera cam)
    {
        if (Mount != null) { Dismount(); return true; }
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(screenPos);
        var plane = new Plane(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float e)) return false;
        var hit = ray.GetPoint(e);

        PetUnit best = null; float bd = float.MaxValue;
        foreach (var u in PetUnit.All)
        {
            if (u == null || !u.Alive || u.team != PetUnit.Team.Player) continue;
            if (u.isAvatar || u.isStructure) continue;
            var d = u.transform.position - hit; d.y = 0f;
            // 덩치만큼 넉넉히 — 큰 펫은 더 쉽게 집힌다
            float reach = Mathf.Max(3f, u.body * 0.7f);
            if (d.magnitude < reach && d.magnitude < bd) { bd = d.magnitude; best = u; }
        }
        if (best == null) return false;
        MountOn(best);
        return true;
    }

    public void MountOn(PetUnit pet)
    {
        if (pet == null || !pet.Alive) return;
        if (Mount != null) Dismount();
        Mount = pet;
        pet.SetMountBuff(mountDamageMul, mountHpMul, mountAtkSpeedMul);   // 두 몫을 한다
        SquadHUD.Toast($"{pet.name} 탑승!  힘·체력·공속 강화");
        FX.Burst(pet.transform.position + Vector3.up * pet.body * 0.6f,
                 new Color(1.8f, 1.6f, 0.6f, 0.95f), 18, pet.body * 0.06f, pet.body * 0.5f);
    }

    public void Dismount()
    {
        if (Mount == null) return;
        Mount.SetMountBuff(1f, 1f, 1f);   // 보정 해제
        SquadHUD.Toast($"{Mount.name}에서 내렸다");
        Mount = null;
    }

}
