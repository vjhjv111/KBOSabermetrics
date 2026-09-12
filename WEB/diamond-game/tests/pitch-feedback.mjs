import assert from 'node:assert/strict';
import {load} from './load-ts.mjs';
const e=load('lib/action-engine.ts'),f=load('lib/pitch-feedback.ts'),b=load('lib/batted-ball.ts');
const pitch=(x,y)=>({id:1,type:'fastball',releaseAt:100000,flightMs:443,releaseX:-.33,releaseY:1.84,releaseZ:-18.32,target:{x,y},breakX:.02,breakY:.03,quality:1,resolved:false});
const game=(batter,p)=>({batter,pitcher:'55730',pitch:p,pace:'full',round:0,balls:2,strikes:2,score:0,history:[]});
const arrival=p=>p.releaseAt+p.flightMs;
for(const [id,sign] of [['52001',-1],['54400',1]])for(const [x,y] of [[1.8,0],[1.62,1.4],[1.84,-1.45]]){
 const p=pitch(x*sign,y),g=game(id,p),r=e.evaluatePitch(g,null,arrival(p)+800);assert.equal(r.outcome,'HBP');assert(r.bodyHit);assert.equal(r.contact,undefined);assert.equal(Math.sign(r.bodyHit.position.x),sign);
 e.finishPitch(g,r);e.finishPitch(g,r);assert.equal(g.round,1);assert.equal(g.score,1);assert.equal(g.balls,0);assert.equal(g.strikes,0);assert.equal(g.history.length,1);
}
for(const [x,y] of [[0,0],[1.8,0],[-1.8,2]])assert.equal(e.findBodyHit('52001','55730',pitch(x,y)),null);
const p=pitch(-1.8,0),g=game('52001',p),hit=e.findBodyHit(g.batter,g.pitcher,p);
let r=e.evaluatePitch(g,{at:hit.at+110,aim:p.target},arrival(p));assert.equal(r.outcome,'HBP','A late click cannot undo a body hit');
r=e.evaluatePitch(g,{at:hit.at+1,aim:p.target},arrival(p));assert.equal(r.kind,'strike');assert(r.bodyHit);assert.equal(r.contact,undefined);
r=e.evaluatePitch(g,{at:hit.at-1,aim:p.target},arrival(p));assert(r.contact);assert.equal(r.bodyHit,undefined);
assert.equal(e.evaluatePitch(game('52001',pitch(-1,.7)),null,101000).outcome,'STRIKE');
const capsule=[{a:{x:0,y:0,z:0},b:{x:0,y:0,z:0},radius:.1}];
assert(f.segmentTouchesBody({x:-1,y:.16499,z:0},{x:1,y:.16499,z:0},capsule),'Swept grazing contact');
assert(!f.segmentTouchesBody({x:-1,y:.166,z:0},{x:1,y:.166,z:0},capsule));
const center=pitch(0,0),cg=game('52001',center),at=arrival(center);
const fair=e.evaluatePitch(cg,{at,aim:{x:0,y:0}},at),ground=e.evaluatePitch(cg,{at:at-80,aim:{x:.1,y:.38}},at),foul=e.evaluatePitch(cg,{at,aim:{x:.9,y:0}},at),miss=e.evaluatePitch(cg,{at,aim:{x:2,y:2}},at);
assert(fair.contact);assert.equal(ground.trajectory,'ground');assert.equal(ground.distance,0);assert.equal(ground.label,'땅볼 아웃');assert(foul.contact);assert.equal(foul.trajectory,'foul');assert.equal(miss.contact,undefined);
for(const result of [fair,ground,foul]){
 assert.deepEqual(b.battedBallPosition(result,result.contact.at),result.contact.position);
 for(const side of ['batter','pitcher']){const before=e.incomingBallPosition(center,result,result.contact.at-.0001,side),after=b.battedBallPosition(result,result.contact.at+.0001);assert(Math.hypot(before.x-after.x,before.y-after.y,before.z-after.z)<.0001);}
}
assert(b.battedBallPosition(ground,ground.contact.at+2000).y<.25);
assert(b.battedBallPosition(ground,ground.contact.at+2000).z<0);
assert(b.battedBallPosition(foul,foul.contact.at+1000).z>0,'Foul ball deflects outside the fair field');
assert.equal(b.carryDistance(fair.exitSpeed,fair.launchAngle,fair.contact.position.y),fair.distance);
let view={pitch:center,history:[]};assert.equal(f.lastPitchMarker(view,at-1),null);assert.deepEqual(f.lastPitchMarker(view,at).position,{x:0,y:1.05,z:0});
view={pitch:{...pitch(.5,.5),id:2,releaseAt:110000},history:[fair]};assert.equal(f.lastPitchMarker(view,109999).id,1);assert.equal(f.lastPitchMarker(view,110000),null);
view={pitch:p,history:[]};assert.equal(f.lastPitchMarker(view,hit.at-1,hit),null);assert.deepEqual(f.lastPitchMarker(view,hit.at,hit).position,hit.position);
console.log('PASS body/head/leg collisions, handedness, no phantom HBP, dead-ball precedence, grazing sweep, one-time scoring, pitch markers, ground/fly/foul contact and continuous trajectories');
