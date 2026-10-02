import React, { useState } from "react";
import { Button, ConfirmationDialog, Panel, Portal, Tooltip } from "cs2/ui";
import { trigger } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { VehicleAsset } from "../model";
import { ActionBar } from "./ActionBar";
import { AssetList } from "./AssetList";
import { AssetSearch } from "./AssetSearch";
import { PanelHeader } from "./PanelHeader";
import { TargetSelector } from "./TargetSelector";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";

type Props = {
  resetCompleted: number;
  buildId: string;
  configurationEditable: boolean;
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

export const RouteFilterPanel = (props: Props) => {
  const [confirmReset, setConfirmReset] = useState(false);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);
  const targetReady = props.selectedTargetKind !== 0;
  return <Portal>
    <Panel id="routefilter-panel" data-build-id={props.buildId} className={styles.panel} contentClassName={styles.panelContent} onMouseEnter={props.onPointerEnter} onMouseLeave={props.onPointerLeave}>
      <PanelHeader title={props.labels.title} version={mod.version} buildId={props.buildId} closeLabel={props.labels.close} onClose={props.onClose} />
      {!props.configurationEditable && <div className={styles.resetStatus} role="status">{tr("RouteFilter.UI.PersistenceLocked", "Save data is incompatible or damaged. Editing is locked; Reset removes RouteFilter configuration.")}</div>}
      <TargetSelector mode={props.targetMode} nodeLabel={props.labels.node} segmentLabel={props.labels.segment} status={props.labels.targetStatus} targetReady={targetReady} onModeChange={props.onTargetModeChange} />
      <div className={styles.listHeading}>
        <div><strong>{props.labels.assetTitle}</strong><span>{props.labels.assetSubtitle}</span></div>
        <small>{props.selectedCount} / {props.assetCount}</small>
      </div>
      <AssetSearch value={props.search} placeholder={props.labels.search} onChange={props.onSearchChange} />
      <AssetList roots={props.roots} childrenByParent={props.childrenByParent} selected={props.selected} expanded={props.expanded} searchTerm={props.search.trim().toLocaleLowerCase()} emptyLabel={props.labels.empty} trailerLabel={props.labels.trailer} expandLabel={props.labels.expand} collapseLabel={props.labels.collapse} roadGroupLabel={props.labels.roadGroup} railGroupLabel={props.labels.railGroup} onToggle={props.onToggleAsset} onExpand={props.onExpandAsset} />
      <ActionBar allowAllLabel={props.labels.allowAll} forbidAllLabel={props.labels.forbidAll} applyLabel={props.labels.apply} clearLabel={props.labels.clear} refreshLabel={props.labels.refresh} targetReady={targetReady && props.configurationEditable} onAllowAll={props.onAllowAll} onForbidAll={props.onForbidAll} onApply={props.onApply} onClear={props.onClear} onRefresh={props.onRefresh} />
      <div className={styles.utilityFooter}>
        <Tooltip tooltip={props.buildId}><span className={styles.buildLabel}>{props.buildId.split("-").slice(-4).join("-")}</span></Tooltip>
        <Button variant="flat" className={styles.utilityAction} onSelect={() => setConfirmReset(true)}>
          <img className={styles.utilityIcon} src={icons.reset} alt="" />
          {tr("RouteFilter.UI.Reset", "Reset RouteFilter")}
        </Button>
      </div>
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
