"""Verify the exact runtime mesh resource and deterministic conversion from the source."""
import math, pathlib, struct, subprocess, sys
root = pathlib.Path(__file__).resolve().parent.parent
resource = root / 'Resources' / 'RF-Plate.mesh'
before = resource.read_bytes()
subprocess.run([sys.executable,str(root/'tools'/'Extract-PlateMesh.py')],check=True)
assert before == resource.read_bytes(), 'Runtime mesh differs from authored FBX; regenerate and review it'
count, indices = struct.unpack_from('<ii',before)
assert count == 120 and indices == 228
vertices = [struct.unpack_from('<5f',before,8+i*20) for i in range(count)]
assert all(math.isfinite(value) for vertex in vertices for value in vertex)
for axis, expected in enumerate([.8,.25,.02]):
    extent = max(v[axis] for v in vertices)-min(v[axis] for v in vertices)
    assert abs(extent-expected) < 1e-6, (axis,extent)
triangles = struct.unpack_from('<'+'i'*indices,before,8+count*20)
assert all(0 <= index < count for index in triangles)
assert all(-.001 <= v[3] <= 1.001 and -.001 <= v[4] <= 1.001 for v in vertices)
assert len(before) == 8+count*20+indices*4
import re
source=(root/'Systems'/'RuntimeSignResources.cs').read_text(encoding='utf-8')
quad=source[source.index('m_TextQuad = new Mesh'):source.index('m_TextQuad.RecalculateNormals')]
points=[tuple(float(value.rstrip('f')) for value in match) for match in re.findall(r'new Vector3\(([-.\d]+f),([-.\d]+f),([-.\d]+f)\)',quad)]
assert len(points)==4
# Local +Z faces approaching traffic. Both sides share this positive-scale frame.
a,b,c=points[0],points[2],points[1]
normal_z=(b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0])
assert normal_z>0 and all(p[2]>.01 for p in points)
assert points[0][0]>points[1][0], 'Front-view UV horizontal axis would mirror text'
assert abs((max(p[0] for p in points)-min(p[0] for p in points))-.75)<1e-6
assert abs((max(p[1] for p in points)-min(p[1] for p in points))-.204)<1e-6
print('PASS: RF-Plate authored mesh/UVs, dimensions, finite geometry, indices, deterministic conversion.')
