using System.Collections.Generic;
using UnityEngine;

/// 아이템 아이콘 — 파일 '이름'으로 자동 연결.
/// Assets/Resources/Icons/ 에 같은 이름 PNG 를 덮어쓰면 다음 실행(또는 UI 다시
/// 그리기) 때 자동 반영. 새 아이템 아이콘도 파일만 넣으면 코드에서 이름으로 사용.
public static class IconLib
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Sprite Get(string name)
    {
        if (cache.TryGetValue(name, out var s) && s != null) return s;
        s = Resources.Load<Sprite>("Icons/" + name);
        cache[name] = s;
        return s;
    }

    /// UI 다시 그리기 때 캐시 비움 — 플레이 중 교체한 파일도 반영
    public static void ClearCache() => cache.Clear();
}
