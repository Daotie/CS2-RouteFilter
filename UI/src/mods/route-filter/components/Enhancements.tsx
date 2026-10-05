import React, { useEffect, useMemo, useRef, useState } from "react";
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
export const Enhancements = ({ targetMode, editable, popup, onPopup, reset, targetReady, hasClipboard, onReset, onAppearance }: Props) => {
  const advancedClosed=useValue(advancedClosed$);
  const presetsRaw = useValue(presets$), missing = useValue(missing$), unsupported = useValue(unsupported$);
  const range = useValue(range$), pending = useValue(pending$), status = useValue(status$);
  const [name,setName] = useState(""), [renaming,setRenaming] = useState("");
  const [palette,setPalette] = useState(false);
  const { translate } = useLocalization();
  const tr = (id: string, fallback: string) => String(translate(`RouteFilter.UI.${id}`,fallback) ?? fallback);
  const presets = useMemo(() => presetsRaw.split("\n").filter(Boolean).flatMap(item => { try { return [decodeURIComponent(item)]; } catch { return []; } }),[presetsRaw]);
  useEffect(() => {
    trigger(mod.id,"setRestrictionMapOpen",popup === "Map");
    trigger(mod.id,"setSegmentBrush",popup === "Range");
    return () => { trigger(mod.id,"setRestrictionMapOpen",false); trigger(mod.id,"setSegmentBrush",false); trigger(mod.id,"setUiPointerArea","utility",false); };
  },[popup]);
  useEffect(() => () => { trigger(mod.id,"closeAdvancedInteraction"); onAppearance(false); },[]);
  useEffect(() => { if (advancedClosed || reset) { setName(""); setPalette(false); onAppearance(false); onPopup(""); } },[advancedClosed,reset,onPopup,onAppearance]);
  useEffect(() => {
    if (!popup && !palette) return;
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); setPalette(false); onAppearance(false); onPopup(""); trigger(mod.id,"closeAdvancedInteraction"); } };
    document.addEventListener("keydown",escape,true);
    return () => document.removeEventListener("keydown",escape,true);
  },[popup,palette,onPopup,onAppearance]);
  return <>
    {popup && <Portal><Panel className={popup === "Map" ? styles.mapPanel : styles.auxPanel} contentClassName={styles.auxContent} onMouseEnter={() => trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={() => trigger(mod.id,"setUiPointerArea","utility",false)}>
      <div className={styles.auxHeader}><strong>{tr(popup,popup)}</strong><Button variant="flat" onSelect={() => onPopup("")} aria-label={tr("Close","Close")}>×</Button></div>
      {popup === "Tools" && <div className={styles.toolsMenu}>
        <Button variant="flat" disabled={!targetReady} onSelect={() => { trigger(mod.id,"copyAssetRestriction"); onPopup(""); }}>{tr("LibraryCopy","Copy vehicle restrictions")}</Button>
        <Button variant="flat" disabled={!targetReady || !hasClipboard || !editable} onSelect={() => { trigger(mod.id,"pasteAssetRestriction"); onPopup(""); }}>{tr("LibraryPaste","Paste vehicle restrictions")}</Button>
        <hr />
        <Button variant="flat" onSelect={() => onPopup("Presets")}>{tr("Presets","Presets")}</Button>
        <Button variant="flat" onSelect={() => onPopup("Map")}>{tr("Map","Restriction map")}</Button>
        <Button variant="flat" onSelect={() => { setPalette(true); onAppearance(true); onPopup(""); }}>{tr("Appearance","Sign position adjustment")}</Button>
        <Button variant="flat" onSelect={() => onPopup("SignStyle")}>{tr("SignStyle","Road restriction sign style")}</Button>
        <Button variant="flat" disabled={targetMode !== 1 || !editable} onSelect={() => { trigger(mod.id,"setSegmentBrush",true); onPopup("Range"); }}>{tr("Brush","Batch segment restrictions")}</Button>
        <hr /><Button variant="flat" onSelect={onReset}>{tr("Reset","Reset RouteFilter")}</Button>
      </div>}
      {popup === "SignStyle" && <RoadSignSelector closeToken={0} onPopupOpen={() => {}} />}
      {popup === "Presets" && <>
        <div className={styles.presetList}>{["Trucks","CargoTrucks"].map(id => <Button key={id} variant="flat" disabled={!editable} onSelect={() => trigger(mod.id,"loadBuiltinPreset",id)}>{tr(`Preset${id}`,id === "Trucks" ? "Trucks" : "Cargo trucks")}</Button>)}</div>
        <label>{tr("PresetName","Preset name")}<input maxLength={80} value={name} onChange={event => setName(event.target.value)} /></label>
        <Button variant="flat" disabled={!editable || !name.trim() || presets.length >= 64 && !presets.includes(name.trim())} onSelect={() => trigger(mod.id,"saveUserPreset",name)}>{tr("PresetSave","Save current selection")}</Button>
        <div className={styles.presetList}>{presets.map(preset => <div key={preset}>
          <Button variant="flat" disabled={!editable} onSelect={() => { setName(preset); trigger(mod.id,"loadUserPreset",preset); }}>{preset}</Button>
          <Button variant="flat" onSelect={() => { setRenaming(preset); setName(preset); }}>{tr("PresetRename","Rename")}</Button>
          <Button variant="flat" onSelect={() => trigger(mod.id,"deleteUserPreset",preset)} aria-label={`${tr("PresetDelete","Delete")} ${preset}`}>×</Button>
        </div>)}</div>
        {renaming && <div className={styles.libraryToolbar}><Button variant="flat" disabled={!name.trim() || presets.includes(name.trim()) && name.trim() !== renaming} onSelect={() => { trigger(mod.id,"renameUserPreset",renaming,name); setRenaming(""); }}>{tr("PresetRename","Rename")}</Button><Button variant="flat" onSelect={() => setRenaming("")}>{tr("Cancel","Cancel")}</Button></div>}
        {(missing > 0 || unsupported > 0) && <span role="status">{tr("PresetMissing","Missing assets")}: {missing} · {tr("PresetUnsupported","Unsupported on this target")}: {unsupported}</span>}
        <small>{tr("PresetHint","Choose a preset, then Apply to the selected road.")}</small>
      </>}
      {popup === "Map" && <RestrictionMap />}
      {popup === "Range" && <>
        <p>{tr("BrushHint","Select the start segment, then the end segment. Review the connected chain and confirm.")}</p>
        <span role="status">{tr(`Range${status}`,status)} · {pending} {tr("RangeSegments","segments in preview")}</span>
        <div className={styles.libraryToolbar}>
          <Button variant="flat" disabled={!range || status !== "Ready" || !pending || !editable} onSelect={() => trigger(mod.id,"confirmSegmentRange",false)}>{tr("RangeApply","Apply to range")}</Button>
          <Button variant="flat" disabled={!range || status !== "Ready" || !pending || !editable} onSelect={() => trigger(mod.id,"confirmSegmentRange",true)}>{tr("RangeClear","Clear range restrictions")}</Button>
          <Button variant="flat" onSelect={() => trigger(mod.id,"cancelSegmentRange")}>{tr("Cancel","Cancel")}</Button>
        </div>
        <small>{tr("RangePolicy","Uses the shortest connected chain by segment count; no vehicle route is calculated.")}</small>
      </>}
    </Panel></Portal>}
    {palette && <AppearancePalette onClose={() => { setPalette(false); onAppearance(false); }} />}
  </>;
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
    <div className={styles.auxHeader}><strong>{tr("Appearance","Sign position adjustment")}</strong><Button variant="flat" onSelect={onClose}>×</Button></div>
    <div className={styles.parameterList}>{parameters.map(([id,label,index,unit]) => <Button key={id} variant="flat" selected={parameter === id} onSelect={() => setParameter(id)}><span>{tr(`Appearance${id}`,label)}</span><span>{(values[index] ?? 0).toFixed(2)} {unit}</span></Button>)}</div>
    <div className={styles.libraryToolbar}><span>{tr("WheelStep","Step")}</span>{[.01,.05,.1,.5].map(value => <Button key={value} variant="flat" selected={step === value} onSelect={() => setStep(value)}>{value} m</Button>)}</div>
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
