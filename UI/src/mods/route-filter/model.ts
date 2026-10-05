export type VehicleAsset = {
  id: number;
  name: string;
  prefabName?: string;
  mode: number;
  maxSpeed: number;
  acceleration: number;
  braking: number;
  parentId: number;
  trailer: boolean;
  category?: string;
  icon?: string;
};

export const parseCatalog = (raw: string): VehicleAsset[] => raw.split("\n").reduce<VehicleAsset[]>((result, line) => {
  const part = line.split("|");
  if (part.length !== 8 && part.length !== 10) return result;

  const id = Number(part[0]);
  if (!Number.isInteger(id)) return result;

  let name = part[1];
  try { name = decodeURIComponent(name); } catch { /* Keep the technical prefab name. */ }

  result.push({
    id,
    name,
    prefabName: name,
    mode: Number(part[2]),
    maxSpeed: Number(part[3]),
    acceleration: Number(part[4]),
    braking: Number(part[5]),
    parentId: Number(part[6]),
    trailer: part[7] === "1",
    category: part[8] || (Number(part[2]) === 2 ? "Rail" : "Other"),
    icon: decodeIcon(part[9]),
  });
  return result;
}, []);

// Bulk scope follows the filtered list, including collapsed children when not searching.
export const filteredAssetIds = (roots: VehicleAsset[], childrenByParent: Map<number, VehicleAsset[]>, searchTerm: string): number[] => {
  const ids = new Set<number>();
  const visit = (asset: VehicleAsset) => {
    if (ids.has(asset.id)) return;
    ids.add(asset.id);
    for (const child of childrenByParent.get(asset.id) ?? []) {
      if (!searchTerm || child.name.toLocaleLowerCase().includes(searchTerm)) visit(child);
    }
  };
  roots.forEach(visit);
  return [...ids];
};

function decodeIcon(value?: string): string {
  try { return value ? decodeURIComponent(value) : ""; } catch { return ""; }
}
export function categoryGroups(roots: VehicleAsset[]): {id:string;assets:VehicleAsset[]}[] {
  const groups = new Map<string,VehicleAsset[]>();
  for (const asset of roots) {
    const id = asset.category || (asset.mode === 2 ? "Rail" : "Other");
    if (!groups.has(id)) groups.set(id,[]);
    groups.get(id)!.push(asset);
  }
  // First appearance retains Recent ordering; categories never alter asset scope.
  return Array.from(groups, ([id,assets])=>({id,assets}));
}
