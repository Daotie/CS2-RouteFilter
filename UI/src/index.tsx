import { ModRegistrar } from "cs2/modding";
import { RouteFilterShell } from "./mods/route-filter";
import { SignInfoSection } from "./mods/route-filter/components/SignInfoSection";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", RouteFilterShell);
  const path = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";
  const sections = moduleRegistry.get(path,"selectedInfoSectionComponents");
  if (sections) sections["RouteFilter.Systems.RouteFilterSignInfoSection"] = SignInfoSection;
  else console.warn("[RouteFilter.UI] native selected-info registry unavailable");
};

export default register;
