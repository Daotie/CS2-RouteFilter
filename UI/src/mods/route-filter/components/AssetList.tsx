import React from "react";
import { Scrollable } from "cs2/ui";
import { VehicleAsset } from "../model";
import { AssetRow } from "./AssetRow";
import styles from "../route-filter.module.scss";

type Props = {
  roots: VehicleAsset[];
  childrenByParent: Map<number, VehicleAsset[]>;
  selected: Set<number>;
  expanded: Set<number>;
  searchTerm: string;
  emptyLabel: string;
  trailerLabel: string;
  expandLabel: string;
  collapseLabel: string;
  roadGroupLabel: string;
  railGroupLabel: string;
  onToggle: (asset: VehicleAsset, hasChildren: boolean) => void;
  onExpand: (id: number) => void;
};

export const AssetList = ({ roots, childrenByParent, selected, expanded, searchTerm, emptyLabel, trailerLabel, expandLabel, collapseLabel, roadGroupLabel, railGroupLabel, onToggle, onExpand }: Props) => {
  const renderRow = (asset: VehicleAsset, child = false): React.ReactNode => {
    const children = childrenByParent.get(asset.id) ?? [];
    const visibleChildren = searchTerm
      ? children.filter(item => item.name.toLocaleLowerCase().includes(searchTerm))
      : children;
    const groupIds = [asset.id, ...children.map(item => item.id)];
    const selectedCount = groupIds.filter(id => selected.has(id)).length;
    const partial = children.length > 0 && selectedCount > 0 && selectedCount < groupIds.length;
    const isExpanded = expanded.has(asset.id) || searchTerm.length > 0;

    return <React.Fragment key={asset.id}>
      <AssetRow asset={asset} child={child} selected={selected.has(asset.id)} partial={partial} childCount={children.length} expanded={isExpanded} trailerLabel={trailerLabel} expandLabel={expandLabel} collapseLabel={collapseLabel} onToggle={() => onToggle(asset, children.length > 0)} onExpand={() => onExpand(asset.id)} />
      {isExpanded && visibleChildren.map(item => renderRow(item, true))}
    </React.Fragment>;
  };

  const roadRoots = roots.filter(asset => asset.mode === 1);
  const railRoots = roots.filter(asset => asset.mode === 2);
  const showGroups = roadRoots.length > 0 && railRoots.length > 0;

  return <Scrollable vertical trackVisibility="scrollable" className={styles.assetList}>
    {showGroups && <div className={styles.assetGroupHeader}>{roadGroupLabel}<span>{roadRoots.length}</span></div>}
    {roadRoots.map(asset => renderRow(asset))}
    {showGroups && <div className={styles.assetGroupHeader}>{railGroupLabel}<span>{railRoots.length}</span></div>}
    {railRoots.map(asset => renderRow(asset))}
    {roots.length === 0 && <div className={styles.emptyState}>{emptyLabel}</div>}
  </Scrollable>;
};
