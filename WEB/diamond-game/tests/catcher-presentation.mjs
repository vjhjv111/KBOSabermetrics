import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const {ballPosition}=load('lib/action-engine.ts');
const {CATCHER_RECEIVE_Z,catcherPitchPlan,receivedBallPosition}=load('lib/catcher-presentation.ts');
const base={id:4,type:'fastball',velocity:145,releaseAt:100000,flightMs:480,releaseX:-.33,releaseY:1.84,releaseZ:-17.2,target:{x:0,y:0},breakX:.22,breakY:.3,quality:1,resolved:false};
const baseResult={id:4,kind:'strike',outcome:'TAKE',at:101000};
const pocket={x:.14,y:.8,z:.74},pose={pocket,catchable:true};
let trajectories=0;
for(const flightMs of [350,480,925])for(const releaseZ of [-15.9,-17.2,-18.44])for(const releaseY of [1.15,1.84,2.15])for(const x of [-2,0,2])for(const y of [-2,0,2]){
 const pitch=Object.freeze({...base,flightMs,releaseZ,releaseY,target:Object.freeze({x,y})});
 const before=JSON.stringify(pitch),plan=catcherPitchPlan(pitch,baseResult);
 assert(plan&&plan.catchBall);assert(plan.prepareAt>pitch.releaseAt);assert(plan.prepareAt<plan.receiveAt);
 assert(plan.receiveAt>pitch.releaseAt+pitch.flightMs,'Receiving happens behind home plate');
 assert(Math.abs(plan.target.z-CATCHER_RECEIVE_Z)<1e-10);
 assert.deepEqual(plan.target,ballPosition(pitch,plan.receiveAt));
 assert(ballPosition(pitch,plan.receiveAt-.01).z<CATCHER_RECEIVE_Z);assert(ballPosition(pitch,plan.receiveAt+.01).z>CATCHER_RECEIVE_Z);
 assert.deepEqual(catcherPitchPlan(pitch,{...baseResult,at:999999}),plan,'Late server adjudication never shifts visual contact');
 for(const ms of [-300,-.001,650,2000])assert.equal(receivedBallPosition(plan,plan.receiveAt+ms,pose),null);
 for(const ms of [0,.001,130,649.999])assert.deepEqual(receivedBallPosition(plan,plan.receiveAt+ms,pose),pocket);
 assert.equal(receivedBallPosition(plan,plan.receiveAt,{...pose,catchable:false}),null,'Unreachable pitches never teleport into the glove');
 const caught=receivedBallPosition(plan,plan.receiveAt,pose);assert.notEqual(caught,pocket);caught.x=100;assert.equal(pocket.x,.14);
 assert.equal(JSON.stringify(pitch),before,'The visual receiving plan does not mutate the pitch');trajectories++;
}
for(const event of ['contact','bodyHit'])for(const relative of [-120,0,80,500]){
 const ordinary=catcherPitchPlan(base),at=ordinary.receiveAt+relative;
 const result={...baseResult,[event]:{at,position:{x:0,y:1,z:0}}},plan=catcherPitchPlan(base,result);
 assert.equal(plan.catchBall,false);assert.equal(plan.cancelAt,at);
 for(const ms of [0,10,130,600])assert.equal(receivedBallPosition(plan,ordinary.receiveAt+ms,pose),null,`${event} owns the ball even for late contact`);
 assert.deepEqual(catcherPitchPlan({...base,reaction:result}),plan,'Stored reaction is respected without an override');
 assert.deepEqual(catcherPitchPlan(base,{...result,id:3}),ordinary,'Another pitch result cannot cancel this reception');
}
for(const patch of [{flightMs:0},{flightMs:-1},{flightMs:NaN},{releaseAt:Infinity},{releaseZ:0},{releaseZ:NaN},{releaseZ:-.7},{target:{x:NaN,y:0}}])assert.equal(catcherPitchPlan({...base,...patch}),null);
assert.equal(catcherPitchPlan(null),null);assert.equal(catcherPitchPlan(undefined),null);
assert.equal(receivedBallPosition(null,101000,pose),null);
assert.equal(receivedBallPosition(catcherPitchPlan(base),NaN,pose),null);
assert.equal(receivedBallPosition(catcherPitchPlan(base),catcherPitchPlan(base).receiveAt,{pocket:{...pocket,y:NaN},catchable:true}),null);

// Execute the actual ActionScene ball branch with its real path samplers and
// current 3D catcher. This catches missing integration and capture precedence.
const THREE=createRequire(import.meta.url)('three');
const {createPlayer,equipCatcher,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts');
const {poseCatcher}=load('lib/catcher-pose.ts');
const {incomingBallPosition}=load('lib/action-engine.ts');
const {fieldBallPosition}=load('lib/field-play-timeline.ts');
const scene=fs.readFileSync('app/action-scene.tsx','utf8'),start=scene.indexOf(' ball.visible=false;for(const t of trail)'),end=scene.indexOf(' const playAge=',start);
assert(start>=0&&end>start,'Actual scene ball-render branch exists');
assert(scene.includes('catcherPose=poseCatcher(catcher,{now,...(reception??{})})'),'Actual scene derives its receiving pose from the same presentation plan');
const runBall=new Function('ball','trail','p','now','reaction','play','pr','side','reception','catcherPose','incomingBallPosition','fieldBallPosition','receivedBallPosition',ts.transpileModule(scene.slice(start,end),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText);
const receiver=createPlayer('#243443');equipCatcher(receiver);receiver.root.position.z=1.1;receiver.root.rotation.y=Math.PI;
const newBall=()=>new THREE.Group(),trails=Array.from({length:3},newBall),pr={view:{code:'QA'},onContact(){}};
function render(pitch,result,now,forcedPose){
 const plan=catcherPitchPlan(pitch,result),pose=forcedPose??poseCatcher(receiver,{now,...plan}),ball=newBall();
 runBall(ball,trails,pitch,now,result,null,pr,'pitcher',plan,pose,incomingBallPosition,fieldBallPosition,receivedBallPosition);
 return {ball,pose,plan};
}
let actualCases=0;
for(const sign of [-1,1])for(const heightCm of [155,185,215]){
 receiver.root.scale.set(sign*heightCm/185,heightCm/185,heightCm/185);dressPlayer(receiver,playerAppearance('LG','포수',27,{heightCm}),sign);
 const plan=catcherPitchPlan(base,baseResult);
 for(const offset of [-1,0,130,650]){
  const now=plan.receiveAt+offset,{ball,pose}=render(base,baseResult,now);
  assert(pose.catchable,`${heightCm}/${sign}: normal middle pitch must be reachable`);
  assert.equal(ball.visible,offset<650);
  if(offset<0){assert.deepEqual(ball.position.toArray(),Object.values(incomingBallPosition(base,baseResult,now,'pitcher')));assert(trails.some(t=>t.visible));}
  else{assert(trails.every(t=>!t.visible),'Old flight trail disappears at reception');if(ball.visible){assert(ball.position.distanceTo(pose.pocket)<1e-9);assert.equal(ball.scale.x,1);}}
  if(offset===0)assert(ball.position.distanceTo(new THREE.Vector3(...Object.values(plan.target)))<.003,'No jump from the trajectory into the glove');
  actualCases++;
 }
}
const slow={...base,flightMs:5000},slowPlan=catcherPitchPlan(slow);
assert((slowPlan.receiveAt+650-slow.releaseAt)/slow.flightMs<1.24);
assert.equal(render(slow,baseResult,slowPlan.receiveAt+650,{...pose,catchable:true}).ball.visible,false,'After capture hold, the old flight must never reappear');
for(const event of ['contact','bodyHit']){
 const at=catcherPitchPlan(base).receiveAt+70,result={...baseResult,kind:event==='contact'?'foul':'hbp',outcome:event==='contact'?'FOUL':'HBP',trajectory:'foul',exitSpeed:65,launchAngle:20,direction:2,[event]:{at,position:{x:0,y:1,z:0}}};
 const now=at-10,{ball}=render(base,result,now,{...pose,catchable:true});
 assert.deepEqual(ball.position.toArray(),Object.values(incomingBallPosition(base,result,now,'pitcher')),'A future hit/body event prevents premature capture');
 const after=render(base,result,at+50,{...pose,catchable:true}).ball;
 assert(after.visible);assert(after.position.distanceTo(new THREE.Vector3(...Object.values(pocket)))>.1,'Hit/dead-ball path wins over the glove');
}
console.log(`PASS ${trajectories} exact receiving trajectories across speeds/arms/locations; behind-plate contact, catch hold boundaries, unreachable and late-contact/HBP precedence, restore determinism and pitch immutability`);
console.log(`PASS ${actualCases} actual ActionScene ball frames with mirrored/scaled receiving poses; flight-to-pocket continuity, trail clearing, held ball, terminal capture and diverted-ball precedence`);
