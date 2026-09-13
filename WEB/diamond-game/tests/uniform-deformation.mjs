import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const THREE=createRequire(import.meta.url)('three'),V=(...args)=>new THREE.Vector3(...args);
const {createPlayer,createBat,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts');
const {poseBatter,posePitcher}=load('lib/player-pose.ts');
const {swingPose,pitchingPose,SWING_DURATION_MS,PITCH_WINDUP_MS,PITCH_RECOVERY_MS,MOUND_HEIGHT}=load('lib/player-motion.ts');
const oldDocument=globalThis.document;
globalThis.document={createElement(tag){assert.equal(tag,'canvas');const canvas={width:0,height:0,getContext(){return context;}},context={fillText(){},strokeText(){},fillRect(){},clearRect(){},beginPath(){},closePath(){},moveTo(){},lineTo(){},arc(){},ellipse(){},stroke(){},fill(){},save(){},restore(){},translate(){},rotate(){},scale(){},setTransform(){},createImageData(width,height){return{data:new Uint8ClampedArray(width*height*4)};},putImageData(){}};return canvas;}};
const worldVertex=(mesh,index)=>mesh.localToWorld(mesh.getVertexPosition(index,V()));
function update(model){model.root.updateWorldMatrix(true,true);model.root.updateMatrixWorld(true);const skeletons=new Set();model.root.traverse(o=>{if(o.isSkinnedMesh)skeletons.add(o.skeleton);});for(const skeleton of skeletons)skeleton.update();}
function resources(scene){const geometries=new Set(),materials=new Set(),textures=new Set(),skeletons=new Set();scene.traverse(o=>{if(o.geometry)geometries.add(o.geometry);if(o.skeleton)skeletons.add(o.skeleton);for(const m of Array.isArray(o.material)?o.material:o.material?[o.material]:[]){materials.add(m);for(const value of Object.values(m))if(value?.isTexture)textures.add(value);}});return{geometries,materials,textures,skeletons};}
const preview=fs.readFileSync('app/character-preview.tsx','utf8'),start=preview.indexOf('const geometries=new Set'),end=preview.indexOf('renderer.dispose()',start);
assert(start>=0&&end>start);
const cleanup=new Function('scene','THREE','lighting',ts.transpileModule(preview.slice(start,end),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText);

// Bind each printed sample to the nearest triangle of the actual shirt in its
// neutral pose. Following that same triangle later checks visible fit, without
// duplicating the implementation's weight formula or assuming rigid lettering.
function decalAnchors(model,shirt){
 const index=shirt.geometry.index,triangles=[];
 for(let i=0;i<index.count;i+=3){const ids=[index.getX(i),index.getX(i+1),index.getX(i+2)];triangles.push({ids,triangle:new THREE.Triangle(...ids.map(id=>worldVertex(shirt,id)))});}
 const result=[];
 for(const slot of model.lettering.filter(slot=>slot.area!=='cap')){
  const position=slot.decal.geometry.getAttribute('position'),uv=slot.decal.geometry.getAttribute('uv');
  const selected=[];
  for(const target of[[.2,0],[.5,0],[.8,0],[.5,.5],[.2,1],[.5,1],[.8,1]]){
   let best=Infinity,chosen=0;for(let i=0;i<position.count;i++){const d=Math.hypot(uv.getX(i)-target[0],uv.getY(i)-target[1]);if(d<best){best=d;chosen=i;}}selected.push(chosen);
  }
  for(const vertex of new Set(selected)){
   const point=worldVertex(slot.decal,vertex);let best=null;
   for(const {ids,triangle}of triangles){const near=triangle.closestPointToPoint(point,V()),gap=point.distanceTo(near);if(!best||gap<best.gap)best={ids,weights:triangle.getBarycoord(near,V()).toArray(),gap};}
   assert(best&&best.gap<.018,'Printed panels must begin close to their underlying shirt');
   result.push({mesh:slot.decal,vertex,area:slot.area,...best});
  }
 }
 return result;
}

let cases=0,maxGap=0,maxStep=0,maxTriangles=0;
try{
 for(const role of['batter','pitcher'])for(const hand of[-1,1])for(const height of[155,185,215])for(const bodyType of['lean','athletic','power']){
  const label=`${role}/${hand}/${height}/${bodyType}`,scale=height/185,scene=new THREE.Scene(),actor=new THREE.Group(),mirror=new THREE.Group();scene.add(actor);actor.add(mirror);
  const model=createPlayer('#dddddd',role==='batter'),look=playerAppearance('LG','다이아',27,{heightCm:height,bodyType});mirror.add(model.root);const bat=createBat(actor);dressPlayer(model,look,hand);
  if(role==='batter'){mirror.scale.set(hand*scale,scale,scale);actor.position.x=.92*hand*scale;}else{model.root.scale.set(hand*scale,scale,scale);actor.position.y=-MOUND_HEIGHT;}
  update(model);
  const panels=[];let triangles=0;model.root.traverse(o=>{if(o.isMesh)triangles+=(o.geometry.index?.count??o.geometry.getAttribute('position').count)/3;if(o.userData.uniformPanel)panels.push(o);});
  maxTriangles=Math.max(maxTriangles,triangles);assert(triangles<=50000,`${label}: preserve the existing 50k model budget`);
  const skeletons=new Set(panels.map(panel=>panel.skeleton));assert(panels.length>=4&&skeletons.size===1,'All cloth panels and front/back labels share one skeleton');
  const skeleton=[...skeletons][0];assert.equal(skeleton.bones.length,2);assert.equal(skeleton.bones[0].parent,model.hips);assert.equal(skeleton.bones[1].parent,model.torso);
  const shirt=panels.filter(panel=>panel.material===model.jersey).sort((a,b)=>b.geometry.getAttribute('position').count-a.geometry.getAttribute('position').count)[0];assert(shirt);
  for(const panel of panels){
   const p=panel.geometry.getAttribute('position'),w=panel.geometry.getAttribute('skinWeight'),indices=panel.geometry.getAttribute('skinIndex');
   assert.equal(p.count,w.count);assert.equal(p.count,indices.count);
   for(let i=0;i<p.count;i++){
    let sum=0;for(let j=0;j<4;j++){const weight=w.getComponent(i,j),index=indices.getComponent(i,j);assert(Number.isFinite(weight)&&weight>=0&&weight<=1);assert(Number.isInteger(index)&&index>=0&&index<2);sum+=weight;}
    assert(Math.abs(sum-1)<1e-6);assert(panel.getVertexPosition(i,V()).distanceTo(V().fromBufferAttribute(p,i))<1e-5,`${label}: the bind must preserve each translated or rotated cloth panel`);
   }
  }
  const p=shirt.geometry.getAttribute('position'),w=shirt.geometry.getAttribute('skinWeight'),hem=[],collar=[];
  for(let i=0;i<p.count;i++){
   if(w.getY(i)<1e-8)hem.push({id:i,local:model.hips.worldToLocal(worldVertex(shirt,i))});
   if(w.getX(i)<1e-8)collar.push({id:i,local:model.torso.worldToLocal(worldVertex(shirt,i))});
  }
  assert(hem.length>8&&collar.length>8,'Both the tucked waist and upper chest have anchored cloth');
  const anchors=decalAnchors(model,shirt),beforeHem=hem.map(v=>worldVertex(shirt,v.id)),beforeCollar=collar.map(v=>worldVertex(shirt,v.id));
  model.torso.rotation.y=.65;update(model);
  assert(Math.max(...hem.map((v,i)=>worldVertex(shirt,v.id).distanceTo(beforeHem[i])))<1e-5,'Turning the shoulders alone must not pull the tucked hem off the waist');
  assert(Math.max(...collar.map((v,i)=>worldVertex(shirt,v.id).distanceTo(beforeCollar[i])))>.06*scale,'The chest must visibly follow the shoulders');
  model.torso.rotation.y=0;update(model);
  for(const sign of[-hand,hand]){dressPlayer(model,look,sign);for(const {area,decal,material}of model.lettering){if(area==='cap')assert.equal(decal.scale.x,sign);else{assert.equal(decal.scale.x,1);assert.equal(material.map.repeat.x,sign);assert.equal(material.map.offset.x,sign<0?1:0);}}}
  const forms=role==='batter'?['swing']:['overhand','sidearm','underhand'];
  for(const form of forms){
   const start=role==='batter'?0:-PITCH_WINDUP_MS,end=role==='batter'?SWING_DURATION_MS:PITCH_RECOVERY_MS;
   const samples=[];for(let time=start;time<=end;time+=10)samples.push(time);if(samples.at(-1)!==end)samples.push(end);
   let previous=null;
   for(const time of samples){
    if(role==='batter'){const aim={x:0,y:(1.05/scale-1.05)/.55};poseBatter(model,mirror,bat,swingPose(time,aim,hand,time,1));}
    else posePitcher(model,pitchingPose(time,form),hand,false,0);
    update(model);
    for(const vertex of hem)assert(model.hips.worldToLocal(worldVertex(shirt,vertex.id)).distanceTo(vertex.local)<1e-5,`${label}: the hem remains attached to the pelvis through ${form}`);
    for(const vertex of collar)assert(model.torso.worldToLocal(worldVertex(shirt,vertex.id)).distanceTo(vertex.local)<1e-5,`${label}: the collar remains attached to the chest through ${form}`);
    const points=[];
    for(const anchor of anchors){
     const point=worldVertex(anchor.mesh,anchor.vertex),surface=V();anchor.ids.forEach((id,i)=>surface.addScaledVector(worldVertex(shirt,id),anchor.weights[i]));
     assert(point.toArray().every(Number.isFinite));const gap=point.distanceTo(surface);maxGap=Math.max(maxGap,gap);
     assert(gap<anchor.gap+.025*scale,`${label} ${form}@${time}: ${anchor.area} printing detached from cloth (${gap.toFixed(4)}m)`);points.push(point);
    }
    if(previous)for(let i=0;i<points.length;i++){const step=points[i].distanceTo(previous[i]);maxStep=Math.max(maxStep,step);assert(step<.12*scale,`${label} ${form}@${time}: cloth or lettering jumps between neighboring motion frames`);}
    previous=points;
   }
   cases++;
  }
  const all=resources(scene),counts=new Map();for(const resource of[...all.geometries,...all.materials,...all.textures]){counts.set(resource,0);resource.addEventListener('dispose',()=>counts.set(resource,counts.get(resource)+1));}
  const skeletonCounts=new Map(),boneTextureCounts=new Map();for(const skeleton of all.skeletons){skeleton.computeBoneTexture();const texture=skeleton.boneTexture;boneTextureCounts.set(texture,0);texture.addEventListener('dispose',()=>boneTextureCounts.set(texture,boneTextureCounts.get(texture)+1));skeletonCounts.set(skeleton,0);const original=skeleton.dispose.bind(skeleton);skeleton.dispose=()=>{skeletonCounts.set(skeleton,skeletonCounts.get(skeleton)+1);original();};}
  let lightingCount=0;cleanup(scene,THREE,{dispose(){lightingCount++;}});assert.equal(lightingCount,1);assert([...counts.values(),...skeletonCounts.values(),...boneTextureCounts.values()].every(n=>n===1),'Production preview cleanup releases shared skeleton and GPU resources only once');
 }
}finally{if(oldDocument===undefined)delete globalThis.document;else globalThis.document=oldDocument;}
console.log(`PASS ${cases} uniform motion cases: both hands, 155/185/215cm, all body types, 6 pitching forms and swings; pelvis hem/chest anchors, fitted printed panels, UV handedness, finite skinning and shared-skeleton cleanup. Max print gap ${maxGap.toFixed(4)}m, 10ms movement ${maxStep.toFixed(4)}m, ${maxTriangles} triangles < 50000.`);
