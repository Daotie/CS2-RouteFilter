import React from "react";
import { trigger } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { Button } from "cs2/ui";
import mod from "mod.json";
import styles from "../route-filter.module.scss";

export const SignInfoSection = (section: { targetKind: string; assets: number; entries: number; owner: string; targetName: string; categories: string; entryOrdinal: number }) => {
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(`RouteFilter.UI.${key}`,fallback) ?? fallback);
  return <div className={styles.nativeSignInfo}>
    <strong>{tr("SignInfo","Road restriction sign")}</strong>
    <span>RouteFilter · {tr(section.targetKind,section.targetKind)}</span>
    {section.targetName && <span>{section.targetName}</span>}
    <span>{tr("VehicleCategories","Vehicle categories")}: {section.categories || tr("SpecifiedVehicles","Selected vehicles")}</span>
    <span>{tr("EntryDirections","Restricted entry")} {section.entryOrdinal}</span>
    <span>{tr("VehicleAssets","Vehicle assets")}: {section.assets} · {tr("EntryDirections","Restricted entries")}: {section.entries}</span>
    <Button variant="flat" onSelect={() => trigger(mod.id,"editSelectedSign")}>{tr("EditSign","Edit in RouteFilter")}</Button>
  </div>;
};
