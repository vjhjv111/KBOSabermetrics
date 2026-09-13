// Home plate is the shared aim origin in the three-dimensional stadium.
export const STADIUM_ASPECT = 1672 / 941;
export const FIELD_CAMERAS = {
  batter: { position: [0, 1.7, 3.96759009], target: [0, .629202775, -18.44], fov: 50 },
  // Offset behind the mound so the whole delivery is visible without covering the zone.
  pitcher: { position: [.85, 2.4, -27], target: [0, 1.3, 0], fov: 31 },
} as const;
export function fieldFov(side: "batter" | "pitcher", aspect: number) {
  return 2 * Math.atan(Math.tan(FIELD_CAMERAS[side].fov * Math.PI / 360) * Math.min(1, STADIUM_ASPECT / aspect)) * 180 / Math.PI;
}
export function ballTrackingCamera(ball:{x:number;y:number;z:number}){
 const distance=Math.min(125,Math.hypot(ball.x,ball.z));
 return {position:[ball.x*.25,13+distance*.13,10-distance*.25] as [number,number,number],target:[ball.x*.8,Math.max(1,Math.min(14,ball.y*.65)),ball.z*.83] as [number,number,number],fov:58};
}
