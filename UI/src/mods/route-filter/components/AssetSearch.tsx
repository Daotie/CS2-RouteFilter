import React from "react";
import styles from "../route-filter.module.scss";
import { icons } from "../assets";

type Props = {
  value: string;
  placeholder: string;
  onChange: (value: string) => void;
};

export const AssetSearch = ({ value, placeholder, onChange }: Props) => (
  <label className={styles.searchBox}>
    <img src={icons.search} alt="" />
    <span className={styles.searchInput}>
      <input value={value} onChange={event => onChange(event.target.value)} aria-label={placeholder} />
      {value === "" && <span className={styles.searchPlaceholder}>{placeholder}</span>}
    </span>
  </label>
);
