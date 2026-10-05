import React from "react";
import classNames from "classnames";
import { bindValue, trigger, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { Button } from "cs2/ui";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";

type Props = {
  targetMode: number;
  editable: boolean;
  copyLabel: string;
  pasteLabel: string;
  copyReady: boolean;
  pasteReady: boolean;
  onCopy: () => void;
  onPaste: () => void;
  allowAllLabel: string;
  forbidAllLabel: string;
  applyLabel: string;
  clearLabel: string;
  refreshLabel: string;
  targetReady: boolean;
  onAllowAll: () => void;
  onForbidAll: () => void;
  onApply: () => void;
  onClear: () => void;
  onRefresh: () => void;
};

export const ActionBar = ({ targetMode, editable, copyLabel, pasteLabel, copyReady, pasteReady, onCopy, onPaste, allowAllLabel, forbidAllLabel, applyLabel, clearLabel, refreshLabel, targetReady, onAllowAll, onForbidAll, onApply, onClear, onRefresh }: Props) => (
  <footer className={styles.actionBar}>
    <div className={styles.bulkActions}>
      <Button variant="flat" className={styles.secondaryAction} onSelect={onAllowAll}>{allowAllLabel}</Button>
      <Button variant="flat" className={styles.secondaryAction} onSelect={onForbidAll}>{forbidAllLabel}</Button>
      <Button variant="flat" className={styles.secondaryAction} onSelect={onRefresh} aria-label={refreshLabel}>{refreshLabel}</Button>
    </div>
    {targetMode===1 && <BatchControls editable={editable}/>}
    <div className={styles.commitActions}>
      <Button variant="flat" className={styles.secondaryAction} disabled={!copyReady} onSelect={onCopy}>{copyLabel}</Button>
      <Button variant="flat" className={styles.secondaryAction} disabled={!pasteReady} onSelect={onPaste}>{pasteLabel}</Button>
      <Button variant="flat" className={classNames(styles.clearAction, { [styles.actionDisabled]: !targetReady })} disabled={!targetReady} onSelect={onClear}><img className={styles.actionIcon} src={icons.trash} alt="" />{clearLabel}</Button>
      <Button variant="primary" className={classNames(styles.applyAction, { [styles.actionDisabled]: !targetReady })} disabled={!targetReady} onSelect={onApply}>{applyLabel}</Button>
    </div>
  </footer>
);

const brush$=bindValue<boolean>(mod.id,"segmentBrush",false),clear$=bindValue<boolean>(mod.id,"segmentBrushClear",false),pending$=bindValue<number>(mod.id,"brushPending",0);
const BatchControls=({editable}:{editable:boolean})=>{
  const active=useValue(brush$),pending=useValue(pending$);
  const {translate}=useLocalization();const tr=(key:string,fallback:string)=>String(translate(`RouteFilter.UI.${key}`,fallback)??fallback);
  const select=(operation:boolean)=>{trigger(mod.id,"setSegmentBrushOperation",operation);trigger(mod.id,"setSegmentBrush",!active);};
  return <div className={styles.batchActions}><button type="button" className={`${styles.secondaryAction} ${active?styles.segmentButtonActive:""}`} aria-pressed={active} disabled={!editable} onClick={()=>select(false)} onContextMenu={event=>{event.preventDefault();select(true);}}>{tr("Batch","Batch")}</button>{active&&<span role="status">{pending>0?`${pending} ${tr("RangeSegments","segments")}`:tr("BatchDrag","Drag road segments")}</span>}</div>;
};
