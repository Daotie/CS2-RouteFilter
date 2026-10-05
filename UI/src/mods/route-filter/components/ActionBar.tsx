import React from "react";
import classNames from "classnames";
import { Button } from "cs2/ui";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";

type Props = {
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

export const ActionBar = ({ copyLabel, pasteLabel, copyReady, pasteReady, onCopy, onPaste, allowAllLabel, forbidAllLabel, applyLabel, clearLabel, refreshLabel, targetReady, onAllowAll, onForbidAll, onApply, onClear, onRefresh }: Props) => (
  <footer className={styles.actionBar}>
    <div className={styles.bulkActions}>
      <Button variant="flat" className={styles.secondaryAction} onSelect={onAllowAll}>{allowAllLabel}</Button>
      <Button variant="flat" className={styles.secondaryAction} onSelect={onForbidAll}>{forbidAllLabel}</Button>
      <Button variant="flat" className={styles.secondaryAction} onSelect={onRefresh} aria-label={refreshLabel}>{refreshLabel}</Button>
    </div>
    <div className={styles.commitActions}>
      <Button variant="flat" className={styles.secondaryAction} disabled={!copyReady} onSelect={onCopy}>{copyLabel}</Button>
      <Button variant="flat" className={styles.secondaryAction} disabled={!pasteReady} onSelect={onPaste}>{pasteLabel}</Button>
      <Button variant="flat" className={classNames(styles.clearAction, { [styles.actionDisabled]: !targetReady })} disabled={!targetReady} onSelect={onClear}><img className={styles.actionIcon} src={icons.trash} alt="" />{clearLabel}</Button>
      <Button variant="primary" className={classNames(styles.applyAction, { [styles.actionDisabled]: !targetReady })} disabled={!targetReady} onSelect={onApply}>{applyLabel}</Button>
    </div>
  </footer>
);
