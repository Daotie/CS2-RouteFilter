import { ModRegistrar } from "cs2/modding";
import { RouteFilterShell } from "./mods/route-filter";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", RouteFilterShell);
};

export default register;
