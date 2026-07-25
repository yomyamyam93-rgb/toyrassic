using UnityEngine;
using UnityEditor;

/// 펫 인스펙터 — 크기 슬라이더로 같은 종(species) 전체를 한 번에 조절.
[CustomEditor(typeof(PetUnit))]
[CanEditMultipleObjects]
public class PetUnitEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var u = (PetUnit)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("🐾 크기 조절 — 같은 종 전체 적용", EditorStyles.boldLabel);
        if (string.IsNullOrEmpty(u.species))
            EditorGUILayout.HelpBox("species 가 비어 있어 이 개체에만 적용됩니다.", MessageType.Info);

        float cur = MaxSide(u.gameObject);
        EditorGUILayout.LabelField("현재 실측", cur.ToString("F1") + " m");
        float want = u.sizeM > 0f ? u.sizeM : cur;
        float next = EditorGUILayout.Slider("목표 크기 (m)", want, 1f, 40f);
        if (Mathf.Abs(next - want) > 0.005f)
        {
            int applied = 0;
            foreach (var o in Object.FindObjectsByType<PetUnit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool same = !string.IsNullOrEmpty(u.species) && o.species == u.species;
                if (o != u && !same) continue;
                Apply(o, next);
                applied++;
            }
            if (!Application.isPlaying)
                UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }
    }

    static float MaxSide(GameObject go)
    {
        var rs = go.GetComponentsInChildren<MeshRenderer>();
        if (rs.Length == 0) return 0f;
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
    }

    static void Apply(PetUnit o, float targetSize)
    {
        float cur = MaxSide(o.gameObject);
        if (cur < 0.01f) return;
        Undo.RecordObject(o.transform, "펫 크기");
        Undo.RecordObject(o, "펫 크기");
        o.transform.localScale *= targetSize / cur;
        o.sizeM = targetSize;

        // 접지 다시 (에디트 모드에서만 — 플레이 중엔 Ground() 가 매 프레임 처리)
        if (!Application.isPlaying)
        {
            var terr = Terrain.activeTerrain;
            var mr = o.GetComponentInChildren<MeshRenderer>();
            if (terr != null && mr != null)
            {
                var pos = o.transform.position;
                pos.y = terr.SampleHeight(pos) + terr.transform.position.y;
                o.transform.position = pos;
                var pp = o.transform.position;
                pp.y += pos.y - mr.bounds.min.y;
                o.transform.position = pp;
            }
        }
        EditorUtility.SetDirty(o);
        EditorUtility.SetDirty(o.transform);
    }
}
