using UnityEngine;

/// 런타임 생성 오브젝트 정리함 — 하이라키가 드랍·이펙트로 어질러지지 않게
/// 종류별 부모 아래로 모은다.
public static class SceneBuckets
{
    static Transform Get(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) go = new GameObject(name);
        return go.transform;
    }

    public static Transform Drops => Get("— 드랍템 —");
    public static Transform Fx => Get("— 이펙트 —");
    public static Transform Bars => Get("— 체력바 —");
}
