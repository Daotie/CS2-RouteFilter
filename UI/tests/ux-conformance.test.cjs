const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const ts = require('typescript');
const React = require('react');
const {renderToStaticMarkup} = require('react-dom/server');
const cache = new Map(), bindings = new Map(), events = [];
const ui = {
 Button: ({children,onSelect,variant,selected,...props}) => React.createElement('button',{...props,onClick:onSelect,'data-selected':selected},children),
 Panel: ({children,contentClassName,...props}) => React.createElement('section',props,children),
 Portal: ({children}) => React.createElement(React.Fragment,null,children),
 Tooltip: ({children}) => React.createElement(React.Fragment,null,children),
 ConfirmationDialog: () => null,
};
function load(file) {
 file=path.resolve(file); if(cache.has(file)) return cache.get(file);
 const exports={}; cache.set(file,exports);
 const source=fs.readFileSync(file,'utf8');
 const compiled=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2020,jsx:ts.JsxEmit.React,esModuleInterop:true}}).outputText;
 const requireMock = name => {
   if(name==='cs2/ui') return ui;
   if(name==='cs2/api') return {bindValue:(group,key,value)=>({key,value}),useValue:binding=>bindings.has(binding.key)?bindings.get(binding.key):binding.value,trigger:(...args)=>events.push(args)};
   if(name==='cs2/l10n') return {useLocalization:()=>({translate:(key,fallback)=>fallback})};
   if(name==='mod.json') return {id:'RouteFilter',version:'2.1.0-dev'};
   if(name.endsWith('.scss')) return {__esModule:true,default:new Proxy({},{get:(_,key)=>String(key)})};
   if(/\.(svg|png)$/.test(name)) return name;
   if(name.startsWith('.')) { const resolved=path.resolve(path.dirname(file),name); return load([resolved+'.tsx',resolved+'.ts',path.join(resolved,'index.ts')].find(fs.existsSync)); }
   return require(name);
 };
 new Function('require','exports','module',compiled)(requireMock,exports,{exports}); return exports;
}
const base=path.join(__dirname,'../src/mods/route-filter');
const {AssetRow}=load(path.join(base,'components/AssetRow.tsx'));
let favorite=0,toggled=0,stopped=0;
const row=AssetRow({favorite:false,onFavorite:()=>favorite++,favoriteLabel:'Favorite',asset:{name:'Truck',mode:1,maxSpeed:80},selected:false,partial:false,childCount:0,expanded:false,onToggle:()=>toggled++,onExpand:()=>{},trailerLabel:'Trailer'});
const children=React.Children.toArray(row.props.children);
assert.equal(children[0].props.children,'☆');
children[0].props.onClick({stopPropagation:()=>stopped++});
assert.equal(favorite,1);assert.equal(toggled,0);assert.equal(stopped,1);
assert.equal(children.at(-1).props.className,'selectionGlyph');
children.at(-1).props.onSelect();assert.equal(toggled,1);
const {Enhancements}=load(path.join(base,'components/Enhancements.tsx'));
const props={targetMode:1,editable:true,popup:'',onPopup:()=>{},reset:0,targetReady:true,hasClipboard:false,onReset:()=>{},onAppearance:()=>{}};
assert.equal(renderToStaticMarkup(React.createElement(Enhancements,props)),'');
const tools=renderToStaticMarkup(React.createElement(Enhancements,{...props,popup:'Tools'}));
assert.match(tools,/Copy vehicle restrictions/);assert.match(tools,/disabled=""[^>]*><span>Paste vehicle restrictions/);
assert.match(tools,/Batch segment restrictions/);
const presets=renderToStaticMarkup(React.createElement(Enhancements,{...props,popup:'Presets'}));
assert.match(presets,/Cargo trucks/);assert.match(presets,/Save current selection/);assert.doesNotMatch(presets,/<input/);assert.match(presets,/Built-in presets/);assert.match(presets,/My presets/);
bindings.set('rangeStatus','End');bindings.set('segmentBrush',true);bindings.set('brushPending',1);
const range=renderToStaticMarkup(React.createElement(Enhancements,{...props,popup:'Range'}));
assert.match(range,/disabled=""[^>]*><span>Apply to range/);
const {SignInfoSection}=load(path.join(base,'components/SignInfoSection.tsx'));
const info=renderToStaticMarkup(React.createElement(SignInfoSection,{targetKind:'Segment',targetName:'Oak Street',categories:'Trucks',assets:2,entries:1,entryOrdinal:1}));
assert.match(info,/Oak Street/);assert.match(info,/Vehicle categories/);assert.match(info,/Edit in RouteFilter/);
console.log('PASS: independent favorite, right checkbox, secondary-only tools, empty clipboard, built-in presets, range confirmation gating, native-section data props.');

const {RoadSignSelector}=load(path.join(base,'components/RoadSignSelector.tsx'));
bindings.set('signageProfile','AUTO');bindings.set('signageProfileResolved','US');bindings.set('signageProfileFallback',true);
const signStyle=renderToStaticMarkup(React.createElement(RoadSignSelector,{}));
assert.match(signStyle,/Signage profile/);assert.match(signStyle,/value="CN"/);assert.match(signStyle,/value="UK"/);assert.match(signStyle,/value="US"/);
assert.doesNotMatch(signStyle,/value="HK"|value="JP"/);
assert.match(signStyle,/Auto resolved/);assert.match(signStyle,/no-entry fallback/);assert.match(signStyle,/unsupported languages fall back to English/);
const emptyInfo=renderToStaticMarkup(React.createElement(SignInfoSection,{targetKind:'Segment',categories:'',assets:1,entries:1,entryOrdinal:1}));
assert.match(emptyInfo,/Selected vehicles/);assert.doesNotMatch(emptyInfo,/All road vehicles/);
console.log('PASS: regional profile choices, hidden unfinished profiles, visible fallback, locale policy and conservative native info.');

const {SegmentedSelector}=load(path.join(base,'components/SegmentedSelector.tsx'));
const segments=renderToStaticMarkup(React.createElement(SegmentedSelector,{value:'favorites',label:'Library',options:[{id:'all',label:'All'},{id:'favorites',label:'★ Favorites'},{id:'recent',label:'Recent'}],onChange:()=>{}}));
assert.equal((segments.match(/aria-selected="true"/g)||[]).length,1);assert.match(segments,/segmentButtonActive/);assert.match(segments,/role="tablist"/);
assert.doesNotMatch(tools,/Reset RouteFilter/);assert.match(tools,/role="menuitem"/);
console.log('PASS: shared single-active segmented selector; real menu rows; progressive preset editor; Reset excluded from secondary menu.');
