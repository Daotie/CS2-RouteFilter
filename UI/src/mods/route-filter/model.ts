export type VehicleAsset = {
  id: number;
  name: string;
  mode: number;
  maxSpeed: number;
  acceleration: number;
  braking: number;
  parentId: number;
  trailer: boolean;
};

export const parseCatalog = (raw: string): VehicleAsset[] => raw.split("\n").reduce<VehicleAsset[]>((result, line) => {
  const part = line.split("|");
  if (part.length !== 8) return result;

  const id = Number(part[0]);
  if (!Number.isInteger(id)) return result;

  let name = part[1];
  try { name = decodeURIComponent(name); } catch { /* Keep the technical prefab name. */ }

  result.push({
    id,
    name,
    mode: Number(part[2]),
    maxSpeed: Number(part[3]),
    acceleration: Number(part[4]),
    braking: Number(part[5]),
    parentId: Number(part[6]),
    trailer: part[7] === "1",
  });
  return result;
}, []);
