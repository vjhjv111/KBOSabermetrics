// World axes: home plate z=0, mound z<0; a right-handed batter stands at x<0.
export type Point3 = [number, number, number];
export const SWING_CONTACT_MS = 95;
export const SWING_DURATION_MS = 980;
export const BAT_SWEET_SPOT = .82;
const unit = (v: Point3): Point3 => { const n = Math.hypot(...v); return v.map(x => x / n) as Point3; };
const mix = (a: Point3, b: Point3, t: number): Point3 => a.map((x, i) => x + (b[i] - x) * t) as Point3;
const mirror = (v: Point3, hand: number): Point3 => [v[0] * hand, v[1], v[2]];
type Key = { t: number; grip: Point3; axis: Point3; turn: number; load: number };

/** Two hands stay on one grip. The barrel sweeps forward, then wraps over the lead shoulder. */
export function swingPose(age: number, aim: { x: number; y: number }, hand: 1 | -1) {
  const target: Point3 = [aim.x * .5 * hand, 1.05 + aim.y * .55, 0];
  const impactAxis = unit([1, .08 + aim.y * .55, -.12]);
  const impactGrip = target.map((x, i) => x - impactAxis[i] * BAT_SWEET_SPOT) as Point3;
  const lift = (target[1] - 1.05) * .65;
  const ready: Key = { t: 0, grip: [-.49, 1.40, .28], axis: [.12, .94, .32], turn: 0, load: 0 };
  const keys: Key[] = [
    ready,
    { t: 30, grip: [-.53, 1.43, .35], axis: [-.06, .92, .39], turn: -.16, load: 1 },
    { t: SWING_CONTACT_MS, grip: impactGrip, axis: impactAxis, turn: .72, load: .15 },
    { t: 185, grip: [-.77, 1.34 + lift * .2, -.22], axis: [.18, .25, -.95], turn: 1.28, load: 0 },
    { t: 350, grip: [-.83, 1.66, -.14], axis: [-.76, .48, -.43], turn: 1.65, load: 0 },
    { t: 540, grip: [-.83, 1.66, -.14], axis: [-.76, .48, -.43], turn: 1.65, load: 0 },
    { t: 770, grip: [-.64, 1.20, .04], axis: [.38, .34, .86], turn: .6, load: 0 },
    { ...ready, t: SWING_DURATION_MS },
  ];
  const active = age >= 0 && age < SWING_DURATION_MS;
  const elapsed = active ? age : 0;
  let index = 0;
  while (index < keys.length - 2 && elapsed > keys[index + 1].t) index++;
  const a = keys[index], b = keys[index + 1], t = (elapsed - a.t) / (b.t - a.t);
  // Time-aware Hermite tangents preserve velocity through the contact frame.
  const sample = (values: number[]) => {
    const tangent = (i: number) => {
      if (i === 0 || i === keys.length - 1 || values[i] === values[i - 1] || values[i] === values[i + 1]) return 0;
      return (values[i + 1] - values[i - 1]) / (keys[i + 1].t - keys[i - 1].t);
    };
    const dt = b.t - a.t, t2 = t * t, t3 = t2 * t;
    return (2*t3-3*t2+1)*values[index]+(t3-2*t2+t)*dt*tangent(index)+(-2*t3+3*t2)*values[index+1]+(t3-t2)*dt*tangent(index+1);
  };
  const grip = mirror([0,1,2].map(i => sample(keys.map(k => k.grip[i]))) as Point3, hand);
  const axis = mirror(unit([0,1,2].map(i => sample(keys.map(k => unit(k.axis)[i]))) as Point3), hand);
  const barrel = grip.map((x, i) => x + axis[i] * BAT_SWEET_SPOT) as Point3;
  let reach = active ? elapsed < SWING_CONTACT_MS ? elapsed / SWING_CONTACT_MS : Math.max(0,1-(elapsed-SWING_CONTACT_MS)/255) : 0;
  reach = reach*reach*(3-2*reach);
  return { grip, axis, barrel, active, reach, bodyShift: target[0] * .64 * reach, crouch: Math.max(0,-aim.y) * .24 * reach, turn: sample(keys.map(k=>k.turn)), load: sample(keys.map(k=>k.load)) };
}

/** Anatomical right hand is -x on a player whose chest faces +z. */
export function pitchingPose(relativeMs: number, underhand: boolean) {
  const ready: Point3 = [-.06, 1.33, .26];
  const release: Point3 = underhand ? [-.58, 1.08, .22] : [-.33, 1.84, .12];
  const keys = underhand
    ? [{ t: -900, p: ready }, { t: -410, p: [-.56, .72, -.38] as Point3 }, { t: -130, p: [-.69, .60, -.15] as Point3 }, { t: 0, p: release }, { t: 230, p: [.13, 1.15, .44] as Point3 }, { t: 640, p: ready }]
    : [{ t: -900, p: ready }, { t: -430, p: [-.46, 1.78, -.25] as Point3 }, { t: -145, p: [-.53, 2.03, -.18] as Point3 }, { t: 0, p: release }, { t: 230, p: [.20, .98, .47] as Point3 }, { t: 640, p: ready }];
  if (relativeMs < -900 || relativeMs >= 640) return { hand: ready, lift: 0, lean: 0 };
  let i = 0; while (i < keys.length - 2 && relativeMs > keys[i + 1].t) i++;
  const a = keys[i], b = keys[i + 1], t = (relativeMs - a.t) / (b.t - a.t);
  return { hand: mix(a.p, b.p, t), lift: relativeMs < 0 ? Math.sin(Math.PI * (relativeMs + 900) / 900) : 0, lean: relativeMs > 0 ? Math.sin(Math.PI * relativeMs / 640) * .35 : underhand ? .16 : 0 };
}
