import React, { useRef, useState } from "react";
import classNames from "classnames";
import { bindValue, trigger, useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { Button, Portal } from "cs2/ui";
import mod from "mod.json";
import icon from "./nodegate.svg";
import styles from "./nodeGateUI.module.scss";

const toolActive$ = bindValue<boolean>(mod.id, "toolActive", false);
const selectedMask$ = bindValue<number>(mod.id, "selectedMask", 1);
const types = [
  ["PrivateCar", 1 << 0], ["Taxi", 1 << 1], ["DeliveryTruck", 1 << 2], ["GoodsDelivery", 1 << 3],
  ["Bus", 1 << 4], ["Tram", 1 << 5], ["PassengerTrain", 1 << 6], ["Subway", 1 << 7],
  ["CargoTrain", 1 << 8], ["PoliceCar", 1 << 9], ["Ambulance", 1 << 10], ["FireEngine", 1 << 11],
  ["GarbageTruck", 1 << 12], ["Hearse", 1 << 13], ["RoadMaintenance", 1 << 14], ["ParkMaintenance", 1 << 15],
  ["PostVan", 1 << 16], ["PrisonerTransport", 1 << 17], ["EvacuationTransport", 1 << 18], ["Bicycle", 1 << 19]
] as const;

export const NodeGateUI = () => {
  const [open, setOpen] = useState(false);
  const anchor = useRef<HTMLDivElement | null>(null);
  const active = useValue(toolActive$);
  const mask = useValue(selectedMask$);
  const { translate } = useLocalization();
  const tr = (key: string, fallback: string) => String(translate(key) ?? fallback);

  const togglePanel = () => {
    setOpen(!open);
    if (!open && !active) trigger(mod.id, "toggleTool");
  };

  return <div ref={anchor}>
    <Button src={icon} variant="floating" className={classNames(styles.toggle, { [styles.selected]: active })} onSelect={togglePanel} />
    {open && <Portal><section className={styles.panel}>
      <header>
        <div><strong>{tr("NodeGate.UI.Title", "NodeGate")}</strong><small>v{mod.version}</small></div>
        <button onClick={() => setOpen(false)}>×</button>
      </header>
      <p>{tr("NodeGate.UI.Instruction", "Choose types, then left-click a node to apply or right-click to clear.")}</p>
      <button className={styles.toolButton} onClick={() => trigger(mod.id, "toggleTool")}>
        {active ? tr("NodeGate.UI.Active", "Tool active") : tr("NodeGate.UI.Inactive", "Open tool")}
      </button>
      <div className={styles.actions}>
        <button onClick={() => trigger(mod.id, "selectAll")}>All / 全选</button>
        <button onClick={() => trigger(mod.id, "selectNone")}>None / 清空</button>
      </div>
      <div className={styles.grid}>{types.map(([name, value]) =>
        <button key={name} className={classNames({ [styles.checked]: (mask & value) !== 0 })}
          onClick={() => trigger(mod.id, "toggleVehicle", value)}>
          <span>{(mask & value) !== 0 ? "✓" : ""}</span>{tr(`NodeGate.Vehicle.${name}`, name)}
        </button>)}</div>
    </section></Portal>}
  </div>;
};
