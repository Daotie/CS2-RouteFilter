import React from "react";

export const ChevronIcon = ({open=false}: {open?:boolean}) => <svg width="12rem" height="12rem" viewBox="0 0 16 16" aria-hidden="true"><path d={open?"M3 6l5 5 5-5":"M6 3l5 5-5 5"} fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"/></svg>;
