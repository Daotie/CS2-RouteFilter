import React from "react";
import classNames from "classnames";
import { Button } from "cs2/ui";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";

type Props = {
  mode: number;
  nodeLabel: string;
  segmentLabel: string;
  status: string;
  targetReady: boolean;
  onModeChange: (mode: number) => void;
};

export const TargetSelector = ({ mode, nodeLabel, segmentLabel, status, targetReady, onModeChange }: Props) => (
  <section className={styles.targetSection}>
    <div className={styles.segmentedControl}>
      <Button variant="flat" selected={mode === 0} className={classNames(styles.segmentButton, { [styles.segmentButtonActive]: mode === 0 })} onSelect={() => onModeChange(0)}>{nodeLabel}</Button>
      <Button variant="flat" selected={mode === 1} className={classNames(styles.segmentButton, { [styles.segmentButtonActive]: mode === 1 })} onSelect={() => onModeChange(1)}>{segmentLabel}</Button>
    </div>
    <div className={classNames(styles.targetStatus, { [styles.targetStatusReady]: targetReady })}>
      <img className={styles.targetInfo} src={icons.info} alt="" />
      <div>
        <strong>{status}</strong>
      </div>
    </div>
  </section>
);
