# 고대 원형 제단 — ★사용자 치수 지시서 그대로 (2026-07-31, 단위 m, 전체 D10)
# 짐작 0: 모든 치수가 지시서 숫자다. 파츠판 + 조립판 동시 추출.
import bpy, bmesh, math

DST_PARTS = r"C:\Users\ysim1\Documents\GitHub\toyrassic\Assets\Resources\Build\부화터_파츠.glb"
DST_ASM   = r"C:\Users\ysim1\Documents\GitHub\toyrassic\Assets\Resources\Build\부화터_조립.glb"

bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name, color, emis=None, strength=0.0, rough=0.85, alpha=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    if alpha < 1.0:
        b.inputs["Alpha"].default_value = alpha
        m.blend_method = 'BLEND'
    if emis is not None:
        b.inputs["Emission Color"].default_value = (*emis, 1)
        b.inputs["Emission Strength"].default_value = strength
    return m

M_STONE = mat("stone", (0.52, 0.50, 0.47))
M_GLOW  = mat("glow", (0.2, 0.9, 0.95), emis=(0.2, 0.9, 0.95), strength=8.0, rough=0.4)
M_GLASS = mat("glass", (0.6, 0.85, 0.95), rough=0.12, alpha=0.3)

def paint(o, m):
    o.data.materials.clear(); o.data.materials.append(m); return o

def annulus(name, r_out, r_in, h, pos, seg=32, m=M_STONE, slope=0.0):
    """각진 링. slope>0 이면 윗면이 바깥으로 내려간다 (기초 링의 완만한 경사)"""
    mesh = bpy.data.meshes.new(name)
    bm = bmesh.new()
    lo_o, lo_i, hi_o, hi_i = [], [], [], []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        c, s = math.cos(a), math.sin(a)
        lo_o.append(bm.verts.new((c * r_out, s * r_out, 0)))
        lo_i.append(bm.verts.new((c * r_in, s * r_in, 0)))
        hi_o.append(bm.verts.new((c * r_out, s * r_out, h - slope)))
        hi_i.append(bm.verts.new((c * r_in, s * r_in, h)))
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((lo_o[i], lo_o[j], hi_o[j], hi_o[i]))
        bm.faces.new((lo_i[j], lo_i[i], hi_i[i], hi_i[j]))
        bm.faces.new((hi_o[i], hi_o[j], hi_i[j], hi_i[i]))
        bm.faces.new((lo_o[j], lo_o[i], lo_i[i], lo_i[j]))
    bm.to_mesh(mesh); bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(o)
    o.location = pos
    return paint(o, m)

def join_as(name, parts):
    for o in bpy.context.view_layer.objects: o.select_set(False)
    for o in parts: o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    j = bpy.context.active_object; j.name = name
    return j

# ── A. 받침대 ──
def 기초링(name, pos):        # 외경10 내경9 H0.2, 바깥으로 완만한 경사
    return annulus(name, 5.0, 4.5, 0.2, pos, slope=0.08)

def 중간받침(name, pos):      # 외경9 내경7.5 H0.5
    return annulus(name, 4.5, 3.75, 0.5, pos)

def 계단받침(name, pos):      # 하단 D7.5 → 상단 D5, 총 H1.5, 3단 (각 0.5)
    made = []
    rs = [3.75, 3.3333, 2.9167]      # 상단이 D5(r2.5)가 되는 균등 3단
    for i, r in enumerate(rs):
        bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=r, depth=0.5,
            location=(pos[0], pos[1], pos[2] + 0.25 + i * 0.5))
        paint(bpy.context.active_object, M_STONE)
        made.append(bpy.context.active_object)
    # 최상단 마감: r2.5 얇은 판 (상단 지름 5 명시)
    bpy.ops.mesh.primitive_cylinder_add(vertices=32, radius=2.5, depth=0.06,
        location=(pos[0], pos[1], pos[2] + 1.5 + 0.03))
    paint(bpy.context.active_object, M_STONE)
    made.append(bpy.context.active_object)
    return join_as(name, made)

# ── B. 코어 ──
def 코어하우징(name, pos):    # 외경2 내경1.5 H0.4 + 바깥 요철 8개
    ring = annulus(name + "_r", 1.0, 0.75, 0.4, pos, seg=24)
    ribs = []
    for i in range(8):
        a = math.radians(i * 45)
        bpy.ops.mesh.primitive_cube_add(
            location=(pos[0] + math.cos(a) * 1.0, pos[1] + math.sin(a) * 1.0, pos[2] + 0.2))
        rb = bpy.context.active_object
        rb.scale = (0.06, 0.10, 0.16)
        rb.rotation_euler = (0, 0, a)
        paint(rb, M_STONE); ribs.append(rb)
    return join_as(name, [ring] + ribs)

def 발광코어(name, pos):      # D1.4 H0.3, 중앙 시안 발광
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.7, depth=0.3,
        location=(pos[0], pos[1], pos[2] + 0.15))
    body = paint(bpy.context.active_object, M_STONE)
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.45, depth=0.06,
        location=(pos[0], pos[1], pos[2] + 0.30))
    glow = paint(bpy.context.active_object, M_GLOW)
    return join_as(name, [body, glow])

def 유리커버(name, pos):      # D1.4 두께0.05
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.7, depth=0.05,
        location=(pos[0], pos[1], pos[2] + 0.025))
    return paint(bpy.context.active_object, M_GLASS)

# ── C. 기둥 구조 ──
def 외곽지지대(name, pos, rz=0.0):   # 1x1x0.8 육면체 + 4면 버트레스
    bpy.ops.mesh.primitive_cube_add(location=(pos[0], pos[1], pos[2] + 0.4))
    core = bpy.context.active_object
    core.scale = (0.5, 0.5, 0.4)
    paint(core, M_STONE)
    parts = [core]
    for i in range(4):
        a = math.radians(i * 90)
        bpy.ops.mesh.primitive_cube_add(
            location=(pos[0] + math.cos(a) * 0.58, pos[1] + math.sin(a) * 0.58, pos[2] + 0.28))
        bu = bpy.context.active_object
        bu.scale = (0.16, 0.30, 0.28)
        bm = bmesh.new(); bm.from_mesh(bu.data)
        for v in bm.verts:
            if v.co.z > 0: v.co.x -= 0.55   # 위가 안쪽으로 기움 = 경사 버트레스
        bm.to_mesh(bu.data); bm.free(); bu.data.update()
        bu.rotation_euler = (0, 0, a)
        paint(bu, M_STONE); parts.append(bu)
    j = join_as(name, parts)
    j.rotation_euler = (0, 0, rz)
    return j

def 기둥(name, pos, rz=0.0):        # 밑0.7² → 위0.4², H3
    bpy.ops.mesh.primitive_cube_add(location=(pos[0], pos[1], pos[2] + 1.5))
    o = bpy.context.active_object; o.name = name
    o.scale = (0.35, 0.35, 1.5)
    bm = bmesh.new(); bm.from_mesh(o.data)
    for v in bm.verts:
        if v.co.z > 0: v.co.x *= 0.5714; v.co.y *= 0.5714   # 0.4/0.7
    bm.to_mesh(o.data); bm.free(); o.data.update()
    o.rotation_euler = (0, 0, rz)
    return paint(o, M_STONE)

def 상인방빔(name, pos, rz=0.0):    # L2.5 x W0.4 x H0.5, 바닥 아치 파임, 끝단 각짐
    bpy.ops.mesh.primitive_cube_add(location=(pos[0], pos[1], pos[2] + 0.25))
    beam = bpy.context.active_object
    beam.scale = (1.25, 0.2, 0.25)
    paint(beam, M_STONE)
    # 아치 컷터 — 빔 길이 방향으로 누운 12각 실린더 (로우폴리 아치 단면)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=1.05, depth=0.6,
        location=(pos[0], pos[1], pos[2] - 0.72), rotation=(math.radians(90), 0, 0))
    cut = bpy.context.active_object
    md = beam.modifiers.new("arch", 'BOOLEAN')
    md.operation = 'DIFFERENCE'; md.object = cut; md.solver = 'EXACT'
    for o in bpy.context.view_layer.objects: o.select_set(False)
    beam.select_set(True)
    bpy.context.view_layer.objects.active = beam
    bpy.ops.object.modifier_apply(modifier="arch")
    bpy.data.objects.remove(cut, do_unlink=True)
    beam.name = name
    beam.rotation_euler = (0, 0, rz)
    return beam

# ── 배치 ──
def build_layout():
    상인방빔("상인방빔", (-8, 6, 0))
    기둥("기둥", (-8, 0, 0))
    외곽지지대("외곽지지대", (-8, -5, 0))
    계단받침("계단받침", (8, 6, 0))
    중간받침("중간받침", (8, -4, 0))
    기초링("기초링", (20, -4, 0))
    유리커버("유리커버", (0, 4, 0))
    발광코어("발광코어", (0, 0, 0))
    코어하우징("코어하우징", (0, -4, 0))

def build_assembled():
    # 지시서 조립 순서 그대로
    기초링("기초링", (0, 0, 0))                    # z 0~0.2
    중간받침("중간받침", (0, 0, 0.2))              # z 0.2~0.7
    계단받침("계단받침", (0, 0, 0.7))              # z 0.7~2.2 (+마감판)
    코어하우징("코어하우징", (0, 0, 2.26))         # 계단 꼭대기 중앙
    발광코어("발광코어", (0, 0, 2.26))
    유리커버("유리커버", (0, 0, 2.56))
    R = 3.2                                       # 지시서: 반지름 3.2m 원형 배열
    # 지지대가 서는 높이 — R3.2 는 계단 2단 위 (1단 z1.2 · 2단 z1.7 상면)
    BASE_Z = 1.2                                  # "계단의 1~2단 사이 부근"
    for i in range(8):
        a = math.radians(22.5 + i * 45)
        x, y = math.cos(a) * R, math.sin(a) * R
        외곽지지대(f"지지대{i}", (x, y, BASE_Z), rz=a)
        기둥(f"기둥{i}", (x, y, BASE_Z + 0.8), rz=a)
    TOP = BASE_Z + 0.8 + 3.0                      # 기둥 꼭대기
    rr = R * math.cos(math.radians(22.5))
    for i in range(8):
        m = math.radians(45 + i * 45)
        상인방빔(f"빔{i}", (math.cos(m) * rr, math.sin(m) * rr, TOP), rz=m + math.radians(90))

def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

build_layout()
bpy.ops.export_scene.gltf(filepath=DST_PARTS, export_format='GLB', export_apply=True)
clear()
build_assembled()
bpy.ops.export_scene.gltf(filepath=DST_ASM, export_format='GLB', export_apply=True)
dg = bpy.context.evaluated_depsgraph_get()
tris = 0
for o in bpy.data.objects:
    if o.type == 'MESH':
        me = o.evaluated_get(dg).to_mesh()
        tris += sum(len(p.vertices) - 2 for p in me.polygons)
print(f"DONE assembled_tris={tris}")
