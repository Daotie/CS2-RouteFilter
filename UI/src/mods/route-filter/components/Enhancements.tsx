import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Panel, Portal } from "cs2/ui";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { parseMap, path } from "../mapGeometry";
import { RoadSignSelector } from "./RoadSignSelector";
import styles from "../route-filter.module.scss";
const advancedClosed$ = bindValue<number>(mod.id,"advancedClosed",0);
const presets$ = bindValue<string>(mod.id,"userPresets","");
const missing$ = bindValue<number>(mod.id,"presetMissing",0);
const unsupported$ = bindValue<number>(mod.id,"presetUnsupported",0);
const map$ = bindValue<string>(mod.id,"restrictionMap","");
const roads$ = bindValue<string>(mod.id,"restrictionMapRoads","");
const range$ = bindValue<boolean>(mod.id,"segmentBrush",false);
const status$ = bindValue<string>(mod.id,"rangeStatus","Start");
const pending$ = bindValue<number>(mod.id,"brushPending",0);
const appearance$ = bindValue<string>(mod.id,"signAppearance","1|0|0|0|0");
type Props = { targetMode: number; editable: boolean; popup: string; onPopup: (value: string) => void; reset: number; targetReady: boolean; hasClipboard: boolean; onReset: () => void; onAppearance: (open: boolean) => void };
export const MenuItem = ({children,onSelect,disabled=false,submenu=false}: {children: React.ReactNode; onSelect:()=>void; disabled?:boolean; submenu?:boolean}) => <button type="button" role="menuitem" className={styles.menuItem} disabled={disabled} onClick={onSelect}><span>{children}</span>{submenu && <span className={styles.menuChevron} aria-hidden="true">›</span>}</button>;
export const Enhancements = ({targetMode,editable,popup,onPopup,reset,targetReady,hasClipboard,onAppearance}: Props) => {
  const advancedClosed=useValue(advancedClosed$),range=useValue(range$),pending=useValue(pending$),status=useValue(status$);
  const [palette,setPalette]=useState(false);
  const menu=useRef<HTMLDivElement>(null);
  const [position,setPosition]=useState({left:18,top:126});
  const {translate}=useLocalization();
  const tr=(id:string,fallback:string)=>String(translate(`RouteFilter.UI.${id}`,fallback)??fallback);
  useEffect(()=>{
    trigger(mod.id,"setRestrictionMapOpen",popup==="Map"); trigger(mod.id,"setSegmentBrush",popup==="Range");
    return ()=>{trigger(mod.id,"setRestrictionMapOpen",false);trigger(mod.id,"setSegmentBrush",false);trigger(mod.id,"setUiPointerArea","utility",false);};
  },[popup]);
  useEffect(()=>()=>{trigger(mod.id,"closeAdvancedInteraction");onAppearance(false);},[]);
  useEffect(()=>{if(advancedClosed||reset){setPalette(false);onAppearance(false);onPopup("");}},[advancedClosed,reset,onPopup,onAppearance]);
  useEffect(()=>{
    if(!popup&&!palette)return;
    const escape=(event:KeyboardEvent)=>{if(event.key==="Escape"){event.preventDefault();event.stopPropagation();setPalette(false);onAppearance(false);onPopup("");trigger(mod.id,"closeAdvancedInteraction");}};
    document.addEventListener("keydown",escape,true);return()=>document.removeEventListener("keydown",escape,true);
  },[popup,palette,onPopup,onAppearance]);
  useLayoutEffect(()=>{
    if(!popup||popup==="Map")return;
    const positionMenu=()=>{
      const anchor=document.getElementById("routefilter-tools")?.getBoundingClientRect();
      const width=menu.current?.getBoundingClientRect().width??300;
      if(anchor)setPosition({left:Math.max(12,Math.min(anchor.right-width,window.innerWidth-width-12)),top:Math.min(anchor.bottom+8,window.innerHeight-160)});
    };
    positionMenu();window.addEventListener("resize",positionMenu);return()=>window.removeEventListener("resize",positionMenu);
  },[popup]);
  useEffect(()=>{
    if(!popup||popup==="Map"||popup==="Range")return;
    const outside=(event:MouseEvent)=>{if(!menu.current?.contains(event.target as Node)&&!document.getElementById("routefilter-tools")?.contains(event.target as Node))onPopup("");};
    document.addEventListener("mousedown",outside);return()=>document.removeEventListener("mousedown",outside);
  },[popup,onPopup]);
  const finish=()=>{setPalette(false);onAppearance(false);trigger(mod.id,"setSignAdjustment","",.05);};
  return <>
    {popup && popup!=="Map" && <Portal><div ref={menu} className={styles.secondaryMenu} style={{left:position.left,top:position.top}} onMouseEnter={()=>trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={()=>trigger(mod.id,"setUiPointerArea","utility",false)}>
      {popup!=="Tools" && <div className={styles.menuHeading}><button type="button" className={styles.windowControl} onClick={()=>onPopup("Tools")} aria-label={tr("Back","Back")}>‹</button><strong>{tr(popup,popup)}</strong><button type="button" className={styles.windowControl} onClick={()=>onPopup("")} aria-label={tr("Close","Close")}>×</button></div>}
      {popup==="Tools" && <div role="menu" aria-label={tr("Tools","Tools")}>
        <MenuItem disabled={!targetReady} onSelect={()=>trigger(mod.id,"copyAssetRestriction")}>{tr("LibraryCopy","Copy vehicle restrictions")}</MenuItem>
        <MenuItem disabled={!targetReady||!hasClipboard||!editable} onSelect={()=>trigger(mod.id,"pasteAssetRestriction")}>{tr("LibraryPaste","Paste vehicle restrictions")}</MenuItem>
        <div className={styles.menuSeparator}/>
        <MenuItem submenu onSelect={()=>onPopup("Presets")}>{tr("Presets","Presets")}</MenuItem>
        <MenuItem submenu onSelect={()=>onPopup("SignStyle")}>{tr("SignStyle","Road restriction sign style")}</MenuItem>
        <MenuItem onSelect={()=>{setPalette(true);onAppearance(true);onPopup("");}}>{tr("Appearance","Sign position adjustment")}</MenuItem>
        <div className={styles.menuSeparator}/>
        <MenuItem onSelect={()=>onPopup("Map")}>{tr("Map","Restriction map")}</MenuItem>
        <MenuItem disabled={targetMode!==1||!editable} onSelect={()=>onPopup("Range")}>{tr("Brush","Batch segment restrictions")}</MenuItem>
      </div>}
      {popup==="Presets" && <PresetMenu editable={editable} onLoaded={()=>onPopup("")} />}
      {popup==="SignStyle" && <RoadSignSelector closeToken={0} onPopupOpen={()=>{}}/>}
      {popup==="Range" && <div className={styles.toolInstructions}>
        <p>{tr("BrushHint","Select start and end segments, review the connected chain, then confirm.")}</p>
        <div role="status">{tr(`Range${status}`,status)} · {pending} {tr("RangeSegments","segments in preview")}</div>
        <MenuItem disabled={!range||status!=="Ready"||!pending||!editable} onSelect={()=>trigger(mod.id,"confirmSegmentRange",false)}>{tr("RangeApply","Apply to range")}</MenuItem>
        <MenuItem disabled={!range||status!=="Ready"||!pending||!editable} onSelect={()=>trigger(mod.id,"confirmSegmentRange",true)}>{tr("RangeClear","Clear range restrictions")}</MenuItem>
        <MenuItem onSelect={()=>{trigger(mod.id,"cancelSegmentRange");onPopup("");}}>{tr("Cancel","Cancel")}</MenuItem>
      </div>}
    </div></Portal>}
    {popup==="Map" && <Portal><Panel className={styles.mapPanel} contentClassName={styles.auxContent} onMouseEnter={()=>trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={()=>trigger(mod.id,"setUiPointerArea","utility",false)}><div className={styles.auxHeader}><strong>{tr("Map","Restriction map")}</strong><button type="button" className={styles.windowControl} onClick={()=>onPopup("")} aria-label={tr("Close","Close")}>×</button></div><RestrictionMap/></Panel></Portal>}
    {palette && <AppearancePalette onClose={finish}/>}
  </>;
};
const PresetMenu=({editable,onLoaded}:{editable:boolean;onLoaded:()=>void})=>{
  const raw=useValue(presets$),missing=useValue(missing$),unsupported=useValue(unsupported$);
  const presets=useMemo(()=>raw.split("\n").filter(Boolean).flatMap(item=>{try{return [decodeURIComponent(item)];}catch{return[];}}),[raw]);
  const [actions,setActions]=useState(""),[editor,setEditor]=useState<{old:string;name:string}|null>(null);
  const {translate}=useLocalization();const tr=(id:string,fallback:string)=>String(translate(`RouteFilter.UI.${id}`,fallback)??fallback);
  return <div role="menu">
    <div className={styles.menuSectionLabel}>{tr("PresetBuiltIn","Built-in presets")}</div>
    {["Trucks","CargoTrucks"].map(id=><MenuItem key={id} disabled={!editable} onSelect={()=>{trigger(mod.id,"loadBuiltinPreset",id);onLoaded();}}>{tr(`Preset${id}`,id==="Trucks"?"Trucks":"Cargo trucks")}</MenuItem>)}
    <div className={styles.menuSeparator}/><div className={styles.menuSectionLabel}>{tr("PresetMine","My presets")}</div>
    {presets.map(preset=><React.Fragment key={preset}><div className={styles.presetMenuRow}><MenuItem disabled={!editable} onSelect={()=>{trigger(mod.id,"loadUserPreset",preset);onLoaded();}}>{preset}</MenuItem><button type="button" className={styles.windowControl} onClick={()=>setActions(actions===preset?"":preset)} aria-label={`${tr("PresetActions","Preset actions")}: ${preset}`}>⋯</button></div>
      {actions===preset && <div className={styles.presetContext}><MenuItem onSelect={()=>{setEditor({old:preset,name:preset});setActions("");}}>{tr("PresetRename","Rename")}</MenuItem><MenuItem onSelect={()=>{trigger(mod.id,"deleteUserPreset",preset);setActions("");}}>{tr("PresetDelete","Delete")}</MenuItem></div>}
    </React.Fragment>)}
    <div className={styles.menuSeparator}/><MenuItem disabled={!editable||presets.length>=64} onSelect={()=>setEditor({old:"",name:""})}>+ {tr("PresetSave","Save current selection")}</MenuItem>
    {editor && <div className={styles.presetEditor}><label>{tr("PresetName","Preset name")}<input autoFocus maxLength={80} value={editor.name} onChange={event=>setEditor({...editor,name:event.target.value})}/></label><div className={styles.presetEditorActions}><button type="button" className={styles.secondaryAction} onClick={()=>setEditor(null)}>{tr("Cancel","Cancel")}</button><button type="button" className={styles.secondaryAction} disabled={!editor.name.trim()||presets.includes(editor.name.trim())&&editor.old!==editor.name.trim()} onClick={()=>{if(editor.old)trigger(mod.id,"renameUserPreset",editor.old,editor.name);else trigger(mod.id,"saveUserPreset",editor.name);setEditor(null);}}>{tr(editor.old?"PresetRename":"PresetSave",editor.old?"Rename":"Save")}</button></div></div>}
    {(missing>0||unsupported>0)&&<div className={styles.workflowStatus} role="status">{tr("PresetMissing","Missing assets")}: {missing} · {tr("PresetUnsupported","Unsupported on this target")}: {unsupported}</div>}
  </div>;
};

const parameters = [["scale","Scale",0,"×"],["offset","Lateral offset",2,"m"],["longitudinal","Longitudinal offset",3,"m"],["height","Height",1,"m"],["rotation","Rotation",4,"°"]] as const;
const AppearancePalette = ({ onClose }: { onClose: () => void }) => {
  const raw = useValue(appearance$), values = useMemo(() => raw.split("|").map(Number),[raw]);
  const [parameter,setParameter] = useState("offset"), [step,setStep] = useState(.05);
  const { translate } = useLocalization();
  const tr = (key: string,fallback: string) => String(translate(`RouteFilter.UI.${key}`,fallback) ?? fallback);
  useEffect(() => { trigger(mod.id,"setSignAdjustment",parameter,step); return () => trigger(mod.id,"setSignAdjustment","",step); },[parameter,step]);
  useEffect(() => () => trigger(mod.id,"setUiPointerArea","appearance",false),[]);
  return <Portal><Panel className={styles.appearancePalette} contentClassName={styles.auxContent} onMouseEnter={() => trigger(mod.id,"setUiPointerArea","appearance",true)} onMouseLeave={() => trigger(mod.id,"setUiPointerArea","appearance",false)}>
    <div className={styles.auxHeader}><strong>{tr("Appearance","Sign position adjustment")}</strong><button type="button" className={styles.windowControl} onClick={onClose} aria-label={tr("Close","Close")}>×</button></div>
    <div className={styles.parameterList}>{parameters.map(([id,label,index,unit]) => <button type="button" key={id} className={`${styles.parameterRow} ${parameter===id?styles.parameterActive:""}`} aria-pressed={parameter===id} onClick={()=>setParameter(id)}><span>{tr(`Appearance${id}`,label)}</span><strong>{(values[index]??0).toFixed(2)} {unit}</strong></button>)}</div>
    <div className={styles.wheelStep}><span>{tr("WheelStep","Wheel step")}</span><div className={styles.segmentedControl}>{[.01,.05,.1,.5].map(value=><button type="button" key={value} className={`${styles.segmentButton} ${step===value?styles.segmentButtonActive:""}`} aria-pressed={step===value} onClick={()=>setStep(value)}>{parameter==="rotation"?`${value*20}°`:parameter==="scale"?`${value}×`:`${value}m`}</button>)}</div></div>
    <div className={styles.paletteActions}><button type="button" className={styles.utilityAction} onClick={()=>trigger(mod.id,"resetSignAppearance")}>{tr("AppearanceReset","Reset position")}</button><button type="button" className={styles.utilityAction} onClick={onClose}>{tr("Finish","Finish")}</button></div>
    <small>{tr("WheelHint","Scroll over the world to adjust. Shift: fine · Ctrl: coarse.")}</small>
  </Panel></Portal>;
};

const RestrictionMap = () => {
  const raw = useValue(map$), roads = useValue(roads$);
  const geometry = useMemo(() => parseMap(roads),[roads]);
  const overlay = useMemo(() => parseMap(raw),[raw]);
  const [zoom,setZoom] = useState(1), [pan,setPan] = useState({x:0,y:0});
  const [layers,setLayers] = useState({roads:true,segments:true,nodes:true,entries:true});
  const drag = useRef<{x:number;y:number;panX:number;panY:number;scale:number} | null>(null);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(`RouteFilter.UI.${key}`,fallback) ?? fallback);
  const bounds = geometry.bounds;
  const viewBox = `${bounds.x + pan.x + bounds.width*(1-1/zoom)/2} ${bounds.y + pan.y + bounds.height*(1-1/zoom)/2} ${bounds.width/zoom} ${bounds.height/zoom}`;
  return <>
    <div className={styles.libraryToolbar}><Button variant="flat" onSelect={() => setZoom(value => Math.min(32,value*1.5))}>+</Button><Button variant="flat" onSelect={() => setZoom(value => Math.max(1,value/1.5))}>−</Button><Button variant="flat" onSelect={() => { setZoom(1);setPan({x:0,y:0}); }}>{tr("MapFit","Fit city")}</Button><span>{overlay.restrictions.length} {tr("MapTargets","restricted roads")}</span></div>
    <div className={styles.libraryToolbar}>{(Object.keys(layers) as (keyof typeof layers)[]).map(key => <Button key={key} variant="flat" selected={layers[key]} onSelect={() => setLayers(current => ({...current,[key]:!current[key]}))}>{tr(`MapLayer${key}`,key)}</Button>)}</div>
    <svg className={styles.restrictionMap} viewBox={viewBox} onWheel={event => { event.preventDefault(); event.stopPropagation(); setZoom(value => Math.max(1,Math.min(32,value*(event.deltaY > 0 ? 1/1.2 : 1.2)))); }} onMouseDown={event => {
      if (event.button !== 0) return;
      const rect = event.currentTarget.getBoundingClientRect();
      drag.current = {x:event.clientX,y:event.clientY,panX:pan.x,panY:pan.y,scale:Math.max(bounds.width/rect.width,bounds.height/rect.height)/zoom};
    }} onMouseMove={event => { const start = drag.current; if (start && event.buttons === 1) setPan({x:start.panX-(event.clientX-start.x)*start.scale,y:start.panY-(event.clientY-start.y)*start.scale}); }} onMouseUp={() => { drag.current = null; }} onMouseLeave={() => { drag.current = null; }}>
      {layers.roads && geometry.backgroundPaths.map((d,index) => <path key={index} d={d} fill="none" stroke="#576571" strokeWidth={1.5} vectorEffect="non-scaling-stroke" />)}
      {overlay.restrictions.filter(row => row.points.length === 1 ? layers.nodes : layers.segments).map(row => <g key={row.key} onMouseDown={event => event.stopPropagation()} onClick={() => trigger(mod.id,"selectMapTarget",row.key)}>
        <title>{`${row.assets} ${tr("MapAssets","vehicle assets")}, ${row.directions} ${tr("EntryDirections","restricted entries")}`}</title>
        {row.points.length === 1 ? <circle cx={row.points[0][0]} cy={row.points[0][1]} r={Math.max(bounds.width,bounds.height)/150/zoom} fill="#ff8877" /> : <><path d={path(row.points)} fill="none" stroke="transparent" strokeWidth={12} vectorEffect="non-scaling-stroke"/><path d={path(row.points)} fill="none" stroke="#ff8877" strokeWidth={3} vectorEffect="non-scaling-stroke" /></>}
      </g>)}
      {layers.entries && overlay.entries.map((row,index) => {
        const [a,b] = row.points, dx=b[0]-a[0],dy=b[1]-a[1],size=Math.max(bounds.width,bounds.height)/220/zoom, length=Math.hypot(dx,dy)||1;
        const ux=dx/length,uy=dy/length;
        return <path key={index} d={path([[a[0]+ux*size,a[1]+uy*size],[a[0]-ux*size-uy*size*.6,a[1]-uy*size+ux*size*.6],[a[0]-ux*size+uy*size*.6,a[1]-uy*size-ux*size*.6],[a[0]+ux*size,a[1]+uy*size]])} fill="#ffd575" onMouseDown={event => event.stopPropagation()} onClick={() => trigger(mod.id,"selectMapTarget",row.key)} />;
      })}
    </svg>
    <small>{tr("MapHint","Click a restricted road to edit. Drag to pan; scroll to zoom.")}</small>
  </>;
};
