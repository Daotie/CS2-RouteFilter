import { ModRegistrar } from "cs2/modding";
import { NodeGateUI } from "./nodeGateUI";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", NodeGateUI);
};

export default register;
