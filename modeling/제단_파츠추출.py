# 제단 파츠 9종을 개별 GLB 로 추출 — "파츠 다듬으면 교체만" 워크플로용
# altar_spec.py 의 파츠 함수들을 그대로 재사용한다 (정본은 그쪽)
import bpy, os

SPEC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "altar_spec.py")
src = open(SPEC, encoding="utf-8").read()
src = src[:src.index("def build_layout():")]   # 파츠 함수 정의부까지만 실행
exec(compile(src, SPEC, "exec"))

PARTS_DIR = os.path.join(r"C:\Users\ysim1\Documents\GitHub\toyrassic",
                         "Assets", "Resources", "Build", "제단파츠")
os.makedirs(PARTS_DIR, exist_ok=True)

def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

PARTS = [
    ("기초링", lambda: 기초링("기초링", (0, 0, 0))),
    ("중간받침", lambda: 중간받침("중간받침", (0, 0, 0))),
    ("계단받침", lambda: 계단받침("계단받침", (0, 0, 0))),
    ("코어하우징", lambda: 코어하우징("코어하우징", (0, 0, 0))),
    ("발광코어", lambda: 발광코어("발광코어", (0, 0, 0))),
    ("유리커버", lambda: 유리커버("유리커버", (0, 0, 0))),
    ("외곽지지대", lambda: 외곽지지대("외곽지지대", (0, 0, 0))),
    ("기둥", lambda: 기둥("기둥", (0, 0, 0))),
    ("상인방빔", lambda: 상인방빔("상인방빔", (0, 0, 0))),
]
for name, fn in PARTS:
    clear()
    fn()
    bpy.ops.export_scene.gltf(filepath=os.path.join(PARTS_DIR, name + ".glb"),
                              export_format='GLB', export_apply=True)
    print(f"PART {name} OK")
print("DONE parts")
