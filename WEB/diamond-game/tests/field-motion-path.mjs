import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const {BASES,DEFENSIVE_SPOTS,fielderPosition,runnerPosition}=load('lib/field-play.ts');
const {fielderMotion,runnerMotion}=load('lib/field-motion-path.ts');
const gap=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z),near=(a,b,message)=>assert(gap(a,b)<1e-9,message);
let samples=0;
for(const start of DEFENSIVE_SPOTS)for(const target of [BASES[1],BASES[2],{x:-34,y:.065,z:-68}]){
 const length=Math.hypot(target.x-start.x,target.z-start.z),arrival=180+length/7*1000;
 for(const time of [-100,0,179.999,180,180.001,arrival*.5,arrival-1,arrival+1,arrival+1000]){
  const motion=fielderMotion(start,target,time);
  assert.deepEqual(motion.position,fielderPosition(start,target,Math.max(0,time)),'The fielding path and arrival timing stay unchanged');
  near(motion.samplePath(motion.travel),motion.position,'Foot support samples the actual body path');
  assert(motion.travel>=0&&motion.travel<=length+1e-9);assert.equal(motion.speed,time>180&&time<arrival?7:0);
  assert.equal(motion.nominalSpeed,7);assert.equal(motion.stoppedFor,Math.max(0,time-arrival));assert(Number.isFinite(motion.heading));
  if(motion.speed){const next=fielderMotion(start,target,time+.0001);assert(Math.abs(gap(next.position,motion.position)/.0000001-motion.speed)<.00001);}
  samples++;
 }
 // Support anchors may extend either end; clamping these to a base makes a
 // foot drag forward as the torso takes its first or last step.
 assert(gap(fielderMotion(start,target,0).samplePath(-.3),start)>.299999);
 const stationary=fielderMotion(start,start,1000);assert.equal(stationary.speed,0);assert.equal(stationary.travel,0);near(stationary.samplePath(30),start);
}
for(let from=0;from<=3;from++)for(let to=from;to<=4;to++)for(const scale of [.5,1,1.8,3]){
 const plan=Object.freeze({playerId:'runner',from,to,out:false}),length=(to-from)*27.432,arrival=length/7.4/scale*1000;
 const times=new Set([0,100,arrival*.5,Math.max(0,arrival-1),arrival,arrival+1,...[1,2,3].map(base=>(base-from)*27.432/7.4/scale*1000).filter(time=>time>=0&&time<=arrival)]);
 for(const time of times){
  const motion=runnerMotion(plan,time,scale),expected=runnerPosition(plan,time*scale);
  assert.deepEqual(motion.position,expected,'Runner scale changes gait frequency, never the existing game path');
  near(motion.samplePath(motion.travel),motion.position,'Support path follows all four base corners');
  assert.equal(motion.speed,time>0&&!expected.finished?7.4*scale:0);
  assert.equal(motion.nominalSpeed,7.4*scale);assert.equal(motion.stoppedFor,length>0?Math.max(0,time-arrival):0);assert(Number.isFinite(motion.heading));
  if(to>from){near(motion.samplePath(0),BASES[from]);near(motion.samplePath(length),BASES[to]);assert(gap(motion.samplePath(-.3),BASES[from])>.299999);assert(gap(motion.samplePath(length+.3),BASES[to])>.299999);}
  samples++;
 }
}
const angleGap=(a,b)=>Math.abs(Math.atan2(Math.sin(a-b),Math.cos(a-b)));
for(const scale of [1,1.8,3])for(const base of [1,2,3]){
 const plan={playerId:'corner',from:0,to:4,out:false},at=base*27.432/7.4/scale*1000;
 const before=runnerMotion(plan,at-.5,scale),after=runnerMotion(plan,at+.5,scale);
 assert(angleGap(before.position.heading,after.position.heading)>1.5,'The original route keeps its actual 90-degree base turn');
 assert(angleGap(before.heading,after.heading)<.04,'The model body turns continuously around that same base');
}
console.log(`PASS ${samples} field/runner path cases: unchanged positions and arrival times, reaction delay, actual gait speeds, base corners and support-anchor endpoint extension`);

// Execute the production fielder loop: a body that is still waiting for its
// first step must not start a far-away catch simply because its speed is zero.
const THREE=createRequire(import.meta.url)('three'),scene=fs.readFileSync('app/action-scene.tsx','utf8');
const start=scene.indexOf(' fielders.forEach((model,i)=>{const start='),end=scene.indexOf(' const runnerActive=',start);
assert(start>0&&end>start);
const compiled=ts.transpileModule(scene.slice(start,end),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText;
const frame=Function('fielders','DEFENSIVE_SPOTS','fieldAction','play','BASES','clamp','playAge','fielderMotion','poseRunning','poseFielding','setPlayerDetail','camera','compact','now','blinkPlayer','fieldBlinkWindows',compiled);
const {fieldPlayTimeline}=load('lib/field-play-timeline.ts');
const result={id:1,kind:'out',outcome:'OUT',trajectory:'ground',exitSpeed:104,launchAngle:4,direction:.13,contact:{at:10000,position:{x:0,y:1.05,z:0}}};
const timeline=fieldPlayTimeline(result),play={at:10000,timeline,fielder:timeline.fielder,fieldTarget:timeline.fieldTarget};
for(const age of [0,100,179.99,180.01,timeline.fieldedAt-10000,timeline.throwAt-10000,timeline.completeAt-10000+100]){
 const models=DEFENSIVE_SPOTS.map(()=>({root:new THREE.Group()})),running=[],fielding=[],blinks=[],windows=[];
 frame(models,DEFENSIVE_SPOTS,true,play,BASES,(n,a,b)=>Math.max(a,Math.min(b,n)),age,fielderMotion,(model,options)=>running.push({model,options}),(model,...args)=>fielding.push({model,args}),()=>{}, {position:new THREE.Vector3(0,10,0)},false,10000+age,(...args)=>blinks.push(args),windows);
 assert.equal(blinks.length,7);blinks.forEach(([model,now,seed,protectedWindows],i)=>{assert.equal(model,models[i]);assert.equal(now,10000+age);assert.equal(seed,'fielder:'+i);assert.equal(protectedWindows,windows);});
 assert.equal(running.length,7);for(const {model,options} of running){assert.equal(options.now,10000+age);near(options.samplePath(options.travel),model.root.position,'Production scene passes the same world path to the gait');}
 if(age<=180){assert(running.every(pose=>pose.options.speed===0));assert.equal(fielding.length,0,'Waiting fielders do not perform an unreachable catch');}
 if(age===180.01)assert(running.some(pose=>pose.options.speed===7),'Gait begins with actual movement');
 if(age===timeline.fieldedAt-10000)assert(fielding.some(pose=>pose.model===models[timeline.fielder]),'Actual collecting fielder enters the receiving pose at the existing fielded time');
}
console.log('PASS actual ActionScene movement integration: reaction delay, seven world path/gait pairs, no premature catch and unchanged receiving boundary');
