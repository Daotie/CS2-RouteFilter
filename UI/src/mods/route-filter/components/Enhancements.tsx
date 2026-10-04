import React, { useEffect, useMemo, useRef, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Panel, Portal } from "cs2/ui";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { parseMap, path } from "../mapGeometry";
import styles from "../route-filter.module.scss";

const presets$ = bindValue<string>(mod.id,"userPresets","");
const missing$ = bindValue<number>(mod.id,"presetMissing",0);
const unsupported$ = bindValue<number>(mod.id,"presetUnsupported",0);
const map$ = bindValue<string>(mod.id,"restrictionMap","");
const brush$ = bindValue<boolean>(mod.id,"segmentBrush",false);
const pending$ = bindValue<number>(mod.id,"brushPending",0);
const appearance$ = bindValue<string>(mod.id,"signAppearance","1|0|0|0.04");

type Props = { targetMode: number; editable: boolean; popup: string; onPopup: (value: string) => void; reset: number };
export const Enhancements = ({ targetMode, editable, popup, onPopup, reset }: Props) => {
  const presetsRaw = useValue(presets$), missing = useValue(missing$), unsupported = useValue(unsupported$);
  const brush = useValue(brush$), pending = useValue(pending$), appearance = useValue(appearance$);
  const [name,setName] = useState("");
  const { translate } = useLocalization();
  const tr = (id: string, fallback: string) => String(translate(`RouteFilter.UI.${id}`,fallback) ?? fallback);
  const presets = useMemo(() => presetsRaw.split("\n").filter(Boolean).flatMap(item => { try { return [decodeURIComponent(item)]; } catch { return []; } }),[presetsRaw]);
  const values = useMemo(() => appearance.split("|").map(Number),[appearance]);
  useEffect(() => { trigger(mod.id,"setRestrictionMapOpen",popup === "Map"); return () => { trigger(mod.id,"setRestrictionMapOpen",false); trigger(mod.id,"setUiPointerArea","utility",false); }; },[popup]);
  useEffect(() => { if (reset) { setName(""); onPopup(""); } },[reset,onPopup]);
  useEffect(() => {
    if (!popup) return;
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { event.stopPropagation(); onPopup(""); } };
    document.addEventListener("keydown",escape,true);
    return () => document.removeEventListener("keydown",escape,true);
  },[popup,onPopup]);
  return <>
    <div className={styles.libraryToolbar}>
      {[ ["Presets","Presets"], ["Map","Restriction Map"], ["Appearance","Sign appearance"] ].map(([id,fallback]) => <Button key={id} variant="flat" selected={popup === id} onSelect={() => onPopup(popup === id ? "" : id)}>{tr(id,fallback)}</Button>)}
      <Button variant="flat" selected={brush} disabled={targetMode !== 1 || !editable} onSelect={() => trigger(mod.id,"setSegmentBrush",!brush)}>{tr("Brush","Segment brush")}</Button>
    </div>
    {brush && <div className={styles.directionSummary}>{tr("BrushHint","Drag LMB to apply / RMB to clear; release to commit. All entries.")} ({pending})</div>}
    {popup && <Portal><Panel className={styles.auxPanel} contentClassName={styles.auxContent} onMouseEnter={() => trigger(mod.id,"setUiPointerArea","utility",true)} onMouseLeave={() => trigger(mod.id,"setUiPointerArea","utility",false)}>
      <div className={styles.auxHeader}><strong>{tr(popup,popup)}</strong><Button variant="flat" onSelect={() => onPopup("")} aria-label={tr("Close","Close")}>×</Button></div>
      {popup === "Presets" && <>
        <label>{tr("PresetName","Preset name")}<input maxLength={80} value={name} onChange={event => setName(event.target.value)} /></label>
        <Button variant="flat" disabled={!name.trim() || presets.length >= 64 && !presets.includes(name.trim())} onSelect={() => trigger(mod.id,"saveUserPreset",name)}>{tr(presets.includes(name.trim()) ? "PresetOverwrite" : "PresetSave",presets.includes(name.trim()) ? "Replace with selected assets" : "Save selected assets")}</Button>
        <div className={styles.presetList}>{presets.map(preset => <div key={preset}>
          <Button variant="flat" disabled={!editable} onSelect={() => { setName(preset); trigger(mod.id,"loadUserPreset",preset); }}>{preset}</Button>
          <Button variant="flat" onSelect={() => trigger(mod.id,"deleteUserPreset",preset)} aria-label={`${tr("PresetDelete","Delete")} ${preset}`}>×</Button>
        </div>)}</div>
        <span role="status">{tr("PresetMissing","Missing assets")}: {missing} · {tr("PresetUnsupported","Unsupported on this target")}: {unsupported}</span>
        <small>{tr("PresetHint","Loads assets into the pending selection; target and entry directions stay unchanged. Press Apply when ready.")}</small>
      </>}
      {popup === "Map" && <RestrictionMap />}
      {popup === "Appearance" && <>{[
        ["scale","Main sign scale",.5,2,.05], ["height","Height (m)",0,5,.1], ["offset","Lateral offset (m)",-.5,3,.1], ["spacing","Plate gap (m)",.02,.2,.01]
      ].map(([id,fallback,min,max,step],index) => <label key={String(id)}>{tr(`Appearance${id}`,String(fallback))} <span>{values[index]}</span>
        <input type="range" min={Number(min)} max={Number(max)} step={Number(step)} value={values[index]} onChange={event => trigger(mod.id,"setSignAppearance",String(id),Number(event.target.value))} />
      </label>)}<small>{tr("AppearanceHint","RF-Plate stays 0.800 × 0.250 × 0.020 m. Tall stacks lift the assembly to retain ground clearance.")}</small></>}
    </Panel></Portal>}
  </>;
};

const RestrictionMap = () => {
  const raw = useValue(map$);
  const model = useMemo(() => parseMap(raw),[raw]);
  const [zoom,setZoom] = useState(1), [pan,setPan] = useState({x:0,y:0});
  const drag = useRef<{x:number;y:number;panX:number;panY:number;scaleX:number;scaleY:number} | null>(null);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(`RouteFilter.UI.${key}`,fallback) ?? fallback);
  const bounds = model.bounds;
  const viewBox = `${bounds.x + pan.x + bounds.width*(1-1/zoom)/2} ${bounds.y + pan.y + bounds.height*(1-1/zoom)/2} ${bounds.width/zoom} ${bounds.height/zoom}`;
  return <>
    <div className={styles.libraryToolbar}><Button variant="flat" onSelect={() => setZoom(value => Math.min(16,value*1.5))}>+</Button><Button variant="flat" onSelect={() => setZoom(value => Math.max(1,value/1.5))}>−</Button><Button variant="flat" onSelect={() => { setZoom(1);setPan({x:0,y:0}); }}>{tr("MapFit","Fit city")}</Button><span>{model.restrictions.length} {tr("MapTargets","targets")}</span></div>
    <svg className={styles.restrictionMap} viewBox={viewBox} onMouseDown={event => {
      if (event.button !== 0) return;
      const rect = event.currentTarget.getBoundingClientRect();
      const scale = Math.max(bounds.width/rect.width,bounds.height/rect.height)/zoom;
      drag.current = {x:event.clientX,y:event.clientY,panX:pan.x,panY:pan.y,scaleX:scale,scaleY:scale};
    }} onMouseMove={event => { const start = drag.current; if (start && event.buttons === 1) setPan({x:start.panX-(event.clientX-start.x)*start.scaleX,y:start.panY-(event.clientY-start.y)*start.scaleY}); }} onMouseUp={() => { drag.current = null; }} onMouseLeave={() => { drag.current = null; }}>
      <path d={model.background} fill="none" stroke="#576571" strokeWidth={1.5} vectorEffect="non-scaling-stroke" />
      {model.restrictions.map(row => <g key={row.key} onMouseDown={event => event.stopPropagation()} onClick={() => trigger(mod.id,"selectMapTarget",row.key)}>
        <title>{`${row.key}: ${row.assets} ${tr("MapAssets","assets")}, ${row.directions} ${tr("EntryDirections","restricted entries")}`}</title>
        {row.points.length === 1 ? <circle cx={row.points[0][0]} cy={row.points[0][1]} r={Math.max(bounds.width,bounds.height)/150/zoom} fill="#ff8877" /> : <><path d={path(row.points)} fill="none" stroke="transparent" strokeWidth={12} vectorEffect="non-scaling-stroke"/><path d={path(row.points)} fill="none" stroke="#ff8877" strokeWidth={3} vectorEffect="non-scaling-stroke" /></>}
      </g>)}
    </svg>
    <select aria-label={tr("MapSelect","Select restricted target")} value="" onChange={event => trigger(mod.id,"selectMapTarget",event.target.value)}><option value="">{tr("MapSelect","Select restricted target")}</option>{model.restrictions.map(row => <option key={row.key} value={row.key}>{row.key} · {row.assets} · {row.directions}</option>)}</select>
    <small>{tr("MapHint","Red targets have restrictions. Click to edit; drag the background to pan.")}</small>
  </>;
};
