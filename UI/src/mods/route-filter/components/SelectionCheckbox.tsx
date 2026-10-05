import React from "react";
import classNames from "classnames";
import { Button } from "cs2/ui";
import { icons } from "../assets";
import styles from "../route-filter.module.scss";

export const SelectionCheckbox = ({selected,partial=false,disabled=false,label,onSelect}: {selected:boolean;partial?:boolean;disabled?:boolean;label:string;onSelect:()=>void}) => <Button variant="flat" role="checkbox" className={classNames(styles.selectionGlyph,{[styles.selectionGlyphActive]:selected,[styles.selectionGlyphPartial]:partial})} onSelect={onSelect} disabled={disabled} aria-label={label} aria-checked={partial?"mixed":selected}>
  {partial?<img className={styles.selectionMark} src={icons.partialSelection} alt=""/>:selected?<img className={styles.selectionMark} src={icons.check} alt=""/>:null}
</Button>;
