// World axes: home plate z=0, mound z<0; a right-handed batter stands at x<0.
export type Point3 = [number, number, number];
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
};
const readyDelivery: DeliveryKey = {
  t: -900, hand: [-.06,1.33,.26], glove: [.055,1.32,.26],
  lead: [.15,.034,.025], trail: [-.15,.034,-.025],
  lift: 0, lean: .035, coil: 0, hips: 0, drop: .035, forward: 0, heel: 0,
};
const overhandDelivery: DeliveryKey[] = [
  readyDelivery,
  {...readyDelivery,t:-710,hand:[-.07,1.45,.28],glove:[.045,1.44,.28],lead:[.15,.24,.0],lift:.45,coil:.16,hips:.09,drop:.022},
  {...readyDelivery,t:-440,hand:[-.46,1.78,-.25],glove:[.24,1.47,.28],lead:[.15,.57,-.065],lift:1,coil:.37,hips:.18,drop:.02,forward:-.025},
  {...readyDelivery,t:-145,hand:[-.49,1.96,-.09],glove:[.37,1.28,.5],lead:[.2,.15,.39],lift:.23,lean:.1,coil:.2,hips:-.12,drop:.085,forward:.055,heel:.08},
  {...readyDelivery,t:0,hand:[-.33,1.84,.12],glove:[.2,1.18,.31],lead:[.2,.034,.49],lean:.16,coil:-.13,hips:-.22,drop:.12,forward:.1,heel:.16},
  {...readyDelivery,t:180,hand:[.18,1.04,.48],glove:[.17,1.06,.29],lead:[.2,.034,.49],trail:[-.14,.18,-.28],lean:.36,coil:-.3,hips:-.24,drop:.16,forward:.16,heel:.36},
  {...readyDelivery,t:350,hand:[.17,1.08,.4],glove:[.13,1.09,.3],lead:[.2,.034,.49],trail:[-.15,.15,.0],lean:.23,coil:-.15,hips:-.13,drop:.14,forward:.13,heel:.17},
  {...readyDelivery,t:500,hand:[.025,1.28,.31],glove:[.09,1.23,.28],lead:[.17,.115,.235],lean:.08,coil:-.04,hips:-.035,drop:.065,forward:.045},
  {...readyDelivery,t:640},
];
const underhandDelivery: DeliveryKey[] = overhandDelivery.map(key => {
  const poses: Record<number, Point3> = {[-440]:[-.48,1.1,-.08],[-145]:[-.63,.84,.025],0:[-.58,1.08,.22],180:[.13,1.15,.44],350:[.08,1.19,.37]};
  return {...key,hand:poses[key.t]??key.hand,lean:key.lean+(key.t>-710&&key.t<640?.11:0)};
});

/** Right hand is -x. Hand and glove tracks are continuous through release, with fixed release coordinates. */
export function pitchingPose(relativeMs: number, underhand: boolean) {
  const keys=underhand?underhandDelivery:overhandDelivery;
  const elapsed=Math.max(-900,Math.min(640,relativeMs));
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
  const point=(key:'hand'|'glove'|'lead'|'trail')=>[0,1,2].map(index=>sample(pose=>pose[key][index])) as Point3;
  const heel=sample(k=>k.heel),trail=point('trail');
  trail[1]+=Math.sin(heel)*.22;
  return {hand:point('hand'),glove:point('glove'),lead:point('lead'),trail,
    lift:sample(k=>k.lift),lean:sample(k=>k.lean),coil:sample(k=>k.coil),hips:sample(k=>k.hips),
    drop:sample(k=>k.drop),forward:sample(k=>k.forward),heel};
}
