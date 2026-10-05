import React from "react";
import styles from "../route-filter.module.scss";

export const ChevronIcon = ({open=false}: {open?:boolean}) => <svg className={`${styles.arrowIcon} ${open?styles.arrowIconOpen:""}`} width="12rem" height="12rem" viewBox="0 0 16 16" aria-hidden="true"><path d="M6 3l5 5-5 5" fill="none" stroke="#fff" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/></svg>;
