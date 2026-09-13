import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const THREE=createRequire(import.meta.url)('three'),V=(...values)=>new THREE.Vector3(...values);
const {createPlayer,createBat,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts');
const {poseBatter}=load('lib/player-pose.ts');
const {swingPose,SWING_CONTACT_MS,SWING_DURATION_MS}=load('lib/player-motion.ts');
const jointNames=['hips','torso','left','right','le','re','ll','rl','lk','rk','lf','rf'];
function setup(height,hand,bodyType){
 const actor=new THREE.Group(),mirror=new THREE.Group(),model=createPlayer('#ddd',true),bat=createBat(actor);
 actor.add(mirror);mirror.add(model.root);const scale=height/185;mirror.scale.set(hand*scale,scale,scale);model.root.position.z=.06;
 dressPlayer(model,playerAppearance('LG','검토',7,{bodyType}),hand);return{actor,mirror,model,bat,scale};
}
function dispose(actor){
 const geometries=new Set(),materials=new Set(),textures=new Set(),skeletons=new Set();
 actor.traverse(o=>{if(o.geometry)geometries.add(o.geometry);if(o.skeleton)skeletons.add(o.skeleton);for(const m of Array.isArray(o.material)?o.material:o.material?[o.material]:[]){materials.add(m);for(const v of Object.values(m))if(v?.isTexture)textures.add(v);}});
 geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());textures.forEach(t=>t.dispose());skeletons.forEach(s=>s.dispose());
}
let frames=0,maxImpact=0,maxStep=0,worstStep;
for(const height of[155,185,215])for(const hand of[-1,1])for(const bodyType of['lean','athletic','power']){
 const live=setup(height,hand,bodyType),control=setup(height,hand,bodyType),{actor,mirror,model,bat,scale}=live;
 for(const aim of[{x:0,y:0},{x:-1,y:-1},{x:1,y:-1},{x:-1,y:1},{x:1,y:1}]){
  const localAim={x:aim.x/scale,y:((1.05+aim.y*.55)/scale-1.05)/.55},target=V(aim.x*.5,1.05+aim.y*.55,0);
  let before=null;
  for(let age=-5;age<=1125;age+=5){
   const pose=swingPose(age,localAim,hand,0,1),frozen=JSON.stringify(pose);
   poseBatter(model,mirror,bat,pose);poseBatter(control.model,control.mirror,control.bat,{...pose,gazeWeight:0});actor.updateMatrixWorld(true);control.actor.updateMatrixWorld(true);
   assert.equal(JSON.stringify(pose),frozen,'Gaze must not mutate the frozen swing input');
   assert.deepEqual(bat.matrixWorld.elements,control.bat.matrixWorld.elements,'Actual bat path is unchanged');
   for(const key of jointNames){assert(model[key].position.equals(control.model[key].position));const a=model[key].quaternion.toArray(),b=control.model[key].quaternion.toArray();assert(a.every((v,i)=>Math.abs(v-b[i])<1e-12),`Gaze changes only the head: ${height}/${hand}/${key}/${age}`);}
   const eye=model.head.localToWorld(V(0,.062,.1)),forward=model.head.localToWorld(V(0,.062,1.1)).sub(eye).normalize();
   const error=forward.angleTo(target.clone().sub(eye))*180/Math.PI;assert(Number.isFinite(error));frames++;
   if(age===SWING_CONTACT_MS){maxImpact=Math.max(maxImpact,error);assert(error<12,`Face follows actual world contact within the neck limit: ${height}/${hand}/${JSON.stringify(aim)}: ${error}`);}
   if(!pose.gazeWeight)assert(model.head.quaternion.equals(control.model.head.quaternion),'Ready/completed pose unchanged');
   if(before){const step=before.angleTo(model.head.quaternion);if(step>maxStep){maxStep=step;worstStep={height,hand,bodyType,aim,age};}}
   before=model.head.quaternion.clone();
  }
  const pose=swingPose(120,localAim,hand,0,1);poseBatter(model,mirror,bat,pose);const first=model.head.quaternion.clone();poseBatter(model,mirror,bat,swingPose(-1,{x:0,y:0},hand));poseBatter(model,mirror,bat,pose);assert(model.head.quaternion.equals(first),'Seeking back into a swing is deterministic');
 }
 dispose(live.actor);dispose(control.actor);
}
assert(maxStep<.3,`No abrupt head reversal at 5ms boundaries: ${maxStep} ${JSON.stringify(worstStep)}`);
for(const age of[-Infinity,-1,0,SWING_DURATION_MS,Infinity])assert.equal(swingPose(age,{x:0,y:0},1).gazeWeight,0);
console.log(`PASS batting gaze: ${frames} frames, real world contact at all height/hand/body corners, untouched bat/joints/input, ready/recovery and deterministic seek`);
console.log(JSON.stringify({maxImpactDegrees:maxImpact,maxHeadRadiansPer5ms:maxStep,worstStep}));
