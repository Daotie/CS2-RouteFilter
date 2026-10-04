export type MapRow = { key: string; points: [number, number][]; assets: number; directions: string };
export function parseMap(raw: string) {
  const background: MapRow[] = [], restrictions: MapRow[] = [];
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const line of raw.split("\n")) {
    const [type, key, geometry, assets, directions] = line.split("|");
    if (!geometry || (type !== "B" && type !== "R")) continue;
    const points = geometry.split(";").map(point => point.split(",").map(Number) as [number, number]);
    if (points.some(point => point.length !== 2 || !point.every(Number.isFinite))) continue;
    for (const [x, y] of points) { minX = Math.min(minX,x); minY = Math.min(minY,y); maxX = Math.max(maxX,x); maxY = Math.max(maxY,y); }
    (type === "B" ? background : restrictions).push({ key, points, assets: Number(assets) || 0, directions: directions || "0/0" });
  }
  const width = Number.isFinite(minX) ? Math.max(100,maxX-minX) : 100;
  const height = Number.isFinite(minY) ? Math.max(100,maxY-minY) : 100;
  const bounds = { x: Number.isFinite(minX) ? minX-width*.04 : 0, y: Number.isFinite(minY) ? minY-height*.04 : 0, width: width*1.08, height: height*1.08 };
  return { background: background.map(row => path(row.points)).join(" "), restrictions, bounds };
}
export const path = (points: [number, number][]) => points.map(([x,y], i) => `${i === 0 ? "M" : "L"}${x},${y}`).join(" ");
