import React from "react";
import favoriteIcon from "../assets/favorite.svg";
import classNames from "classnames";
import { Button, Tooltip } from "cs2/ui";
import { VehicleAsset } from "../model";
import styles from "../route-filter.module.scss";
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

const AssetGlyph = ({ mode, trailer, name }: { mode: number; trailer: boolean; name: string }) => {
  const lower = name.toLocaleLowerCase();
  const kind = mode === 2 ? "train" : lower.includes("bicycle") || lower.includes("bike") ? "bike" : lower.includes("bus") ? "bus" : lower.includes("tram") ? "tram" : lower.includes("subway") || lower.includes("metro") ? "subway" : lower.includes("truck") ? "truck" : lower.includes("van") ? "van" : lower.includes("pickup") ? "pickup" : lower.includes("suv") ? "suv" : "car";
  return <img className={styles.assetGlyph} src={icons[kind as keyof typeof icons] ?? icons.car} alt="" />;
};

export const AssetRow = ({ favorite, onFavorite, favoriteLabel, asset, child = false, selected, partial, childCount, expanded, trailerLabel, expandLabel, collapseLabel, disabled = false, onToggle, onExpand }: Props) => (
  <div className={classNames(styles.assetRow, {
    [styles.assetRowChild]: child,
    [styles.assetRowSelected]: selected,
    [styles.assetRowPartial]: partial,
    [styles.assetRowDisabled]: disabled,
  })}>
    {childCount > 0
      ? <Button variant="flat" className={styles.expandButton} onSelect={onExpand} aria-label={expanded ? collapseLabel : expandLabel}><img className={classNames(styles.chevron, { [styles.chevronExpanded]: expanded })} src={icons.chevron} alt="" /></Button>
      : <span className={styles.expandSpacer} />}
    <Button variant="flat" className={classNames(styles.selectionGlyph, { [styles.selectionGlyphActive]: selected, [styles.selectionGlyphPartial]: partial })} onSelect={onToggle} disabled={disabled} aria-label={asset.name} aria-pressed={partial ? "mixed" : selected}>
      {partial ? <img className={styles.selectionMark} src={icons.partialSelection} alt="" /> : selected ? <img className={styles.selectionMark} src={icons.check} alt="" /> : null}
    </Button>
    <Button variant="flat" className={favorite ? styles.favoriteActive : styles.favoriteButton} onSelect={onFavorite} aria-label={favoriteLabel} aria-pressed={favorite}><img src={favoriteIcon} alt="" /></Button>
    <AssetGlyph mode={asset.mode} trailer={asset.trailer} name={asset.name} />
    <div className={styles.assetIdentity}>
      <Tooltip tooltip={asset.name} direction="right"><span>{asset.name}</span></Tooltip>
      {asset.trailer && <small>{trailerLabel}</small>}
    </div>
    {childCount === 0 && asset.maxSpeed > 0 && <span className={styles.assetMeta}>{Math.round(asset.maxSpeed)} km/h</span>}
    {childCount > 0 && <span className={styles.groupCount}>{childCount + 1}</span>}
  </div>
);
