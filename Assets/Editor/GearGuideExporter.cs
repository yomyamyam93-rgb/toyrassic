using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

/// ★장비 모델링 가이드 내보내기 — 블렌더에서 무기를 '정확한 자리'에 만들기 위한 기준 파일.
///
/// 캐릭터 몸통(실제 메시)·양손·무기 슬롯을 게임에서 보이는 실제 크기 그대로 OBJ 로 뽑는다.
/// 블렌더에서 열어 슬롯에 맞춰 무기를 만들고, 가이드 오브젝트를 지운 뒤 glb 로 내보내면
/// 축·크기·손잡이 위치가 그대로 맞는다.
///
/// ※들여오기/내보내기 축 설정만 서로 같으면 (블렌더 기본값이 그렇다) 좌표 변환은
///   왕복하며 상쇄되므로 따로 맞출 필요 없다.
public static class GearGuideExporter
{
    [MenuItem("토이라기/장비 모델링 가이드 내보내기")]
    public static void Export()
    {
        string path = EditorUtility.SaveFilePanel("장비 모델링 가이드 저장", "", "toyrassic_장비가이드.obj", "obj");
        if (string.IsNullOrEmpty(path)) return;
        ExportTo(path);
    }

    /// 경로를 직접 주고 뽑기 (메뉴·자동화 공용)
    public static string ExportTo(string path)
    {
        var pb = Object.FindFirstObjectByType<PlayerBow>();
        if (pb == null) return "씬에서 PlayerBow(플레이어)를 찾지 못했습니다.";

        var t = pb.transform;
        float pScale = t.localScale.x;                       // 캐릭터 전체 배율
        var so = new SerializedObject(pb);
        Vector3 gripPos = so.FindProperty("gripPosOffset").vector3Value;
        Vector3 gripEuler = so.FindProperty("gripEuler").vector3Value;
        float toolScale = so.FindProperty("toolScale").floatValue;

        // ── 실제 배치 계산 (LateUpdate 의 손 위치 식과 동일) ──
        float handDia = pb.handRadius * 2f * pScale;                       // 손 지름(월드)
        var handR = new Vector3(pb.handSide, pb.handUp, 0.3f);             // 오른손 — 도구를 든다
        var handL = new Vector3(-pb.handSide * 0.92f, pb.handUp, 0.5f);    // 왼손 — 활을 든다

        // 도구 슬롯: 손의 자식 → 손 배율이 곱해진다
        float handChildScale = pb.handRadius * 2f * pScale;
        Vector3 toolOrigin = handR + Quaternion.Euler(gripEuler) * gripPos * handChildScale;
        float toolWorldScale = handChildScale * toolScale;
        float toolLen = pb.toolLength * toolWorldScale;                    // 자루 끝 → 머리 끝 길이
        float toolGirth = toolLen * 0.16f;

        // 활 슬롯: 왼손 위치, 활대는 세로(±Y), 그립이 +Z 로 볼록
        float bowLen = pb.bowSize * 2f * pScale;

        var obj = new StringBuilder();
        obj.AppendLine("# 토이라기 장비 모델링 가이드");
        obj.AppendLine("# 게임에서 보이는 실제 크기. 캐릭터 중심이 원점(0,0,0), +Z 가 캐릭터 정면.");
        obj.AppendLine("# ─ 무기는 '무기슬롯_도구' 안을 채우도록 만들고, 오브젝트 원점을");
        obj.AppendLine("#   손잡이 끝(그립 표시 구)에 두세요. 자루 방향 = +Z.");
        obj.AppendLine("# ─ 활은 '무기슬롯_활' — 활대는 세로, 그립이 +Z 쪽으로 볼록.");
        obj.AppendLine("# ─ 다 만들면 가이드 오브젝트는 전부 지우고 무기만 남겨 glb 로 내보내세요.");
        int vbase = 1;

        // ① 캐릭터 몸통 — 실제 메시 그대로
        var bodyMf = t.GetComponent<MeshFilter>();
        if (bodyMf != null && bodyMf.sharedMesh != null)
            WriteMesh(obj, ref vbase, "가이드_캐릭터몸통", bodyMf.sharedMesh,
                      Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * pScale));

        // ② 양손
        var sphere = PrimMesh(PrimitiveType.Sphere);
        WriteMesh(obj, ref vbase, "가이드_오른손_도구를_든다", sphere,
                  Matrix4x4.TRS(handR, Quaternion.identity, Vector3.one * handDia));
        WriteMesh(obj, ref vbase, "가이드_왼손_활을_든다", sphere,
                  Matrix4x4.TRS(handL, Quaternion.identity, Vector3.one * handDia));

        // ③ 도구 슬롯 — 그립(원점)에서 +Z 로 뻗는다
        var cube = PrimMesh(PrimitiveType.Cube);
        var toolRot = Quaternion.Euler(gripEuler);
        WriteMesh(obj, ref vbase, "무기슬롯_도구_이_안을_채우세요", cube,
                  Matrix4x4.TRS(toolOrigin + toolRot * new Vector3(0f, 0f, toolLen * 0.5f), toolRot,
                                new Vector3(toolGirth, toolGirth, toolLen)));
        // 그립 지점 — 여기가 모델 원점(0,0,0)이어야 한다
        WriteMesh(obj, ref vbase, "기준_그립_여기가_모델원점", sphere,
                  Matrix4x4.TRS(toolOrigin, Quaternion.identity, Vector3.one * toolGirth * 1.5f));
        // 자루 방향 화살표 — 머리(타격부)가 가는 쪽
        WriteMesh(obj, ref vbase, "기준_자루방향_플러스Z_머리쪽", cube,
                  Matrix4x4.TRS(toolOrigin + toolRot * new Vector3(0f, 0f, toolLen * 1.12f), toolRot,
                                new Vector3(toolGirth * 2.2f, toolGirth * 0.5f, toolGirth * 2.2f)));

        // ④ 활 슬롯 — 왼손 기준, 세로로 선 납작한 상자
        WriteMesh(obj, ref vbase, "무기슬롯_활_세로로_서게", cube,
                  Matrix4x4.TRS(handL + new Vector3(0f, 0f, bowLen * 0.09f), Quaternion.identity,
                                new Vector3(bowLen * 0.07f, bowLen, bowLen * 0.36f)));
        WriteMesh(obj, ref vbase, "기준_활_그립_볼록한쪽_플러스Z", sphere,
                  Matrix4x4.TRS(handL + new Vector3(0f, 0f, bowLen * 0.21f), Quaternion.identity,
                                Vector3.one * bowLen * 0.09f));

        // ⑤ 바닥 — 캐릭터가 서는 높이 (크기 감 잡기용)
        float footY = bodyMf != null && bodyMf.sharedMesh != null
                    ? bodyMf.sharedMesh.bounds.min.y * pScale : -pScale;
        WriteMesh(obj, ref vbase, "가이드_바닥", cube,
                  Matrix4x4.TRS(new Vector3(0f, footY - 0.02f, 0f), Quaternion.identity,
                                new Vector3(8f, 0.04f, 8f)));

        System.IO.File.WriteAllText(path, obj.ToString(), new UTF8Encoding(false));
        string info = $"저장 완료: {path}\n  도구 길이 {toolLen:F2}m · 활 길이 {bowLen:F2}m\n" +
                      $"  그립(모델 원점) {toolOrigin.ToString("F2")} — 캐릭터 중심 기준";
        Debug.Log("[장비 가이드] " + info);
        return info;
    }

    static readonly Dictionary<PrimitiveType, Mesh> prims = new Dictionary<PrimitiveType, Mesh>();
    static Mesh PrimMesh(PrimitiveType p)
    {
        if (prims.TryGetValue(p, out var m) && m != null) return m;
        var go = GameObject.CreatePrimitive(p);
        m = go.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(go);
        prims[p] = m;
        return m;
    }

    /// OBJ 한 덩어리 — 정점/법선/면. 정점 번호는 파일 전체 통산이라 offset 을 넘겨받는다.
    static void WriteMesh(StringBuilder sb, ref int vbase, string name, Mesh mesh, Matrix4x4 trs)
    {
        var ci = CultureInfo.InvariantCulture;
        var verts = mesh.vertices;
        var norms = mesh.normals;
        sb.AppendLine($"o {name}");
        foreach (var v in verts)
        {
            var p = trs.MultiplyPoint3x4(v);
            sb.AppendLine($"v {p.x.ToString("F5", ci)} {p.y.ToString("F5", ci)} {p.z.ToString("F5", ci)}");
        }
        bool hasN = norms != null && norms.Length == verts.Length;
        if (hasN)
            foreach (var n in norms)
            {
                var d = trs.MultiplyVector(n).normalized;
                sb.AppendLine($"vn {d.x.ToString("F5", ci)} {d.y.ToString("F5", ci)} {d.z.ToString("F5", ci)}");
            }
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = vbase + tris[i], b = vbase + tris[i + 1], c = vbase + tris[i + 2];
                // OBJ 는 반시계 방향 — 유니티(시계)와 반대라 두 번째·세 번째를 바꾼다
                sb.AppendLine(hasN ? $"f {a}//{a} {c}//{c} {b}//{b}" : $"f {a} {c} {b}");
            }
        }
        vbase += verts.Length;
    }
}
