import React, { useMemo, useRef, useState } from "react";
import classNames from "classnames";
import { bindValue, trigger, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { Button, Portal } from "cs2/ui";
import mod from "mod.json";
import icon from "./routefilter.svg";
import styles from "./routeFilterUI.module.scss";

type VehicleAsset = { id: number; name: string };

const toolActive$ = bindValue<boolean>(mod.id, "toolActive", false);
const targetMode$ = bindValue<number>(mod.id, "targetMode", 0);
const assetCatalog$ = bindValue<string>(mod.id, "assetCatalog", "");
const selectedAssetIds$ = bindValue<string>(mod.id, "selectedAssetIds", "");

const parseCatalog = (raw: string): VehicleAsset[] => raw.split("\n").flatMap(line => {
  const divider = line.indexOf("|");
  if (divider < 1) return [];
  const id = Number(line.slice(0, divider));
  if (!Number.isInteger(id)) return [];
  try { return [{ id, name: decodeURIComponent(line.slice(divider + 1)) }]; }
  catch { return [{ id, name: line.slice(divider + 1) }]; }
});

export const RouteFilterUI = () => {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");
  const anchor = useRef<HTMLDivElement | null>(null);
  const active = useValue(toolActive$);
  const targetMode = useValue(targetMode$);
  const catalogRaw = useValue(assetCatalog$);
  const selectedRaw = useValue(selectedAssetIds$);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);
  const assets = useMemo(() => parseCatalog(catalogRaw), [catalogRaw]);
  const selected = useMemo(() => new Set(selectedRaw.split(",").map(Number).filter(Number.isInteger)), [selectedRaw]);
  const normalizedSearch = search.trim().toLocaleLowerCase();
  const visible = useMemo(() => normalizedSearch
    ? assets.filter(asset => asset.name.toLocaleLowerCase().includes(normalizedSearch))
    : assets, [assets, normalizedSearch]);

  const togglePanel = () => {
    setOpen(!open);
    if (!open && !active) trigger(mod.id, "toggleTool");
  };

  return <div ref={anchor}>
    <Button src={icon} variant="floating" className={classNames(styles.toggle, { [styles.selected]: active })} onSelect={togglePanel} />
    {open && <Portal><section className={styles.panel}>
      <header>
        <div><strong>{tr("RouteFilter.UI.Title", "RouteFilter")}</strong><small>v{mod.version}</small></div>
        <button onClick={() => setOpen(false)}>×</button>
      </header>
      <p>{tr("RouteFilter.UI.Instruction", "Choose a target and vehicle assets; left-click to apply or right-click to clear.")}</p>
      <div className={styles.modes}>
        <button className={classNames({ [styles.checked]: targetMode === 0 })} onClick={() => trigger(mod.id, "setTargetMode", 0)}>
          {tr("RouteFilter.UI.Node", "Node")}
        </button>
        <button className={classNames({ [styles.checked]: targetMode === 1 })} onClick={() => trigger(mod.id, "setTargetMode", 1)}>
          {tr("RouteFilter.UI.Segment", "Segment")}
        </button>
      </div>
      <button className={styles.toolButton} onClick={() => trigger(mod.id, "toggleTool")}>{active ? tr("RouteFilter.UI.Active", "Tool active") : tr("RouteFilter.UI.Inactive", "Open tool")}</button>
      <div className={styles.assetHeader}>
        <strong>{tr("RouteFilter.UI.Assets", "Vehicle assets")}</strong>
        <small>{selected.size} / {assets.length} {tr("RouteFilter.UI.Selected", "selected")}</small>
      </div>
      <input className={styles.search} value={search} onChange={event => setSearch(event.target.value)} placeholder={tr("RouteFilter.UI.Search", "Search vehicle assets")} />
      <div className={styles.actions}>
        <button onClick={() => trigger(mod.id, "selectAllAssets")}>{tr("RouteFilter.UI.All", "Select all")}</button>
        <button onClick={() => trigger(mod.id, "selectNoAssets")}>{tr("RouteFilter.UI.None", "Clear selection")}</button>
        <button onClick={() => trigger(mod.id, "refreshAssets")}>{tr("RouteFilter.UI.Refresh", "Refresh assets")}</button>
      </div>
      <div className={styles.assetList}>{visible.map(asset =>
        <button key={asset.id} className={classNames({ [styles.checked]: selected.has(asset.id) })} onClick={() => trigger(mod.id, "toggleAsset", asset.id)} title={asset.name}>
          <span>{selected.has(asset.id) ? "✓" : ""}</span><em>{asset.name}</em>
        </button>)}
        {visible.length === 0 && <p className={styles.empty}>{tr("RouteFilter.UI.Empty", "No matching vehicle assets")}</p>}
      </div>
    </section></Portal>}
  </div>;
};
