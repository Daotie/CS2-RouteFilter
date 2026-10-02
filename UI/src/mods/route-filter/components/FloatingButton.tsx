import React from "react";
import { FloatingButton as GameFloatingButton } from "cs2/ui";
import toolbarIcon from "../assets/toolbar-prohibition.svg";

type Props = {
  active: boolean;
  label: string;
  onToggle: () => void;
};

export const FloatingButton = ({ active, label, onToggle }: Props) => (
  <GameFloatingButton src={toolbarIcon} selected={active} onSelect={onToggle} tooltipLabel={label} />
);
