import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const T=createRequire(import.meta.url)('three'),V=(...x)=>new T.Vector3(...x);
const {createPlayer,dressPlayer,equipCatcher,equipRunnerHands,equipPitchGrip,getPitchGrip}=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts'),{pitchingPose,pitchReleasePosition}=load('lib/player-motion.ts'),{posePitcher}=load('lib/player-pose.ts');
const {attachPitchGripBall,PITCH_GRIP_BALL_RADIUS}=load('lib/player-pitch-grip.ts');
const meshes=root=>{const found=[];root.traverse(o=>{if(o.isMesh)found.push(o);});return found;};
const triangleCount=root=>meshes(root).reduce((n,m)=>n+m.geometry.index.count/3,0);
function resources(root){const geometries=new Set(),materials=new Set(),textures=new Set(),skeletons=new Set();root.traverse(o=>{if(o.geometry)geometries.add(o.geometry);if(o.skeleton)skeletons.add(o.skeleton);for(const material of Array.isArray(o.material)?o.material:o.material?[o.material]:[]){materials.add(material);for(const v of Object.values(material))if(v?.isTexture)textures.add(v);}});return{geometries,materials,textures,skeletons};}
function dispose(root){const all=resources(root);for(const set of Object.values(all))for(const item of set)item.dispose();}
function snapshot(geometry){return{attributes:Object.fromEntries(Object.entries(geometry.attributes).map(([key,a])=>[key,Array.from(a.array)])),indices:Array.from(geometry.index.array)};}
function ordinaryHand(model){return model.re.children.find(root=>meshes(root).some(mesh=>mesh.name==='Anatomical hand palm'||mesh.userData.playerBatchParts?.some(part=>part.name==='Anatomical hand palm')));}

// The default/fielder, catcher and running factories keep their ordinary hands.
for(const role of['fielder','catcher','runner']){
 const model=createPlayer('#eeeeee',role==='runner');if(role==='catcher')equipCatcher(model);if(role==='runner')equipRunnerHands(model);
 assert.equal(getPitchGrip(model),undefined);assert(!model.root.getObjectByName('Pitcher grip hand'));
 if(role!=='runner')assert(ordinaryHand(model));dispose(model.root);
}

let frames=0,maxAnchorError=0,maxStep=0,maxTriangles=0,minClearance=Infinity,maxPadGap=-Infinity,lifecycleCases=0;
for(const height of[155,185,188,215])for(const bodyType of['lean','athletic','power']){
 const look=playerAppearance('LG','투수',27,{heightCm:height,bodyType,skinTone:'#aa7755'}),scale=height/185,model=createPlayer('#eeeeee');
 dressPlayer(model,look);const before=resources(model.root),old=ordinaryHand(model),retired=meshes(old).map(mesh=>mesh.geometry),oldCounts=retired.map(()=>0);
 retired.forEach((g,i)=>g.addEventListener('dispose',()=>oldCounts[i]++));const untouched=meshes(model.root).filter(mesh=>!retired.includes(mesh.geometry)).map(mesh=>({mesh,geometry:mesh.geometry,material:mesh.material}));
 const grip=equipPitchGrip(model),geometry=grip.mesh.geometry,originalShape=snapshot(geometry);assert.equal(getPitchGrip(model),grip);assert.equal(equipPitchGrip(model),grip);assert.equal(grip.mesh.geometry,geometry);assert(oldCounts.every(count=>count===1));
 untouched.forEach(({mesh,geometry,material})=>{assert.equal(mesh.geometry,geometry);assert.equal(mesh.material,material);assert(mesh.parent);});
 assert.deepEqual(resources(model.root).materials,before.materials);assert.deepEqual(resources(model.root).textures,before.textures);assert.deepEqual(resources(model.root).skeletons,before.skeletons);
 assert.equal(grip.mesh.material.userData.playerSurface,'skin');assert.equal(grip.mesh.material.color.getHexString(),'aa7755');
 // Equipping before and after dressing produces identical final hand surfaces.
 const early=createPlayer('#eeeeee'),earlyGrip=equipPitchGrip(early);dressPlayer(early,look);assert.deepEqual(snapshot(earlyGrip.mesh.geometry),snapshot(grip.mesh.geometry));dispose(early.root);

 const g=grip.mesh.geometry,p=g.attributes.position,index=g.index;assert([...Object.values(g.attributes)].every(a=>[...a.array].every(Number.isFinite)));
 for(const [a,b] of g.userData.normalSeamPairs)assert(V().fromBufferAttribute(g.attributes.normal,a).distanceTo(V().fromBufferAttribute(g.attributes.normal,b))<1e-7);
 for(const part of grip.parts){let distance=Infinity;const a=V(),b=V(),c=V(),near=V(),face=new T.Triangle();
  for(let i=part.vertexStart;i<part.vertexStart+part.vertexCount;i++)assert(V().fromBufferAttribute(p,i).distanceTo(grip.ballCenter)*scale>=PITCH_GRIP_BALL_RADIUS,'No hand vertex is buried in the ball');
  for(let i=part.indexStart;i<part.indexStart+part.indexCount;i+=3){a.fromBufferAttribute(p,index.getX(i));b.fromBufferAttribute(p,index.getX(i+1));c.fromBufferAttribute(p,index.getX(i+2));face.set(a,b,c);distance=Math.min(distance,face.closestPointToPoint(grip.ballCenter,near).distanceTo(grip.ballCenter)*scale);}
  minClearance=Math.min(minClearance,distance-PITCH_GRIP_BALL_RADIUS);assert(distance>=PITCH_GRIP_BALL_RADIUS-.0001,'No deeply intersecting skin triangles');
  if(['Index finger','Middle finger','Thumb'].includes(part.name)){maxPadGap=Math.max(maxPadGap,distance-PITCH_GRIP_BALL_RADIUS);assert(distance<=PITCH_GRIP_BALL_RADIUS+.001,'Three pitching pads contact the sphere within 1mm');}
 }
 // Accept a different source sphere size but normalize its WORLD radius.
 const ball=new T.Group(),sphere=new T.Mesh(new T.SphereGeometry(.065,20,16),new T.MeshStandardMaterial({color:'white'}));ball.add(sphere);attachPitchGripBall(grip,ball,.065);assert.equal(attachPitchGripBall(grip,ball,.065),ball);assert.equal(ball.parent,grip.root);const ballShape=snapshot(sphere.geometry);
 for(const sign of[-1,1])for(const style of['overhand','sidearm','underhand']){
  model.root.position.z=-18.44;model.root.scale.set(sign*scale,scale,scale);let previous=null;
  for(let age=-1800;age<=1050;age+=5){
   const target=posePitcher(model,pitchingPose(age,style),sign),actual=ball.getWorldPosition(V()),error=actual.distanceTo(target);maxAnchorError=Math.max(maxAnchorError,error);assert(error<1e-8,'Grip holds the animation/engine ball centre');
   if(age===0)assert(actual.distanceTo(V(...pitchReleasePosition(style,sign,height)))<1e-8,'The server release position is unchanged');
   const worldScale=sphere.getWorldScale(V());for(const component of worldScale.toArray())assert(Math.abs(Math.abs(component)*.065-PITCH_GRIP_BALL_RADIUS)<1e-10,'Uniform fixed world ball radius through mirror/height/wrist motion');
   const current=[model.right.getWorldQuaternion(new T.Quaternion()),model.re.getWorldQuaternion(new T.Quaternion()),grip.root.getWorldQuaternion(new T.Quaternion())];if(previous)for(let i=0;i<current.length;i++)maxStep=Math.max(maxStep,previous[i].angleTo(current[i]));previous=current;frames++;
  }
 }
 maxTriangles=Math.max(maxTriangles,triangleCount(model.root));assert(triangleCount(model.root)<=50000);assert(meshes(model.root).length<=120);
 const slot=grip.mesh,skin=slot.material,samplers=Object.values(skin).filter(v=>v?.isTexture);let sharedDisposals=0;[skin,...samplers].forEach(resource=>resource.addEventListener('dispose',()=>sharedDisposals++));
 let retiredShape=grip.mesh.geometry;
 for(const next of['power','lean','athletic',bodyType]){
  let disposed=0;retiredShape.addEventListener('dispose',()=>disposed++);const previousBody=model.appearance.bodyType;
  dressPlayer(model,{...look,bodyType:next});assert.equal(grip.mesh,slot);assert.equal(slot.material,skin);assert.deepEqual(Object.values(skin).filter(v=>v?.isTexture),samplers);assert.equal(sharedDisposals,0);
  assert.equal(disposed,previousBody===next?0:1);retiredShape=grip.mesh.geometry;assert.deepEqual(snapshot(sphere.geometry),ballShape,'Body customization never stretches the baseball geometry');
 }
 assert.deepEqual(snapshot(grip.mesh.geometry),originalShape,'Body round trip is independent of previous dimensions');
 const stable=grip.mesh.geometry;dressPlayer(model,look);assert.equal(grip.mesh.geometry,stable,'Unchanged appearance does not allocate');
 let heightRefitDisposed=0;stable.addEventListener('dispose',()=>heightRefitDisposed++);const changedHeight=height===188?185:188;
 dressPlayer(model,{...look,heightCm:changedHeight});model.root.scale.set(changedHeight/185,changedHeight/185,changedHeight/185);const target=posePitcher(model,pitchingPose(0,'overhand'),1);
 assert.equal(heightRefitDisposed,1,'A profile height change refits the grip even when body type is unchanged');assert(ball.getWorldPosition(V()).distanceTo(target)<1e-8);assert(Math.abs(sphere.getWorldScale(V()).y*.065-PITCH_GRIP_BALL_RADIUS)<1e-10);assert.deepEqual(snapshot(sphere.geometry),ballShape);
 const live=resources(model.root),counts=new Map();for(const set of Object.values(live))for(const resource of set){counts.set(resource,0);if(resource.addEventListener)resource.addEventListener('dispose',()=>counts.set(resource,counts.get(resource)+1));else{const oldDispose=resource.dispose.bind(resource);resource.dispose=()=>{counts.set(resource,counts.get(resource)+1);oldDispose();};}}
 dispose(model.root);assert([...counts.values()].every(count=>count===1),'Scene cleanup releases current geometry/material/texture/skeleton exactly once');assert(oldCounts.every(count=>count===1));lifecycleCases++;
}
assert(maxStep<.42,'No upper/lower arm or wrist roll flips');
console.log('PASS explicit pitcher grip: default actor isolation, exact release/handedness/height, three-pad surface contact, body round trips, fixed world ball radius, sampler identity and disposal');
console.log(JSON.stringify({frames,maxAnchorError,maxStepRadiansPer5ms:maxStep,minSkinClearanceMm:minClearance*1000,maxPadGapMm:maxPadGap*1000,maxTriangles,lifecycleCases}));
