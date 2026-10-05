import React from "react";
import classNames from "classnames";
import { SegmentedSelector } from "./SegmentedSelector";
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
    <SegmentedSelector value={String(mode)} label={`${nodeLabel} / ${segmentLabel}`} options={[{id:"0",label:nodeLabel},{id:"1",label:segmentLabel}]} onChange={id=>onModeChange(Number(id))} />
    <div className={classNames(styles.targetStatus, { [styles.targetStatusReady]: targetReady })}>
      <img className={styles.targetInfo} src={icons.info} alt="" />
      <div>
        <strong>{status}</strong>
      </div>
    </div>
  </section>
);
