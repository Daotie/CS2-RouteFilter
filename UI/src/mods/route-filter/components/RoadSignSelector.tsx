import React, { useMemo, useState, useRef, useEffect, useCallback } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Portal } from "cs2/ui";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { icons } from "../assets";
import { SelectionCheckbox } from "./SelectionCheckbox";
import { ChevronIcon } from "./ChevronIcon";
import styles from "../route-filter.module.scss";

const profile$ = bindValue<string>(mod.id,"signageProfile","AUTO");
const profileResolved$ = bindValue<string>(mod.id,"signageProfileResolved","GENERIC_EUROPE");
const profileFallback$ = bindValue<boolean>(mod.id,"signageProfileFallback",false);
const profiles = ["AUTO","GENERIC_EUROPE","US"] as const;
const profileNames: Record<string,string> = {AUTO:"Auto",GENERIC_EUROPE:"Generic Europe",CN:"Mainland China",UK:"United Kingdom",US:"United States",MIXED:"Resolved per entry road theme"};
const forcePlate$=bindValue<boolean>(mod.id,"forceSupplementaryPlate",false);
const catalog$ = bindValue<string>(mod.id, "roadSignCatalog", "");
const selection$ = bindValue<string>(mod.id, "roadSignSelection", "");
const resolved$ = bindValue<string>(mod.id, "roadSignResolved", "");
const unavailable$ = bindValue<boolean>(mod.id, "roadSignUnavailable", false);

export const RoadSignSelector = ({ closeToken = 0, onPopupOpen, onOpenChange }: {closeToken?: number; onPopupOpen?: () => void; onOpenChange?: (open:boolean)=>void}) => {
  const forcePlate = useValue(forcePlate$);
  const catalog = useValue(catalog$);
  const profile = useValue(profile$);
  const profileResolved = useValue(profileResolved$);
  const profileFallback = useValue(profileFallback$);
  const selection = useValue(selection$);
  const resolved = useValue(resolved$);
  const unavailable = useValue(unavailable$);
  const [popup, setPopup] = useState<{ left: number; top: number; width: number; maxHeight: number; rowHeight: number } | null>(null);
  const [search, setSearch] = useState("");
  const [scrollTop, setScrollTop] = useState(0);
  const scroller = useRef<HTMLDivElement>(null);
  const anchor = useRef<HTMLDivElement>(null);
  const menu = useRef<HTMLDivElement>(null);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);
  const name = (value: string) => {
    const fallback = value.replace(/[_-]+/g," ").replace(/([a-z])([A-Z])/g,"$1 $2").replace(/([A-Za-z])(\d+)/g,"$1 $2").trim();
    return String(translate(`Assets.NAME[${value}]`,fallback) ?? fallback);
  };
  const options = useMemo(() => catalog.split("\n").filter(Boolean).flatMap(line => {
    try {
      const [id, icon] = line.split("|").map(decodeURIComponent);
      return id ? [{ id, icon: icon || icons.prohibition }] : [];
    } catch { return []; }
  }), [catalog]);
  const close = useCallback(() => { setPopup(null); onOpenChange?.(false); trigger(mod.id, "setUiPointerArea", "sign", false); },[onOpenChange]);
  useEffect(() => { close(); },[closeToken,close]);
  useEffect(() => () => { trigger(mod.id,"setUiPointerArea","sign",false); },[]);
  useEffect(() => {
    if (!popup) return;
    const outside = (event: MouseEvent) => {
      if (!anchor.current?.contains(event.target as Node) && !menu.current?.contains(event.target as Node)) close();
    };
    const escape = (event: KeyboardEvent) => { if (event.key === "Escape") { event.stopPropagation(); close(); trigger(mod.id,"closeAdvancedInteraction"); } };
    document.addEventListener("mousedown", outside);
    document.addEventListener("keydown", escape, true);
    window.addEventListener("resize", close);
    return () => { document.removeEventListener("mousedown", outside); document.removeEventListener("keydown", escape, true); window.removeEventListener("resize", close); };
  }, [popup,close]);
  const toggle = () => {
    if (popup) { close(); return; }
    const rect = anchor.current?.getBoundingClientRect();
    if (!rect) return;
    const rowHeight = Math.max(1, rect.height);
    const gap=rowHeight/36*8;
    const width = Math.min(rect.width, Math.max(1, window.innerWidth - 24));
    const left = rect.right+gap+width<=window.innerWidth-12 ? rect.right+gap : Math.max(12,rect.left-width-gap);
    const top=Math.max(12,Math.min(rect.top,window.innerHeight-rowHeight*2-12));
    const maxHeight = Math.max(rowHeight*2,Math.min(rowHeight*6,window.innerHeight-top-12));
    setSearch(""); setScrollTop(0);
    onPopupOpen?.();onOpenChange?.(true);
    setPopup({ left, top, width, maxHeight, rowHeight });
  };
  const select = (value: string) => { trigger(mod.id, "selectRoadSignPrefab", value); close(); };
  const selected = useMemo(() => options.find(option => option.id === selection), [options, selection]);
  const automatic = useMemo(() => options.find(option => option.id === resolved), [options, resolved]);
  const auto = tr("RouteFilter.UI.RoadSignAuto", "Auto — Match Road Theme");
  const query = search.trim().toLocaleLowerCase();
  const namedOptions = useMemo(() => options.map(option => ({ ...option, displayName: name(option.id) })), [options, translate]);
  const filtered = useMemo(() => namedOptions.filter(option => !query || `${option.displayName} ${option.id}`.toLocaleLowerCase().includes(query)), [namedOptions, query]);
  const firstRow = popup ? Math.max(0, Math.floor(scrollTop / popup.rowHeight) - 2) : 0;
  const visibleRows = popup ? filtered.slice(firstRow, firstRow + Math.ceil(popup.maxHeight / popup.rowHeight) + 4) : [];
  const changeSearch = (value: string) => { setSearch(value); setScrollTop(0); if (scroller.current) scroller.current.scrollTop = 0; };
  const row = (icon: string, text: string) => <span className={styles.signOption}>
    <img src={icon} alt="" onError={event => { event.currentTarget.onerror = null; event.currentTarget.src = icons.prohibition; }} />
    <span title={text}>{text}</span>
  </span>;
  return <div className={styles.signSelector}>
    <div className={styles.signTitle}>{tr("RouteFilter.UI.RoadSignStyle", "Road restriction sign style")}</div>
    <div className={styles.signTitle}>{tr("RouteFilter.UI.SignageProfile","Signage profile")}</div>
    <div className={styles.profileChoices}>{profiles.map(id=><button type="button" key={id} className={profile===id?styles.profileChoiceActive:styles.profileChoice} aria-pressed={profile===id} onClick={()=>trigger(mod.id,"selectSignageProfile",id)}>{tr(`RouteFilter.UI.Profile.${id}`,profileNames[id])}</button>)}</div>
    {profile==="AUTO" && <div className={styles.signResolved}>{tr("RouteFilter.UI.RoadSignResolved","Auto resolved")}: {tr(`RouteFilter.UI.Profile.${profileResolved}`,profileNames[profileResolved] ?? profileNames.GENERIC_EUROPE)}</div>}
    {profileFallback && <div className={styles.signResolved}>{tr("RouteFilter.UI.ProfileFallback","Using compatible sign.")}</div>}
    <div ref={anchor}>
      <Button variant="flat" className={styles.signToggle} onSelect={toggle} aria-expanded={!!popup} aria-haspopup="listbox">
        {row(selected?.icon ?? automatic?.icon ?? icons.prohibition, selection ? name(selection) : auto)}
        <ChevronIcon open={!!popup}/>
      </Button>
    </div>
    {selection && <div className={styles.forcePlate}><SelectionCheckbox selected={forcePlate} label={tr("RouteFilter.UI.ForcePlate","Force supplementary plate")} onSelect={()=>trigger(mod.id,"setForceSupplementaryPlate",!forcePlate)}/><span>{tr("RouteFilter.UI.ForcePlate","Force supplementary plate")}</span></div>}
    {popup && <Portal><div ref={menu} className={styles.signDropdownPopup} style={{ left: popup.left, top: popup.top, width: popup.width, height: popup.maxHeight, maxHeight: popup.maxHeight }} role="listbox"
      onMouseEnter={() => trigger(mod.id, "setUiPointerArea", "sign", true)} onMouseLeave={() => trigger(mod.id, "setUiPointerArea", "sign", false)}>
      {options.length > 12 && <input className={styles.signSearch} value={search} onChange={event => changeSearch(event.target.value)} placeholder={tr("RouteFilter.UI.RoadSignSearch", "Search sign assets…")} />}
      <Button variant="flat" className={`${styles.signItem} ${!selection ? styles.signItemSelected : ""}`} onSelect={() => select("")}>{row(icons.prohibition, auto)}</Button>
      <div ref={scroller} className={styles.signScroll} onScroll={event => setScrollTop(event.currentTarget.scrollTop)}>
        <div style={{ position: "relative", height: filtered.length * popup.rowHeight }}>
          {visibleRows.map((option, index) => <div key={option.id} style={{ position: "absolute", left: 0, right: 0, top: (firstRow + index) * popup.rowHeight, height: popup.rowHeight }}>
            <Button variant="flat" className={`${styles.signItem} ${selection === option.id ? styles.signItemSelected : ""}`} onSelect={() => select(option.id)}>
              {row(option.icon, option.displayName)}
            </Button>
          </div>)}
        </div>
      </div>
    </div></Portal>}
    {!selection && <div className={styles.signResolved}>{resolved ? `${tr("RouteFilter.UI.RoadSignResolved", "Auto resolved")}: ${name(resolved)}` : tr("RouteFilter.UI.RoadSignUnavailable", "No compatible sign")}</div>}
    {unavailable && <div className={styles.signResolved}>{tr("RouteFilter.UI.RoadSignMissing", "Using Auto style")}</div>}
  </div>;
};
