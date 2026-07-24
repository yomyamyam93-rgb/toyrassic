using UnityEditor;
using UnityEngine;

/// GrassManager 의 커스텀 인스펙터 — 폴드아웃 섹션 + 슬라이더 + 위험 존.
/// '적용' = 설정대로 지형 디테일맵을 다시 굽는다. 색은 만지는 즉시 재질에 반영.
[CustomEditor(typeof(GrassManager))]
[InitializeOnLoad]
public class GrassManagerEditor : Editor
{
    static bool fTypes = true, fLayers = true, fDensity, fEdge, fRemove, fColor;

    // ── 자동 적용: 슬라이더에서 손을 뗀 뒤 0.5초 지나면 한 번만 다시 심는다 ──
    static GrassManager pending;
    static double lastChange;
    static GrassManagerEditor()
    {
        EditorApplication.update += () =>
        {
            if (pending == null) return;
            if (GUIUtility.hotControl != 0) { lastChange = EditorApplication.timeSinceStartup; return; }  // 아직 드래그 중
            if (EditorApplication.timeSinceStartup - lastChange < 0.5) return;
            var gm = pending; pending = null;
            if (gm != null && gm.terrain != null) Rebuild(gm);
        };
        // Ctrl+Z/Y 로 설정이 되돌아가면 그 값대로 잔디도 다시 심는다
        Undo.undoRedoPerformed += () =>
        {
            var sel = Selection.activeGameObject;
            if (sel == null) return;
            var gm = sel.GetComponent<GrassManager>();
            if (gm != null && gm.autoApply && gm.terrain != null)
            { pending = gm; lastChange = EditorApplication.timeSinceStartup; }
        };
    }
    static readonly string[] GrassMats = {
        "Assets/Models/GrassCross_a.mat", "Assets/Models/GrassCross_b.mat", "Assets/Models/GrassCross_c.mat" };

    public override void OnInspectorGUI()
    {
        var gm = (GrassManager)target;
        if (gm.terrain == null)
        {
            var go = GameObject.Find("Island");
            if (go != null) gm.terrain = go.GetComponent<Terrain>();
        }
        gm.terrain = (Terrain)EditorGUILayout.ObjectField("지형", gm.terrain, typeof(Terrain), true);
        if (gm.terrain == null) { EditorGUILayout.HelpBox("지형(Island)을 연결할 것.", MessageType.Warning); return; }
        var td = gm.terrain.terrainData;

        Undo.RecordObject(gm, "GrassManager");
        gm.autoApply = EditorGUILayout.ToggleLeft("자동 적용 (슬라이더 놓으면 다시 심기)", gm.autoApply);
        EditorGUILayout.Space(2);
        EditorGUI.BeginChangeCheck();

        // ── 풀 종류 ─────────────────────────────────────
        fTypes = EditorGUILayout.BeginFoldoutHeaderGroup(fTypes, "풀 종류 / Grass Types");
        if (fTypes)
        {
            EditorGUILayout.HelpBox("Active OFF = 적용 시 그 종류는 안 심음. Weight = 출현량, Size = 크기.", MessageType.None);
            if (gm.types.Length != td.detailPrototypes.Length - Mathf.Max(0, gm.edgeProtoCount))
                EditorGUILayout.HelpBox("지형 종류 수와 안 맞음 — 아래 '종류 불러오기'를 누를 것.", MessageType.Warning);
            int removeIdx = -1;
            for (int i = 0; i < gm.types.Length; i++)
            {
                var t = gm.types[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i}: {t.name}", EditorStyles.boldLabel);
                t.active = EditorGUILayout.ToggleLeft("Active", t.active, GUILayout.Width(70));
                GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
                if (GUILayout.Button("삭제", GUILayout.Width(44)) &&
                    EditorUtility.DisplayDialog("종류 삭제", $"'{t.name}' 종류와 심어진 것 전부를 지형에서 지운다.", "삭제", "취소"))
                    removeIdx = i;
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                t.weight = EditorGUILayout.Slider("  Weight", t.weight, 0f, 2f);
                t.size = EditorGUILayout.Slider("  Size", t.size, 0.5f, 2f);
            }
            if (removeIdx >= 0) RemoveType(gm, td, removeIdx);

            EditorGUILayout.BeginHorizontal();
            newProto = EditorGUILayout.ObjectField("추가 (프리팹/텍스처)", newProto, typeof(Object), false);
            GUI.enabled = newProto is GameObject || newProto is Texture2D;
            if (GUILayout.Button("추가", GUILayout.Width(50))) { AddType(gm, td, newProto); newProto = null; }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("종류 불러오기 (지형과 동기화)")) SyncTypes(gm, td);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 배치 레이어 ─────────────────────────────────
        fLayers = EditorGUILayout.BeginFoldoutHeaderGroup(fLayers, "배치 레이어 / 어떤 재질 위에 심을지");
        if (fLayers)
        {
            EditorGUILayout.HelpBox("재질(TerrainLayer)을 아래 칸에 끌어다 놓으면 리스트에 올라간다.\n체크 ON = 그 재질 위에 심음.", MessageType.None);
            if (gm.placeLayers.Count == 0) SyncLayers(gm, td);
            int rm = -1;
            for (int i = 0; i < gm.placeLayers.Count; i++)
            {
                var pl = gm.placeLayers[i];
                EditorGUILayout.BeginHorizontal();
                pl.on = EditorGUILayout.Toggle(pl.on, GUILayout.Width(18));
                pl.layer = (TerrainLayer)EditorGUILayout.ObjectField(pl.layer, typeof(TerrainLayer), false);
                if (GUILayout.Button("X", GUILayout.Width(22))) rm = i;
                EditorGUILayout.EndHorizontal();
            }
            if (rm >= 0) gm.placeLayers.RemoveAt(rm);

            // 드래그해서 추가
            var add = (TerrainLayer)EditorGUILayout.ObjectField("재질 끌어다 추가", null, typeof(TerrainLayer), false);
            if (add != null && !gm.placeLayers.Exists(x => x.layer == add))
                gm.placeLayers.Add(new GrassManager.PlaceLayer { layer = add, on = true });

            if (GUILayout.Button("지형 레이어 전부 불러오기")) SyncLayers(gm, td);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 밀도 ────────────────────────────────────────
        fDensity = EditorGUILayout.BeginFoldoutHeaderGroup(fDensity, "밀도 / Density");
        if (fDensity)
        {
            gm.density = EditorGUILayout.Slider("전체 밀도", gm.density, 0f, 1.5f);
            gm.drawDistance = EditorGUILayout.Slider("그리기 거리(m)", gm.drawDistance, 50f, 400f);
            gm.cellSize = EditorGUILayout.Slider("격자 크기(m/칸) — 작을수록 정밀", gm.cellSize, 1f, 4f);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 경계 다듬기 ─────────────────────────────────
        fEdge = EditorGUILayout.BeginFoldoutHeaderGroup(fEdge, "경계 다듬기 / 길·모래와 만나는 가장자리");
        if (fEdge)
        {
            gm.layerThreshold = EditorGUILayout.Slider("경계 문턱 (어디부터 잔디인가)", gm.layerThreshold, 0.05f, 0.9f);
            gm.blockStrength = EditorGUILayout.Slider("밀어내기 강도 (체크 안 한 재질)", gm.blockStrength, 0f, 1f);
            gm.edgeBand = EditorGUILayout.Slider("경계 폭 (m) — 가장자리 취급 거리", gm.edgeBand, 0.5f, 8f);
            gm.edgeDensity = EditorGUILayout.Slider("경계 개체수 배율", gm.edgeDensity, 0f, 1f);
            gm.edgeSize = EditorGUILayout.Slider("경계 크기 배율 (작은 잔디)", gm.edgeSize, 0.5f, 1f);
            gm.edgeJitter = EditorGUILayout.Slider("들쭉날쭉 (경계선 흔들기)", gm.edgeJitter, 0f, 0.3f);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();

        // ── 경사·높이 제거 ──────────────────────────────
        fRemove = EditorGUILayout.BeginFoldoutHeaderGroup(fRemove, "경사·높이로 지우기");
        if (fRemove)
        {
            gm.maxSlope = EditorGUILayout.Slider("최대 경사(°)", gm.maxSlope, 0f, 60f);
            gm.minHeight = EditorGUILayout.FloatField("최소 높이(m)", gm.minHeight);
            gm.maxHeight = EditorGUILayout.FloatField("최대 높이(m)", gm.maxHeight);
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        bool placementChanged = EditorGUI.EndChangeCheck();

        // ── 색 (즉시 반영) ──────────────────────────────
        fColor = EditorGUILayout.BeginFoldoutHeaderGroup(fColor, "색 조정 (즉시 반영)");
        bool colorChanged = false;
        if (fColor)
        {
            EditorGUI.BeginChangeCheck();
            gm.tint = EditorGUILayout.ColorField("전체 색조", gm.tint);
            gm.brightness = EditorGUILayout.Slider("밝기 보정 (바닥 대비)", gm.brightness, 0.7f, 1.3f);
            gm.rootDark = EditorGUILayout.Slider("밑동 어둠", gm.rootDark, 0.4f, 1f);
            gm.tipBoost = EditorGUILayout.Slider("잎끝 밝기", gm.tipBoost, 1f, 1.6f);
            colorChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("잔디색은 지형 스플랫×타일 텍스처를 직접 섞는다(지형과 같은 계산 = 항상 일치).\n" +
                "지형 레이어 텍스처를 교체했을 때만 아래 버튼으로 다시 연결.", MessageType.Info);
            if (GUILayout.Button("지형 재질 다시 연결")) { WireTerrainSplat(gm); ApplyColors(gm); }
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        if (colorChanged) ApplyColors(gm);

        if (placementChanged || colorChanged) EditorUtility.SetDirty(gm);
        if (placementChanged)
        {
            gm.terrain.detailObjectDistance = gm.drawDistance;   // 그리기 거리는 값싸서 즉시
            if (gm.autoApply) { pending = gm; lastChange = EditorApplication.timeSinceStartup; }
        }

        // ── 적용 ────────────────────────────────────────
        EditorGUILayout.Space(6);
        GUI.backgroundColor = new Color(0.65f, 0.9f, 0.65f);
        if (GUILayout.Button("적용 — 설정대로 잔디 다시 심기", GUILayout.Height(30)))
        { Rebuild(gm); ApplyColors(gm); }
        GUI.backgroundColor = Color.white;

        // ── 위험 존 ─────────────────────────────────────
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("― Danger Zone / 위험 구역 ―", EditorStyles.centeredGreyMiniLabel);
        GUI.backgroundColor = new Color(0.9f, 0.35f, 0.35f);
        if (GUILayout.Button("잔디 전체 삭제 / Clear All Grass") &&
            EditorUtility.DisplayDialog("잔디 전체 삭제", "지형의 잔디를 전부 지운다. '적용'으로 되살릴 수 있다.", "삭제", "취소"))
            ClearAll(td);
        GUI.backgroundColor = Color.white;
    }

    static Object newProto;   // '종류 추가' 후보

    // ── 지형 스플랫×타일 텍스처를 잔디 재질에 연결 (지형과 같은 계산 = 항상 일치) ──
    static void WireTerrainSplat(GrassManager gm)
    {
        var td = gm.terrain.terrainData;
        var ctrl = td.alphamapTextures;
        var layers = td.terrainLayers;
        var o = gm.terrain.transform.position;
        Vector4 ta = Vector4.one * 30f, tb = Vector4.one * 30f;
        for (int i = 0; i < 8 && i < layers.Length; i++)
        {
            float t = layers[i] != null ? Mathf.Max(0.01f, layers[i].tileSize.x) : 30f;
            if (i < 4) ta[i] = t; else tb[i - 4] = t;
        }
        foreach (var p in GrassMats)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            m.SetTexture("_Control0", ctrl.Length > 0 ? ctrl[0] : null);
            m.SetTexture("_Control1", ctrl.Length > 1 ? ctrl[1] : null);
            for (int i = 0; i < 8; i++)
                m.SetTexture("_L" + i, i < layers.Length && layers[i] != null ? layers[i].diffuseTexture : null);
            m.SetVector("_TileA", ta); m.SetVector("_TileB", tb);
            m.SetFloat("_WorldMin", o.x); m.SetFloat("_WorldSize", td.size.x);
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[잔디] 지형 재질 연결 완료 — 잔디색이 지형과 같은 계산으로 나온다.");
    }

    // ── 종류 추가/삭제 ──────────────────────────────────
    static void AddType(GrassManager gm, TerrainData td, Object obj)
    {
        Undo.RegisterCompleteObjectUndo(td, "잔디 종류 추가");
        StripEdgeProtos(gm, td);   // 경계용 프로토는 걷어내고 작업 (다음 적용 때 재생성)
        var protos = new System.Collections.Generic.List<DetailPrototype>(td.detailPrototypes);
        var p = new DetailPrototype
        {
            minWidth = 0.85f, maxWidth = 1.15f, minHeight = 0.85f, maxHeight = 1.2f,
            noiseSpread = 0.3f, healthyColor = Color.white, dryColor = Color.white
        };
        if (obj is GameObject go)
        {
            var src = protos.Find(x => x.usePrototypeMesh);   // 기존 메시 종류 설정을 이어받음
            if (src != null) { p.renderMode = src.renderMode; p.useInstancing = src.useInstancing; }
            else { p.renderMode = DetailRenderMode.VertexLit; p.useInstancing = true; }
            p.usePrototypeMesh = true; p.prototype = go;
        }
        else if (obj is Texture2D tex)
        {
            p.renderMode = DetailRenderMode.Grass; p.prototypeTexture = tex;
        }
        else return;
        protos.Add(p);
        td.detailPrototypes = protos.ToArray();
        SyncTypes(gm, td);
        Debug.Log($"[잔디] 종류 추가: {obj.name} — Weight·Size 잡고 '적용'하면 심어진다.");
    }

    static void RemoveType(GrassManager gm, TerrainData td, int idx)
    {
        Undo.RegisterCompleteObjectUndo(td, "잔디 종류 삭제");
        StripEdgeProtos(gm, td);   // 경계용 프로토는 걷어내고 작업
        int res = td.detailResolution;
        var protos = new System.Collections.Generic.List<DetailPrototype>(td.detailPrototypes);
        var maps = new System.Collections.Generic.List<int[,]>();
        for (int l = 0; l < protos.Count; l++) maps.Add(td.GetDetailLayer(0, 0, res, res, l));
        string name = idx < gm.types.Length ? gm.types[idx].name : "?";
        protos.RemoveAt(idx); maps.RemoveAt(idx);
        td.detailPrototypes = protos.ToArray();
        for (int l = 0; l < protos.Count; l++) td.SetDetailLayer(0, 0, l, maps[l]);   // 인덱스 밀림 보정
        SyncTypes(gm, td);
        Debug.Log($"[잔디] 종류 삭제: {name}");
    }

    // ── 동기화 ──────────────────────────────────────────
    static void SyncTypes(GrassManager gm, TerrainData td)
    {
        var protos = td.detailPrototypes;
        int userCount = Mathf.Max(0, protos.Length - Mathf.Max(0, gm.edgeProtoCount));   // 끝의 경계용 프로토는 목록에서 제외
        var list = new System.Collections.Generic.List<GrassManager.GrassType>();
        for (int i = 0; i < userCount; i++)
        {
            var old = i < gm.types.Length ? gm.types[i] : null;
            string n = protos[i].prototype != null ? protos[i].prototype.name
                     : protos[i].prototypeTexture != null ? protos[i].prototypeTexture.name : $"type{i}";
            var t = old ?? new GrassManager.GrassType();
            t.name = n;
            if (old == null && n.ToLower().Contains("flower")) t.weight = 0.1f;  // 꽃은 뜨문뜨문이 기본
            list.Add(t);
        }
        gm.types = list.ToArray();
        EditorUtility.SetDirty(gm);
    }

    static void SyncLayers(GrassManager gm, TerrainData td)
    {
        // 지형의 레이어를 전부 리스트에 올린다 (이미 있는 항목의 체크 상태는 유지)
        foreach (var l in td.terrainLayers)
        {
            if (l == null || gm.placeLayers.Exists(x => x.layer == l)) continue;
            string n = l.name.ToLower();   // 기본: 잔디 계열 + 마른흙만 ON
            gm.placeLayers.Add(new GrassManager.PlaceLayer { layer = l, on = n.Contains("grass") || n.Contains("drysoil") });
        }
        EditorUtility.SetDirty(gm);
    }

    // ── 적용: 디테일맵 다시 굽기 ─────────────────────────
    /// 이전 적용 때 자동 생성한 '경계용 작은 프로토'를 걷어낸다 (항상 목록 끝에 붙어 있음)
    static void StripEdgeProtos(GrassManager gm, TerrainData td)
    {
        if (gm.edgeProtoCount <= 0) return;
        var list = new System.Collections.Generic.List<DetailPrototype>(td.detailPrototypes);
        int n = Mathf.Min(gm.edgeProtoCount, list.Count);
        list.RemoveRange(list.Count - n, n);
        td.detailPrototypes = list.ToArray();
        gm.edgeProtoCount = 0;
    }

    static void Rebuild(GrassManager gm)
    {
        var td = gm.terrain.terrainData;
        Undo.RegisterCompleteObjectUndo(td, "잔디 적용");   // Ctrl+Z 가능하게
        StripEdgeProtos(gm, td);
        if (gm.types.Length != td.detailPrototypes.Length) SyncTypes(gm, td);
        if (gm.placeLayers.Count == 0) SyncLayers(gm, td);

        // 리스트(재질+체크) → 지형 레이어 인덱스별 허용 여부
        var tls = td.terrainLayers;
        var allowIdx = new bool[tls.Length];
        for (int i = 0; i < tls.Length; i++)
        {
            var found = gm.placeLayers.Find(x => x.layer == tls[i]);
            allowIdx[i] = found != null && found.on;
        }

        // 크기 배율을 프로토타입에 반영 (기준 0.85~1.2 에 size 곱)
        var protoList = new System.Collections.Generic.List<DetailPrototype>(td.detailPrototypes);
        int typeCount = protoList.Count;
        for (int i = 0; i < typeCount; i++)
        {
            float s = gm.types[i].size;
            protoList[i].minWidth = 0.85f * s; protoList[i].maxWidth = 1.15f * s;
            protoList[i].minHeight = 0.85f * s; protoList[i].maxHeight = 1.2f * s;
        }
        // 경계 크기<1 이면 종류마다 '작은 경계용 프로토'를 하나씩 뒤에 추가
        bool useEdge = gm.edgeSize < 0.995f;
        var edgeIdx = new int[typeCount];
        if (useEdge)
        {
            for (int i = 0; i < typeCount; i++)
            {
                var s = protoList[i];
                edgeIdx[i] = protoList.Count;
                protoList.Add(new DetailPrototype
                {
                    renderMode = s.renderMode, usePrototypeMesh = s.usePrototypeMesh,
                    useInstancing = s.useInstancing, prototype = s.prototype,
                    prototypeTexture = s.prototypeTexture, noiseSpread = s.noiseSpread,
                    healthyColor = s.healthyColor, dryColor = s.dryColor,
                    minWidth = s.minWidth * gm.edgeSize, maxWidth = s.maxWidth * gm.edgeSize,
                    minHeight = s.minHeight * gm.edgeSize, maxHeight = s.maxHeight * gm.edgeSize
                });
            }
            gm.edgeProtoCount = typeCount;
        }
        td.detailPrototypes = protoList.ToArray();

        // 격자 해상도 — 칸이 작을수록 경계가 정밀하고 덜 각진다
        int wantRes = Mathf.Clamp(Mathf.CeilToInt(td.size.x / Mathf.Max(1f, gm.cellSize)), 512, 4096);
        if (td.detailResolution != wantRes)
            td.SetDetailResolution(wantRes, 32);   // 맵은 아래서 전부 다시 쓴다
        int res = td.detailResolution;

        int aw = td.alphamapWidth, ah = td.alphamapHeight, al = td.alphamapLayers;
        var splat = td.GetAlphamaps(0, 0, aw, ah);

        // ① 알파맵 텍셀별 마스크(허용 − 금지×강도) 그리드
        var maskGrid = new float[aw * ah];
        for (int y = 0; y < ah; y++)
        for (int x = 0; x < aw; x++)
        {
            float allow = 0f, blocked = 0f;
            for (int l = 0; l < al; l++)
            {
                float w = splat[y, x, l];
                if (l < allowIdx.Length && allowIdx[l]) allow += w; else blocked += w;
            }
            maskGrid[y * aw + x] = allow - blocked * gm.blockStrength;
        }
        float MaskAt(float u, float v)   // 부드러운(bilinear) 샘플
        {
            float gx = Mathf.Clamp01(u) * (aw - 1), gy = Mathf.Clamp01(v) * (ah - 1);
            int x0 = (int)gx, y0 = (int)gy;
            int x1 = Mathf.Min(x0 + 1, aw - 1), y1 = Mathf.Min(y0 + 1, ah - 1);
            float tx = gx - x0, ty = gy - y0;
            float a = Mathf.Lerp(maskGrid[y0 * aw + x0], maskGrid[y0 * aw + x1], tx);
            float b = Mathf.Lerp(maskGrid[y1 * aw + x0], maskGrid[y1 * aw + x1], tx);
            return Mathf.Lerp(a, b, ty);
        }

        // ② 셀 공통 판정 한 번만: 높이·경사 + 셀 '네 모서리' 마스크
        //    최솟값 기준 → 칸 일부라도 길·soil 이면 안 심는다 = 길 위로 안 삐져나감
        var cellMin = new float[res * res];
        var cellAvg = new float[res * res];
        var cellBand = new float[res * res];       // 0 = 경계 바로 옆 ~ 1 = 안쪽
        float inv = 1f / (res - 1);
        float stepM = Mathf.Max(td.size.x / res, 1f);   // 한 칸 크기(m)
        float invSizeM = 1f / td.size.x;
        float thB = gm.layerThreshold;
        for (int y = 0; y < res; y++)
        for (int x = 0; x < res; x++)
        {
            int ci = y * res + x;
            cellMin[ci] = -9f;                     // 기본 = 심기 불가
            float fx = x * inv, fz = y * inv;
            float h = td.GetInterpolatedHeight(fx, fz);
            if (h < gm.minHeight || h > gm.maxHeight) continue;
            if (Vector3.Angle(td.GetInterpolatedNormal(fx, fz), Vector3.up) > gm.maxSlope) continue;
            float m00 = MaskAt(fx, fz),       m10 = MaskAt(fx + inv, fz);
            float m01 = MaskAt(fx, fz + inv), m11 = MaskAt(fx + inv, fz + inv);
            cellMin[ci] = Mathf.Min(Mathf.Min(m00, m10), Mathf.Min(m01, m11));
            cellAvg[ci] = (m00 + m10 + m01 + m11) * 0.25f;

            // 경계까지 실제 거리(m)를 재서 밴드로 — 사방으로 edgeBand(m) 안에
            // 잔디 아닌 땅이 있으면 그 거리 비율만큼 가장자리 취급
            float dist = gm.edgeBand;
            for (float r = stepM; r <= gm.edgeBand; r += stepM)
            {
                float rn = r * invSizeM;
                if (MaskAt(fx + rn, fz) < thB || MaskAt(fx - rn, fz) < thB ||
                    MaskAt(fx, fz + rn) < thB || MaskAt(fx, fz - rn) < thB)
                { dist = r - stepM; break; }
            }
            cellBand[ci] = Mathf.Clamp01(dist / gm.edgeBand);
        }

        const int AMT_MAX = 16;
        long total = 0;
        for (int layer = 0; layer < typeCount; layer++)
        {
            var t = gm.types[layer];
            var map = new int[res, res];
            var emap = useEdge ? new int[res, res] : null;
            if (t.active && t.weight > 0.001f && gm.density > 0.001f)
            {
                for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float mn = cellMin[y * res + x];
                    if (mn < -2f) continue;
                    float fx = x * inv, fz = y * inv;

                    // 들쭉날쭉: 문턱을 노이즈로 흔들어 경계선이 직선으로 안 보이게
                    float th = gm.layerThreshold
                             + (Mathf.PerlinNoise(fx * 220f, fz * 220f) - 0.5f) * 2f * gm.edgeJitter;
                    if (mn < th) continue;
                    float band = cellBand[y * res + x];   // 경계까지 실거리 기반 (0=가장자리, 1=안쪽)

                    float d = Mathf.PerlinNoise(fx * 90f + layer * 37.7f, fz * 90f + layer * 37.7f);
                    if (d < 0.05f) continue;
                    float dens = Mathf.Lerp(8f, AMT_MAX, (d - 0.05f) / 0.95f)
                               * gm.density * t.weight * Mathf.Clamp01(cellAvg[y * res + x]);

                    if (band >= 1f)
                    {   // 안쪽: 정상 크기·정상 밀도
                        int amt = Mathf.RoundToInt(dens);
                        if (amt > 0) { map[y, x] = Mathf.Min(AMT_MAX, amt); total++; }
                    }
                    else
                    {   // 경계: 개체수 배율 + 바깥으로 갈수록 성기게, 작은 프로토로 심음
                        int amt = Mathf.RoundToInt(dens * gm.edgeDensity * Mathf.Lerp(0.25f, 1f, band));
                        if (amt > 0)
                        {
                            if (useEdge) emap[y, x] = Mathf.Min(AMT_MAX, amt);
                            else map[y, x] = Mathf.Min(AMT_MAX, amt);
                            total++;
                        }
                    }
                }
            }
            td.SetDetailLayer(0, 0, layer, map);   // OFF 종류는 빈 맵 = 지움
            if (useEdge) td.SetDetailLayer(0, 0, edgeIdx[layer], emap);
        }
        gm.terrain.detailObjectDistance = gm.drawDistance;
        gm.terrain.detailObjectDensity = 1f;
        AssetDatabase.SaveAssets();
        Debug.Log($"[잔디] 적용 완료 — {total:N0}칸 (밀도 {gm.density:F2}, 경사≤{gm.maxSlope:F0}°)");
    }

    static void ClearAll(TerrainData td)
    {
        Undo.RegisterCompleteObjectUndo(td, "잔디 전체 삭제");
        int res = td.detailResolution;
        var empty = new int[res, res];
        for (int l = 0; l < td.detailPrototypes.Length; l++) td.SetDetailLayer(0, 0, l, empty);
        Debug.Log("[잔디] 전체 삭제 완료 — '적용'으로 되살릴 수 있다.");
    }

    static void ApplyColors(GrassManager gm)
    {
        foreach (var p in GrassMats)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) continue;
            if (m.HasProperty("_Tint")) { var c = gm.tint * gm.brightness; c.a = 1f; m.SetColor("_Tint", c); }
            if (m.HasProperty("_BaseDark")) m.SetFloat("_BaseDark", gm.rootDark);
            if (m.HasProperty("_TipBoost")) m.SetFloat("_TipBoost", gm.tipBoost);
            EditorUtility.SetDirty(m);
        }
    }
}
