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
   if(name==='react') return {...React,useLayoutEffect:React.useEffect};
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
assert.match(renderToStaticMarkup(children[0]), /<svg/);assert.doesNotMatch(renderToStaticMarkup(children[0]), /★|☆/);
children[0].props.onClick({stopPropagation:()=>stopped++});
assert.equal(favorite,1);assert.equal(toggled,0);assert.equal(stopped,1);
assert.match(renderToStaticMarkup(children.at(-1)),/selectionGlyph/);assert.match(renderToStaticMarkup(children.at(-1)),/role="checkbox"/);
children.at(-1).props.onSelect();assert.equal(toggled,1);
const {Enhancements}=load(path.join(base,'components/Enhancements.tsx'));
const props={targetMode:1,editable:true,popup:'',onPopup:()=>{},reset:0,targetReady:true,hasClipboard:false,onReset:()=>{},onAppearance:()=>{}};
const persistent=renderToStaticMarkup(React.createElement(Enhancements,props));assert.match(persistent,/Traffic sign/);assert.match(persistent,/Signage profile/);assert.equal((persistent.match(/inputMode="decimal"/g)||[]).length,6);assert.doesNotMatch(persistent,/type="range"/);
const tools=renderToStaticMarkup(React.createElement(Enhancements,{...props,popup:'Tools'}));
assert.doesNotMatch(tools,/Copy vehicle restrictions|Paste vehicle restrictions|Sign position adjustment/);
assert.doesNotMatch(tools,/Batch segment restrictions/);
const presets=renderToStaticMarkup(React.createElement(Enhancements,{...props,popup:'Presets'}));
assert.match(presets,/Cargo trucks/);assert.match(presets,/Save current selection/);assert.doesNotMatch(presets,/Preset name/);assert.match(presets,/Built-in presets/);assert.match(presets,/My presets/);
bindings.set('rangeStatus','End');bindings.set('segmentBrush',true);bindings.set('brushPending',1);
const range=renderToStaticMarkup(React.createElement(Enhancements,{...props,popup:'Range'}));
assert.doesNotMatch(range,/Apply to range/);
const {SignInfoSection}=load(path.join(base,'components/SignInfoSection.tsx'));
const info=renderToStaticMarkup(React.createElement(SignInfoSection,{targetKind:'Segment',targetName:'Oak Street',categories:'Trucks',assets:2,entries:1,entryOrdinal:1}));
assert.match(info,/Oak Street/);assert.match(info,/Vehicle categories/);assert.match(info,/Edit in RouteFilter/);
console.log('PASS: independent favorite, right checkbox, compact utility menu, persistent sign tool, built-in presets and footer clipboard gating, native-section data props.');

const {RoadSignSelector}=load(path.join(base,'components/RoadSignSelector.tsx'));
bindings.set('signageProfile','AUTO');bindings.set('signageProfileResolved','US');bindings.set('signageProfileFallback',true);
const signStyle=renderToStaticMarkup(React.createElement(RoadSignSelector,{}));
assert.match(signStyle,/Signage profile/);assert.match(signStyle,/Mainland China/);assert.match(signStyle,/United Kingdom/);assert.match(signStyle,/United States/);assert.doesNotMatch(signStyle,/<select|<option/);
assert.doesNotMatch(signStyle,/value="HK"|value="JP"/);
assert.match(signStyle,/Auto resolved/);assert.match(signStyle,/Using compatible sign/);assert.doesNotMatch(signStyle,/unsupported languages fall back to English/);
const emptyInfo=renderToStaticMarkup(React.createElement(SignInfoSection,{targetKind:'Segment',categories:'',assets:1,entries:1,entryOrdinal:1}));
assert.match(emptyInfo,/Selected vehicles/);assert.doesNotMatch(emptyInfo,/All road vehicles/);
console.log('PASS: regional profile choices, hidden unfinished profiles, visible fallback, locale policy and conservative native info.');

const {SegmentedSelector}=load(path.join(base,'components/SegmentedSelector.tsx'));
const segments=renderToStaticMarkup(React.createElement(SegmentedSelector,{value:'favorites',label:'Library',options:[{id:'all',label:'All'},{id:'favorites',label:'★ Favorites'},{id:'recent',label:'Recent'}],onChange:()=>{}}));
assert.equal((segments.match(/aria-selected="true"/g)||[]).length,1);assert.match(segments,/segmentButtonActive/);assert.match(segments,/role="tablist"/);
assert.doesNotMatch(tools,/Reset RouteFilter/);assert.match(tools,/role="menuitem"/);
console.log('PASS: shared single-active segmented selector; real menu rows; progressive preset editor; Reset excluded from secondary menu.');

bindings.set('libraryFeedback','Copied|2|1');
const copiedTools=renderToStaticMarkup(React.createElement(Enhancements,{...props,popup:'Tools',hasClipboard:true}));
assert.doesNotMatch(copiedTools,/Copy vehicle restrictions/);
assert.doesNotMatch(copiedTools,/disabled=""[^>]*><span>Paste vehicle restrictions/);
console.log('PASS: Clipboard actions remain outside the utility menu.');

const {ActionBar}=load(path.join(base,'components/ActionBar.tsx'));
const footer=renderToStaticMarkup(React.createElement(ActionBar,{copyLabel:'复制',pasteLabel:'粘贴',copyReady:true,pasteReady:false,targetReady:true,applyLabel:'应用',clearLabel:'清除'}));
assert.match(footer,/commitActions[^]*复制[^]*粘贴[^]*应用/);assert.match(footer,/disabled=""[^>]*>粘贴/);
console.log('PASS: persistent merged sign tool, five numeric fields, no sliders, SVG favorites and footer clipboard gating.');

const segmentFooter=renderToStaticMarkup(React.createElement(ActionBar,{targetMode:1,editable:true}));
const nodeFooter=renderToStaticMarkup(React.createElement(ActionBar,{targetMode:0,editable:true}));
assert.match(segmentFooter,/Batch apply/);assert.match(segmentFooter,/Batch clear/);assert.doesNotMatch(nodeFooter,/Batch apply|Batch clear/);
console.log('PASS: segment-only batch controls in lower functional row, absent from tools menu and Node mode.');

const {AssetList}=load(path.join(base,'components/AssetList.tsx'));
ui.Scrollable=({children,vertical,trackVisibility,...props})=>React.createElement('div',props,children);
const bus={id:1,name:'Bus',category:'Bus',mode:1,parentId:0};
const category=renderToStaticMarkup(React.createElement(AssetList,{roots:[bus],childrenByParent:new Map(),selected:new Set(),expanded:new Set(),favorites:new Set(),onFavorite:()=>{},onToggle:()=>{},onExpand:()=>{},searchTerm:'',editable:true}));
assert.match(category,/role="checkbox"/);assert.match(category,/bus-hd.png/);assert.doesNotMatch(category,/categoryAction|>Enable<|>Disable</);assert.match(category,/<svg/);assert.match(category,/stroke="#fff"/);assert.doesNotMatch(category,/⌄|›/);
console.log('PASS: category bulk controls and SVG expand; profile choices avoid unsupported native HTML select.');
