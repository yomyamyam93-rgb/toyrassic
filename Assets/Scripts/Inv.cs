using UnityEngine;

/// ★진짜 슬롯 인벤토리 — 24칸, 칸마다 (아이템 ID, 수량). 칸끼리 드래그 이동/합치기.
/// 아이템 ID = 아이콘 파일 이름 (ItemDB). 모든 획득/소모는 여기를 거친다.
public static class Inv
{
    public const int Size = 24;

    public struct Slot
    {
        public string id;
        public int count;
        public bool Empty => count <= 0 || string.IsNullOrEmpty(id);
    }

    public static readonly Slot[] Slots = new Slot[Size];

    static Inv() { Add("활", 1); }   // 기본 무기

    public static int Count(string id)
    {
        int n = 0;
        for (int i = 0; i < Size; i++)
            if (!Slots[i].Empty && Slots[i].id == id) n += Slots[i].count;
        return n;
    }

    public static bool CanAdd(string id)
    {
        for (int i = 0; i < Size; i++)
            if (Slots[i].Empty || Slots[i].id == id) return true;
        return false;
    }

    /// 획득 — 같은 아이템 칸에 쌓고, 없으면 빈 칸에. 가득 차면 false
    public static bool Add(string id, int n)
    {
        if (n <= 0) return true;
        for (int i = 0; i < Size; i++)
            if (!Slots[i].Empty && Slots[i].id == id) { Slots[i].count += n; return true; }
        for (int i = 0; i < Size; i++)
            if (Slots[i].Empty) { Slots[i].id = id; Slots[i].count = n; return true; }
        return false;
    }

    /// 소모 — 부족하면 false (아무것도 안 뺌)
    public static bool Consume(string id, int n)
    {
        if (Count(id) < n) return false;
        for (int i = 0; i < Size && n > 0; i++)
        {
            if (Slots[i].Empty || Slots[i].id != id) continue;
            int take = Mathf.Min(Slots[i].count, n);
            Slots[i].count -= take; n -= take;
            if (Slots[i].count <= 0) Slots[i] = default;
        }
        return true;
    }

    /// 칸 이동 — 같은 아이템이면 합치기, 다르면 자리 교환
    public static void Move(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= Size || b >= Size) return;
        var A = Slots[a]; var B = Slots[b];
        if (!A.Empty && !B.Empty && A.id == B.id)
        {
            B.count += A.count;
            A = default;
        }
        else { var t = A; A = B; B = t; }
        Slots[a] = A; Slots[b] = B;
    }
}
