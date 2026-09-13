import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

// These checks follow the resulting joints and shoe contact points. A foot
// matching a moving IK target is insufficient evidence that it is planted.
const THREE=createRequire(import.meta.url)('three');
const V=(...n)=>new THREE.Vector3(...n);
const motion=load('lib/player-motion.ts');
const {createPlayer}=load('lib/player-model.ts');
const {poseBatter,posePitcher}=load('lib/player-pose.ts');
const failures=[],metrics={pitch:[],swing:[]};
const check=(condition,message)=>{if(!condition)failures.push(message);};
const distance=(a,b)=>Math.hypot(...a.map((v,i)=>v-b[i]));
const elapsed=(start,end,step=5)=>Array.from({length:Math.floor((end-start)/step)+1},(_,i)=>start+i*step);
const quat=q=>q.toArray();
const angle=(a,b)=>2*Math.acos(Math.min(1,Math.abs(a.reduce((sum,v,i)=>sum+v*b[i],0))));
function joints(model){
 model.root.updateWorldMatrix(true,true);
 const result={};
 for(const key of ['hips','torso','head','left','right','le','re','ll','rl','lk','rk','lf','rf']){
  const joint=model[key];result[key]={position:joint.getWorldPosition(V()).toArray(),rotation:quat(joint.getWorldQuaternion(new THREE.Quaternion()))};
  assert([...result[key].position,...result[key].rotation].every(Number.isFinite),`Non-finite posed joint ${key}`);
 }
 for(const [name,foot] of [['lead',model.lf],['rear',model.rf]]){
  // The cleat tips are .031 m below the ankle; two distinct points detect yaw
  // slips that an ankle-only assertion would miss.
  result[name+'Toe']=foot.localToWorld(V(0,-.031,.205)).toArray();
  result[name+'Heel']=foot.localToWorld(V(0,-.031,-.015)).toArray();
 }
 return result;
}
function maxDrift(frames,key,start,end){
 const interval=frames.filter(f=>f.time>=start&&f.time<=end),first=interval[0].joints[key];
 return Math.max(...interval.map(f=>distance(first,f.joints[key])));
}
function rotationMotion(frames){
 let largest=0,label='',largestPositionStep=0,positionJoint='';
 for(let i=1;i<frames.length;i++)for(const key of ['left','right','le','re','ll','rl','lk','rk','lf','rf']){
  const value=angle(frames[i-1].joints[key].rotation,frames[i].joints[key].rotation);
  if(value>largest){largest=value;label=key+' at '+frames[i].time;}
  const movement=distance(frames[i-1].joints[key].position,frames[i].joints[key].position);
  if(movement>largestPositionStep){largestPositionStep=movement;positionJoint=key+' at '+frames[i].time;}
 }
 return {radiansPerStep:largest,joint:label,metresPerStep:largestPositionStep,positionJoint};
}
function peakSpeed(frames,key,start,end,direction=1){
 let peak={time:start,speed:-Infinity};
 for(let i=1;i<frames.length;i++){
  const a=frames[i-1],b=frames[i];if(a.time<start||b.time>end)continue;
  const speed=(b.pose[key]-a.pose[key])/(b.time-a.time)*1000*direction;
  if(speed>peak.speed)peak={time:(a.time+b.time)/2,speed};
 }
 return peak;
}
function kneeFlex(frame,side='l'){
 const hip=V(...frame.joints[side+'l'].position),knee=V(...frame.joints[side+'k'].position),ankle=V(...frame.joints[side+'f'].position);
 return Math.PI-hip.sub(knee).angleTo(ankle.sub(knee));
}
function dispose(model){
 const geometries=new Set(),materials=new Set(),textures=new Set();
 model.root.traverse(object=>{if(object.geometry)geometries.add(object.geometry);if(object.material)for(const material of Array.isArray(object.material)?object.material:[object.material])materials.add(material);if(object.skeleton)object.skeleton.dispose();});
 for(const material of materials){for(const value of Object.values(material))if(value instanceof THREE.Texture)textures.add(value);material.dispose();}
 for(const geometry of geometries)geometry.dispose();for(const texture of textures)texture.dispose();
}

const windup=motion.PITCH_WINDUP_MS??1800,recovery=motion.PITCH_RECOVERY_MS??1050;
for(const style of ['overhand','sidearm','underhand']){
 const mirrored=[];
 for(const hand of [1,-1]){
  const model=createPlayer('#315476'),frames=[];model.root.position.z=-18.44;model.root.scale.x=hand;
  for(const time of elapsed(-windup,recovery)){
   const pose=motion.pitchingPose(time,style);posePitcher(model,pose,hand,style==='underhand');frames.push({time,pose,joints:joints(model)});
  }
  const initial=frames[0].pose,separation=frames.map(f=>distance(f.pose.hand,f.pose.glove));
  const lift=frames.find(f=>f.pose.lead[1]>initial.lead[1]+.07)?.time;
  const split=frames.find((f,i)=>separation[i]>.3)?.time;
  check(separation[0]<.18,`${style}/${hand}: hands must begin together`);
  check(Math.max(...separation)>.45,`${style}/${hand}: throwing hand must separate from glove`);
  check(lift!==undefined&&split!==undefined&&lift<split,`${style}/${hand}: leg lift must precede hand separation`);
  const highKnee=frames.reduce((best,f)=>f.pose.lead[1]>best.pose.lead[1]?f:best),liftFlex=kneeFlex(highKnee);
  check(liftFlex>.7,`${style}/${hand}: leg lift is a straight-leg kick (${liftFlex.toFixed(3)} rad knee flexion)`);
  const toeDrift=maxDrift(frames,'leadToe',-65,350),heelDrift=maxDrift(frames,'leadHeel',-65,350);
  check(Math.max(toeDrift,heelDrift)<.015,`${style}/${hand}: planted shoe slipped ${Math.max(toeDrift,heelDrift).toFixed(4)} m`);
  const hips=peakSpeed(frames,'hips',-900,0,-1),torso=peakSpeed(frames,'coil',-900,0,-1);
  check(hips.speed>.1&&torso.speed>.1&&hips.time<torso.time,`${style}/${hand}: pelvis must unwind before shoulders (${hips.time}/${torso.time} ms)`);
  const continuity=rotationMotion(frames);
  check(continuity.radiansPerStep<.42,`${style}/${hand}: joint flip ${continuity.joint}: ${continuity.radiansPerStep.toFixed(3)} rad/5ms`);
  const release=frames.find(f=>f.time===0),expected=motion.pitchReleasePosition(style,hand,185);
  posePitcher(model,release.pose,hand,style==='underhand');
  check(distance(model.re.localToWorld(V(0,-.34,0)).toArray(),expected)<.001,`${style}/${hand}: release separated from server origin`);
  metrics.pitch.push({style,hand,liftAt:lift,handSplitAt:split,liftKneeFlex:liftFlex,toeDrift,heelDrift,pelvisPeak:hips,torsoPeak:torso,continuity});
  mirrored.push(frames);dispose(model);
 }
 let symmetry=0;
 for(let i=0;i<mirrored[0].length;i++)for(const key of ['hips','torso','head','left','right','le','re','ll','rl','lk','rk','lf','rf']){
  const a=mirrored[0][i].joints[key].position,b=mirrored[1][i].joints[key].position;symmetry=Math.max(symmetry,distance([-a[0],a[1],a[2]],b));
 }
 check(symmetry<1e-8,`${style}: mirrored delivery joints differ by ${symmetry} m`);
}

const duration=motion.SWING_DURATION_MS;
for(const preparation of [0,.25,.5,1]){
 const ready=motion.swingPose(-1,{x:0,y:0},1,0,preparation),start=motion.swingPose(0,{x:0,y:0},1,0,preparation);
 for(const track of ['grip','axis'])check(distance(ready[track],start[track])<1e-9,`Input snaps ${track} from preparation ${preparation}`);
 const recovering=motion.swingPose(duration-.001,{x:0,y:0},1,0,preparation),finished=motion.swingPose(duration,{x:0,y:0},1,0,preparation);
 for(const track of ['grip','axis'])check(distance(recovering[track],finished[track])<1e-7,`Recovery snaps ${track} back to a stale load`);
}
for(const height of [155,185,215])for(const hand of [1,-1])for(const aim of [{x:0,y:0},{x:-1,y:-1},{x:1,y:1}]){
 const model=createPlayer('#315476',true),mirror=new THREE.Group(),bat=new THREE.Group(),frames=[];
 const scale=height/185,localAim={x:aim.x/scale,y:((1.05+aim.y*.55)/scale-1.05)/.55};
 mirror.scale.set(hand*scale,scale,scale);mirror.add(model.root);model.root.position.z=.06;
 for(const time of elapsed(0,duration)){
  const pose=motion.swingPose(time,localAim,hand,0,1);poseBatter(model,mirror,bat,pose);frames.push({time,pose,joints:joints(model)});
 }
 const hasSequence=frames.every(f=>Number.isFinite(f.pose.pelvisTurn)&&Number.isFinite(f.pose.torsoTurn));
 check(hasSequence,'Swing must expose independent finite pelvis and torso tracks');
 const pelvis=hasSequence?peakSpeed(frames,'pelvisTurn',0,motion.SWING_CONTACT_MS):null;
 const torso=hasSequence?peakSpeed(frames,'torsoTurn',0,motion.SWING_CONTACT_MS):null;
 if(hasSequence)check(pelvis.speed>.1&&torso.speed>.1&&pelvis.time<torso.time,`${height}/${hand}/${JSON.stringify(aim)}: swing shoulders accelerate before pelvis (${pelvis.time}/${torso.time} ms)`);
 const toeDrift=maxDrift(frames,'leadToe',60,350),heelDrift=maxDrift(frames,'leadHeel',60,350);
 check(Math.max(toeDrift,heelDrift)<.015,`${height}/${hand}/${JSON.stringify(aim)}: swing lead shoe slipped ${Math.max(toeDrift,heelDrift).toFixed(4)} m`);
 const continuity=rotationMotion(frames);
 check(continuity.radiansPerStep<.42,`${height}/${hand}/${JSON.stringify(aim)}: swing joint flip ${continuity.joint}: ${continuity.radiansPerStep.toFixed(3)} rad/5ms`);
 const target=V(aim.x*.5,1.05+aim.y*.55,0),impact=motion.swingPose(motion.SWING_CONTACT_MS,localAim,hand,0,1);
 check(V(...impact.barrel).multiplyScalar(scale).distanceTo(target)<1e-9,'Swing preparation changed the physical contact point');
 // The bat must travel through contact instead of stopping at an exact target.
 const before=motion.swingPose(motion.SWING_CONTACT_MS-2,localAim,hand,0,1).barrel,after=motion.swingPose(motion.SWING_CONTACT_MS+2,localAim,hand,0,1).barrel;
 const impactSpeed=distance(before,after)*scale/.004;
 check(impactSpeed>4&&impactSpeed<70,`${height}/${hand}/${JSON.stringify(aim)}: implausible stopped/explosive impact speed ${impactSpeed.toFixed(2)} m/s`);
 metrics.swing.push({height,hand,aim,pelvisPeak:pelvis,torsoPeak:torso,toeDrift,heelDrift,impactSpeed,continuity});
 dispose(model);
}
console.log(JSON.stringify({pass:failures.length===0,failures,metrics},null,2));
assert.equal(failures.length,0,failures.join('\n'));
