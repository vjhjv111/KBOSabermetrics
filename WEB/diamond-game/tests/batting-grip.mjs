import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three'),V=(...a)=>new THREE.Vector3(...a);
const {createPlayer,createBat,dressPlayer}=load('lib/player-model.ts'),{playerAppearance,playerDimensions}=load('lib/player-appearance.ts');
const {BATTING_WRIST_CUFFS}=load('lib/player-equipment.ts'),{poseBatter}=load('lib/player-pose.ts'),{swingPose,BAT_SWEET_SPOT,SWING_DURATION_MS}=load('lib/player-motion.ts');
const meshes=root=>{const all=[];root.traverse(o=>{if(o.isMesh)all.push(o);});return all;};
function update(model){model.root.updateWorldMatrix(true,true);model.root.updateMatrixWorld(true);for(const skeleton of new Set(meshes(model.root).filter(o=>o.isSkinnedMesh).map(o=>o.skeleton)))skeleton.update();}
const vertex=(mesh,i)=>mesh.localToWorld(mesh.getVertexPosition(i,V()));
function mean(points){const result=V();for(const point of points)result.add(point);return result.divideScalar(points.length);}
function dispose(root){const all=new Set();for(const mesh of meshes(root)){all.add(mesh.geometry);if(mesh.skeleton)all.add(mesh.skeleton);for(const m of Array.isArray(mesh.material)?mesh.material:[mesh.material]){all.add(m);for(const v of Object.values(m))if(v?.isTexture)all.add(v);}}for(const resource of all)resource.dispose();}
let frames=0,maxWristError=0,maxChestShift=0,uniformFrames=0,maxUniformError=0,maxContactError=0,maxTriangles=0;
const surfaces=[];
for(const height of[155,185,215])for(const hand of[-1,1])for(const bodyType of['lean','athletic','power']){
 const root=new THREE.Group(),mirror=new THREE.Group(),model=createPlayer('#eeeeee',true),bat=createBat(root),scale=height/185;
 root.add(mirror);mirror.add(model.root);mirror.scale.set(hand*scale,scale,scale);model.root.position.z=.06;
 dressPlayer(model,playerAppearance('LG','QA',27,{heightCm:height,bodyType}),hand);
 const depth=playerDimensions(model.appearance).depthScale,skinWrist=V(0,-.341,.006*depth),palms=bat.getObjectByName('Batting glove palms and knuckle pads');
 const rings=[['left',model.left,model.le],['right',model.right,model.re]].map(([side,arm,lower])=>{
  const skin=meshes(arm).find(mesh=>mesh.name==='Continuous anatomical arm'),p=skin.geometry.getAttribute('position'),skinIds=[];
  // The real first skin ring has 24 unique angular samples plus its UV seam.
  for(let i=0;i<p.count;i++)if(Math.abs(p.getY(i)+.681)<1e-6)skinIds.push(i);
  assert.equal(skinIds.length,25);skinIds.pop();
  const cuff=BATTING_WRIST_CUFFS[side],c=palms.geometry.getAttribute('position'),cuffIds=[];
  for(let i=0;i<c.count;i++)if(Math.abs(c.getX(i)-cuff[0])<1e-6&&Math.abs(c.getY(i)-cuff[1])<.03)cuffIds.push(i);
  assert.equal(cuffIds.length,19);cuffIds.pop();
  return{side,arm,lower,skin,skinIds,cuff,cuffIds};
 });
 const triangles=meshes(model.root).reduce((n,mesh)=>n+(mesh.geometry.index?.count??mesh.geometry.getAttribute('position').count)/3,0);maxTriangles=Math.max(maxTriangles,triangles);assert(triangles<=50000);
 const fixed=new Map([model.left,model.right,model.le,model.re].map(bone=>[bone,bone.position.clone()]));
 for(const x of[-1,0,1])for(const y of[-1,0,1])for(const load of[0,1])for(const time of[0,25,30,60,95,145,235,370,560,780,960,SWING_DURATION_MS]){
  const label=`${height}/${hand}/${bodyType}/${x}/${y}/${load}/${time}`,pose=swingPose(time,{x:x/scale,y:((1.05+y*.55)/scale-1.05)/.55},hand,0,load);
  const wrists=poseBatter(model,mirror,bat,pose);update(model);bat.updateWorldMatrix(true,true);frames++;
  // Every point on the axis is unchanged even though the asymmetric gloves
  // mirror. These are the actual world positions used by hit/contact logic.
  for(const along of[-.154,0,BAT_SWEET_SPOT,.98]){
   const expected=V(...pose.grip).addScaledVector(V(...pose.axis),along).multiplyScalar(scale),actual=bat.localToWorld(V(0,along,0));
   assert(actual.distanceTo(expected)<1e-10,`${label}: bat axis changed`);
  }
  assert.equal(Math.sign(bat.scale.x),hand,'Attached glove shapes mirror with the player');
  for(const ring of rings){
   const cuffCenter=mean(ring.cuffIds.map(id=>vertex(palms,id))),skinCenter=mean(ring.skinIds.map(id=>vertex(ring.skin,id))),target=bat.localToWorld(V(...ring.cuff));
   assert(cuffCenter.distanceTo(target)<1e-7,'Shared cuff constant must describe its real mesh, not an arbitrary IK point');
   const error=skinCenter.distanceTo(cuffCenter);maxWristError=Math.max(maxWristError,error);assert(error<1e-6,`${label}/${ring.side}: skin wrist does not meet glove opening (${error})`);
   assert(ring.lower.localToWorld(skinWrist.clone()).distanceTo(wrists[ring.side+'Wrist'])<1e-6,`${label}: anatomical wrist anchor mismatch`);
   assert([...ring.arm.quaternion.toArray(),...ring.lower.quaternion.toArray()].every(Number.isFinite));
  }
  for(const [bone,position]of fixed)assert(bone.position.distanceTo(position)<1e-12,'Fitting the glove cannot lengthen or move a shoulder/elbow pivot');
  if(time===95){const error=bat.localToWorld(V(0,BAT_SWEET_SPOT,0)).distanceTo(V(x*.5,1.05+y*.55,0));maxContactError=Math.max(maxContactError,error);assert(error<1e-10);}
  const crouch=Math.max(pose.crouch+.045*(1-pose.reach),.075+Math.abs(pose.bodyShift)*.18),restChest=V(0,.95-crouch,pose.weightShift),shift=model.torso.position.clone().sub(restChest),worldShift=shift.clone().transformDirection(model.root.matrixWorld).multiplyScalar(shift.length()*scale);
  maxChestShift=Math.max(maxChestShift,worldShift.length());assert(worldShift.length()<.01,'Reach correction stays below one centimetre; do not replace the original body motion');
  assert(model.hips.position.distanceTo(V(0,.93-crouch,pose.weightShift))<1e-12,'Chest fitting cannot move the pelvis');
  if(worldShift.length()>1e-7){
   // Reset only the new small chest translation, then measure the real skinned
   // panels. Zero-weight hem stays tucked; all upper cloth/decal vertices share
   // the chest translation in proportion to their existing binding weights.
   const panels=meshes(model.root).filter(mesh=>mesh.userData.uniformPanel),before=panels.map(mesh=>Array.from({length:mesh.geometry.getAttribute('position').count},(_,i)=>vertex(mesh,i)));
   model.torso.position.copy(restChest);update(model);
   panels.forEach((mesh,j)=>{const weights=mesh.geometry.getAttribute('skinWeight');for(let i=0;i<weights.count;i++){
    const observed=before[j][i].clone().sub(vertex(mesh,i)),expected=worldShift.clone().multiplyScalar(weights.getY(i)),error=observed.distanceTo(expected);maxUniformError=Math.max(maxUniformError,error);assert(error<1e-6,`${label}: fitted shirt/detail no longer follows its shared waist/chest binding`);
   }});
   model.torso.position.copy(restChest.add(shift));update(model);uniformFrames++;
  }
  if(x===0&&y===0&&time===95)surfaces.push({height,hand,bodyType,load});
 }
 // Cold/ready and completed-swing boundaries are independent of pose history.
 for(const load of[0,1]){
  const aim={x:0,y:(1.05/scale-1.05)/.55},ready=swingPose(-1,aim,hand,0,load),start=swingPose(0,aim,hand,0,load);
  poseBatter(model,mirror,bat,ready);const before=rings.map(r=>r.lower.getWorldQuaternion(new THREE.Quaternion()));poseBatter(model,mirror,bat,start);
  rings.forEach((r,i)=>assert(before[i].angleTo(r.lower.getWorldQuaternion(new THREE.Quaternion()))<1e-7));
 }
 dispose(root);
}
console.log(`PASS batting grip: ${frames} actual skin/cuff frames across both hands, three heights/body types, loaded/unloaded and prepare/contact/follow-through; shared mesh anchors, immutable bat axis/95ms contact, original arm pivots, ${uniformFrames} chest-fit cloth checks.`);
console.log(JSON.stringify({maxWristError,maxContactError,maxChestShift,maxUniformError,maxTriangles}));
