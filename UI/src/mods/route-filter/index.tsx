import React, { useEffect, useMemo, useRef, useState } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { FloatingButton } from "./components/FloatingButton";
import { RouteFilterPanel } from "./components/RouteFilterPanel";
import { filteredAssetIds, parseCatalog, VehicleAsset } from "./model";

const toolActive$ = bindValue<boolean>(mod.id, "toolActive", false);
const targetMode$ = bindValue<number>(mod.id, "targetMode", 0);
const targetTransport$ = bindValue<number>(mod.id, "targetTransport", 0);
const selectedTargetKind$ = bindValue<number>(mod.id, "selectedTargetKind", 0);
const assetCatalog$ = bindValue<string>(mod.id, "assetCatalog", "");
const selectedAssetIds$ = bindValue<string>(mod.id, "selectedAssetIds", "");
const resetCompleted$ = bindValue<number>(mod.id, "resetCompleted", 0);
const panelClose$ = bindValue<number>(mod.id, "panelClose", 0);
const buildId$ = bindValue<string>(mod.id, "buildId", "unknown");
const configurationEditable$ = bindValue<boolean>(mod.id, "configurationEditable", true);
const entryCount$ = bindValue<number>(mod.id, "entryDirectionCount", 0);
const enabledEntryCount$ = bindValue<number>(mod.id, "enabledEntryDirectionCount", 0);
const entriesSupported$ = bindValue<boolean>(mod.id, "entryDirectionsSupported", false);

export const RouteFilterShell = () => {
  const [search, setSearch] = useState("");
  const [expanded, setExpanded] = useState<Set<number>>(() => new Set());
  const [panelOpen, setPanelOpen] = useState(false);
  const previousToolActive = useRef(false);
  const active = useValue(toolActive$);
  const targetMode = useValue(targetMode$);
  const targetTransport = useValue(targetTransport$);
  const selectedTargetKind = useValue(selectedTargetKind$);
  const catalogRaw = useValue(assetCatalog$);
  const selectedRaw = useValue(selectedAssetIds$);
  const resetCompleted = useValue(resetCompleted$);
  const panelClose = useValue(panelClose$);
  useEffect(() => { if (panelClose) setPanelOpen(false); }, [panelClose]);
  useEffect(() => {
    if (panelOpen) trigger(mod.id, "activateTool");
  }, [panelOpen]);
  const buildId = useValue(buildId$);
  const configurationEditable = useValue(configurationEditable$);
  const entryCount = useValue(entryCount$);
  const enabledEntryCount = useValue(enabledEntryCount$);
  const entriesSupported = useValue(entriesSupported$);
  useEffect(() => {
    if (!resetCompleted) return;
    setSearch("");
    setExpanded(new Set());
  }, [resetCompleted]);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);

  const assets = useMemo(() => parseCatalog(catalogRaw), [catalogRaw]);
  const selected = useMemo(() => new Set(selectedRaw.split(",").filter(Boolean).map(Number).filter(Number.isInteger)), [selectedRaw]);
  const relevant = useMemo(() => assets.filter(asset => targetTransport === 0 || (asset.mode & targetTransport) !== 0), [assets, targetTransport]);
  const childrenByParent = useMemo(() => {
    const result = new Map<number, VehicleAsset[]>();
    relevant.forEach(asset => {
      if (asset.parentId) result.set(asset.parentId, [...(result.get(asset.parentId) ?? []), asset]);
    });
    return result;
  }, [relevant]);
  const assetIds = useMemo(() => new Set(relevant.map(asset => asset.id)), [relevant]);
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const roots = useMemo(() => relevant.filter(asset => !asset.parentId || !assetIds.has(asset.parentId)).filter(asset => {
    if (!normalizedSearch) return true;
    return asset.name.toLocaleLowerCase().includes(normalizedSearch)
      || (childrenByParent.get(asset.id) ?? []).some(child => child.name.toLocaleLowerCase().includes(normalizedSearch));
  }), [relevant, assetIds, childrenByParent, normalizedSearch]);

  const bulkAssetIds = useMemo(() => filteredAssetIds(roots, childrenByParent, normalizedSearch).join(","), [roots, childrenByParent, normalizedSearch]);

  useEffect(() => {
    if (!active) trigger(mod.id, "setPointerOverUi", false);
    return () => { trigger(mod.id, "setPointerOverUi", false); };
  }, [active]);


  useEffect(() => {
    if (buildId !== "unknown") console.info(`[RouteFilter.Build] loaded id=${buildId}`);
  }, [buildId]);

  useEffect(() => {
    if (active && !previousToolActive.current) {
      setPanelOpen(true);
    }
    previousToolActive.current = active;
  }, [active]);


  const targetStatus = selectedTargetKind === 1
    ? tr("RouteFilter.UI.NodeSelected", "Node selected")
    : selectedTargetKind === 2
      ? tr("RouteFilter.UI.SegmentSelected", "Segment selected")
      : tr("RouteFilter.UI.SelectTarget", "Select a node or segment on the map");
  const assetTitle = targetTransport === 1
    ? tr("RouteFilter.UI.RoadAssets", "Road vehicles")
    : targetTransport === 2
      ? tr("RouteFilter.UI.RailAssets", "Rail vehicles")
      : targetTransport === 3
        ? tr("RouteFilter.UI.MixedAssets", "Road and rail vehicles")
        : tr("RouteFilter.UI.HoverTarget", "Vehicle assets");

  const labels = {
    title: tr("RouteFilter.UI.Title", "RouteFilter"),
    close: tr("RouteFilter.UI.Close", "Close"),
    node: tr("RouteFilter.UI.Node", "Node"),
    segment: tr("RouteFilter.UI.Segment", "Segment"),
    targetStatus,
    cancel: tr("RouteFilter.UI.CancelTarget", "Cancel"),
    assetTitle,
    assetSubtitle: tr("RouteFilter.UI.ForbiddenHint", "Selected assets are forbidden"),
    search: tr("RouteFilter.UI.Search", "Search assets..."),
    empty: tr("RouteFilter.UI.Empty", "No matching vehicle assets"),
    trailer: tr("RouteFilter.UI.Trailer", "Trailer"),
    expand: tr("RouteFilter.UI.Expand", "Expand"),
    collapse: tr("RouteFilter.UI.Collapse", "Collapse"),
    allowAll: tr("RouteFilter.UI.AllowAll", "Allow All"),
    forbidAll: tr("RouteFilter.UI.ForbidAll", "Forbid All"),
    apply: tr("RouteFilter.UI.ApplyToTarget", "Apply"),
    clear: tr("RouteFilter.UI.ClearTarget", "Clear"),
    refresh: tr("RouteFilter.UI.RefreshAssets", "Refresh assets"),
    select: tr("RouteFilter.UI.Select", "Select"),
    cancelAction: tr("RouteFilter.UI.Cancel", "Cancel"),
    roadGroup: tr("RouteFilter.UI.RoadAssets", "Road vehicles"),
    railGroup: tr("RouteFilter.UI.RailAssets", "Rail vehicles"),
  };

  const toggleExpanded = (id: number) => setExpanded(current => {
    const next = new Set(current);
    if (next.has(id)) next.delete(id); else next.add(id);
    return next;
  });

  return <>
    <FloatingButton active={panelOpen} label={labels.title} onToggle={() => {
      if (panelOpen) {
        setPanelOpen(false);
        trigger(mod.id, "setPointerOverUi", false);
        trigger(mod.id, "deactivateTool");
      } else {
        setPanelOpen(true);
      }
    }} />
    {panelOpen && <RouteFilterPanel
      resetCompleted={resetCompleted}
      buildId={buildId}
      configurationEditable={configurationEditable}
      entryCount={entryCount}
      enabledEntryCount={enabledEntryCount}
      entriesSupported={entriesSupported}
      showEntryDirections={selectedTargetKind !== 0 && (targetTransport & 1) !== 0}
      targetMode={targetMode}
      selectedTargetKind={selectedTargetKind}
      selectedCount={relevant.filter(asset => selected.has(asset.id)).length}
      assetCount={relevant.length}
      roots={roots}
      childrenByParent={childrenByParent}
      selected={selected}
      expanded={expanded}
      search={search}
      labels={labels}
      onClose={() => {
        setPanelOpen(false);
        trigger(mod.id, "setPointerOverUi", false);
        trigger(mod.id, "deactivateTool");
      }}
      onTargetModeChange={mode => {
        trigger(mod.id, "setTargetMode", mode);
        trigger(mod.id, "activateTool");
      }}
      onCancelTarget={() => trigger(mod.id, "cancelSelection")}
      onSearchChange={setSearch}
      onToggleAsset={(asset, hasChildren) => {
        if (hasChildren) trigger(mod.id, "toggleAssetGroup", asset.id, !(expanded.has(asset.id) || normalizedSearch.length > 0));
        else trigger(mod.id, "toggleAsset", asset.id);
      }}
      onExpandAsset={toggleExpanded}
      onAllowAll={() => { trigger(mod.id, "setFilteredAssetSelection", bulkAssetIds, false); }}
      onForbidAll={() => { trigger(mod.id, "setFilteredAssetSelection", bulkAssetIds, true); }}
      onApply={() => { if (selectedTargetKind !== 0) trigger(mod.id, "applySelection"); }}
      onClear={() => { if (selectedTargetKind !== 0) trigger(mod.id, "clearSelectedRestriction"); }}
      onRefresh={() => { trigger(mod.id, "refreshAssets"); }}
      onPointerEnter={() => trigger(mod.id, "setPointerOverUi", true)}
      onPointerLeave={() => trigger(mod.id, "setPointerOverUi", false)}
    />}
  </>;
};

export default RouteFilterShell;
