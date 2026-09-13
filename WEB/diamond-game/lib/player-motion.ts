// World axes: home plate z=0, mound z<0; a right-handed batter stands at x<0.
export type Point3 = [number, number, number];
export type DeliveryStyle = "overhand" | "sidearm" | "underhand";
export const MOUND_HEIGHT = .254;
/** Explicit profile settings take precedence over descriptive roster text. */
export function normalizeDelivery(value?: string | boolean | null, description = ""): DeliveryStyle {
  if (typeof value === "boolean") return value ? "underhand" : "overhand";
  const parse = (text: string): DeliveryStyle | undefined => {
    const compact = text.toLowerCase().replace(/[\s_-]/g, "");
    if (/underhand|submarine|언더|잠수|^[좌우]언/.test(compact)) return "underhand";
    if (/sidearm|사이드|^[좌우]사/.test(compact)) return "sidearm";
    if (/overhand|overarm|오버|일반/.test(compact)) return "overhand";
  };
  return parse(value ?? "") ?? parse(description) ?? "overhand";
}
export function profileThrowingHand(value?: string | null, description = ""): "L" | "R" | undefined {
  const hand = value?.trim().toLowerCase();
  if (hand === "l" || hand === "left" || hand?.startsWith("좌")) return "L";
  if (hand === "r" || hand === "right" || hand?.startsWith("우")) return "R";
  const text = description.trim();
  return text.startsWith("좌") ? "L" : text.startsWith("우") ? "R" : undefined;
}
export const PITCH_RELEASE: Record<DeliveryStyle, Point3> = {
  overhand: [-.33, 1.84, .12], sidearm: [-.72, 1.43, .20], underhand: [-.58, 1.08, .22],
};
export function pitcherBodyScale(heightCm?: number | null) {
  return typeof heightCm === "number" && Number.isFinite(heightCm) && heightCm > 0 && heightCm <= 250 ? heightCm / 185 : 1;
}
/** World position of the baseball center, shared by the release animation and pitch engine. */
export function pitchReleasePosition(style: DeliveryStyle | boolean, hand: 1 | -1, heightCm?: number | null): Point3 {
  const local = PITCH_RELEASE[normalizeDelivery(style)], scale = pitcherBodyScale(heightCm);
  return [local[0] * hand * scale, MOUND_HEIGHT + local[1] * scale, -18.44 + local[2] * scale];
}
export const SWING_CONTACT_MS = 95;
export const SWING_DURATION_MS = 980;
export const BAT_SWEET_SPOT = .82;
const unit = (v: Point3): Point3 => { const n = Math.hypot(...v); return v.map(x => x / n) as Point3; };
const mirror = (v: Point3, hand: number): Point3 => [v[0] * hand, v[1], v[2]];
type Key = { t: number; grip: Point3; axis: Point3; turn: number; load: number };

/** Two hands stay on one grip. The barrel sweeps forward, then wraps over the lead shoulder. */
export function swingPose(age: number, aim: { x: number; y: number }, hand: 1 | -1, idleMs = 0) {
  const target: Point3 = [aim.x * .5 * hand, 1.05 + aim.y * .55, 0];
  const impactAxis = unit([1, .08 + aim.y * .55, -.12]);
  const impactGrip = target.map((x, i) => x - impactAxis[i] * BAT_SWEET_SPOT) as Point3;
  const lift = (target[1] - 1.05) * .65;
  const ready: Key = { t: 0, grip: [-.59, 1.59, .28], axis: [-.55, .75, .38], turn: 0, load: 0 };
  const keys: Key[] = [
    ready,
    { t: 30, grip: [-.62, 1.62, .32], axis: [-.59, .73, .35], turn: -.16, load: 1 },
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
  // Small wrist-led bat waggle between pitches; the timed swing and contact path stay exact.
  if (!active && idleMs) {
    const breath = Math.sin(idleMs / 870), waggle = Math.sin(idleMs / 390) * .012;
    grip[1] += breath * .006;
    const rested = unit([axis[0] + waggle * hand, axis[1], axis[2] + waggle * .5]);
    axis[0] = rested[0]; axis[1] = rested[1]; axis[2] = rested[2];
  }
  const barrel = grip.map((x, i) => x + axis[i] * BAT_SWEET_SPOT) as Point3;
  let reach = active ? elapsed < SWING_CONTACT_MS ? elapsed / SWING_CONTACT_MS : Math.max(0,1-(elapsed-SWING_CONTACT_MS)/255) : 0;
  reach = reach*reach*(3-2*reach);
  return { grip, axis, barrel, active, reach, bodyShift: target[0] * .64 * reach, crouch: Math.max(0,-aim.y) * .24 * reach, turn: sample(keys.map(k=>k.turn)), load: sample(keys.map(k=>k.load)) };
}

type DeliveryKey = {
  t: number; hand: Point3; glove: Point3; lead: Point3; trail: Point3;
  lift: number; lean: number; coil: number; hips: number; drop: number; forward: number; heel: number;
  sideBend: number; throwElbow: Point3; gloveElbow: Point3;
};
const readyDelivery: DeliveryKey = {
  t: -900, hand: [-.06,1.33,.26], glove: [.055,1.32,.26],
  lead: [.15,.034,.025], trail: [-.15,.034,-.025],
  lift: 0, lean: .035, coil: 0, hips: 0, drop: .035, forward: 0, heel: 0,
  sideBend: 0, throwElbow: [-1,.1,-.15], gloveElbow: [1,-.3,.1],
};
const overhandDelivery: DeliveryKey[] = [
  readyDelivery,
  {...readyDelivery,t:-710,hand:[-.07,1.45,.28],glove:[.045,1.44,.28],lead:[.15,.24,.0],lift:.45,coil:.16,hips:.09,drop:.022},
  {...readyDelivery,t:-440,hand:[-.46,1.78,-.25],glove:[.24,1.47,.28],lead:[.15,.57,-.065],lift:1,coil:.37,hips:.18,drop:.02,forward:-.025},
  {...readyDelivery,t:-145,hand:[-.49,1.96,-.09],glove:[.37,1.28,.5],lead:[.2,.15,.39],lift:.23,lean:.1,coil:.2,hips:-.12,drop:.085,forward:.055,heel:.08},
  {...readyDelivery,t:0,hand:PITCH_RELEASE.overhand,glove:[.2,1.18,.31],lead:[.2,.034,.49],lean:.16,coil:-.13,hips:-.22,drop:.12,forward:.1,heel:.16},
  {...readyDelivery,t:180,hand:[.18,1.04,.48],glove:[.17,1.06,.29],lead:[.2,.034,.49],trail:[-.14,.18,-.28],lean:.36,coil:-.3,hips:-.24,drop:.16,forward:.16,heel:.36},
  {...readyDelivery,t:350,hand:[.17,1.08,.4],glove:[.13,1.09,.3],lead:[.2,.034,.49],trail:[-.15,.15,.0],lean:.23,coil:-.15,hips:-.13,drop:.14,forward:.13,heel:.17},
  {...readyDelivery,t:500,hand:[.025,1.28,.31],glove:[.09,1.23,.28],lead:[.17,.115,.235],lean:.08,coil:-.04,hips:-.035,drop:.065,forward:.045},
  {...readyDelivery,t:640},
];
// The sidearm arm slot stays beside the shoulder while the trunk rotates across the planted front leg.
const sidearmDelivery: DeliveryKey[] = [
  readyDelivery,
  {...readyDelivery,t:-710,hand:[-.08,1.41,.25],glove:[.04,1.4,.25],lead:[.17,.2,.015],lift:.35,coil:.2,hips:.12,drop:.025},
  {...readyDelivery,t:-440,hand:[-.53,1.43,-.22],glove:[.29,1.38,.32],lead:[.18,.43,-.08],lift:.75,lean:.075,coil:.42,hips:.22,drop:.04,forward:-.035,sideBend:.04,throwElbow:[-.8,.3,-.35]},
  {...readyDelivery,t:-145,hand:[-.75,1.40,-.13],glove:[.3,1.2,.43],lead:[.24,.11,.4],lift:.16,lean:.14,coil:.24,hips:-.16,drop:.11,forward:.07,heel:.12,sideBend:.08,throwElbow:[-.9,.25,-.35]},
  {...readyDelivery,t:0,hand:PITCH_RELEASE.sidearm,glove:[.18,1.1,.29],lead:[.24,.034,.53],lean:.21,coil:-.22,hips:-.28,drop:.15,forward:.13,heel:.23,sideBend:.11,throwElbow:[-.7,.25,-.3]},
  {...readyDelivery,t:180,hand:[.12,1.12,.5],glove:[.14,1.04,.27],lead:[.24,.034,.53],trail:[-.18,.17,-.22],lean:.32,coil:-.4,hips:-.3,drop:.18,forward:.18,heel:.33,sideBend:.08,throwElbow:[-.55,.2,-.25]},
  {...readyDelivery,t:350,hand:[.18,1.17,.38],glove:[.1,1.09,.28],lead:[.24,.034,.53],trail:[-.17,.12,.04],lean:.22,coil:-.24,hips:-.17,drop:.14,forward:.13,heel:.16,sideBend:.04},
  {...readyDelivery,t:500,hand:[.015,1.27,.30],glove:[.075,1.25,.28],lead:[.19,.095,.23],lean:.085,coil:-.07,hips:-.05,drop:.075,forward:.05,sideBend:.02},
  {...readyDelivery,t:640},
];
// The underhand delivery lowers the center of mass and sweeps upward from below the waist.
const underhandDelivery: DeliveryKey[] = [
  readyDelivery,
  {...readyDelivery,t:-710,hand:[-.07,1.39,.27],glove:[.045,1.38,.27],lead:[.16,.17,.015],lift:.3,lean:.075,coil:.13,hips:.08,drop:.05},
  {...readyDelivery,t:-440,hand:[-.48,1.1,-.08],glove:[.24,1.31,.31],lead:[.19,.36,-.065],lift:.62,lean:.21,coil:.3,hips:.18,drop:.10,forward:-.025,sideBend:.20,throwElbow:[-.8,-.1,-.35]},
  {...readyDelivery,t:-145,hand:[-.63,.84,.025],glove:[.29,1.05,.37],lead:[.24,.09,.43],lift:.12,lean:.34,coil:.18,hips:-.16,drop:.21,forward:.085,heel:.13,sideBend:.23,throwElbow:[-.75,-.35,-.25]},
  {...readyDelivery,t:0,hand:PITCH_RELEASE.underhand,glove:[.16,1.01,.28],lead:[.24,.034,.55],lean:.29,coil:-.15,hips:-.27,drop:.20,forward:.15,heel:.25,sideBend:.21,throwElbow:[-.7,-.25,-.2]},
  {...readyDelivery,t:180,hand:[.13,1.15,.44],glove:[.13,1.01,.27],lead:[.24,.034,.55],trail:[-.18,.13,-.20],lean:.35,coil:-.34,hips:-.3,drop:.22,forward:.20,heel:.30,sideBend:.17,throwElbow:[-.65,.1,-.3]},
  {...readyDelivery,t:350,hand:[.08,1.19,.37],glove:[.1,1.07,.27],lead:[.24,.034,.55],trail:[-.17,.11,.03],lean:.25,coil:-.21,hips:-.18,drop:.16,forward:.14,heel:.15,sideBend:.12},
  {...readyDelivery,t:500,hand:[.025,1.27,.30],glove:[.09,1.24,.28],lead:[.19,.085,.25],lean:.11,coil:-.065,hips:-.05,drop:.08,forward:.055,sideBend:.045},
  {...readyDelivery,t:640},
];

/** Right hand is -x. Hand and glove tracks are continuous through release, with fixed release coordinates. */
export function pitchingPose(relativeMs: number, delivery: DeliveryStyle | boolean = "overhand") {
  const style=normalizeDelivery(delivery),keys=style==="underhand"?underhandDelivery:style==="sidearm"?sidearmDelivery:overhandDelivery;
  const elapsed=Number.isNaN(relativeMs)?-900:Math.max(-900,Math.min(640,relativeMs));
  let i=0;while(i<keys.length-2&&elapsed>keys[i+1].t)i++;
  const a=keys[i],b=keys[i+1],dt=b.t-a.t,t=(elapsed-a.t)/dt,t2=t*t,t3=t2*t;
  const sample=(read:(key:DeliveryKey)=>number)=>{
    const slope=(index:number)=>{
      if(index===0||index===keys.length-1)return 0;
      const prev=read(keys[index-1]),here=read(keys[index]),next=read(keys[index+1]);
      if((here-prev)*(next-here)<=0)return 0;
      // Bound the derivative at a short interval so feet never overshoot the ground.
      const left=(here-prev)/(keys[index].t-keys[index-1].t),right=(next-here)/(keys[index+1].t-keys[index].t);
      return Math.sign(left)*Math.min(Math.abs((next-prev)/(keys[index+1].t-keys[index-1].t)),3*Math.abs(left),3*Math.abs(right));
    };
    return (2*t3-3*t2+1)*read(a)+(t3-2*t2+t)*dt*slope(i)+(-2*t3+3*t2)*read(b)+(t3-t2)*dt*slope(i+1);
  };
  const point=(key:'hand'|'glove'|'lead'|'trail'|'throwElbow'|'gloveElbow')=>[0,1,2].map(index=>sample(pose=>pose[key][index])) as Point3;
  const heel=sample(k=>k.heel),trail=point('trail');
  trail[1]+=Math.sin(heel)*.22;
  return {style,hand:point('hand'),glove:point('glove'),lead:point('lead'),trail,throwElbow:point('throwElbow'),gloveElbow:point('gloveElbow'),sideBend:sample(k=>k.sideBend),
    lift:sample(k=>k.lift),lean:sample(k=>k.lean),coil:sample(k=>k.coil),hips:sample(k=>k.hips),
    drop:sample(k=>k.drop),forward:sample(k=>k.forward),heel};
}
