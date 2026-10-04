import zipfile, struct, json, sys
from compression import zstd
from collections import defaultdict

def inspect(path):
    z = zipfile.ZipFile(path)
    f = z.open(next(n for n in z.namelist() if n.endswith('SaveGameData')))
    def integer(data, p=0): return struct.unpack_from('<i', data, p)[0]
    f.read(integer(f.read(4)))
    def block(decode=True):
        raw, packed = struct.unpack('<ii', f.read(8))
        data = f.read(packed)
        return (zstd.decompress(data) if decode and raw else b''), raw, packed
    def types(component):
        data, _, _ = block(); count=integer(data); p=4; result=[]
        for _ in range(count):
            size=integer(data,p); p+=4; start=p
            q=p+int(component); length=integer(data,q); q+=4
            result.append(data[q:q+length].decode('utf-8'))
            p=start+size
        assert p==len(data)
        return result
    components=types(True); systems=types(False)
    data,_,_=block(); count=integer(data); p=4; archetypes=[]
    for i in range(count):
        size=integer(data,p); p+=4; end=p+size
        entities,n=struct.unpack_from('<ii',data,p); p+=8
        indices=struct.unpack_from('<'+'i'*n,data,p); p=end
        archetypes.append((entities,[components[j] for j in indices]))
    assert p==len(data)
    rows=[]; total=0
    for entities,names in archetypes:
        _,raw,packed=block(False); total+=raw
        rows.append(dict(entities=entities,raw=raw,packed=packed,types=[n.split(',')[0] for n in names]))
    systemrows=[]
    for name in systems:
        _,raw,packed=block(False); total+=raw
        systemrows.append(dict(name=name.split(',')[0],raw=raw,packed=packed))
    assert not f.read(1), 'trailing data'
    return dict(path=path,uncompressed=total,entities=sum(r['entities'] for r in rows),archetypes=count,
                largest=sorted(rows,key=lambda r:r['raw'],reverse=True)[:20],
                systems=sorted(systemrows,key=lambda r:r['raw'],reverse=True),
                mod_components=[n for n in components if ', Game,' not in n and ', Unity.' not in n])

results=[inspect(p) for p in sys.argv[1:]]
print(json.dumps(results,ensure_ascii=False,indent=2))
