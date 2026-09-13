import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import {load} from './load-ts.mjs';

const THREE=createRequire(import.meta.url)('three');
const {createPlayer,equipCatcher,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance,playerDimensions}=load('lib/player-appearance.ts');
const {poseCatcher}=load('lib/catcher-pose.ts');
const V=(...args)=>new THREE.Vector3(...args),receiveAt=1000,prepareAt=350;
const times=[0,600,1000,1160,1500,1950];
const targets=[[-.5,.5],[0,.5],[.5,.5],[-.5,1.05],[0,1.05],[.5,1.05],[-.5,1.6],[0,1.6],[.5,1.6]];
let checks=0,samples=0,received=0,minArmor=Infinity,maxGroundError=0,maxReachError=0;
function check(value,message){checks++;assert(value,message);}
function near(a,b,tolerance=1e-7,message='Values must agree'){check(Math.abs(a-b)<=tolerance,`${message}: ${a} / ${b}`);}
function nearPoint(a,b,tolerance=1e-7,message='Points must agree'){check(a.distanceTo(b)<=tolerance,`${message}: ${a.toArray()} / ${b.toArray()}`);}
function modelFor(height=185,bodyType='athletic',hand=1){
 const model=createPlayer('#254968');equipCatcher(model);dressPlayer(model,playerAppearance('LG','Catcher QA',27,{heightCm:height,bodyType}),hand);
 const scale=height/185;model.root.position.set(0,0,1.1);model.root.rotation.y=Math.PI;model.root.scale.set(hand*scale,scale,scale);return model;
}
function parts(root){
 const result=[];root.traverse(mesh=>{if(!mesh.isMesh)return;
  for(const part of mesh.userData.playerBatchParts??[{name:mesh.name,vertexStart:0,vertexCount:mesh.geometry.attributes.position.count,indexStart:0,indexCount:mesh.geometry.index?.count??mesh.geometry.attributes.position.count}])result.push({mesh,...part});
 });return result;
}
function update(model){model.root.updateWorldMatrix(true,true);for(const skeleton of new Set(parts(model.root).map(part=>part.mesh.skeleton).filter(Boolean)))skeleton.update();}
function vertex(mesh,index){return mesh.localToWorld(mesh.getVertexPosition(index,V()));}
function minY(root){let low=Infinity;root.traverse(mesh=>{if(mesh.isMesh)for(let i=0;i<mesh.geometry.attributes.position.count;i++)low=Math.min(low,vertex(mesh,i).y);});return low;}
function surfaceDistance(point,list){
 let result=Infinity;
 for(const part of list){const index=part.mesh.geometry.index;
  for(let i=part.indexStart;i<part.indexStart+part.indexCount;i+=3){
   const triangle=new THREE.Triangle(...[0,1,2].map(offset=>vertex(part.mesh,index?index.getX(i+offset):i+offset)));
   result=Math.min(result,triangle.closestPointToPoint(point,V()).distanceTo(point));
  }
 }return result;
}
function geometryHash(model){
 const hash=createHash('sha256');
 model.root.traverse(mesh=>{if(!mesh.isMesh)return;
  for(const attr of [...Object.values(mesh.geometry.attributes),mesh.geometry.index].filter(Boolean))hash.update(new Uint8Array(attr.array.buffer,attr.array.byteOffset,attr.array.byteLength));
 });return hash.digest('hex');
}
function gloveFor(model){return model.le.children.find(child=>parts(child).some(part=>part.name==='Deep leather mitt pocket'));}
function poseSnapshot(model){return ['root','hips','torso','head','left','right','le','re','ll','rl','lk','rk','lf','rf'].flatMap(name=>[...model[name].position.toArray(),...model[name].quaternion.toArray(),...model[name].scale.toArray()]);}
function dispose(model){
 const geometries=new Set(),materials=new Set(),skeletons=new Set();
 model.root.traverse(mesh=>{if(mesh.isMesh){geometries.add(mesh.geometry);if(mesh.skeleton)skeletons.add(mesh.skeleton);for(const material of Array.isArray(mesh.material)?mesh.material:[mesh.material])materials.add(material);}});
 for(const object of [...geometries,...materials,...skeletons])object.dispose();
}

// Actual factory geometry, mirrored hand rigs and all supported height/body
// extremes: checking ankle transforms alone would miss floating soles or studs.
for(const height of [155,185,215])for(const bodyType of ['lean','athletic','power'])for(const hand of [-1,1]){
 const model=modelFor(height,bodyType,hand),glove=gloveFor(model),scale=height/185,depth=playerDimensions(model.appearance).depthScale;
 const geometryBefore=geometryHash(model),rootScale=model.root.scale.clone(),rootRotation=model.root.quaternion.clone();
 const fixed=['left','right','le','re','lk','rk','lf','rf'].map(name=>[name,model[name].position.clone()]);
 const armor=parts(model.root).filter(part=>/Catcher fitted chest|Catcher segmented|Catcher open sightline|Catcher cheek|Catcher curved shoulder/.test(part.name));
 const ballAnchor=V(0,-.02,-.052*depth+.065/scale),wrist=V(0,-.341,.006*depth),cuff=V(0,-.118,-.036*depth);
 check(glove,'Catcher factory must expose the real mitt after mesh batching');
 for(const [x,y]of targets){
  const target={x,y,z:.65},plan={target,receiveAt,prepareAt};let first;
  for(const now of times){
   const pose=poseCatcher(model,{now,...plan});update(model);samples++;
   check([...pose.pocket.toArray(),...pose.catchPocket.toArray(),pose.reachError].every(Number.isFinite),'Pose output must stay finite at every target/timing/appearance');
   nearPoint(pose.pocket,glove.localToWorld(ballAnchor.clone()),1e-7,'Returned point is the actual ball-centre pocket');
   nearPoint(glove.localToWorld(cuff.clone()),model.le.localToWorld(wrist.clone()),1e-7,'Cuff stays seated on the existing anatomical wrist');
   for(const foot of [model.lf,model.rf]){const error=Math.abs(minY(foot));maxGroundError=Math.max(maxGroundError,error);near(error,0,1e-7,'Actual cleat sole/studs stay on y=0');}
   nearPoint(model.root.scale,rootScale);near(model.root.quaternion.angleTo(rootRotation),0,1e-7);near(model.root.position.x,0);near(model.root.position.z,1.1);
   for(const [name,position]of fixed)nearPoint(model[name].position,position,1e-7,`${name}: original shoulder/bone/ankle anchor stays fixed`);
   if(!first)first=pose;else{nearPoint(pose.catchPocket,first.catchPocket);near(pose.reachError,first.reachError);check(pose.catchable===first.catchable,'Reachability does not change with animation or response age');}
   if(now===receiveAt){
    received+=Number(pose.catchable);maxReachError=Math.max(maxReachError,pose.reachError);
    if(x===0)check(pose.catchable,`Centre low/middle/high course is reachable for ${height}/${bodyType}/${hand}`);
    if(pose.catchable)nearPoint(pose.pocket,V(x,y,.65),.022*scale,'A caught ball already meets the pocket at the real receive point');
   }
   if(now===1160&&pose.catchable){
    const gap=surfaceDistance(pose.pocket,armor);minArmor=Math.min(minArmor,gap);
    check(gap>=.065-1e-5,`Absorption must not pull a 65 mm ball into the mask/chest: ${height}/${bodyType}/${hand}/${x}/${y}, ${gap}`);
    check(pose.absorption>0&&pose.phase==='absorb','Reachable pitch visibly gives after contact');
   }
   if(now===1950){near(pose.receiveProgress,0);near(pose.absorption,0);check(pose.phase==='ready','Completed receive returns to the ready pose');}
  }
 }
 check(geometryHash(model)===geometryBefore,'Receiving poses never alter geometry, UVs, normals, indices or skin weights');dispose(model);
}

// Direct late restore and out-of-order rendering must use the contact pose,
// not the current recovered wrist, to decide whether the original ball fit.
const live=modelFor(),cold=modelFor(),plan={target:{x:.3,y:1.1,z:.65},receiveAt,prepareAt};
for(const now of [0,600,1000,1160])poseCatcher(live,{now,...plan});
const lateLive=poseCatcher(live,{now:1900,...plan}),lateCold=poseCatcher(cold,{now:1900,...plan});
nearPoint(lateLive.pocket,lateCold.pocket);nearPoint(lateLive.catchPocket,lateCold.catchPocket);near(lateLive.reachError,lateCold.reachError);
check(lateLive.catchable===lateCold.catchable,'A first render 900 ms after arrival preserves catchability');
const before=poseCatcher(live,{now:600,...plan}),beforeSnapshot=poseSnapshot(live);poseCatcher(live,{now:2200,...plan});const replay=poseCatcher(live,{now:600,...plan});
nearPoint(before.pocket,replay.pocket);poseSnapshot(live).forEach((value,index)=>near(value,beforeSnapshot[index],1e-7,'Reordered time produces the same articulated pose'));

// No hit/HBP-specific data is owned here: callers cancel with the retained
// target and event time, and a forbidden catch never enters absorption.
const cancelled=poseCatcher(live,{now:900,...plan,catchBall:false,cancelAt:850});
check(cancelled.phase==='recover'&&cancelled.receiveProgress>0,'Cancellation blends the prepared target back over 300 ms');near(cancelled.absorption,0);check(!cancelled.catchable,'Cancelled ball cannot attach');
const done=poseCatcher(live,{now:1150,...plan,catchBall:false,cancelAt:850});near(done.receiveProgress,0);near(done.absorption,0);check(done.phase==='ready','Cancel completes at its fixed recovery boundary');
const neverCatch=poseCatcher(live,{now:1160,...plan,catchBall:false});near(neverCatch.absorption,0);check(!neverCatch.catchable&&neverCatch.phase!=='absorb','Hit/HBP cannot start a fake absorption even after the receive time');
const plainReady=poseCatcher(live,{now:2400});near(plainReady.receiveProgress,0);check(plainReady.phase==='ready'&&!plainReady.catchable,'No pitch is an ordinary ready pose');
const wild=poseCatcher(live,{now:1000,receiveAt,target:{x:2,y:1,z:.65}});check(!wild.catchable&&wild.reachError>.4,'Far-outside pitches are honestly unreachable');
const dirt=poseCatcher(live,{now:1000,receiveAt,target:{x:0,y:.05,z:.65}});check(dirt.blocking&&!dirt.catchable,'A ground pitch uses the low/blocking cue without an impossible glove catch');
const invalid=poseCatcher(live,{now:1000,receiveAt,target:{x:NaN,y:1,z:.65}});check(invalid.phase==='ready'&&invalid.pocket.toArray().every(Number.isFinite),'Non-finite target never corrupts the rig');

// World-space target and ground ownership also work under a transformed parent.
const parent=new THREE.Group();parent.position.set(1.2,.4,-.3);parent.rotation.y=.6;parent.add(cold.root);
const originalXZ=cold.root.getWorldPosition(V()),worldTarget=cold.root.localToWorld(V(0,.8,.46));worldTarget.y=.92;
const shifted=poseCatcher(cold,{now:1000,receiveAt,target:worldTarget,groundY:.12});update(cold);
near(cold.root.getWorldPosition(V()).x,originalXZ.x);near(cold.root.getWorldPosition(V()).z,originalXZ.z);
near(minY(cold.lf),.12,1e-7);near(minY(cold.rf),.12,1e-7);check(shifted.pocket.toArray().every(Number.isFinite),'Parent transform does not corrupt the world-space solve');

// Appearance edits invalidate the contact cache and retain exact cuff/pocket fit.
parent.remove(cold.root);cold.root.position.set(0,0,1.1);cold.root.rotation.y=Math.PI;
for(const bodyType of ['power','lean','athletic']){
 dressPlayer(cold,playerAppearance('LG','Catcher QA',27,{bodyType,heightCm:185}));
 const pose=poseCatcher(cold,{now:1000,receiveAt,target:{x:0,y:1,z:.65}}),glove=gloveFor(cold),depth=playerDimensions(cold.appearance).depthScale;
 nearPoint(pose.pocket,glove.localToWorld(V(0,-.02,-.052*depth+.065)),1e-7,'New body-depth refreshes the true ball-centre anchor');check(pose.catchable,'Body edits preserve centre receiving');
}
dispose(live);dispose(cold);
console.log(`PASS catcher pose: ${checks} assertions; planted soles, real pocket/cuff, mirrored height/body extremes, deterministic restore/cancel and armor-safe absorption`);
console.log(JSON.stringify({samples,receiveSamples:162,received,maxGroundErrorMetres:maxGroundError,maxReachErrorMetres:maxReachError,minAbsorbArmorGapMetres:minArmor}));
