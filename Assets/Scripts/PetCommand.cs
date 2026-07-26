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

    [Header("소집 (E)")]
    [Tooltip("이 거리 안의 내 펫을 부른다")] public float callRadius = 20f;
    [Tooltip("★탄 펫까지 포함해 총 몇 마리를 데리고 다닐 수 있나")] public int maxParty = 4;

    [Header("탑승 보정 — 탄 펫은 두 몫을 한다")]
    [Tooltip("탄 펫의 공격력 배수")] public float mountDamageMul = 1.5f;
    [Tooltip("탄 펫의 체력 배수")] public float mountHpMul = 1.6f;
    [Tooltip("탄 펫의 공격 속도 배수")] public float mountAtkSpeedMul = 1.25f;

    /// 따라다닐 수 있는 수 — 탄 펫이 한 자리를 차지한다
    public int MaxFollowers => Mathf.Max(0, maxParty - (Mount != null ? 1 : 0));
    [Tooltip("주인 뒤로 이 정도 거리에 붙는다")] public float followGap = 4.5f;
    [Tooltip("이보다 멀어지면 뛰어서 따라붙는다")] public float catchUpDist = 9f;

    [Header("돌격 (R)")]
    [Tooltip("명령할 수 있는 최대 거리")] public float orderRange = 45f;

    /// 지금 따라다니는 펫들
    public static readonly List<PetUnit> Followers = new List<PetUnit>();
    /// 타고 있는 펫 (없으면 null)
    public static PetUnit Mount;

    void Awake() { I = this; Followers.Clear(); Mount = null; }

    Camera cam;

    void Update()
    {
        for (int i = Followers.Count - 1; i >= 0; i--)
            if (Followers[i] == null || !Followers[i].Alive) Followers.RemoveAt(i);
        MoveFollowers();

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

    // ── E: 소집 ─────────────────────────────────────────────
    public void Gather()
    {
        int added = 0, already = Followers.Count;
        foreach (var u in PetUnit.All)
        {
            if (Followers.Count >= MaxFollowers) break;
            if (u == null || !u.Alive || u.team != PetUnit.Team.Player) continue;
            if (u.isAvatar || u.isStructure || u.mounted) continue;
            if (Followers.Contains(u)) continue;
            var d = u.transform.position - transform.position; d.y = 0f;
            if (d.magnitude > callRadius) continue;
            Followers.Add(u);
            u.following = true;
            u.forceTarget = null;          // 명령 해제 — 이제 나를 따라온다
            added++;
            FX.Burst(u.transform.position + Vector3.up * u.body * 0.5f,
                     new Color(0.6f, 1.4f, 1.9f, 0.9f), 10, u.body * 0.05f, u.body * 0.4f);
        }
        if (added > 0)
            SquadHUD.Toast($"소집!  {Followers.Count}/{MaxFollowers}마리가 따라온다" + (Mount != null ? "  (+탑승 1)" : ""));
        else if (already >= MaxFollowers)
            SquadHUD.Toast($"더는 못 데려간다 (탄 펫 포함 최대 {maxParty}마리)");
        else
            SquadHUD.Toast("근처에 부를 펫이 없다");
    }

    // ── R: 돌격 명령 ────────────────────────────────────────
    public void OrderTo(Vector3 spot)
    {
        if (Followers.Count == 0) { SquadHUD.Toast("데리고 있는 펫이 없다"); return; }
        var d = spot - transform.position; d.y = 0f;
        if (d.magnitude > orderRange) spot = transform.position + d.normalized * orderRange;

        int n = Followers.Count;
        for (int i = 0; i < n; i++)
        {
            var u = Followers[i];
            if (u == null) continue;
            u.following = false;
            // 한 점에 겹치지 않게 살짝 벌려서 보낸다
            float a = n > 1 ? (i / (float)n) * Mathf.PI * 2f : 0f;
            u.orderSpot = spot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (n > 1 ? 3f : 0f);
            u.hasOrder = true;
        }
        Followers.Clear();
        FX.Sweep(spot, 0f, 360f, 4f, new Color(1.9f, 0.6f, 0.4f, 0.9f), 0.4f, 0.3f);
        FX.Burst(spot + Vector3.up * 0.5f, new Color(1.9f, 0.7f, 0.4f, 0.95f), 18, 0.3f, 4f);
        SquadHUD.Toast($"돌격!  {n}마리를 보냈다");
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
        Followers.Remove(pet);
        pet.following = false; pet.hasOrder = false;
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

    // ── 따라다니기 ──────────────────────────────────────────
    void MoveFollowers()
    {
        for (int i = 0; i < Followers.Count; i++)
        {
            var u = Followers[i];
            if (u == null || !u.Alive || u.mounted) continue;
            // 주인 뒤쪽에 부채꼴로 붙는다 (우글우글, 단 앞은 안 막게)
            float side = (i - (Followers.Count - 1) * 0.5f) * 2.6f;
            var back = -transform.forward * followGap + transform.right * side;
            u.followSpot = transform.position + back;
        }
    }
}
