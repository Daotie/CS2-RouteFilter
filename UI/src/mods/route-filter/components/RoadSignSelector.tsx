import React, { useMemo } from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import { Dropdown, DropdownToggle, DropdownItem } from "cs2/ui";
import { useLocalization } from "cs2/l10n";
import mod from "mod.json";
import { icons } from "../assets";
import styles from "../route-filter.module.scss";

const catalog$ = bindValue<string>(mod.id, "roadSignCatalog", "");
const selection$ = bindValue<string>(mod.id, "roadSignSelection", "");
const resolved$ = bindValue<string>(mod.id, "roadSignResolved", "");
const unavailable$ = bindValue<boolean>(mod.id, "roadSignUnavailable", false);

export const RoadSignSelector = () => {
  const catalog = useValue(catalog$);
  const selection = useValue(selection$);
  const resolved = useValue(resolved$);
  const unavailable = useValue(unavailable$);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);
  const name = (value: string) => String(translate(`Assets.NAME[${value}]`, value) ?? value);
  const options = useMemo(() => catalog.split("\n").filter(Boolean).flatMap(line => {
    try {
      const [id, icon] = line.split("|").map(decodeURIComponent);
      return id ? [{ id, icon: icon || icons.prohibition }] : [];
    } catch { return []; }
  }), [catalog]);
  const selected = options.find(option => option.id === selection);
  const auto = tr("RouteFilter.UI.RoadSignAuto", "Auto — Match Road Theme");
  const title = tr("RouteFilter.UI.RoadSignStyle", "Road Restriction Sign Prefab");
  const select = (value: string) => trigger(mod.id, "selectRoadSignPrefab", value);
  const row = (icon: string, text: string) => <span className={styles.signOption}>
    <img src={icon} alt="" onError={event => { event.currentTarget.onerror = null; event.currentTarget.src = icons.prohibition; }} />
    <span>{text}</span>
  </span>;
  const automatic = options.find(option => option.id === resolved);
  const theme = { dropdownToggle: styles.signToggle, label: styles.signLabel,
    indicator: styles.signIndicator, hiddenIcon: styles.signArrow, visibleIcon: styles.signArrowOpen,
    dropdownPopup: styles.signDropdownPopup, dropdownMenu: styles.signMenu,
    scrollable: styles.signScroll, dropdownItem: styles.signItem };
  return <div className={styles.signSelector}>
    <div className={styles.signTitle}>{title}</div>
    <Dropdown theme={theme} content={<div className={`${styles.signMenu} ${styles.signScroll}`} onMouseEnter={() => trigger(mod.id, "setPointerOverUi", true)} onMouseLeave={() => trigger(mod.id, "setPointerOverUi", false)}>
      <DropdownItem value="" selected={selection === ""} onChange={select}>{row(icons.prohibition, auto)}</DropdownItem>
      {options.map(option => <DropdownItem key={option.id} value={option.id} selected={selection === option.id} onChange={select}>
        {row(option.icon, name(option.id))}
      </DropdownItem>)}
    </div>}>
      <DropdownToggle theme={theme} openIconComponent={<img src={icons.chevron} alt="" />} closeIconComponent={<img src={icons.chevron} alt="" />}>
        {row(selected?.icon ?? automatic?.icon ?? icons.prohibition, selection ? name(selection) : auto)}
      </DropdownToggle>
    </Dropdown>
    {!selection && <div className={styles.signResolved}>{resolved ? `${tr("RouteFilter.UI.RoadSignResolved", "Auto resolved")}: ${name(resolved)}` : tr("RouteFilter.UI.RoadSignUnavailable", "No compatible sign; restrictions remain active")}</div>}
    {unavailable && <div className={styles.signResolved}>{tr("RouteFilter.UI.RoadSignMissing", "Custom sign unavailable; using Auto temporarily")}</div>}
  </div>;
};
