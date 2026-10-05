import React from "react";
import { Button, Tooltip } from "cs2/ui";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";
import toolbarIcon from "../assets/toolbar-prohibition.svg";

type Props = {
  title: string;
  version: string;
  buildId: string;
  closeLabel: string;
  onClose: () => void;
  onTools: () => void;
  toolsLabel: string;
};

export const PanelHeader = ({ title, version, buildId, closeLabel, onClose, onTools, toolsLabel }: Props) => (
  <div className={styles.panelHeader}>
    <span className={styles.brandIcon}><img className={styles.toolGlyph} src={toolbarIcon} alt="" /></span>
    <div className={styles.brandText}>
      <strong>{title}</strong>
      <span>{version}</span>
      <span className={styles.buildMarker} title={`RouteFilter build ${buildId}`} aria-hidden="true" />
    </div>
    <Button variant="flat" className={styles.closeButton} onSelect={onTools} aria-label={toolsLabel}>⋯</Button>
    <Tooltip tooltip={closeLabel} direction="down" alignment="end">
      <Button variant="flat" className={styles.closeButton} onSelect={onClose} aria-label={closeLabel}>
        <img className={styles.closeGlyph} src={icons.close} alt="" />
      </Button>
    </Tooltip>
  </div>
);
