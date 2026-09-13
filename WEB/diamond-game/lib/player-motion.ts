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
export const SWING_DURATION_MS = 1120;
export const PITCH_WINDUP_MS = 1800;
export const PITCH_RECOVERY_MS = 1050;
export const BAT_SWEET_SPOT = .82;
const clamp01 = (v: number) => Math.max(0, Math.min(1, Number.isFinite(v) ? v : 0));
const unit = (v: Point3): Point3 => { const n = Math.hypot(...v); return n > 1e-9 ? v.map(x => x / n) as Point3 : [0, 1, 0]; };
const mirror = (v: Point3, hand: number): Point3 => [v[0] * hand, v[1], v[2]];
const mix = (a: number, b: number, t: number) => a + (b-a)*t;
const ease = (value:number,start:number,end:number) => { const t=clamp01((value-start)/(end-start)); return t*t*(3-2*t); };

/** Monotone, time-aware Hermite curves keep feet above the ground and velocities continuous. */
function track<K extends {t:number}>(keys: K[], elapsed: number) {
  let i=0; while(i<keys.length-2 && elapsed>keys[i+1].t)i++;
  const a=keys[i],b=keys[i+1],dt=b.t-a.t,t=(elapsed-a.t)/dt,t2=t*t,t3=t2*t;
  return (read:(key:K)=>number) => {
    const slope=(index:number)=>{
      if(index===0||index===keys.length-1)return 0;
      const previous=read(keys[index-1]),here=read(keys[index]),next=read(keys[index+1]);
      if((here-previous)*(next-here)<=0)return 0;
      const left=(here-previous)/(keys[index].t-keys[index-1].t),right=(next-here)/(keys[index+1].t-keys[index].t);
      return Math.sign(left)*Math.min(Math.abs((next-previous)/(keys[index+1].t-keys[index-1].t)),3*Math.abs(left),3*Math.abs(right));
    };
    return (2*t3-3*t2+1)*read(a)+(t3-2*t2+t)*dt*slope(i)+(-2*t3+3*t2)*read(b)+(t3-t2)*dt*slope(i+1);
  };
}

type SwingKey = {
  t:number;grip:Point3;axis:Point3;rootTurn:number;pelvisTurn:number;torsoTurn:number;
  load:number;frontLift:number;stride:number;heel:number;weightShift:number;
};
const swingReady:SwingKey={
  t:0,grip:[-.59,1.59,.28],axis:[-.55,.75,.38],rootTurn:0,pelvisTurn:0,torsoTurn:0,
  load:0,frontLift:0,stride:0,heel:0,weightShift:0,
};
/**
 * anticipation is the pitch-linked 0..1 load BEFORE a swing. Keep its value frozen
 * when a swing starts (also for bat trails) so the input frame has no pose jump.
 * Timing judgement remains at 95ms; recovery and loading never move that contact.
 */
export function swingPose(age:number,aim:{x:number;y:number},hand:1|-1,idleMs=0,anticipation=0) {
  const load=clamp01(anticipation);
  const loaded:SwingKey={...swingReady,
    grip:[mix(-.59,-.64,load),mix(1.59,1.48,load),mix(.28,.35,load)],
    axis:[mix(-.55,-.65,load),mix(.75,.69,load),mix(.38,.32,load)],
    rootTurn:-.035*load,pelvisTurn:-.10*load,torsoTurn:-.24*load,
    load,frontLift:.065*load,stride:.045*load,weightShift:.035*load,
  };
  const target:Point3=[aim.x*.5*hand,1.05+aim.y*.55,0];
  const impactAxis=unit([1,.08+aim.y*.55,-.12]);
  const impactGrip=target.map((value,index)=>value-impactAxis[index]*BAT_SWEET_SPOT) as Point3;
  const lift=(target[1]-1.05)*.65;
  const inside=clamp01(-target[0]/.5),highInside=inside*clamp01((target[1]-1)/.6);
  const insideArc=inside*.08;
  const keys:SwingKey[]=[
    loaded,
    // The pelvis opens while the hands stay behind the shoulder: no post-click knee kick.
    {...loaded,t:28,grip:[-.55+target[0]*.15,1.46-load*.11+Math.max(0,target[1]-1.05)*.22*(1-inside),.32],axis:[-.47,.62,.63],
      rootTurn:.015,pelvisTurn:.19,torsoTurn:-.12*load,load:load*.55,frontLift:.018*load,stride:.018*load,heel:.065,weightShift:.015*load},
    {...loaded,t:60,grip:[mix(-.62,impactGrip[0],.68)+insideArc,mix(1.50,impactGrip[1],.72)-highInside*.10,impactGrip[2]+.14],axis:[.43,.21+aim.y*.28,.85],
      rootTurn:.11,pelvisTurn:.46,torsoTurn:.25,load:0,frontLift:0,stride:0,heel:.12,weightShift:0},
    {...swingReady,t:SWING_CONTACT_MS,grip:impactGrip,axis:impactAxis,
      rootTurn:.18,pelvisTurn:.59,torsoTurn:.756,heel:.18},
    {...swingReady,t:145,grip:[mix(impactGrip[0],-.76,.55),mix(impactGrip[1],1.29+lift*.18,.55),-.11],axis:[.62,.19,-.78],
      rootTurn:.255,pelvisTurn:.84,torsoTurn:1.10,heel:.26,weightShift:-.025},
    {...swingReady,t:235,grip:[-.80,1.48+lift*.10,-.21],axis:[-.34,.36,-.87],
      rootTurn:.35,pelvisTurn:1.12,torsoTurn:1.47,heel:.34,weightShift:-.055},
    {...swingReady,t:370,grip:[-.84,1.70,-.12],axis:[-.80,.49,-.32],
      rootTurn:.41,pelvisTurn:1.30,torsoTurn:1.75,heel:.37,weightShift:-.06},
    {...swingReady,t:560,grip:[-.82,1.67,-.10],axis:[-.78,.53,-.33],
      rootTurn:.40,pelvisTurn:1.28,torsoTurn:1.71,heel:.35,weightShift:-.055},
    {...swingReady,t:780,grip:[-.64,1.25,.045],axis:[.18,.46,.87],
      rootTurn:.22,pelvisTurn:.73,torsoTurn:.93,heel:.16,weightShift:-.018},
    {...swingReady,t:960,grip:[-.61,1.43,.20],axis:[-.35,.81,.46],
      rootTurn:.05,pelvisTurn:.15,torsoTurn:.20,heel:.025},
    {...swingReady,t:SWING_DURATION_MS},
  ];
  const active=Number.isFinite(age)&&age>=0&&age<SWING_DURATION_MS;
  // A completed swing recovers to its neutral stance rather than snapping back to a stale load.
  const idle=age<0||!Number.isFinite(age)?loaded:swingReady;
  const sample=track(keys,active?age:0);
  const read=(get:(key:SwingKey)=>number)=>active?sample(get):get(idle);
  const grip=mirror([0,1,2].map(i=>read(k=>k.grip[i])) as Point3,hand);
  const axis=mirror(unit([0,1,2].map(i=>read(k=>unit(k.axis)[i])) as Point3),hand);
  // On a high inside pitch, the knob passes in front of the rear shoulder,
  // not through it. The short arc has zero value and velocity at contact.
  if(active&&age>20&&age<SWING_CONTACT_MS)grip[2]-=highInside*.22*Math.sin(Math.PI*(age-20)/(SWING_CONTACT_MS-20))**2;
  // Breathing fades as the hitter loads. Never add a waggle during the contact path.
  if(!active&&idleMs){
    const breath=Math.sin(idleMs/870)*.006*(1-load),waggle=Math.sin(idleMs/390)*.012*(1-load);
    grip[1]+=breath;
    const rested=unit([axis[0]+waggle*hand,axis[1],axis[2]+waggle*.5]);
    axis[0]=rested[0];axis[1]=rested[1];axis[2]=rested[2];
  }
  const barrel=grip.map((value,index)=>value+axis[index]*BAT_SWEET_SPOT) as Point3;
  let reach=active?age<SWING_CONTACT_MS?age/SWING_CONTACT_MS:Math.max(0,1-(age-SWING_CONTACT_MS)/255):0;
  reach=reach*reach*(3-2*reach);
  const torsoTurn=read(k=>k.torsoTurn);
  return {grip,axis,barrel,active,reach,bodyShift:target[0]*.64*reach,crouch:Math.max(0,-aim.y)*.24*reach,
    gazeTarget:mirror(target,hand),gazeWeight:active?ease(age,18,95)*(1-ease(age,280,760)):0,
    // turn remains available for callers using the old approximate rotation.
    turn:torsoTurn/1.05,load:read(k=>k.load),rootTurn:read(k=>k.rootTurn),pelvisTurn:read(k=>k.pelvisTurn),torsoTurn,
    frontLift:read(k=>k.frontLift),stride:read(k=>k.stride),heel:read(k=>k.heel),weightShift:read(k=>k.weightShift)};
}

type DeliveryKey={
  t:number;hand:Point3;glove:Point3;lead:Point3;trail:Point3;
  lift:number;lean:number;coil:number;hips:number;drop:number;forward:number;heel:number;
  sideBend:number;throwElbow:Point3;gloveElbow:Point3;hipShift:number;leadPitch:number;leadYaw:number;trailYaw:number;
};
const readyDelivery:DeliveryKey={
  t:-PITCH_WINDUP_MS,hand:[-.06,1.33,.26],glove:[.055,1.32,.26],
  lead:[.15,.034,.025],trail:[-.15,.034,-.025],
  lift:0,lean:.035,coil:0,hips:0,drop:.035,forward:0,heel:0,sideBend:0,
  throwElbow:[-1,.1,-.15],gloveElbow:[1,-.3,.1],hipShift:0,leadPitch:0,leadYaw:-.08,trailYaw:.08,
};
// The hands remain together over the raised knee. Separation happens as the stride begins.
const loadDelivery:DeliveryKey[]=[
  readyDelivery,
  {...readyDelivery,t:-1570,hand:[-.075,1.40,.27],glove:[.04,1.39,.27],lead:[.17,.048,-.005],hips:.10,coil:.08,hipShift:-.025,forward:-.015},
  {...readyDelivery,t:-1270,hand:[-.11,1.53,.26],glove:[.005,1.52,.26],lead:[.13,.37,-.10],lift:.55,hips:.32,coil:.39,hipShift:-.06,drop:.02,forward:-.04,leadPitch:.08,leadYaw:-.18},
  {...readyDelivery,t:-1000,hand:[-.13,1.57,.24],glove:[-.015,1.56,.24],lead:[.095,.64,-.14],lift:1,hips:.48,coil:.76,hipShift:-.075,drop:.025,forward:-.035,leadPitch:.14,leadYaw:-.25},
  {...readyDelivery,t:-800,hand:[-.24,1.34,.045],glove:[.11,1.43,.32],lead:[.12,.47,.04],lift:.74,hips:.42,coil:.79,hipShift:-.055,drop:.055,forward:.005,leadPitch:.15,leadYaw:-.20},
];
const overhandDelivery:DeliveryKey[]=[
  ...loadDelivery,
  {...readyDelivery,t:-560,hand:[-.43,1.36,-.15],glove:[.32,1.37,.44],lead:[.18,.21,.34],lift:.33,lean:.07,hips:.21,coil:.65,drop:.085,forward:.08,hipShift:-.035,leadPitch:.13,throwElbow:[-.85,-.1,-.4]},
  {...readyDelivery,t:-330,hand:[-.53,1.77,-.17],glove:[.39,1.30,.50],lead:[.20,.075,.58],lift:.08,lean:.11,hips:-.17,coil:.43,drop:.135,forward:.17,heel:.08,leadPitch:.07,throwElbow:[-.8,.18,-.4]},
  {...readyDelivery,t:-150,hand:[-.50,1.90,-.105],glove:[.30,1.20,.42],lead:[.20,.041,.635],lean:.20,hips:-.44,coil:.20,drop:.15,forward:.205,heel:.16,leadPitch:.025,throwElbow:[-.82,.25,-.3]},
  {...readyDelivery,t:-65,hand:[-.45,1.94,.025],glove:[.23,1.15,.36],lead:[.20,.034,.64],lean:.27,hips:-.48,coil:-.03,drop:.155,forward:.22,heel:.22,throwElbow:[-.8,.3,-.3]},
  {...readyDelivery,t:0,hand:PITCH_RELEASE.overhand,glove:[.18,1.10,.33],lead:[.20,.034,.64],lean:.34,hips:-.49,coil:-.28,drop:.155,forward:.235,heel:.26,throwElbow:[-.78,.3,-.25]},
  {...readyDelivery,t:85,hand:[-.015,1.43,.52],glove:[.13,1.03,.31],lead:[.20,.034,.64],trail:[-.15,.105,-.12],lean:.46,hips:-.53,coil:-.51,drop:.17,forward:.27,heel:.42,throwElbow:[-.65,-.2,-.2]},
  {...readyDelivery,t:210,hand:[.15,1.04,.47],glove:[.11,1.01,.31],lead:[.20,.034,.64],trail:[-.17,.34,-.33],lean:.54,hips:-.57,coil:-.61,drop:.18,forward:.29,heel:.57,throwElbow:[-.55,-.35,-.15]},
  {...readyDelivery,t:350,hand:[.12,1.11,.38],glove:[.11,1.04,.30],lead:[.20,.034,.64],trail:[-.19,.32,-.09],lean:.33,hips:-.47,coil:-.45,drop:.16,forward:.26,heel:.4,throwElbow:[-.65,-.35,-.1]},
  {...readyDelivery,t:550,hand:[.075,1.22,.36],glove:[.10,1.16,.29],lead:[.20,.034,.64],trail:[-.16,.06,.20],lean:.18,hips:-.25,coil:-.21,drop:.12,forward:.19,heel:.09},
  {...readyDelivery,t:770,hand:[-.03,1.29,.30],glove:[.07,1.26,.27],lead:[.17,.14,.31],trail:[-.15,.034,.11],lean:.065,hips:-.06,coil:-.04,drop:.07,forward:.07},
  {...readyDelivery,t:PITCH_RECOVERY_MS},
];
const sidearmDelivery:DeliveryKey[]=[
  ...loadDelivery.map(k=>({...k,lead:[k.lead[0],.034+(k.lead[1]-.034)*.8,k.lead[2]] as Point3,lift:k.lift*.8,coil:k.coil*1.08})),
  {...readyDelivery,t:-560,hand:[-.46,1.18,-.12],glove:[.34,1.33,.44],lead:[.22,.18,.39],lift:.25,lean:.10,hips:.25,coil:.72,drop:.11,forward:.11,hipShift:-.03,sideBend:.05,leadPitch:.11,throwElbow:[-.8,-.2,-.35]},
  {...readyDelivery,t:-330,hand:[-.69,1.40,-.13],glove:[.36,1.24,.49],lead:[.24,.064,.64],lift:.05,lean:.15,hips:-.19,coil:.47,drop:.16,forward:.21,heel:.1,sideBend:.08,leadPitch:.06,throwElbow:[-.9,.3,-.35]},
  {...readyDelivery,t:-150,hand:[-.76,1.42,-.04],glove:[.27,1.15,.39],lead:[.24,.039,.695],lean:.20,hips:-.48,coil:.20,drop:.19,forward:.25,heel:.19,sideBend:.10,leadPitch:.02,throwElbow:[-.9,.3,-.3]},
  {...readyDelivery,t:-65,hand:[-.765,1.43,.065],glove:[.22,1.09,.33],lead:[.24,.034,.70],lean:.225,hips:-.53,coil:-.08,drop:.195,forward:.265,heel:.25,sideBend:.13,throwElbow:[-.8,.3,-.3]},
  {...readyDelivery,t:0,hand:PITCH_RELEASE.sidearm,glove:[.17,1.04,.30],lead:[.24,.034,.70],lean:.24,hips:-.55,coil:-.36,drop:.19,forward:.28,heel:.29,sideBend:.14,throwElbow:[-.7,.25,-.3]},
  {...readyDelivery,t:85,hand:[-.34,1.14,.45],glove:[.13,1.0,.30],lead:[.24,.034,.70],trail:[-.16,.10,-.13],lean:.33,hips:-.6,coil:-.67,drop:.20,forward:.31,heel:.43,sideBend:.11,throwElbow:[-.65,.1,-.3]},
  {...readyDelivery,t:210,hand:[.14,1.09,.40],glove:[.11,1.00,.29],lead:[.24,.034,.70],trail:[-.19,.29,-.27],lean:.40,hips:-.62,coil:-.76,drop:.21,forward:.33,heel:.53,sideBend:.085,throwElbow:[-.55,-.25,-.25]},
  {...readyDelivery,t:350,hand:[.12,1.16,.37],glove:[.10,1.07,.29],lead:[.24,.034,.70],trail:[-.20,.25,-.06],lean:.29,hips:-.51,coil:-.54,drop:.17,forward:.27,heel:.35,sideBend:.06},
  {...readyDelivery,t:550,hand:[.06,1.24,.34],glove:[.09,1.18,.29],lead:[.24,.034,.70],trail:[-.17,.055,.22],lean:.15,hips:-.26,coil:-.25,drop:.125,forward:.20,heel:.08,sideBend:.03},
  {...readyDelivery,t:770,hand:[-.025,1.30,.29],glove:[.075,1.27,.27],lead:[.19,.12,.34],trail:[-.15,.034,.12],lean:.065,hips:-.065,coil:-.045,drop:.065,forward:.075,sideBend:.015},
  {...readyDelivery,t:PITCH_RECOVERY_MS},
];
const underhandDelivery:DeliveryKey[]=[
  ...loadDelivery.map(k=>({...k,lead:[k.lead[0],.034+(k.lead[1]-.034)*.58,k.lead[2]] as Point3,lift:k.lift*.58,coil:k.coil*.87,lean:k.lean+.025*k.lift,drop:k.drop+.035*k.lift})),
  {...readyDelivery,t:-560,hand:[-.41,.98,-.06],glove:[.31,1.24,.41],lead:[.22,.14,.40],lift:.20,lean:.20,hips:.23,coil:.57,drop:.15,forward:.12,hipShift:-.025,sideBend:.15,leadPitch:.10,throwElbow:[-.8,-.2,-.3]},
  {...readyDelivery,t:-330,hand:[-.55,.75,-.07],glove:[.33,1.09,.42],lead:[.24,.061,.66],lift:.04,lean:.32,hips:-.17,coil:.39,drop:.22,forward:.22,heel:.1,sideBend:.23,leadPitch:.055,throwElbow:[-.75,-.35,-.3]},
  {...readyDelivery,t:-150,hand:[-.64,.81,.00],glove:[.25,1.00,.35],lead:[.24,.039,.735],lean:.38,hips:-.43,coil:.18,drop:.26,forward:.275,heel:.20,sideBend:.27,leadPitch:.02,throwElbow:[-.75,-.35,-.25]},
  {...readyDelivery,t:-65,hand:[-.645,.93,.095],glove:[.20,.97,.32],lead:[.24,.034,.74],lean:.355,hips:-.49,coil:-.045,drop:.255,forward:.29,heel:.26,sideBend:.255,throwElbow:[-.7,-.3,-.23]},
  {...readyDelivery,t:0,hand:PITCH_RELEASE.underhand,glove:[.16,.97,.30],lead:[.24,.034,.74],lean:.32,hips:-.51,coil:-.29,drop:.235,forward:.305,heel:.30,sideBend:.235,throwElbow:[-.7,-.25,-.2]},
  {...readyDelivery,t:85,hand:[-.25,1.29,.44],glove:[.12,.97,.29],lead:[.24,.034,.74],trail:[-.17,.095,-.11],lean:.34,hips:-.55,coil:-.55,drop:.235,forward:.33,heel:.42,sideBend:.205,throwElbow:[-.65,.08,-.3]},
  {...readyDelivery,t:210,hand:[.12,1.24,.39],glove:[.10,1.00,.29],lead:[.24,.034,.74],trail:[-.19,.25,-.22],lean:.38,hips:-.57,coil:-.66,drop:.225,forward:.345,heel:.48,sideBend:.16,throwElbow:[-.6,.15,-.3]},
  {...readyDelivery,t:350,hand:[.10,1.23,.36],glove:[.10,1.07,.28],lead:[.24,.034,.74],trail:[-.20,.22,-.055],lean:.28,hips:-.47,coil:-.46,drop:.18,forward:.285,heel:.30,sideBend:.10},
  {...readyDelivery,t:550,hand:[.055,1.26,.34],glove:[.10,1.19,.28],lead:[.24,.034,.74],trail:[-.17,.055,.23],lean:.15,hips:-.24,coil:-.22,drop:.13,forward:.21,heel:.075,sideBend:.055},
  {...readyDelivery,t:770,hand:[-.025,1.30,.29],glove:[.075,1.27,.27],lead:[.19,.105,.36],trail:[-.15,.034,.12],lean:.075,hips:-.06,coil:-.045,drop:.07,forward:.08,sideBend:.02},
  {...readyDelivery,t:PITCH_RECOVERY_MS},
];

/** Right hand is -x. Every arm slot crosses the engine's exact release point at t=0. */
export function pitchingPose(relativeMs:number,delivery:DeliveryStyle|boolean="overhand") {
  const style=normalizeDelivery(delivery),keys=style==="underhand"?underhandDelivery:style==="sidearm"?sidearmDelivery:overhandDelivery;
  const elapsed=Number.isNaN(relativeMs)?-PITCH_WINDUP_MS:Math.max(-PITCH_WINDUP_MS,Math.min(PITCH_RECOVERY_MS,relativeMs));
  const sample=track(keys,elapsed);
  const point=(key:'hand'|'glove'|'lead'|'trail'|'throwElbow'|'gloveElbow')=>[0,1,2].map(index=>sample(pose=>pose[key][index])) as Point3;
  const heel=sample(k=>k.heel),trail=point('trail'),lead=point('lead'),hips=sample(k=>k.hips),hand=point('hand'),glove=point('glove');
  // The stance sits behind the release plane. Advancing the pelvis from here
  // puts the chest behind the ball at release instead of throwing from behind the head.
  const backset=.4;
  const handSetback=track([{t:-PITCH_WINDUP_MS,z:.3},{t:-560,z:.3},{t:-150,z:.075},{t:0,z:0},{t:210,z:0},{t:550,z:.15},{t:PITCH_RECOVERY_MS,z:.3}],elapsed)(k=>k.z);
  lead[2]-=backset;trail[2]-=backset;hand[2]-=handSetback;glove[2]-=.3;
  // Lift the heel around the rear toe, then the entire trailing leg leaves the ground.
  const trailYaw=sample(k=>k.trailYaw)+hips*.5;
  trail[0]-=Math.sin(trailYaw)*(1-Math.cos(heel))*.22;
  trail[1]+=Math.sin(heel)*.22;
  trail[2]-=Math.cos(trailYaw)*(1-Math.cos(heel))*.22;
  // These bend planes stay transverse to the complete wrist paths. A pole along
  // the release arm can cross zero and flip the elbow to the other side in one frame.
  const throwElbow:Point3=style==="overhand"?[-.7725,-.2951,.5623]:style==="sidearm"?[-.6945,-.6805,.2336]:[.09145,-.9427,.3209];
  const gloveElbow:Point3=style==="overhand"?[.8615,-.5059,-.0431]:style==="sidearm"?[.9761,-.2153,-.0280]:[.99863,.0433,.02924];
  return {style,hand,glove,lead,trail,throwElbow,gloveElbow,
    sideBend:sample(k=>k.sideBend),lift:sample(k=>k.lift),lean:sample(k=>k.lean),coil:sample(k=>k.coil),hips,
    drop:sample(k=>k.drop),forward:sample(k=>k.forward)-backset,heel,hipShift:sample(k=>k.hipShift),leadPitch:sample(k=>k.leadPitch),
    leadYaw:sample(k=>k.leadYaw),trailYaw};
}
