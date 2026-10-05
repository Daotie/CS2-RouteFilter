import React from "react";
import classNames from "classnames";
import { Button } from "cs2/ui";
import styles from "../route-filter.module.scss";
type Props = { value: string; label: string; options: {id: string; label: string}[]; onChange: (id: string) => void };
export const SegmentedSelector = ({value,label,options,onChange}: Props) => <div className={styles.segmentedControl} role="tablist" aria-label={label}>
  {options.map(option=><Button key={option.id} variant="flat" selected={value===option.id} role="tab" aria-selected={value===option.id} className={classNames(styles.segmentButton,{[styles.segmentButtonActive]:value===option.id})} onSelect={()=>onChange(option.id)}>{option.label}</Button>)}
</div>;
