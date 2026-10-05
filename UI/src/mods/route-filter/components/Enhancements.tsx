import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Panel, Portal } from "cs2/ui";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { parseMap, path, fitMap, mapMatrix, zoomMap, MapView } from "../mapGeometry";
import { RoadSignSelector } from "./RoadSignSelector";
import styles from "../route-filter.module.scss";
const feedback$ = bindValue<string>(mod.id,"libraryFeedback","");
const advancedClosed$ = bindValue<number>(mod.id,"advancedClosed",0);
const presets$ = bindValue<string>(mod.id,"userPresets","");
const missing$ = bindValue<number>(mod.id,"presetMissing",0);
const unsupported$ = bindValue<number>(mod.id,"presetUnsupported",0);
const map$ = bindValue<string>(mod.id,"restrictionMap","");
const terrain$ = bindValue<string>(mod.id,"restrictionMapTerrain","");
const roads$ = bindValue<string>(mod.id,"restrictionMapRoads","");
const range$ = bindValue<boolean>(mod.id,"segmentBrush",false);
const status$ = bindValue<string>(mod.id,"rangeStatus","Start");
const pending$ = bindValue<number>(mod.id,"brushPending",0);
const appearance$ = bindValue<string>(mod.id,"signAppearance","1|0|0|0|0");
type Props = { targetMode: number; editable: boolean; popup: string; onPopup: (value: string) => void; reset: number; targetReady: boolean; hasClipboard: boolean; onReset: () => void; onAppearance: (open: boolean) => void };
export const MenuItem = ({children,onSelect,disabled=false,submenu=false}: {children: React.ReactNode; onSelect:()=>void; disabled?:boolean; submenu?:boolean}) => <button type="button" role="menuitem" className={styles.menuItem} disabled={disabled} onClick={onSelect}><span>{children}</span>{submenu && <span className={styles.menuChevron} aria-hidden="true">›</span>}</button>;
export const Enhancements = ({targetMode,editable,popup,onPopup,reset,targetReady,hasClipboard,onAppearance}: Props) => {
  const feedback=useValue(feedback$);
  const [feedbackKind,feedbackCount]=feedback.split("|");
  const feedbackFallback:Record<string,string>={Copied:"Copied {count} vehicle restrictions.",Pasted:"Loaded {count} vehicle restrictions. Press Apply; entry directions stay unchanged.",Incompatible:"No clipboard vehicles match this target."};
  const advancedClosed=useValue(advancedClosed$),range=useValue(range$),pending=useValue(pending$),status=useValue(status$);

  const menu=useRef<HTMLDivElement>(null);
  const [position,setPosition]=useState({left:18,top:126});
  const {translate}=useLocalization();
  const tr=(id:string,fallback:string)=>String(translate(`RouteFilter.UI.${id}`,fallback)??fallback);
  useEffect(()=>{trigger(mod.id,"setSecondaryInteraction",Boolean(popup));return()=>trigger(mod.id,"setSecondaryInteraction",false);},[popup]);
  useEffect(()=>{
    trigger(mod.id,"setRestrictionMapOpen",popup==="Map"); trigger(mod.id,"setSegmentBrush",popup==="Range");
    return ()=>{trigger(mod.id,"setRestrictionMapOpen",false);trigger(mod.id,"setSegmentBrush",false);trigger(mod.id,"setUiPointerArea","utility",false);};
  },[popup]);
  useEffect(()=>()=>{trigger(mod.id,"closeAdvancedInteraction");onAppearance(false);},[]);
  useEffect(()=>{if(advancedClosed||reset){onAppearance(false);onPopup("");}},[advancedClosed,reset,onPopup,onAppearance]);
  useEffect(()=>{
    if(!popup)return;
    const escape=(event:KeyboardEvent)=>{if(event.key==="Escape"){event.preventDefault();event.stopPropagation();onAppearance(false);onPopup("");trigger(mod.id,"closeAdvancedInteraction");}};
    document.addEventListener("keydown",escape,true);return()=>document.removeEventListener("keydown",escape,true);
  },[popup,onPopup,onAppearance]);
  useLayoutEffect(()=>{
    if(!popup||popup==="Map")return;
    const positionMenu=()=>{
      const anchor=document.getElementById("routefilter-tools")?.getBoundingClientRect();
      const rectangle=menu.current?.getBoundingClientRect();
      const width=rectangle?.width??300, height=rectangle?.height??160;
      if(anchor)setPosition({left:Math.max(12,Math.min(anchor.right-width,window.innerWidth-width-12)),top:Math.max(12,Math.min(anchor.bottom+8,window.innerHeight-height-12))});
    };
    positionMenu();window.addEventListener("resize",positionMenu);return()=>window.removeEventListener("resize",positionMenu);
  },[popup]);
  useEffect(()=>{
    if(!popup||popup==="Map"||popup==="Range")return;
    const outside=(event:MouseEvent)=>{if(!menu.current?.contains(event.target as Node)&&!document.getElementById("routefilter-tools")?.contains(event.target as Node))onPopup("");};
    document.addEventListener("mousedown",outside);return()=>document.removeEventListener("mousedown",outside);
  },[popup,onPopup]);

  return <>
    {popup && popup!=="Map" && <Portal><div ref={menu} className={styles.secondaryMenu} style={{left:position.left,top:position.top}} onMouseEnter={()=>trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={()=>trigger(mod.id,"setUiPointerArea","utility",false)}>
      {popup!=="Tools" && <div className={styles.menuHeading}><button type="button" className={styles.windowControl} onClick={()=>onPopup("Tools")} aria-label={tr("Back","Back")}>‹</button><strong>{tr(popup,popup)}</strong><button type="button" className={styles.windowControl} onClick={()=>onPopup("")} aria-label={tr("Close","Close")}>×</button></div>}
      {popup==="Tools" && <div role="menu" aria-label={tr("Tools","Tools")}>
        <MenuItem submenu onSelect={()=>onPopup("Presets")}>{tr("Presets","Presets")}</MenuItem>
        <div className={styles.menuSeparator}/>
        <MenuItem onSelect={()=>onPopup("Map")}>{tr("Map","Restriction map")}</MenuItem>
        <MenuItem disabled={targetMode!==1||!editable} onSelect={()=>onPopup("Range")}>{tr("Brush","Batch segment restrictions")}</MenuItem>
        {feedbackFallback[feedbackKind]&&<div className={styles.workflowStatus} role="status">{tr(`Feedback.${feedbackKind}`,feedbackFallback[feedbackKind]).replace("{count}",feedbackCount??"0")}</div>}
      </div>}
      {popup==="Presets" && <PresetMenu editable={editable} onLoaded={()=>onPopup("")} />}
      {popup==="Range" && <div className={styles.toolInstructions}>
        <p>{tr("BrushHint","Select start and end segments, review the connected chain, then confirm.")}</p>
        <div role="status">{tr(`Range${status}`,status)} · {pending} {tr("RangeSegments","segments in preview")}</div>
        <MenuItem disabled={!range||status!=="Ready"||!pending||!editable} onSelect={()=>trigger(mod.id,"confirmSegmentRange",false)}>{tr("RangeApply","Apply to range")}</MenuItem>
        <MenuItem disabled={!range||status!=="Ready"||!pending||!editable} onSelect={()=>trigger(mod.id,"confirmSegmentRange",true)}>{tr("RangeClear","Clear range restrictions")}</MenuItem>
        <MenuItem onSelect={()=>{trigger(mod.id,"cancelSegmentRange");onPopup("");}}>{tr("Cancel","Cancel")}</MenuItem>
      </div>}
    </div></Portal>}
    {popup==="Map" && <Portal><Panel className={styles.mapPanel} contentClassName={styles.auxContent} onMouseEnter={()=>trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={()=>trigger(mod.id,"setUiPointerArea","utility",false)}><div className={styles.auxHeader}><strong>{tr("Map","Restriction map")}</strong><button type="button" className={styles.windowControl} onClick={()=>onPopup("")} aria-label={tr("Close","Close")}>×</button></div><div className={styles.mapBody}><RestrictionMap/></div></Panel></Portal>}
    <AppearancePalette reset={advancedClosed+reset} onAppearance={onAppearance}/>
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
const ParameterInput = ({id,label,value,unit,active,onActivate}: {id:string;label:string;value:number;unit:string;active:boolean;onActivate:()=>void}) => {
  const [draft,setDraft]=useState(value.toFixed(2));
  const editing=useRef(false),cancelled=useRef(false);
  useEffect(()=>{if(!editing.current)setDraft(value.toFixed(2));},[value]);
  const commit=()=>{editing.current=false;if(cancelled.current){cancelled.current=false;setDraft(value.toFixed(2));return;}const number=Number(draft);if(draft.trim() && Number.isFinite(number))trigger(mod.id,"setSignAppearance",id,number);else setDraft(value.toFixed(2));};
  return <label className={`${styles.parameterRow} ${active?styles.parameterActive:""}`}><span>{label}</span><input aria-label={label} type="text" inputMode="decimal" value={draft} onFocus={()=>{editing.current=true;cancelled.current=false;onActivate();}} onChange={event=>setDraft(event.target.value)} onBlur={commit} onKeyDown={event=>{if(event.key==="Enter")event.currentTarget.blur();if(event.key==="Escape"){cancelled.current=true;setDraft(value.toFixed(2));editing.current=false;event.currentTarget.blur();}}}/><span className={styles.parameterUnit}>{unit}</span></label>;
};
const AppearancePalette = ({ reset,onAppearance }: { reset:number;onAppearance:(active:boolean)=>void }) => {
  const raw = useValue(appearance$), values = useMemo(() => raw.split("|").map(Number),[raw]);
  const [parameter,setParameter] = useState(""), [step,setStep] = useState(.05);
  const { translate } = useLocalization();
  const tr = (key: string,fallback: string) => String(translate(`RouteFilter.UI.${key}`,fallback) ?? fallback);
  useEffect(() => { trigger(mod.id,"setSignAdjustment",parameter,step);onAppearance(Boolean(parameter));return () => trigger(mod.id,"setSignAdjustment","",step); },[parameter,step,onAppearance]);
  useEffect(()=>{setParameter("");},[reset]);
  useEffect(() => () => trigger(mod.id,"setUiPointerArea","appearance",false),[]);
  return <Portal><Panel className={styles.appearancePalette} contentClassName={styles.auxContent} onMouseEnter={() => trigger(mod.id,"setUiPointerArea","appearance",true)} onMouseLeave={() => trigger(mod.id,"setUiPointerArea","appearance",false)}>
    <div className={styles.auxHeader}><strong>{tr("SignTool","Traffic sign")}</strong></div>
    <RoadSignSelector closeToken={reset}/>
    <div className={styles.parameterList}>{parameters.map(([id,label,index,unit]) => <ParameterInput key={id} id={id} label={tr(`Appearance${id}`,label)} value={values[index]??0} unit={unit} active={parameter===id} onActivate={()=>setParameter(id)}/>)}</div>
    <div className={styles.wheelStep}><span>{tr("WheelStep","Wheel step")}</span><div className={styles.segmentedControl}>{[.01,.05,.1,.5].map(value=><button type="button" key={value} className={`${styles.segmentButton} ${step===value?styles.segmentButtonActive:""}`} aria-pressed={step===value} onClick={()=>setStep(value)}>{parameter==="rotation"?`${value*20}°`:parameter==="scale"?`${value}×`:`${value}m`}</button>)}</div></div>
    <div className={styles.paletteActions}><button type="button" className={styles.utilityAction} onClick={()=>trigger(mod.id,"resetSignAppearance")}>{tr("AppearanceReset","Reset position")}</button><button type="button" className={styles.utilityAction} disabled={!parameter} onClick={()=>setParameter("")}>{tr("Finish","Finish")}</button></div>
    <small>{tr("WheelHint","Scroll over the world to adjust. Shift: fine · Ctrl: coarse.")}</small>
  </Panel></Portal>;
};

export const RestrictionMap = () => {
  const raw=useValue(map$), roads=useValue(roads$), terrain=useValue(terrain$);
  const geometry=useMemo(()=>parseMap(roads+"\n"+terrain),[roads,terrain]), overlay=useMemo(()=>parseMap(raw),[raw]);
  const host=useRef<HTMLDivElement>(null);
  const [size,setSize]=useState({width:720,height:440});
  const [view,setView]=useState<MapView>({cx:0,cy:0,scale:1});
  const [layers,setLayers]=useState({roads:true,segments:true,nodes:true,entries:true});
  const drag=useRef<{x:number;y:number;view:MapView}|null>(null);
  const {translate}=useLocalization();
  const tr=(key:string,fallback:string)=>String(translate(`RouteFilter.UI.${key}`,fallback)??fallback);
  useLayoutEffect(()=>{
    const measure=()=>{const rect=host.current?.getBoundingClientRect();if(rect&&rect.width>0&&rect.height>0)setSize({width:rect.width,height:rect.height});};
    measure();window.addEventListener("resize",measure);return()=>window.removeEventListener("resize",measure);
  },[]);
  useEffect(()=>{setView(fitMap(geometry.bounds,size.width,size.height));},[geometry,size.width,size.height]);
  useEffect(()=>{console.info("[RouteFilter.Map.UI]",{roads:geometry.roadCount,characters:roads.length,bounds:geometry.bounds,viewport:size});},[geometry,roads,size]);
  useEffect(()=>{const release=()=>{drag.current=null;};window.addEventListener("mouseup",release);return()=>{window.removeEventListener("mouseup",release);drag.current=null;};},[]);
  const zoom=(factor:number)=>setView(current=>zoomMap(current,factor,size.width/2,size.height/2,size.width,size.height));
  return <>
    <div className={styles.mapToolbar}><button type="button" className={styles.windowControl} onClick={()=>zoom(1.5)} aria-label={tr("MapZoomIn","Zoom in")}>+</button><button type="button" className={styles.windowControl} onClick={()=>zoom(1/1.5)} aria-label={tr("MapZoomOut","Zoom out")}>−</button><button type="button" className={styles.secondaryAction} onClick={()=>setView(fitMap(geometry.bounds,size.width,size.height))}>{tr("MapFit","Fit city")}</button><span>{overlay.restrictions.length} {tr("MapTargets","restricted roads")}</span></div>
    <div className={styles.mapToolbar}>{(Object.keys(layers) as (keyof typeof layers)[]).map(key=><button type="button" key={key} className={`${styles.mapLayer} ${layers[key]?styles.mapLayerActive:""}`} aria-pressed={layers[key]} onClick={()=>setLayers(current=>({...current,[key]:!current[key]}))}>{tr(`MapLayer${key}`,key)}</button>)}</div>
    <div ref={host} className={styles.mapViewport}>
      <svg className={styles.restrictionMap} width={size.width} height={size.height} viewBox={`0 0 ${size.width} ${size.height}`} onWheel={event=>{
        event.preventDefault();event.stopPropagation();const rect=event.currentTarget.getBoundingClientRect();
        setView(current=>zoomMap(current,event.deltaY>0?1/1.2:1.2,(event.clientX-rect.left)*size.width/rect.width,(event.clientY-rect.top)*size.height/rect.height,size.width,size.height));
      }} onMouseDown={event=>{if(event.button===0)drag.current={x:event.clientX,y:event.clientY,view};}} onMouseMove={event=>{
        const start=drag.current;if(start&&event.buttons===1)setView({...start.view,cx:start.view.cx-(event.clientX-start.x)/start.view.scale,cy:start.view.cy+(event.clientY-start.y)/start.view.scale});
      }} onMouseUp={()=>{drag.current=null;}} onMouseLeave={()=>{drag.current=null;}}>
        <g transform={mapMatrix(view,size.width,size.height)}>
          <path d={geometry.waterPath} fill="#233849"/>
          <path d={geometry.landPath} fill="#46514c" stroke="#46514c" strokeWidth={.5/view.scale}/>
          {layers.roads&&geometry.backgroundPaths.map((d,index)=><path key={index} d={d} fill="none" stroke="#576571" strokeWidth={1.5/view.scale}/>)}
          {overlay.restrictions.filter(row=>row.points.length===1?layers.nodes:layers.segments).map(row=><g key={row.key} onMouseDown={event=>event.stopPropagation()} onClick={()=>trigger(mod.id,"selectMapTarget",row.key)}>
            <title>{`${row.assets} ${tr("MapAssets","vehicle assets")}, ${row.directions} ${tr("EntryDirections","restricted entries")}`}</title>
            {row.points.length===1?<><circle cx={row.points[0][0]} cy={row.points[0][1]} r={10/view.scale} fill="transparent"/><circle cx={row.points[0][0]} cy={row.points[0][1]} r={4/view.scale} fill="#ff8877"/></>:<><path d={path(row.points)} fill="none" stroke="transparent" strokeWidth={14/view.scale}/><path d={path(row.points)} fill="none" stroke="#ff8877" strokeWidth={3/view.scale}/></>}
          </g>)}
          {layers.entries&&overlay.entries.map((row,index)=>{
            const [a,b]=row.points,dx=b[0]-a[0],dy=b[1]-a[1],length=Math.hypot(dx,dy)||1,ux=dx/length,uy=dy/length,size=6/view.scale;
            return <g key={index} onMouseDown={event=>event.stopPropagation()} onClick={()=>trigger(mod.id,"selectMapTarget",row.key)}><circle cx={a[0]} cy={a[1]} r={10/view.scale} fill="transparent"/><path d={path([[a[0]+ux*size,a[1]+uy*size],[a[0]-ux*size-uy*size*.6,a[1]-uy*size+ux*size*.6],[a[0]-ux*size+uy*size*.6,a[1]-uy*size-ux*size*.6],[a[0]+ux*size,a[1]+uy*size]])} fill="#ffd575"/></g>;
          })}
        </g>
      </svg>
      {!geometry.roadCount&&<div className={styles.mapEmpty} role="status"><span>{tr("MapEmpty","Road geometry is not available yet.")}</span><button type="button" className={styles.secondaryAction} onClick={()=>trigger(mod.id,"refreshRestrictionMap")}>{tr("MapRefresh","Refresh")}</button></div>}
    </div>
  </>;
};
