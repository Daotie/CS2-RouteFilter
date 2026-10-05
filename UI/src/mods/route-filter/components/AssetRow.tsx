import React from "react";
import classNames from "classnames";
import { Button, Tooltip } from "cs2/ui";
import { VehicleAsset } from "../model";
import styles from "../route-filter.module.scss";
import { CategoryGlyph } from "./CategoryGlyph";
import { FavoriteIcon } from "./FavoriteIcon";
import { icons } from "../assets";

type Props = {
  favorite: boolean;
  onFavorite: () => void;
  favoriteLabel: string;
  asset: VehicleAsset;
  child?: boolean;
  selected: boolean;
  partial: boolean;
  childCount: number;
  expanded: boolean;
  trailerLabel: string;
  expandLabel: string;
  collapseLabel: string;
  disabled?: boolean;
  onToggle: () => void;
  onExpand: () => void;
};

const AssetGlyph = ({asset}: {asset:VehicleAsset}) => {
  const [failed,setFailed]=React.useState(false);
  React.useEffect(()=>setFailed(false),[asset.icon]);
  return asset.icon && !failed ? <img className={styles.assetGlyph} src={asset.icon} alt="" onError={()=>setFailed(true)}/> : <span className={styles.assetGlyph}><CategoryGlyph category={asset.category || (asset.mode===2 ? "Rail" : "Other")}/></span>;
};

export const AssetRow = ({ favorite, onFavorite, favoriteLabel, asset, child = false, selected, partial, childCount, expanded, trailerLabel, expandLabel, collapseLabel, disabled = false, onToggle, onExpand }: Props) => (
  <div className={classNames(styles.assetRow, {
    [styles.assetRowChild]: child,
    [styles.assetRowSelected]: selected,
    [styles.assetRowPartial]: partial,
    [styles.assetRowDisabled]: disabled,
  })}>
    <button type="button" className={favorite ? styles.favoriteActive : styles.favoriteButton} onClick={event => { event.stopPropagation(); onFavorite(); }} aria-label={favoriteLabel} aria-pressed={favorite}><FavoriteIcon filled={favorite}/></button>
    {childCount > 0
      ? <Button variant="flat" className={styles.expandButton} onSelect={onExpand} aria-label={expanded ? collapseLabel : expandLabel}><img className={classNames(styles.chevron, { [styles.chevronExpanded]: expanded })} src={icons.chevron} alt="" /></Button>
      : <span className={styles.expandSpacer} />}
    <AssetGlyph asset={asset} />
    <div className={styles.assetIdentity}>
      <Tooltip tooltip={asset.name} direction="right"><span>{asset.name}</span></Tooltip>
      {asset.trailer && <small>{trailerLabel}</small>}
    </div>
    {childCount === 0 && asset.maxSpeed > 0 && <span className={styles.assetMeta}>{Math.round(asset.maxSpeed)} km/h</span>}
    {childCount > 0 && <span className={styles.groupCount}>{childCount + 1}</span>}
    <Button variant="flat" className={classNames(styles.selectionGlyph, { [styles.selectionGlyphActive]: selected, [styles.selectionGlyphPartial]: partial })} onSelect={onToggle} disabled={disabled} aria-label={asset.name} aria-pressed={partial ? "mixed" : selected}>
      {partial ? <img className={styles.selectionMark} src={icons.partialSelection} alt="" /> : selected ? <img className={styles.selectionMark} src={icons.check} alt="" /> : null}
    </Button>
  </div>
);
