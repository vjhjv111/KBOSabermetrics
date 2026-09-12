import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {load} from './load-ts.mjs';
import * as THREE from 'three';
const motion=load('lib/player-motion.ts'),engine=load('lib/action-engine.ts');
const profiles=JSON.parse(fs.readFileSync('lib/player-profiles.json','utf8')).players;
const byName=name=>Object.entries(profiles).find(([,p])=>p.name===name)[0];
assert(engine.batsLeft(byName('디아즈'),byName('폰세')));
assert(!engine.batsLeft(byName('안현민'),byName('폰세')));
for(const name of ['김주원','레이예스']){assert(engine.batsLeft(byName(name),byName('폰세')));assert(!engine.batsLeft(byName(name),byName('양현종')));}
assert(engine.isUnderhand(byName('고영표')));assert(!engine.throwsLeft(byName('폰세')));assert(engine.throwsLeft(byName('양현종')));
const source=fs.readFileSync('app/action-scene.tsx','utf8');
const helpers=ts.transpileModule(source.slice(source.indexOf('const V='),source.indexOf('export default function')),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText;
const {player,poseBatter,poseArm}=new Function('THREE','clamp',helpers+';return {player,poseBatter,poseArm};')(THREE,engine.clamp);
const V=(...n)=>new THREE.Vector3(...n);let worstWrist=0;
for(const hand of [1,-1]){
 const b=player('#224466',true),mirror=new THREE.Group(),bat=new THREE.Group();mirror.scale.x=hand;mirror.add(b.root);b.root.position.z=.06;
 for(const x of [-1,-.5,0,.5,1])for(const y of [-1,-.5,0,.5,1])for(let age=0;age<=980;age+=5){
  const pose=motion.swingPose(age,{x,y},hand),wrists=poseBatter(b,mirror,bat,pose);
  for(const [arm,target] of [[b.le,wrists.leftWrist],[b.re,wrists.rightWrist]]){const error=arm.localToWorld(V(0,-.34,0)).distanceTo(target);worstWrist=Math.max(worstWrist,error);assert(error<.01,`Detached wrist: ${JSON.stringify({hand,x,y,age,error})}`);}
  assert(Math.abs(Math.hypot(...pose.axis)-1)<1e-10);
  if(age===motion.SWING_CONTACT_MS)assert(V(...pose.barrel).distanceTo(V(x*.5,1.05+y*.55,0))<1e-10);
 }
}
const h=.001,t=motion.SWING_CONTACT_MS,p0=V(...motion.swingPose(t,{x:0,y:0},1).barrel),p1=V(...motion.swingPose(t-h,{x:0,y:0},1).barrel),p2=V(...motion.swingPose(t+h,{x:0,y:0},1).barrel);
assert(p0.clone().sub(p1).divideScalar(h/1000).distanceTo(p2.sub(p0).divideScalar(h/1000))<.01,'Impact velocity must remain continuous');
for(const name of ['폰세','고영표','양현종']){
 const id=byName(name),scale=engine.playerProfile(id,'pitcher').heightCm/185,hand=engine.throwsLeft(id)?-1:1,underhand=engine.isUnderhand(id),p=player('#224466');
 p.root.position.z=-18.44;p.root.scale.set(hand*scale,scale,scale);const pose=motion.pitchingPose(0,underhand);p.torso.rotation.set(pose.lean,pose.lift*.32,underhand?-.14:0);p.root.updateWorldMatrix(true,true);
 const target=p.root.localToWorld(V(...pose.hand));poseArm(p.right,p.re,target,V(-hand,.1,-.15));const actual=p.re.localToWorld(V(0,-.34,0));
 const pitch=engine.createPitch({pitcher:id,pitchCount:0,pace:'practice',mode:'ai'},engine.arsenal(id)[0].type,{x:0,y:0},1,0),release=engine.ballPosition(pitch,pitch.releaseAt);
 assert(actual.distanceTo(V(release.x,release.y,release.z))<1e-10);assert.equal(Math.sign(release.x),-hand);
}
console.log(`PASS local DB handedness, switch hitting, both swing directions, contact alignment, continuous impact, wrists (max ${(worstWrist*100).toFixed(3)} cm), scaled and underhand releases`);
