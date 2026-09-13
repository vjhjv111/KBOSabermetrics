import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const native=createRequire(import.meta.url),THREE=native('three'),V=(...a)=>new THREE.Vector3(...a);
const {batchStaticPlayerMeshes}=load('lib/player-batching.ts');
// Use the SAME current factory for both sides, even after its integration gains
// batching calls. Only its batching dependency is disabled in the control.
const controlCache=new Map();
function loadControl(file){
 file=path.resolve(file);if(controlCache.has(file))return controlCache.get(file).exports;
 const mod={exports:{}};controlCache.set(file,mod);
 const require=id=>id.startsWith('.')&&path.basename(id)==='player-batching'?{batchStaticPlayerMeshes(){return{groups:0,removedMeshes:0,disposedGeometries:0};}}:id.startsWith('.')?loadControl(path.resolve(path.dirname(file),id+'.ts')):native(id);
 new Function('require','module','exports',ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText)(require,mod,mod.exports);return mod.exports;
}
const {createPlayer,equipCatcher,dressPlayer,setPlayerDetail,createBat}=loadControl('lib/player-model.ts');
const integrated=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts');
const {poseBatter,posePitcher}=load('lib/player-pose.ts');
const {swingPose,pitchingPose}=load('lib/player-motion.ts');
const oldDocument=globalThis.document;
globalThis.document={createElement(tag){assert.equal(tag,'canvas');const context={fillText(){},strokeText(){},fillRect(){},clearRect(){},beginPath(){},closePath(){},moveTo(){},lineTo(){},arc(){},ellipse(){},stroke(){},fill(){},save(){},restore(){},translate(){},rotate(){},scale(){},setTransform(){},createImageData(w,h){return{data:new Uint8ClampedArray(w*h*4)};},putImageData(){}};return{width:0,height:0,getContext(){return context;}};}};
const meshes=root=>{const all=[];root.traverse(o=>{if(o.isMesh)all.push(o);});return all;};
const visible=mesh=>{for(let o=mesh;o;o=o.parent)if(!o.visible)return false;return true;};
const count=root=>{let draws=0,triangles=0;for(const mesh of meshes(root))if(visible(mesh)){draws++;triangles+=(mesh.geometry.index?.count??mesh.geometry.getAttribute('position').count)/3;}return{draws,triangles};};
function update(model){model.root.updateWorldMatrix(true,true);model.root.updateMatrixWorld(true);for(const skeleton of new Set(meshes(model.root).filter(o=>o.isSkinnedMesh).map(o=>o.skeleton)))skeleton.update();}
function equalArray(actual,expected,label){assert.equal(actual.length,expected.length,label+' length');for(let i=0;i<actual.length;i++)assert.equal(actual[i],expected[i],`${label}[${i}]`);}
function nearArray(actual,expected,label){assert.equal(actual.length,expected.length,label+' length');for(let i=0;i<actual.length;i++)assert(Math.abs(actual[i]-expected[i])<1e-9,`${label}[${i}] ${actual[i]} != ${expected[i]}`);}
function linkModels(candidate,reference){const current=meshes(candidate.root),raw=meshes(reference.root);assert.equal(current.length,raw.length);return new Map(current.map((mesh,i)=>[mesh.uuid,{mesh,raw:raw[i],parent:mesh.parent,geometry:mesh.geometry,material:mesh.material}]));}
function compare(candidate,reference,original,label,geometry=true){
 update(candidate);update(reference);
 let partsCount=0;
 for(const mesh of meshes(candidate.root)){
  const parts=mesh.userData.playerBatchParts??[{sourceId:mesh.uuid,vertexStart:0,vertexCount:mesh.geometry.getAttribute('position').count,indexStart:0,indexCount:mesh.geometry.index?.count??mesh.geometry.getAttribute('position').count}];
  for(const part of parts){
   const entry=original.get(part.sourceId);assert(entry,`${label}: source identity survives batching`);const raw=entry.raw;
   assert.equal(mesh.parent,entry.parent,`${label}: no pivot or motion parent changes`);assert.equal(mesh.material,entry.material,`${label}: original material object remains`);
   assert.equal(visible(mesh),visible(raw),`${label}: every original surface retains visibility`);
   nearArray(mesh.matrixWorld.elements,raw.matrixWorld.elements,`${label}: world transform`);
   assert.equal(mesh.castShadow,raw.castShadow);assert.equal(mesh.receiveShadow,raw.receiveShadow);assert.equal(mesh.layers.mask,raw.layers.mask);
   if(!mesh.userData.playerBatchParts){assert.equal(mesh,entry.mesh,`${label}: independent meshes retain identity`);if(mesh.isSkinnedMesh){assert.equal(mesh.skeleton,entry.mesh.skeleton);nearArray(mesh.bindMatrix.elements,raw.bindMatrix.elements,label+' skin bind');}}
   if(geometry){
    assert.deepEqual(Object.keys(mesh.geometry.attributes).sort(),Object.keys(raw.geometry.attributes).sort(),label+' attributes');
    for(const name of Object.keys(raw.geometry.attributes)){
     const a=mesh.geometry.getAttribute(name),b=raw.geometry.getAttribute(name),slice=a.array.subarray(part.vertexStart*a.itemSize,(part.vertexStart+part.vertexCount)*a.itemSize);
     assert.equal(a.itemSize,b.itemSize);assert.equal(a.normalized,b.normalized);assert.equal(a.array.constructor,b.array.constructor);assert.equal(a.usage,b.usage);assert.equal(a.gpuType,b.gpuType);
     equalArray(slice,b.array,`${label}: ${name}`);
    }
    const index=mesh.geometry.index,rawIndex=raw.geometry.index;assert.equal(part.indexCount,rawIndex?.count??part.vertexCount);
    for(let i=0;i<part.indexCount;i++)assert.equal((index?index.getX(part.indexStart+i):i)-part.vertexStart,rawIndex?rawIndex.getX(i):i,`${label}: triangle topology`);
    const rest=mesh.geometry.userData.restShape,rawRest=raw.geometry.userData.restShape;if(rawRest){assert(rest);equalArray(rest.subarray(part.vertexStart*3,(part.vertexStart+part.vertexCount)*3),rawRest,label+' immutable shape');}
    const pairs=(mesh.geometry.userData.normalSeamPairs??[]).filter(([a,b])=>a>=part.vertexStart&&a<part.vertexStart+part.vertexCount&&b>=part.vertexStart&&b<part.vertexStart+part.vertexCount).map(pair=>pair.map(i=>i-part.vertexStart));
    assert.deepEqual(pairs,raw.geometry.userData.normalSeamPairs??[],label+' declared smooth seams');
   }
   partsCount++;
  }
 }
 assert.equal(partsCount,meshes(reference.root).length,`${label}: every original mesh has one surface range`);
 assert.equal(count(candidate.root).triangles,count(reference.root).triangles,label+' visible triangle count');
}
function resources(root){const geometries=new Set(),materials=new Set(),textures=new Set(),skeletons=new Set();root.traverse(o=>{if(o.geometry)geometries.add(o.geometry);if(o.skeleton)skeletons.add(o.skeleton);for(const material of Array.isArray(o.material)?o.material:o.material?[o.material]:[]){materials.add(material);for(const value of Object.values(material))if(value?.isTexture)textures.add(value);}});return{geometries,materials,textures,skeletons};}
const preview=fs.readFileSync('app/character-preview.tsx','utf8'),cleanupStart=preview.indexOf('const geometries=new Set'),cleanupEnd=preview.indexOf('renderer.dispose()',cleanupStart);
assert(cleanupStart>=0&&cleanupEnd>cleanupStart);
const cleanup=new Function('scene','THREE','lighting',ts.transpileModule(preview.slice(cleanupStart,cleanupEnd),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText);
const exclusions=model=>model.lettering.map(slot=>slot.decal);
let shapeCases=0,poseCases=0,maxTriangles=0;const metrics=[];
try{
 for(const role of['batter','pitcher','catcher'])for(const dressedFirst of[false,true]){
  const reference=createPlayer('#ddd',role==='batter'),candidate=createPlayer('#ddd',role==='batter');if(role==='catcher'){equipCatcher(reference);equipCatcher(candidate);}
  const original=linkModels(candidate,reference),modelResources=resources(candidate.root),disposals=new Map();
  for(const resource of[...modelResources.geometries,...modelResources.materials,...modelResources.textures]){disposals.set(resource,0);resource.addEventListener('dispose',()=>disposals.set(resource,disposals.get(resource)+1));}
  if(dressedFirst){const appearance=playerAppearance('LG','검증',27,{bodyType:'power'});dressPlayer(reference,appearance);dressPlayer(candidate,appearance);}
  const before=count(candidate.root),report=batchStaticPlayerMeshes(candidate.root,candidate.detailMeshes,exclusions(candidate));
  assert(report.removedMeshes>0,`${role}: useful safe merge groups must exist`);compare(candidate,reference,original,`${role}/initial`);
  assert.equal(report.removedMeshes,before.draws-count(candidate.root).draws);
  assert.deepEqual(batchStaticPlayerMeshes(candidate.root,candidate.detailMeshes,exclusions(candidate)),{groups:0,removedMeshes:0,disposedGeometries:0},'Batching is idempotent');
  for(const mesh of meshes(candidate.root))if(mesh.userData.playerBatchParts)assert(!mesh.isSkinnedMesh&&!exclusions(candidate).includes(mesh)&&mesh.name!=='Custom hair');
  const materialDisposals=[...modelResources.materials].map(m=>disposals.get(m));assert(materialDisposals.every(n=>n===0),'Shared materials survive batching');
  const actorA=new THREE.Group(),mirrorA=new THREE.Group(),actorB=new THREE.Group(),mirrorB=new THREE.Group();actorA.add(mirrorA);actorB.add(mirrorB);mirrorA.add(candidate.root);mirrorB.add(reference.root);
  const batA=createBat(actorA),batB=createBat(actorB);
  // A round trip after pre-dressing catches accidental second application of
  // body width and missing seam metadata; each shape is compared vertex-wise.
  for(const bodyType of['power','lean','athletic','power','athletic']){
   const appearance=playerAppearance('LG','검증',27,{bodyType,hairStyle:bodyType==='lean'?'bald':bodyType==='power'?'flow':'short',skinTone:'#ad7755',gloveColor:'#654321',cleatColor:'#182632'});
   dressPlayer(reference,appearance);dressPlayer(candidate,appearance);compare(candidate,reference,original,`${role}/${bodyType}`);shapeCases++;
   for(const hand of[-1,1])for(const height of[155,185,215]){
    const scale=height/185;dressPlayer(reference,appearance,hand);dressPlayer(candidate,appearance,hand);
    for(const model of[candidate,reference])model.root.scale.set(1,1,1);
    for(const mirror of[mirrorA,mirrorB])mirror.scale.set(hand*scale,scale,scale);
    for(const age of role==='batter'?[0,60,95,350,1120]:[-1000,-65,0,350,1050]){
     if(role==='batter'){poseBatter(candidate,mirrorA,batA,swingPose(age,{x:.4,y:-.2},hand,0,1));poseBatter(reference,mirrorB,batB,swingPose(age,{x:.4,y:-.2},hand,0,1));}
     else{const style=['overhand','sidearm','underhand'][[155,185,215].indexOf(height)];posePitcher(candidate,pitchingPose(age,style),hand,false,0);posePitcher(reference,pitchingPose(age,style),hand,false,0);}
     compare(candidate,reference,original,`${role}/${bodyType}/${hand}/${height}@${age}`,false);poseCases++;
    }
   }
  }
  for(const compact of[false,true])for(const distance of[5,12,20,60]){
   setPlayerDetail(candidate,distance,compact);setPlayerDetail(reference,distance,compact);compare(candidate,reference,original,`${role}/${distance}m/${compact}`,false);
   if(!dressedFirst)metrics.push({role,distance,compact,raw:count(reference.root),batched:count(candidate.root),batchedMeshes:meshes(candidate.root).length});
  }
  setPlayerDetail(candidate,5);setPlayerDetail(reference,5);maxTriangles=Math.max(maxTriangles,count(candidate.root).triangles);assert(count(candidate.root).triangles<=50000,'Preserve the existing 50k player triangle budget');
  // All geometries retired by merging are disposed once. The real cleanup
  // then releases retained and new geometries, textures and shared skeletons.
  const retained=resources(actorA),finalCounts=new Map();for(const resource of[...retained.geometries,...retained.materials,...retained.textures]){finalCounts.set(resource,0);resource.addEventListener('dispose',()=>finalCounts.set(resource,finalCounts.get(resource)+1));}
  const skeletonCounts=new Map();for(const skeleton of retained.skeletons){skeletonCounts.set(skeleton,0);const dispose=skeleton.dispose.bind(skeleton);skeleton.dispose=()=>{skeletonCounts.set(skeleton,skeletonCounts.get(skeleton)+1);dispose();};}
  cleanup(actorA,THREE,{dispose(){}});assert([...finalCounts.values(),...skeletonCounts.values()].every(n=>n===1),'Actual preview cleanup disposes current resources exactly once');
  assert([...modelResources.geometries].every(g=>disposals.get(g)===1),'Retired and retained original geometries each disposed exactly once');
  cleanup(actorB,THREE,{dispose(){}});
 }

 // Negative cases are intentionally ineligible or in separate groups. Each is
 // tested against one otherwise mergeable sibling so silent flag loss fails.
 const material=new THREE.MeshStandardMaterial(),make=()=>new THREE.Mesh(new THREE.BoxGeometry(.1,.1,.1),material);
 const cases={
  parent(a,b,root){const parent=new THREE.Group();root.add(parent);parent.add(b);},material(a,b){b.material=material.clone();},transform(a,b){b.position.x=.1;},detail(a,b,root,details){details.push(b);},shadow(a,b){b.castShadow=true;},receive(a,b){b.receiveShadow=true;},visible(a,b){b.visible=false;},layer(a,b){b.layers.set(2);},renderOrder(a,b){b.renderOrder=1;},frustum(a,b){b.frustumCulled=false;},hair(a,b){b.name='Custom hair';},handle(a,b,root,details,excluded){excluded.push(b);},transparent(a,b){b.material=material.clone();b.material.transparent=true;},geometryMetadata(a,b){b.geometry.userData.liveShape=true;},meshMetadata(a,b){b.userData.livePart=true;},beforeRender(a,b){b.onBeforeRender=()=>{};},beforeShadow(a,b){b.onBeforeShadow=()=>{};},morph(a,b){b.morphTargetInfluences=[0];},drawRange(a,b){b.geometry.setDrawRange(0,6);},attribute(a,b){b.geometry.setAttribute('color',new THREE.Uint8BufferAttribute(new Uint8Array(b.geometry.getAttribute('position').count*3),3,true));},halfFloat(a,b){b.geometry.setAttribute('uv',new THREE.Float16BufferAttribute(new Uint16Array(b.geometry.getAttribute('position').count*2),2));},upload(a,b){b.geometry.getAttribute('position').onUpload(()=>{});},skinned(a,b,root){root.remove(b);const mesh=new THREE.SkinnedMesh(b.geometry,b.material);mesh.bind(new THREE.Skeleton([]));root.add(mesh);},instanced(a,b,root){root.remove(b);root.add(new THREE.InstancedMesh(b.geometry,b.material,2));}
 };
 for(const [name,mutate]of Object.entries(cases)){const root=new THREE.Group(),a=make(),b=make(),details=[],excluded=[];root.add(a,b);mutate(a,b,root,details,excluded);assert.equal(batchStaticPlayerMeshes(root,details,excluded).removedMeshes,0,name+' must not collapse incompatible meshes');cleanup(root,THREE,{dispose(){}});}

 // Same parent/transform/data format can mix indexed and unindexed input; all
 // raw normalized color/UV data and seam offsets must survive unchanged.
 {const root=new THREE.Group(),a=make(),b=make();b.geometry=b.geometry.toNonIndexed();for(const mesh of[a,b]){const count=mesh.geometry.getAttribute('position').count;mesh.geometry.setAttribute('color',new THREE.Uint8BufferAttribute(Uint8Array.from({length:count*3},(_,i)=>(i*13)%256),3,true));mesh.geometry.userData.normalSeamPairs=[[0,1]];}root.add(a,b);const original=meshes(root).map(mesh=>({id:mesh.uuid,geometry:mesh.geometry}));assert.equal(batchStaticPlayerMeshes(root,[]).removedMeshes,1);const merged=meshes(root)[0];for(const part of merged.userData.playerBatchParts){const source=original.find(o=>o.id===part.sourceId).geometry;equalArray(merged.geometry.getAttribute('color').array.subarray(part.vertexStart*3,(part.vertexStart+part.vertexCount)*3),source.getAttribute('color').array,'normalized colors');}assert.deepEqual(merged.geometry.userData.normalSeamPairs,[[0,1],[a.geometry.getAttribute('position').count,a.geometry.getAttribute('position').count+1]]);cleanup(root,THREE,{dispose(){}});}

 // A sibling outside the model may share an owned geometry. Retain that buffer
 // until the containing scene's normal cleanup, not the model's merge pass.
 {const scene=new THREE.Scene(),root=new THREE.Group(),a=make(),b=make(),outside=new THREE.Mesh(a.geometry,material);scene.add(root,outside);root.add(a,b);let shared=0,retired=0;a.geometry.addEventListener('dispose',()=>shared++);b.geometry.addEventListener('dispose',()=>retired++);assert.equal(batchStaticPlayerMeshes(root,[]).removedMeshes,1);assert.equal(shared,0);assert.equal(retired,1);cleanup(scene,THREE,{dispose(){}});assert.equal(shared,1);assert.equal(retired,1);}

 // Verify the production factory's two integration points, including the
 // catcher pass after the base model has already been merged.
 for(const role of['batter','pitcher','catcher']){const model=integrated.createPlayer('#ddd',role==='batter');if(role==='catcher')integrated.equipCatcher(model);const raw=createPlayer('#ddd',role==='batter');if(role==='catcher')equipCatcher(raw);const report=metrics.find(row=>row.role===role&&row.distance===5&&!row.compact);assert.equal(meshes(model.root).length,report.batchedMeshes,'Integrated factory has expected batched graph');assert.equal(count(model.root).triangles,count(raw.root).triangles,'Integrated factory preserves triangles');assert.deepEqual(batchStaticPlayerMeshes(model.root,model.detailMeshes,exclusions(model)),{groups:0,removedMeshes:0,disposedGeometries:0},'Factory and catcher integrations remain idempotent');cleanup(model.root,THREE,{dispose(){}});cleanup(raw.root,THREE,{dispose(){}});}
}finally{if(oldDocument===undefined)delete globalThis.document;else globalThis.document=oldDocument;}
console.log(`PASS ${shapeCases} exact shape comparisons, ${poseCases} posed surface comparisons, 24 exclusion cases, lifecycle/shared geometry, seam metadata, indexed/unindexed attributes and production integration. Maximum ${maxTriangles} triangles < 50000.`);
console.table(metrics.filter(row=>row.distance===5||row.distance===20).map(row=>({role:row.role,compact:row.compact,distance:row.distance,drawsBefore:row.raw.draws,drawsAfter:row.batched.draws,triangles:row.batched.triangles})));
