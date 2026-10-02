import React from "react";
import classNames from "classnames";
import { Button, Tooltip } from "cs2/ui";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";

type Props = {
  active: boolean;
  label: string;
  onToggle: () => void;
};

export const FloatingButton = ({ active, label, onToggle }: Props) => (
  <Tooltip tooltip={label} direction="right" alignment="center">
    <Button variant="flat"
      className={classNames(styles.floatingButton, { [styles.floatingButtonActive]: active })}
      onSelect={onToggle}
      aria-label={label}
      aria-pressed={active}
    >
      <img className={styles.toolGlyph} src={icons.prohibition} alt="" />
    </Button>
  </Tooltip>
);
