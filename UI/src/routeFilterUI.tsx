import React, { useEffect, useMemo, useRef, useState } from "react";
import classNames from "classnames";
import { bindValue, trigger, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { Button, Portal } from "cs2/ui";
import mod from "mod.json";
import icon from "./routefilter.svg";
import styles from "./routeFilterUI.module.scss";

type VehicleAsset = {
  id: number; name: string; mode: number; maxSpeed: number; acceleration: number;
  braking: number; parentId: number; trailer: boolean;
};

const toolActive$ = bindValue<boolean>(mod.id, "toolActive", false);
const targetMode$ = bindValue<number>(mod.id, "targetMode", 0);
const targetTransport$ = bindValue<number>(mod.id, "targetTransport", 0);
const selectedTargetKind$ = bindValue<number>(mod.id, "selectedTargetKind", 0);
const assetCatalog$ = bindValue<string>(mod.id, "assetCatalog", "");
const selectedAssetIds$ = bindValue<string>(mod.id, "selectedAssetIds", "");

const parseCatalog = (raw: string): VehicleAsset[] => raw.split("\n").reduce<VehicleAsset[]>((result, line) => {
  const part = line.split("|");
  if (part.length !== 8) return result;
  const id = Number(part[0]);
  if (!Number.isInteger(id)) return result;
  let name = part[1];
  try { name = decodeURIComponent(name); } catch { /* retain the technical name */ }
  result.push({ id, name, mode: Number(part[2]), maxSpeed: Number(part[3]), acceleration: Number(part[4]), braking: Number(part[5]), parentId: Number(part[6]), trailer: part[7] === "1" });
  return result;
}, []);

const AssetGlyph = ({ mode, trailer }: { mode: number; trailer: boolean }) => <svg className={styles.glyph} viewBox="0 0 24 24">
  {mode === 2 ? <>
    <path d="M5 15V7c0-2 2-3 7-3s7 1 7 3v8c0 2-1 3-3 3H8c-2 0-3-1-3-3Z" />
    <path d="M8 8h8M8 14h.1M16 14h.1M8 18l-2 3M16 18l2 3M7 21h10" />
  </> : <>
    <path d={trailer ? "M3 8h13v8H3zM16 11h3l2 3v2h-5z" : "M4 9h3l2-3h7l3 3 2 2v5H3v-5Z"} />
    <path d="M7 16a2 2 0 1 0 0 .1M17 16a2 2 0 1 0 0 .1" />
  </>}
</svg>;

export const RouteFilterUI = () => {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const [expanded, setExpanded] = useState<Set<number>>(() => new Set());
  const [hovered, setHovered] = useState<VehicleAsset | null>(null);
  const [page, setPage] = useState(0);
  const anchor = useRef<HTMLDivElement | null>(null);
  const active = useValue(toolActive$);
  const targetMode = useValue(targetMode$);
  const targetTransport = useValue(targetTransport$);
  const selectedTargetKind = useValue(selectedTargetKind$);
  const catalogRaw = useValue(assetCatalog$);
  const selectedRaw = useValue(selectedAssetIds$);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);
  const assets = useMemo(() => parseCatalog(catalogRaw), [catalogRaw]);
  const selected = useMemo(() => new Set(selectedRaw.split(",").map(Number).filter(Number.isInteger)), [selectedRaw]);
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const relevant = useMemo(() => assets.filter(asset => targetTransport === 0 || (asset.mode & targetTransport) !== 0), [assets, targetTransport]);
  const children = useMemo(() => {
    const map = new Map<number, VehicleAsset[]>();
    relevant.forEach(asset => { if (asset.parentId) map.set(asset.parentId, [...(map.get(asset.parentId) ?? []), asset]); });
    return map;
  }, [relevant]);
  const roots = useMemo(() => relevant.filter(asset => !asset.parentId || !relevant.some(candidate => candidate.id === asset.parentId)).filter(asset => {
    if (!normalizedSearch) return true;
    return asset.name.toLocaleLowerCase().includes(normalizedSearch) || (children.get(asset.id) ?? []).some(child => child.name.toLocaleLowerCase().includes(normalizedSearch));
  }), [relevant, children, normalizedSearch]);
  const selectedRelevant = relevant.filter(asset => selected.has(asset.id)).length;
  const pageSize = 30;
  const pageCount = Math.max(1, Math.ceil(roots.length / pageSize));
  const pageIndex = Math.min(page, pageCount - 1);
  const visibleRoots = roots.slice(pageIndex * pageSize, (pageIndex + 1) * pageSize);

  useEffect(() => setPage(0), [search, targetTransport]);
  useEffect(() => {
    if (!open) return;
    return () => { trigger(mod.id, "setPointerOverUi", false); };
  }, [open]);

  const togglePanel = () => {
    setOpen(!open);
    if (!open && !active) trigger(mod.id, "toggleTool");
  };
  const toggleExpanded = (id: number) => setExpanded(current => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  const renderAsset = (asset: VehicleAsset, child = false) => {
    const childAssets = children.get(asset.id) ?? [];
    const isExpanded = expanded.has(asset.id) || normalizedSearch.length > 0;
    const groupIds = [asset.id, ...childAssets.map(item => item.id)];
    const selectedCount = groupIds.filter(id => selected.has(id)).length;
    const partial = childAssets.length > 0 && selectedCount > 0 && selectedCount < groupIds.length;
    return <React.Fragment key={asset.id}>
      <div className={classNames(styles.assetRow, { [styles.child]: child, [styles.forbidden]: selected.has(asset.id), [styles.partial]: partial })}
        onMouseEnter={() => setHovered(asset)} onMouseOver={() => setHovered(asset)}>
        {childAssets.length > 0 ? <button type="button" className={styles.expand} onClick={() => toggleExpanded(asset.id)}>{isExpanded ? "⌄" : "›"}</button> : <span className={styles.expandSpacer} />}
        <button type="button" aria-label={selected.has(asset.id) ? "Allow asset" : "Forbid asset"}
          className={classNames(styles.check, { [styles.checked]: selected.has(asset.id), [styles.partialCheck]: partial })}
          onClick={() => childAssets.length ? trigger(mod.id, "toggleAssetGroup", asset.id, !isExpanded) : trigger(mod.id, "toggleAsset", asset.id)}>
          {partial ? "−" : selected.has(asset.id) ? "×" : ""}
        </button>
        <div className={styles.assetMain}>
          <AssetGlyph mode={asset.mode} trailer={asset.trailer} /><em>{asset.name}</em>
          {childAssets.length > 0 && <small>{childAssets.length + 1}</small>}
        </div>
      </div>
      {childAssets.length > 0 && isExpanded && childAssets.filter(item => !normalizedSearch || item.name.toLocaleLowerCase().includes(normalizedSearch)).map(item => renderAsset(item, true))}
    </React.Fragment>;
  };

  const targetLabel = targetTransport === 1 ? tr("RouteFilter.UI.RoadAssets", "Road vehicles") : targetTransport === 2 ? tr("RouteFilter.UI.RailAssets", "Rail vehicles") : targetTransport === 3 ? tr("RouteFilter.UI.MixedAssets", "Road and rail vehicles") : tr("RouteFilter.UI.HoverTarget", "Hover a target to filter assets");
  const selectedTargetLabel = selectedTargetKind === 1 ? tr("RouteFilter.UI.NodeSelected", "Node selected") : selectedTargetKind === 2 ? tr("RouteFilter.UI.SegmentSelected", "Segment selected") : tr("RouteFilter.UI.SelectTarget", "Click a node or segment on the map first");

  return <div ref={anchor}>
    <Button src={icon} variant="floating" className={classNames(styles.toggle, { [styles.selected]: active })} onSelect={togglePanel} />
    {open && <Portal><section className={styles.panel} onMouseEnter={() => trigger(mod.id, "setPointerOverUi", true)} onMouseLeave={() => trigger(mod.id, "setPointerOverUi", false)}>
      <header><div><strong>{tr("RouteFilter.UI.Title", "RouteFilter")}</strong><small>v{mod.version}</small></div><button onClick={() => setOpen(false)}>×</button></header>
      <div className={styles.warning}><strong>{tr("RouteFilter.UI.ForbiddenTitle", "Forbidden assets")}</strong><span>{tr("RouteFilter.UI.ForbiddenHint", "Selected assets will be blocked. Unselected assets remain allowed.")}</span></div>
      <div className={styles.modes}>
        <button className={classNames({ [styles.modeSelected]: targetMode === 0 })} onClick={() => trigger(mod.id, "setTargetMode", 0)}>{tr("RouteFilter.UI.Node", "Node")}</button>
        <button className={classNames({ [styles.modeSelected]: targetMode === 1 })} onClick={() => trigger(mod.id, "setTargetMode", 1)}>{tr("RouteFilter.UI.Segment", "Segment")}</button>
      </div>
      <button className={styles.toolButton} onClick={() => trigger(mod.id, "toggleTool")}>{active ? tr("RouteFilter.UI.Active", "Tool active") : tr("RouteFilter.UI.Inactive", "Open tool")}</button>
      <div className={classNames(styles.targetStatus, { [styles.targetReady]: selectedTargetKind !== 0 })}>
        <strong>{selectedTargetLabel}</strong><span>{tr("RouteFilter.UI.SelectionHint", "Left-click selects; right-click cancels the selection.")}</span>
      </div>
      <div className={styles.assetHeader}><strong>{targetLabel}</strong><small>{selectedRelevant} / {relevant.length} {tr("RouteFilter.UI.ForbiddenCount", "forbidden")}</small></div>
      <input className={styles.search} value={search} onChange={event => setSearch(event.target.value)} placeholder={tr("RouteFilter.UI.Search", "Search vehicle assets")} />
      <div className={styles.details}>{hovered ? <>
        <div><AssetGlyph mode={hovered.mode} trailer={hovered.trailer} /><strong>{hovered.name}</strong></div>
        <span>{tr("RouteFilter.UI.MaxSpeed", "Maximum speed")}: <b>{hovered.maxSpeed} km/h</b></span>
        <span>{tr("RouteFilter.UI.Acceleration", "Acceleration")}: <b>{hovered.acceleration}</b></span>
        <span>{tr("RouteFilter.UI.Braking", "Braking")}: <b>{hovered.braking}</b></span>
      </> : <span>{tr("RouteFilter.UI.HoverInfo", "Hover an asset to view its base parameters.")}</span>}</div>
      <div className={styles.actions}>
        <button onClick={() => trigger(mod.id, "selectAllAssets", 0)}>{tr("RouteFilter.UI.ForbidAll", "Forbid all assets")}</button>
        <button onClick={() => trigger(mod.id, "selectNoAssets", 0)}>{tr("RouteFilter.UI.AllowAll", "Allow all assets")}</button>
        <button className={styles.refresh} onClick={() => trigger(mod.id, "refreshAssets")}>↻</button>
      </div>
      <div className={styles.assetList} onWheel={event => { event.currentTarget.scrollTop += event.deltaY; event.stopPropagation(); }} onMouseLeave={() => setHovered(null)}>
        {visibleRoots.map(asset => renderAsset(asset))}
        {roots.length === 0 && <p className={styles.empty}>{tr("RouteFilter.UI.Empty", "No matching vehicle assets")}</p>}
      </div>
      <div className={styles.pager}>
        <button onClick={() => setPage(Math.max(0, pageIndex - 1))} disabled={pageIndex === 0}>‹</button>
        <span>{pageIndex + 1} / {pageCount} · {selected.size} / {assets.length}</span>
        <button onClick={() => setPage(Math.min(pageCount - 1, pageIndex + 1))} disabled={pageIndex + 1 >= pageCount}>›</button>
      </div>
      <div className={styles.applyActions}>
        <button className={styles.applyButton} disabled={selectedTargetKind === 0} onClick={() => selectedTargetKind !== 0 && trigger(mod.id, "applySelection")}>{tr("RouteFilter.UI.ApplyToTarget", "Apply list to selected target")}</button>
        <button disabled={selectedTargetKind === 0} onClick={() => selectedTargetKind !== 0 && trigger(mod.id, "clearSelectedRestriction")}>{tr("RouteFilter.UI.ClearTarget", "Clear target restrictions")}</button>
        <button disabled={selectedTargetKind === 0} onClick={() => selectedTargetKind !== 0 && trigger(mod.id, "cancelSelection")}>{tr("RouteFilter.UI.CancelTarget", "Cancel selection")}</button>
      </div>
    </section></Portal>}
  </div>;
};
