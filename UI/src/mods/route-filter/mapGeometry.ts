export type MapRow = { key: string; points: [number, number][]; assets: number; directions: string };
export function parseMap(raw: string) {
  const background: MapRow[] = [], restrictions: MapRow[] = [], entries: MapRow[] = [], land: MapRow[] = [], water: MapRow[] = [];
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const line of raw.split("\n")) {
    const [type, key, geometry, assets, directions] = line.split("|");
    if (!geometry || (type !== "B" && type !== "R" && type !== "E" && type !== "L" && type !== "W")) continue;
    const points = geometry.split(";").map(point => point.split(",").map(Number) as [number, number]);
    if (points.some(point => point.length !== 2 || !point.every(Number.isFinite))) continue;
    if (type === "E" && points.length !== 2) continue;
    for (const [x, y] of points) { minX = Math.min(minX,x); minY = Math.min(minY,y); maxX = Math.max(maxX,x); maxY = Math.max(maxY,y); }
    (type === "B" ? background : type === "E" ? entries : type === "L" ? land : type === "W" ? water : restrictions).push({ key, points, assets: Number(assets) || 0, directions: directions || "0/0" });
  }
  const width = Number.isFinite(minX) ? Math.max(100,maxX-minX) : 100;
  const height = Number.isFinite(minY) ? Math.max(100,maxY-minY) : 100;
  const bounds = { x: Number.isFinite(minX) ? (minX+maxX)/2-width*.54 : -54, y: Number.isFinite(minY) ? (minY+maxY)/2-height*.54 : -54, width: width*1.08, height: height*1.08 };
  const backgroundPaths = [] as string[];
  for (let i=0; i<background.length; i+=500) backgroundPaths.push(background.slice(i,i+500).map(row => path(row.points)).join(" "));
  const landPath = land.map(row=>path(row.points)+" Z").join(" "), waterPath = water.map(row=>path(row.points)+" Z").join(" ");
  return { landPath, waterPath, roadCount: background.length, background: backgroundPaths.join(" "), backgroundPaths, restrictions, entries, bounds };
}
export const path = (points: [number, number][]) => points.map(([x,y], i) => `${i === 0 ? "M" : "L"}${x},${y}`).join(" ");

export type MapView = { cx: number; cy: number; scale: number };
export const fitMap = (bounds: {x:number;y:number;width:number;height:number}, width:number,height:number):MapView => ({cx:bounds.x+bounds.width/2,cy:bounds.y+bounds.height/2,scale:Math.max(.0001,Math.min((width-48)/bounds.width,(height-48)/bounds.height))});
export const mapMatrix = (view:MapView,width:number,height:number) => `matrix(${view.scale} 0 0 ${-view.scale} ${width/2-view.cx*view.scale} ${height/2+view.cy*view.scale})`;
export const projectMap = (point:[number,number],view:MapView,width:number,height:number):[number,number] => [width/2+(point[0]-view.cx)*view.scale,height/2-(point[1]-view.cy)*view.scale];
export function zoomMap(view:MapView,factor:number,x:number,y:number,width:number,height:number):MapView {
  const scale=Math.max(.0001,Math.min(8,view.scale*factor));
  return {scale,cx:view.cx+(x-width/2)*(1/view.scale-1/scale),cy:view.cy-(y-height/2)*(1/view.scale-1/scale)};
}

// Fixed world geometry: visible arrow size follows map zoom, unlike the hit target.
export function navigationArrow(a:[number,number],b:[number,number],baseScale:number):[number,number][] {
  const dx=b[0]-a[0],dy=b[1]-a[1],length=Math.hypot(dx,dy)||1,ux=dx/length,uy=dy/length,size=6/baseScale;
  return [[a[0]+ux*size,a[1]+uy*size],[a[0]-ux*size-uy*size*.6,a[1]-uy*size+ux*size*.6],[a[0]-ux*size*.25,a[1]-uy*size*.25],[a[0]-ux*size+uy*size*.6,a[1]-uy*size-ux*size*.6],[a[0]+ux*size,a[1]+uy*size]];
}
