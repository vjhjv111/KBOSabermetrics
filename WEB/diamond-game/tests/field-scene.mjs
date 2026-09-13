import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three'),V=(...x)=>new THREE.Vector3(...x);
const field=load('lib/field-play.ts'),motion=load('lib/player-motion.ts'),cameraTools=load('lib/field-camera.ts'),{poseBatter}=load('lib/player-pose.ts'),{createPlayer}=load('lib/player-model.ts');
for(let i=0;i<4;i++)assert(Math.abs(V(...Object.values(field.BASES[i])).distanceTo(V(...Object.values(field.BASES[i+1])))-27.432)<1e-10);
const before={id:'test',inning:1,half:'top',plateAppearances:0,bases:[{playerId:'a'},{playerId:'b'},{playerId:'c'}],homeRuns:0,awayRuns:0};
const result=(kind,outcome,contact)=>({kind,outcome,...(contact?{contact:{at:0,position:{x:0,y:1,z:0}}}:{})});
const homer=field.runnerPlans(before,'hitter',result('hit','HR',true));assert.equal(homer.length,4);assert(homer.every(p=>p.to===4));
for(const plan of homer){let previous=field.runnerPosition(plan,0);for(let time=10;time<=16000;time+=10){const next=field.runnerPosition(plan,time);assert(Object.values(next).every(x=>typeof x==='boolean'||Number.isFinite(x)));assert(Math.hypot(next.x-previous.x,next.z-previous.z)<=.07400001);previous=next;}assert(previous.finished);assert(Math.hypot(previous.x,previous.z)<1e-10);}
assert.equal(field.runnerPlans(before,'hitter',result('out','K',false)).length,3,'Strikeout must not animate batter running to first');
const walk=field.runnerPlans(before,'hitter',result('walk','BB',false));assert.deepEqual(walk.map(p=>p.to),[4,3,2,1]);
const after={...before,plateAppearances:1,awayRuns:1,bases:[{playerId:'hitter'},null,{playerId:'a'}]};
const corrected=field.runnerPlans({...before,bases:[{playerId:'a'},null,{playerId:'c'}]},'hitter',result('hit','1B',true),after);assert.equal(corrected.find(p=>p.playerId==='a').to,3);assert.equal(corrected.find(p=>p.playerId==='c').to,4);
for(const target of [{x:-60,y:0,z:-90},{x:0,y:0,z:-30},{x:45,y:10,z:-70}]){const index=field.nearestFielder(target);assert(index>=0&&index<7);const start=field.DEFENSIVE_SPOTS[index];let previous=start;for(let t=0;t<5000;t+=10){const pos=field.fielderPosition(start,target,t);assert(Math.hypot(pos.x-previous.x,pos.z-previous.z)<=.070001);previous=pos;}}
for(const aspect of[.5,.9,1.62,1.9,2.4])for(const side of['batter','pitcher']){const data=cameraTools.FIELD_CAMERAS[side],camera=new THREE.PerspectiveCamera(cameraTools.fieldFov(side,aspect),aspect,.03,450);camera.position.set(...data.position);camera.lookAt(...data.target);camera.updateMatrixWorld(true);const ray=new THREE.Raycaster(),plane=new THREE.Plane(V(0,0,1),0);for(const x of[-1,0,1])for(const y of[-1,0,1]){const target=V(x*.5,1.05+y*.55,0),ndc=target.clone().project(camera);ray.setFromCamera(new THREE.Vector2(ndc.x,ndc.y),camera);assert(ray.ray.intersectPlane(plane,V()).distanceTo(target)<1e-10);}}
let maxWrist=0,poses=0;
for(const height of[155,185,215])for(const hand of[1,-1]){const scale=motion.pitcherBodyScale(height),model=createPlayer('#224466',true),mirror=new THREE.Group(),bat=new THREE.Group();mirror.add(model.root);mirror.scale.set(hand*scale,scale,scale);model.root.position.z=.06;
 for(const x of[-1,0,1])for(const y of[-1,0,1])for(let time=0;time<=980;time+=5){const localAim={x:x/scale,y:((1.05+y*.55)/scale-1.05)/.55},pose=motion.swingPose(time,localAim,hand),wrists=poseBatter(model,mirror,bat,pose);for(const [arm,wrist]of[[model.le,wrists.leftWrist],[model.re,wrists.rightWrist]]){const error=arm.localToWorld(V(0,-.34,0)).distanceTo(wrist);maxWrist=Math.max(maxWrist,error);assert(error<.01,`Custom batter hand detaches: ${height} ${hand} ${x} ${y} ${time} ${error}`);}if(time===95){const contact=bat.localToWorld(V(0,motion.BAT_SWEET_SPOT,0));assert(contact.distanceTo(V(x*.5,1.05+y*.55,0))<1e-10);}poses++;}
}
console.log(`PASS base paths, HR/BB/K runner behavior, authoritative runner correction, fielder movement, aim ray roundtrips, ${poses} scaled batter poses (wrist ${maxWrist}m), exact scaled contact`);
