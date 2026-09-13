import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {load} from './load-ts.mjs';
import {createRequire} from 'node:module';
const THREE=createRequire(import.meta.url)('three');
const motion=load('lib/player-motion.ts'),engine=load('lib/action-engine.ts');
const profiles=JSON.parse(fs.readFileSync('lib/player-profiles.json','utf8')).players;
const byName=name=>Object.entries(profiles).find(([,p])=>p.name===name)[0];
assert(engine.batsLeft(byName('디아즈'),byName('폰세')));
assert(!engine.batsLeft(byName('안현민'),byName('폰세')));
for(const name of ['김주원','레이예스']){assert(engine.batsLeft(byName(name),byName('폰세')));assert(!engine.batsLeft(byName(name),byName('양현종')));}
assert(engine.isUnderhand(byName('고영표')));assert(!engine.throwsLeft(byName('폰세')));assert(engine.throwsLeft(byName('양현종')));
const {createPlayer:player}=load('lib/player-model.ts');
const {poseBatter,posePitcher}=load('lib/player-pose.ts');
const {playerDimensions}=load('lib/player-appearance.ts');
const V=(...n)=>new THREE.Vector3(...n);let worstWrist=0,skinPoseChecks=0,worstPitchWrist=0,worstPlantedFoot=0;
function inspectSkinPose(model){
 model.root.updateWorldMatrix(true,true);model.root.updateMatrixWorld(true);
 const origin=model.root.getWorldPosition(V()),point=V();
 model.root.traverse(object=>{
  if(!object.isSkinnedMesh)return;object.skeleton.update();
  for(const value of object.skeleton.boneMatrices)assert(Number.isFinite(value),'Animated bone matrices must remain finite');
  for(let vertex=0;vertex<object.geometry.attributes.position.count;vertex++){
   object.getVertexPosition(vertex,point).applyMatrix4(object.matrixWorld);
   assert(point.toArray().every(Number.isFinite)&&point.distanceTo(origin)<3,'Animated trousers must remain finite and attached to the player');
  }
 });
 skinPoseChecks++;
}
for(const hand of [1,-1]){
 const b=player('#224466',true),mirror=new THREE.Group(),bat=new THREE.Group();mirror.scale.x=hand;mirror.add(b.root);b.root.position.z=.06;
 for(const anticipation of [0,1])for(const x of [-1,-.5,0,.5,1])for(const y of [-1,-.5,0,.5,1])for(let age=0;age<=motion.SWING_DURATION_MS;age+=5){
  const pose=motion.swingPose(age,{x,y},hand,0,anticipation),wrists=poseBatter(b,mirror,bat,pose);
  // The arm's skin wrist ring is below/forward of the elbow pivot. Its centre
  // must meet the modeled glove cuff, rather than the old bat-axis placeholder.
  for(const [arm,target] of [[b.le,wrists.leftWrist],[b.re,wrists.rightWrist]]){const error=arm.localToWorld(V(0,-.341,.006*playerDimensions(b.appearance).depthScale)).distanceTo(target);worstWrist=Math.max(worstWrist,error);assert(error<.01,`Detached wrist: ${JSON.stringify({hand,x,y,age,error})}`);}
  assert(Math.abs(Math.hypot(...pose.axis)-1)<1e-10);
  if(age===motion.SWING_CONTACT_MS)assert(V(...pose.barrel).distanceTo(V(x*.5,1.05+y*.55,0))<1e-10);
  if(age===motion.SWING_CONTACT_MS){
   const planted=mirror.worldToLocal(b.lf.getWorldPosition(V()));
   const expected=V(-.925,.034,-.255);
   worstPlantedFoot=Math.max(worstPlantedFoot,planted.distanceTo(expected));
   assert(planted.distanceTo(expected)<.01,'The lead foot must brace at contact rather than slide with the torso');
  }
  if(Number.isInteger(x)&&Number.isInteger(y)&&[0,motion.SWING_CONTACT_MS,185,350,770].includes(age))inspectSkinPose(b);
 }
}
const h=.001,t=motion.SWING_CONTACT_MS,p0=V(...motion.swingPose(t,{x:0,y:0},1).barrel),p1=V(...motion.swingPose(t-h,{x:0,y:0},1).barrel),p2=V(...motion.swingPose(t+h,{x:0,y:0},1).barrel);
assert(p0.clone().sub(p1).divideScalar(h/1000).distanceTo(p2.sub(p0).divideScalar(h/1000))<.01,'Impact velocity must remain continuous');
for(const name of ['폰세','고영표','양현종']){
 const id=byName(name),scale=engine.playerProfile(id,'pitcher').heightCm/185,hand=engine.throwsLeft(id)?-1:1,underhand=engine.isUnderhand(id),p=player('#224466');
 p.root.position.z=-18.44;p.root.scale.set(hand*scale,scale,scale);const pose=motion.pitchingPose(0,underhand);
 posePitcher(p,pose,hand,underhand);const actual=p.re.localToWorld(V(0,-.34,0));
 const pitch=engine.createPitch({pitcher:id,pitchCount:0,pace:'practice',mode:'ai'},engine.arsenal(id)[0].type,{x:0,y:0},1,0),release=engine.ballPosition(pitch,pitch.releaseAt);
 assert(actual.distanceTo(V(release.x,release.y,release.z))<1e-10);assert.equal(Math.sign(release.x),-hand);
 for(let time=-motion.PITCH_WINDUP_MS;time<=motion.PITCH_RECOVERY_MS;time+=5){
  const delivery=motion.pitchingPose(time,underhand);posePitcher(p,delivery,hand,underhand);
  for(const [arm,position] of [[p.re,delivery.hand],[p.le,delivery.glove]]){
   const error=arm.localToWorld(V(0,-.34,0)).distanceTo(p.root.localToWorld(V(...position)));
   worstPitchWrist=Math.max(worstPitchWrist,error);assert(error<.01,`Delivery wrist detached at ${name} ${time}: ${error}`);
  }
  assert(delivery.lead[1]>=.034-1e-10&&delivery.trail[1]>=.034-1e-10,'Foot tracks must not overshoot the ground');
  if(time>=0&&time<=350){
   const error=p.lf.getWorldPosition(V()).distanceTo(p.root.localToWorld(V(...delivery.lead)));
   worstPlantedFoot=Math.max(worstPlantedFoot,error);assert(error<.01,'The lead shoe stays planted through release and follow-through');
  }
  if([-motion.PITCH_WINDUP_MS,-1000,-560,-150,0,210,350,motion.PITCH_RECOVERY_MS].includes(time))inspectSkinPose(p);
 }
 for(const time of [-motion.PITCH_WINDUP_MS,-1570,-1270,-1000,-800,-560,-330,-150,-65,0,85,210,350,550,770,motion.PITCH_RECOVERY_MS]){
  const before=motion.pitchingPose(time-h,underhand),at=motion.pitchingPose(time,underhand),after=motion.pitchingPose(time+h,underhand);
  for(const track of ['hand','glove','lead','trail']){
   const from=V(...at[track]).sub(V(...before[track])).divideScalar(h/1000),to=V(...after[track]).sub(V(...at[track])).divideScalar(h/1000);
   assert(from.distanceTo(to)<.01,`Delivery velocity must be continuous at ${time} on ${track}`);
  }
 }
}
console.log(`PASS local DB handedness, switch hitting, both swing directions, exact contact/release alignment, continuous swing/delivery tracks, wrists (swing ${(worstWrist*100).toFixed(3)} cm; pitch ${(worstPitchWrist*100).toFixed(3)} cm), planted shoes (${(worstPlantedFoot*100).toFixed(3)} cm), and finite clothing through ${skinPoseChecks} posed frames`);
