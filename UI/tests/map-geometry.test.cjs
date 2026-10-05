const assert = require('node:assert/strict');
const fs = require('node:fs');
const ts = require('typescript');
const source = fs.readFileSync(require('node:path').join(__dirname,'../src/mods/route-filter/mapGeometry.ts'),'utf8');
const compiled = ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020}}).outputText;
const result = {exports:{}}; new Function('exports','module',compiled)(result.exports,result);
const {parseMap,path} = result.exports;
const empty = parseMap(''); assert.ok(Object.values(empty.bounds).every(Number.isFinite));
const sample = parseMap('B||-30,-50;20,30\nR|12:3|5,10;20,30|2|2/4\nR|44:1|7,9|1|4/4');
assert.equal(sample.restrictions.length,2); assert.equal(sample.restrictions[0].directions,'2/4');
assert.equal(sample.restrictions[0].key,'12:3'); assert.equal(sample.restrictions[1].points.length,1);
assert.ok(sample.bounds.width >= 100 && sample.bounds.height >= 100);
assert.equal(sample.background,'M-30,-50 L20,30');
assert.equal(path([[1,2],[3,4]]),'M1,2 L3,4');
const bad = parseMap('B||NaN,2;3,4\nR|x|Infinity,0|1|2/4\nR|y|1|1|4/4');
assert.equal(bad.restrictions.length,0); assert.equal(bad.background,'');
const backgrounds = parseMap(Array.from({length:1000},(_,i)=>`B||${i},0;${i},1`).join('\n'));
assert.equal(backgrounds.background.split('M').length-1,1000);
assert.equal(backgrounds.backgroundPaths.length,2);
const entries=parseMap('E|12:3|0,0;8,4\nE|bad|0,0\nE|invalid|NaN,0;1,2');
assert.equal(entries.entries.length,1);assert.equal(entries.entries[0].key,'12:3');
assert.equal(entries.restrictions.length,0);
console.log('PASS: empty/invalid map data, finite bounds, geometry, stable selection IDs, directional counts, combined background path.');

const {fitMap,projectMap,zoomMap,mapMatrix}=result.exports;
for(const data of ['B||-10000,-2000;8000,10000','B||30,40;30,40','B||-1,-1;1,1']) {
  const parsed=parseMap(data),view=fitMap(parsed.bounds,720,440);
  assert.equal(parsed.roadCount,1);
  const endpoints=data.split('|')[2].split(';').map(p=>p.split(',').map(Number));
  for(const point of endpoints){const [x,y]=projectMap(point,view,720,440);assert.ok(x>=24&&x<=696&&y>=24&&y<=416);}
  const center=projectMap([view.cx,view.cy],view,720,440);assert.deepEqual(center,[360,220]);
  const anchor=endpoints[0],before=projectMap(anchor,view,720,440),after=projectMap(anchor,zoomMap(view,1.5,...before,720,440),720,440);
  assert.ok(Math.abs(before[0]-after[0])<1e-6&&Math.abs(before[1]-after[1])<1e-6);
  assert.ok(mapMatrix(view,720,440).startsWith('matrix('));
  assert.ok(projectMap([view.cx,view.cy+10],view,720,440)[1]<220);
}
console.log('PASS: production fit, negative/flat/large networks, Z inversion and cursor-anchored zoom.');
