import React, { useState, useMemo } from "react";
import { Button, ConfirmationDialog, Panel, Portal, Tooltip } from "cs2/ui";
import { bindValue, trigger, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { VehicleAsset, filteredAssetIds } from "../model";
import { ActionBar } from "./ActionBar";
import { AssetList } from "./AssetList";
import { AssetSearch } from "./AssetSearch";
import { PanelHeader } from "./PanelHeader";
import { TargetSelector } from "./TargetSelector";
import { RoadSignSelector } from "./RoadSignSelector";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";

type Props = {
  resetCompleted: number;
  buildId: string;
  configurationEditable: boolean;
  entryCount: number;
  enabledEntryCount: number;
  entriesSupported: boolean;
  showEntryDirections: boolean;
  targetMode: number;
  selectedTargetKind: number;
  selectedCount: number;
  assetCount: number;
  roots: VehicleAsset[];
  childrenByParent: Map<number, VehicleAsset[]>;
  selected: Set<number>;
  expanded: Set<number>;
  search: string;
  labels: Record<string, string>;
  onClose: () => void;
  onTargetModeChange: (mode: number) => void;
  onCancelTarget: () => void;
  onSearchChange: (value: string) => void;
  onToggleAsset: (asset: VehicleAsset, hasChildren: boolean) => void;
  onExpandAsset: (id: number) => void;
  onAllowAll: () => void;
  onForbidAll: () => void;
  onApply: () => void;
  onClear: () => void;
  onRefresh: () => void;
  onPointerEnter: () => void;
  onPointerLeave: () => void;
};

const favorites$ = bindValue<string>(mod.id, "favoriteAssetIds", "");
const recent$ = bindValue<string>(mod.id, "recentAssetIds", "");
const clipboard$ = bindValue<boolean>(mod.id, "hasAssetClipboard", false);

export const RouteFilterPanel = (props: Props) => {
  const favoritesRaw = useValue(favorites$), recentRaw = useValue(recent$), hasClipboard = useValue(clipboard$);
  const favorites = useMemo(() => new Set(favoritesRaw.split(",").filter(Boolean).map(Number)), [favoritesRaw]);
  const recent = useMemo(() => recentRaw.split(",").filter(Boolean).map(Number), [recentRaw]);
  const [view, setView] = useState("all");
  const [confirmReset, setConfirmReset] = useState(false);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);
  const targetReady = props.selectedTargetKind !== 0;
  const visible = useMemo(() => {
    const matches = (id: number) => view === "all" || (view === "favorites" ? favorites.has(id) : recent.includes(id));
    const children = new Map<number, VehicleAsset[]>();
    for (const [id, items] of props.childrenByParent) children.set(id, items.filter(item => matches(item.id)));
    const roots = props.roots.filter(item => matches(item.id) || (children.get(item.id)?.length ?? 0) > 0);
    if (view === "recent") roots.sort((a, b) => recent.indexOf(a.id) - recent.indexOf(b.id));
    return { roots, children, matches };
  }, [props.roots, props.childrenByParent, view, favorites, recent]);
  const bulk = (forbidden: boolean) => {
    const ids = filteredAssetIds(visible.roots, visible.children, props.search.trim().toLocaleLowerCase()).filter(visible.matches);
    trigger(mod.id, "setFilteredAssetSelection", ids.join(","), forbidden);
  };
  return <Portal>
    <Panel id="routefilter-panel" data-build-id={props.buildId} className={styles.panel} contentClassName={styles.panelContent} onMouseEnter={props.onPointerEnter} onMouseLeave={props.onPointerLeave}>
      <PanelHeader title={props.labels.title} version={mod.version} buildId={props.buildId} closeLabel={props.labels.close} onClose={props.onClose} />
      {!props.configurationEditable && <div className={styles.resetStatus} role="status">{tr("RouteFilter.UI.PersistenceLocked", "Save data is incompatible or damaged. Editing is locked; Reset removes RouteFilter configuration.")}</div>}
      <TargetSelector mode={props.targetMode} nodeLabel={props.labels.node} segmentLabel={props.labels.segment} status={props.labels.targetStatus} targetReady={targetReady} onModeChange={props.onTargetModeChange} />
      {props.showEntryDirections && <Tooltip tooltip={tr(props.entriesSupported ? "RouteFilter.UI.EntryDirectionsHint" : "RouteFilter.UI.EntryDirectionsUnsupported", props.entriesSupported ? "Click approach bars, then Apply." : "Entries cannot be separated reliably; all-entry restrictions remain active.")}>
        <div className={styles.directionSummary}>
          <span>{tr("RouteFilter.UI.EntryDirections", "Restricted entries")} {props.entriesSupported ? `${props.enabledEntryCount} / ${props.entryCount}` : tr("RouteFilter.UI.EntryDirectionsUnavailable", "All entries (unavailable)")}</span>
          <Button variant="flat" disabled={!props.configurationEditable} onSelect={() => trigger(mod.id, "allEntryDirections")}>
            {tr("RouteFilter.UI.AllEntries", "All entries")}
          </Button>
        </div>
      </Tooltip>}
      <div className={styles.listHeading}>
        <div><strong>{props.labels.assetTitle}</strong><span>{props.labels.assetSubtitle}</span></div>
        <small>{props.selectedCount} / {props.assetCount}</small>
      </div>
      <div className={styles.libraryToolbar}>
        {[ ["all", tr("RouteFilter.UI.LibraryAll", "All")], ["favorites", tr("RouteFilter.UI.LibraryFavorites", "Favorites")], ["recent", tr("RouteFilter.UI.LibraryRecent", "Recent")] ].map(([id, label]) => <Button key={id} variant="flat" selected={view === id} onSelect={() => setView(id)}>{label}</Button>)}
        <Button variant="flat" disabled={!targetReady} onSelect={() => trigger(mod.id, "copyAssetRestriction")}>{tr("RouteFilter.UI.LibraryCopy", "Copy")}</Button>
        <Button variant="flat" disabled={!targetReady || !hasClipboard || !props.configurationEditable} onSelect={() => trigger(mod.id, "pasteAssetRestriction")}>{tr("RouteFilter.UI.LibraryPaste", "Paste")}</Button>
      </div>
      <AssetSearch value={props.search} placeholder={props.labels.search} onChange={props.onSearchChange} />
      <AssetList favorites={favorites} onFavorite={id => trigger(mod.id, "toggleFavoriteAsset", id)} favoriteLabel={tr("RouteFilter.UI.LibraryFavoriteToggle", "Toggle favorite")} roots={visible.roots} childrenByParent={visible.children} selected={props.selected} expanded={props.expanded} searchTerm={props.search.trim().toLocaleLowerCase()} emptyLabel={props.labels.empty} trailerLabel={props.labels.trailer} expandLabel={props.labels.expand} collapseLabel={props.labels.collapse} roadGroupLabel={props.labels.roadGroup} railGroupLabel={props.labels.railGroup} onToggle={props.onToggleAsset} onExpand={props.onExpandAsset} />
      <ActionBar allowAllLabel={props.labels.allowAll} forbidAllLabel={props.labels.forbidAll} applyLabel={props.labels.apply} clearLabel={props.labels.clear} refreshLabel={props.labels.refresh} targetReady={targetReady && props.configurationEditable} onAllowAll={() => bulk(false)} onForbidAll={() => bulk(true)} onApply={props.onApply} onClear={props.onClear} onRefresh={props.onRefresh} />
      <div className={styles.utilityFooter}>
        <Tooltip tooltip={props.buildId}><span className={styles.buildLabel}>{props.buildId.split("-").slice(-4).join("-")}</span></Tooltip>
        <Button variant="flat" className={styles.utilityAction} onSelect={() => setConfirmReset(true)}>
          <img className={styles.utilityIcon} src={icons.reset} alt="" />
          {tr("RouteFilter.UI.Reset", "Reset RouteFilter")}
        </Button>
      </div>
      <RoadSignSelector />
      {props.resetCompleted > 0 && <div className={styles.resetStatus} role="status">{tr("RouteFilter.UI.ResetCompleted", "Reset completed. Unknown lane and path state was left unchanged.")}</div>}
    </Panel>
    {confirmReset && <ConfirmationDialog
      title={tr("RouteFilter.UI.Reset", "Reset RouteFilter")}
      message={tr("RouteFilter.Settings.ResetConfirmation", "This removes all RouteFilter restrictions and safely identifiable runtime state. This cannot be undone. State with uncertain ownership is not modified.")}
      confirm={tr("RouteFilter.UI.ConfirmReset", "Confirm reset")}
      cancel={tr("RouteFilter.UI.Cancel", "Cancel")}
      onConfirm={() => { setConfirmReset(false); trigger(mod.id, "resetRouteFilterConfirmed"); }}
      onCancel={() => setConfirmReset(false)}
    />}
  </Portal>;
};
