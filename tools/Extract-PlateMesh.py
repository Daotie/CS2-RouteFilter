"""Extract the authored binary FBX geometry and UVs into a small runtime resource.
No Blender/FBX importer dependency is needed on the player's computer.
"""
import pathlib, struct, zlib

root = pathlib.Path(__file__).resolve().parent.parent
data = (root / 'RF-Plate.fbx').read_bytes()
assert data[:23] == b'Kaydara FBX Binary  \x00\x1a\x00'
version = struct.unpack_from('<I', data, 23)[0]
wide = version >= 7500
header = '<QQQB' if wide else '<IIIB'
header_size = struct.calcsize(header)

def prop(pos):
    kind = chr(data[pos]); pos += 1
    scalar = {'Y':'h', 'C':'?', 'I':'i', 'F':'f', 'D':'d', 'L':'q'}
    if kind in scalar:
        fmt = '<' + scalar[kind]
        return struct.unpack_from(fmt, data, pos)[0], pos + struct.calcsize(fmt)
    if kind in 'SR':
        size = struct.unpack_from('<I', data, pos)[0]; pos += 4
        value = data[pos:pos+size]
        return (value.decode('utf8', errors='replace') if kind == 'S' else value), pos+size
    count, encoding, size = struct.unpack_from('<III', data, pos); pos += 12
    raw = data[pos:pos+size]
    if encoding: raw = zlib.decompress(raw)
    fmt = {'f':'f','d':'d','l':'q','i':'i','b':'?','c':'b'}[kind]
    return list(struct.unpack('<' + fmt * count, raw)), pos+size

def node(pos):
    end, count, length, namesize = struct.unpack_from(header, data, pos)
    if not end: return None, pos+header_size
    pos += header_size
    name = data[pos:pos+namesize].decode(); pos += namesize
    values = []
    for _ in range(count):
        value, pos = prop(pos); values.append(value)
    children = []
    while pos < end - header_size:
        child, pos = node(pos)
        if child: children.append(child)
    return (name, values, children), end

nodes = []; pos = 27
while pos < len(data)-header_size:
    item, pos = node(pos)
    if not item: break
    nodes.append(item)

def find(items, name):
    for item in items:
        if item[0] == name: yield item
        yield from find(item[2], name)

geometry = next(find(nodes, 'Geometry'))
vertices = next(find(geometry[2], 'Vertices'))[1][0]
indices = next(find(geometry[2], 'PolygonVertexIndex'))[1][0]
uv = next(find(geometry[2], 'UV'))[1][0]
uv_indices = next(find(geometry[2], 'UVIndex'))[1][0]
points = [vertices[i:i+3] for i in range(0,len(vertices),3)]
mins = [min(p[j] for p in points) for j in range(3)]
maxs = [max(p[j] for p in points) for j in range(3)]
# Authored axes are inspected by dimensions; map width/height/depth to Unity x/y/z.
axes = sorted(range(3), key=lambda j: maxs[j]-mins[j], reverse=True)
dimensions = [.8, .25, .02]
out = []; triangles = []; polygon = []
for i, raw in enumerate(indices):
    point = points[-raw-1 if raw < 0 else raw]
    position = [(point[a]-(mins[a]+maxs[a])/2) / (maxs[a]-mins[a]) * dimensions[j] for j,a in enumerate(axes)]
    out.append(position + uv[uv_indices[i]*2:uv_indices[i]*2+2]); polygon.append(i)
    if raw < 0:
        for k in range(1,len(polygon)-1): triangles.extend([polygon[0],polygon[k+1],polygon[k]])
        polygon=[]
dest = root / 'Resources' / 'RF-Plate.mesh'; dest.parent.mkdir(exist_ok=True)
with dest.open('wb') as f:
    f.write(struct.pack('<ii', len(out),len(triangles)))
    for vertex in out: f.write(struct.pack('<5f',*vertex))
    f.write(struct.pack('<'+'i'*len(triangles),*triangles))
print(f'RF-Plate: {len(out)} vertices, {len(triangles)//3} triangles; source bounds {mins}/{maxs}; axes {axes}; 0.800 x 0.250 x 0.020 m')
