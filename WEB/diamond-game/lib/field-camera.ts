// Camera landmarks match the two stadium backdrops; handedness never shifts the lens.
export const STADIUM_ASPECT = 1672 / 941;
export const FIELD_CAMERAS = {
  batter: { position: [0, 1.7, 3.96759009], target: [0, .629202775, -18.44], fov: 50 },
  // Offset behind the mound so the whole delivery is visible without covering the zone.
  pitcher: { position: [.85, 2.4, -27], target: [0, 1.3, 0], fov: 31 },
} as const;
export function fieldFov(side: "batter" | "pitcher", aspect: number) {
  return 2 * Math.atan(Math.tan(FIELD_CAMERAS[side].fov * Math.PI / 360) * Math.min(1, STADIUM_ASPECT / aspect)) * 180 / Math.PI;
}
