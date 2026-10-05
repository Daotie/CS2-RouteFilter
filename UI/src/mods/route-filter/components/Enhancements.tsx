import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Panel, Portal } from "cs2/ui";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { parseMap, path, fitMap, mapMatrix, zoomMap, navigationArrow, MapView } from "../mapGeometry";
import { RoadSignSelector } from "./RoadSignSelector";
import { ChevronIcon } from "./ChevronIcon";
import styles from "../route-filter.module.scss";
const advancedClosed$ = bindValue<number>(mod.id,"advancedClosed",0);
const presets$ = bindValue<string>(mod.id,"userPresets","");
const missing$ = bindValue<number>(mod.id,"presetMissing",0);
const unsupported$ = bindValue<number>(mod.id,"presetUnsupported",0);
const map$ = bindValue<string>(mod.id,"restrictionMap","");
const terrain$ = bindValue<string>(mod.id,"restrictionMapTerrain","");
const roads$ = bindValue<string>(mod.id,"restrictionMapRoads","");
const appearance$ = bindValue<string>(mod.id,"signAppearance","1|0|0|0|0");
type Props = { targetMode: number; editable: boolean; popup: string; onPopup: (value: string) => void; reset: number; targetReady: boolean; hasClipboard: boolean; onReset: () => void; onAppearance: (open: boolean) => void };
export const MenuItem = ({children,onSelect,disabled=false,submenu=false}: {children: React.ReactNode; onSelect:()=>void; disabled?:boolean; submenu?:boolean}) => <button type="button" role="menuitem" className={styles.menuItem} disabled={disabled} onClick={onSelect}><span>{children}</span>{submenu && <span className={styles.menuChevron}><ChevronIcon/></span>}</button>;
export const Enhancements = ({targetMode,editable,popup,onPopup,reset,targetReady,hasClipboard,onAppearance}: Props) => {
  const advancedClosed=useValue(advancedClosed$);

  const [signMenuOpen,setSignMenuOpen]=useState(false);
  const menu=useRef<HTMLDivElement>(null);
  const [position,setPosition]=useState({left:18,top:126});
  const {translate}=useLocalization();
  const tr=(id:string,fallback:string)=>String(translate(`RouteFilter.UI.${id}`,fallback)??fallback);
  useEffect(()=>{trigger(mod.id,"setSecondaryInteraction",Boolean(popup||signMenuOpen));return()=>trigger(mod.id,"setSecondaryInteraction",false);},[popup,signMenuOpen]);
  useEffect(()=>{
    trigger(mod.id,"setRestrictionMapOpen",popup==="Map");
    return ()=>{trigger(mod.id,"setRestrictionMapOpen",false);trigger(mod.id,"setSegmentBrush",false);trigger(mod.id,"setUiPointerArea","utility",false);};
  },[popup]);
  useEffect(()=>()=>{trigger(mod.id,"closeAdvancedInteraction");onAppearance(false);},[]);
  useEffect(()=>{if(advancedClosed||reset){onAppearance(false);onPopup("");}},[advancedClosed,reset,onPopup,onAppearance]);
  useEffect(()=>{
    if(!popup)return;
    const escape=(event:KeyboardEvent)=>{if(event.key==="Escape"){event.preventDefault();event.stopPropagation();onAppearance(false);onPopup("");trigger(mod.id,"closeAdvancedInteraction");}};
    document.addEventListener("keydown",escape,true);return()=>document.removeEventListener("keydown",escape,true);
  },[popup,onPopup,onAppearance]);
  const utilityOpen=Boolean(popup&&popup!=="Map");
  useLayoutEffect(()=>{
    if(!utilityOpen)return;
    const positionMenu=()=>{
      const anchor=document.getElementById("routefilter-tools")?.getBoundingClientRect();
      const panel=document.getElementById("routefilter-panel")?.getBoundingClientRect();
      if(anchor&&panel){const gap=panel.width/400*8;setPosition({left:panel.right+gap,top:anchor.top});}

    };
    positionMenu();window.addEventListener("resize",positionMenu);return()=>window.removeEventListener("resize",positionMenu);
  },[utilityOpen]);
  useEffect(()=>{
    if(!popup||popup==="Map"||popup==="Range")return;
    const outside=(event:MouseEvent)=>{if(!menu.current?.contains(event.target as Node)&&!document.getElementById("routefilter-tools")?.contains(event.target as Node))onPopup("");};
    document.addEventListener("mousedown",outside);return()=>document.removeEventListener("mousedown",outside);
  },[popup,onPopup]);

  return <>
    {popup && popup!=="Map" && <div ref={menu} className={styles.secondaryMenu} style={{left:position.left,top:position.top}} onMouseEnter={()=>trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={()=>trigger(mod.id,"setUiPointerArea","utility",false)}>
      {popup!=="Tools" && <div className={styles.menuHeading}><button type="button" className={styles.windowControl} onClick={()=>onPopup("Tools")} aria-label={tr("Back","Back")}><span className={styles.backArrow}><ChevronIcon/></span></button><strong>{tr(popup,popup)}</strong><button type="button" className={styles.windowControl} onClick={()=>onPopup("")} aria-label={tr("Close","Close")}>×</button></div>}
      <div key={popup} className={styles.menuPage}>
      {popup==="Tools" && <div role="menu" aria-label={tr("Tools","Tools")}>
        <MenuItem submenu onSelect={()=>onPopup("Presets")}>{tr("Presets","Presets")}</MenuItem>
        <div className={styles.menuSeparator}/>
        <MenuItem onSelect={()=>onPopup("Map")}>{tr("Map","Restriction map")}</MenuItem>
      </div>}
      {popup==="Presets" && <PresetMenu editable={editable} onLoaded={()=>onPopup("")} />}
      </div>

    </div>}
    {popup==="Map" && <Portal><Panel className={styles.mapPanel} contentClassName={styles.auxContent} onMouseEnter={()=>trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={()=>trigger(mod.id,"setUiPointerArea","utility",false)}><div className={styles.auxHeader}><strong>{tr("Map","Restriction map")}</strong><button type="button" className={styles.windowControl} onClick={()=>onPopup("")} aria-label={tr("Close","Close")}>×</button></div><div className={styles.mapBody}><RestrictionMap/></div></Panel></Portal>}
    <AppearancePalette reset={advancedClosed+reset} onAppearance={onAppearance} onSignMenuChange={setSignMenuOpen}/>
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

const parameters = [["offset","Lateral offset",2,"m"],["longitudinal","Longitudinal offset",3,"m"],["height","Height",1,"m"],["rotation","Rotation",4,"°"]] as const;
const ParameterInput = ({id,label,value,unit,active,onActivate,onCommit,onLeave,onWheelValue,precision=2}: {id:string;label:string;value:number;unit:string;active:boolean;onActivate:()=>void;onCommit?:(value:number)=>void;onLeave?:()=>void;onWheelValue?:(direction:number)=>number;precision?:number}) => {
  const [draft,setDraft]=useState(value.toFixed(precision));
  const editing=useRef(false),cancelled=useRef(false);
  useEffect(()=>{if(!editing.current)setDraft(value.toFixed(precision));},[value,precision]);
  const commit=()=>{editing.current=false;if(cancelled.current){cancelled.current=false;setDraft(value.toFixed(precision));return;}const number=Number(draft);if(draft.trim() && Number.isFinite(number)){if(onCommit)onCommit(number);else trigger(mod.id,"setSignAppearance",id,number);setDraft(value.toFixed(precision));}else setDraft(value.toFixed(precision));};
  return <label className={`${styles.parameterRow} ${active?styles.parameterActive:""}`}><span>{label}</span><input aria-label={label} type="text" inputMode="decimal" value={draft} onMouseEnter={onActivate} onMouseLeave={onLeave} onWheel={event=>{if(!onWheelValue || event.deltaY===0)return;event.preventDefault();event.stopPropagation();editing.current=false;setDraft(onWheelValue(event.deltaY<0?1:-1).toFixed(precision));}} onFocus={()=>{editing.current=true;cancelled.current=false;}} onChange={event=>{const text=event.target.value;setDraft(text);const number=Number(text);if(text.trim()&&Number.isFinite(number)){if(onCommit)onCommit(number);else trigger(mod.id,"setSignAppearance",id,number);}}} onBlur={commit} onKeyDown={event=>{if(event.key==="Enter")event.currentTarget.blur();if(event.key==="Escape"){cancelled.current=true;setDraft(value.toFixed(precision));editing.current=false;event.currentTarget.blur();}}}/><span className={styles.parameterUnit}>{unit}</span></label>;
};
const AppearancePalette = ({ reset,onAppearance,onSignMenuChange }: { reset:number;onAppearance:(active:boolean)=>void;onSignMenuChange:(open:boolean)=>void }) => {
  const raw = useValue(appearance$), values = useMemo(() => raw.split("|").map(Number),[raw]);
  const [parameter,setParameter] = useState(""), [step,setStep] = useState(.05);
  const { translate } = useLocalization();
  const tr = (key: string,fallback: string) => String(translate(`RouteFilter.UI.${key}`,fallback) ?? fallback);
  useEffect(() => { trigger(mod.id,"setSignAdjustment",parameter,step);onAppearance(Boolean(parameter));return () => trigger(mod.id,"setSignAdjustment","",step); },[parameter,step,onAppearance]);
  useEffect(()=>{setParameter("");},[reset]);
  useEffect(() => () => trigger(mod.id,"setUiPointerArea","appearance",false),[]);
  return <div className={styles.appearancePalette} onMouseEnter={() => trigger(mod.id,"setUiPointerArea","appearance",true)} onMouseLeave={() => trigger(mod.id,"setUiPointerArea","appearance",false)}>
    <div className={styles.auxHeader}><strong>{tr("SignTool","Traffic sign")}</strong></div>
    <RoadSignSelector closeToken={reset} onOpenChange={onSignMenuChange}/>
    <div className={styles.parameterList}>{parameters.map(([id,label,index,unit]) => <ParameterInput precision={3} key={id} id={id} label={tr(`Appearance${id}`,label)} value={values[index]??0} unit={unit} active={parameter===id} onActivate={()=>setParameter(id)} onLeave={()=>setParameter("")} onWheelValue={direction=>{const limits:Record<string,[number,number]>={offset:[-.5,3],longitudinal:[-10,10],height:[0,5],rotation:[-180,180]};const [min,max]=limits[id];const next=Math.max(min,Math.min(max,(values[index]??0)+direction*step));trigger(mod.id,"setSignAppearance",id,next);return next;}}/>)}</div>
    <div className={styles.wheelStep}><ParameterInput precision={3} id="wheelStep" label={tr("WheelStep","Wheel step")} value={step} unit={parameter==="rotation"?"°":"m"} active={false} onActivate={()=>setParameter("wheelStep")} onLeave={()=>setParameter("")} onWheelValue={direction=>{const next=Math.max(.001,Math.min(1,step+direction*.001));setStep(next);return next;}} onCommit={value=>{if(value>0)setStep(Math.max(.001,Math.min(1,value)));}}/><div className={styles.segmentedControl}>{[.01,.05,.1,.5].map(value=><button type="button" key={value} className={`${styles.segmentButton} ${step===value?styles.segmentButtonActive:""}`} aria-pressed={step===value} onClick={()=>setStep(value)}>{parameter==="rotation"?`${value}°`:`${value}m`}</button>)}</div></div>
    <div className={styles.paletteActions}><button type="button" className={styles.utilityAction} onClick={()=>trigger(mod.id,"resetSignAppearance")}>{tr("AppearanceReset","Reset position")}</button></div>
  </div>;
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
            const [a,b]=row.points,arrow=navigationArrow(a,b);
            return <g key={index} onMouseDown={event=>event.stopPropagation()} onClick={()=>trigger(mod.id,"selectMapTarget",row.key)}><circle cx={a[0]} cy={a[1]} r={10/view.scale} fill="transparent"/><path d={path(arrow)} fill="#ffd575"/></g>;
          })}
        </g>
      </svg>
      {!geometry.roadCount&&<div className={styles.mapEmpty} role="status"><span>{tr("MapEmpty","Road geometry is not available yet.")}</span><button type="button" className={styles.secondaryAction} onClick={()=>trigger(mod.id,"refreshRestrictionMap")}>{tr("MapRefresh","Refresh")}</button></div>}
    </div>
  </>;
};
