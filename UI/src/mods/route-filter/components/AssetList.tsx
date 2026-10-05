import React, { useMemo, useState } from "react";
import { useLocalization } from "cs2/l10n";
import { CategoryGlyph } from "./CategoryGlyph";
import { Scrollable } from "cs2/ui";
import { VehicleAsset, categoryGroups } from "../model";
import { AssetRow } from "./AssetRow";
import styles from "../route-filter.module.scss";

type Props = {
  favorites: Set<number>;
  onFavorite: (id: number) => void;
  favoriteLabel: string;
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

export const AssetList = ({ favorites, onFavorite, favoriteLabel, roots, childrenByParent, selected, expanded, searchTerm, emptyLabel, trailerLabel, expandLabel, collapseLabel, roadGroupLabel, railGroupLabel, onToggle, onExpand }: Props) => {
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
      <AssetRow favorite={favorites.has(asset.id)} onFavorite={() => onFavorite(asset.id)} favoriteLabel={favoriteLabel} asset={asset} child={child} selected={selected.has(asset.id)} partial={partial} childCount={children.length} expanded={isExpanded} trailerLabel={trailerLabel} expandLabel={expandLabel} collapseLabel={collapseLabel} onToggle={() => onToggle(asset, children.length > 0)} onExpand={() => onExpand(asset.id)} />
      {isExpanded && visibleChildren.map(item => renderRow(item, true))}
    </React.Fragment>;
  };

  const [collapsed,setCollapsed]=useState<Set<string>>(()=>new Set());
  const groups=useMemo(()=>categoryGroups(roots),[roots]);
  const {translate}=useLocalization();
  const names:Record<string,string>={Bus:"Buses",Taxi:"Taxis",Goods:"Goods vehicles",Emergency:"Emergency vehicles",Service:"Service vehicles",Rail:railGroupLabel,Other:"Other road vehicles"};
  const categoryIds=(assets:VehicleAsset[])=>{const ids=new Set<number>();const visit=(asset:VehicleAsset)=>{if(ids.has(asset.id))return;ids.add(asset.id);for(const child of childrenByParent.get(asset.id)??[])visit(child);};assets.forEach(visit);return [...ids];};
  return <Scrollable vertical trackVisibility="scrollable" className={styles.assetList}>
    {groups.map(group=>{
      const ids=categoryIds(group.assets),count=ids.filter(id=>selected.has(id)).length;
      const open=Boolean(searchTerm)||!collapsed.has(group.id);
      const label=String(translate(`RouteFilter.UI.Category.${group.id}`,names[group.id]??names.Other)??names.Other);
      return <React.Fragment key={group.id}><button type="button" className={styles.categoryHeader} aria-expanded={open} aria-label={`${label}: ${count} / ${ids.length}`} onClick={()=>setCollapsed(previous=>{const next=new Set(previous);if(next.has(group.id))next.delete(group.id);else next.add(group.id);return next;})}><span className={styles.categoryChevron}>{open?"⌄":"›"}</span><CategoryGlyph category={group.id}/><strong>{label}</strong><span>{count} / {ids.length}</span></button>{open&&group.assets.map(asset=>renderRow(asset))}</React.Fragment>;
    })}
    {roots.length === 0 && <div className={styles.emptyState}>{emptyLabel}</div>}
  </Scrollable>;
};
