import { ModRegistrar } from "cs2/modding";
import { RouteFilterUI } from "./routeFilterUI";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", RouteFilterUI);
};

export default register;
