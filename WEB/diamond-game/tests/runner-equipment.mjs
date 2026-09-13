import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three'),V=(...p)=>new THREE.Vector3(...p);
const {createPlayer,equipRunnerHands,dressPlayer,setPlayerDetail}=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts');
const gloves=model=>[model.root.getObjectByName('Runner left batting glove'),model.root.getObjectByName('Runner right batting glove')];
const geometryPoints=root=>{const values=[];root.updateWorldMatrix(true,true);root.traverse(object=>{if(object.isMesh)values.push(...object.geometry.attributes.position.array);});return values;};
let maxTriangles=0;
for(const bodyType of ['athletic','lean','power']){
 const look=playerAppearance('LG','주자','27',{bodyType}),early=createPlayer('#eeeeee',true),late=createPlayer('#eeeeee',true);
 const original=new Map();early.root.traverse(object=>{if(object.isMesh)original.set(object,object.geometry);});
 const pivots=[early.le,early.re,early.left,early.right].map(pivot=>[pivot.position.clone(),pivot.quaternion.clone()]);
 equipRunnerHands(early);dressPlayer(early,look);dressPlayer(late,look);equipRunnerHands(late);
 for(let i=0;i<2;i++){
  const a=gloves(early)[i],b=gloves(late)[i];assert(a&&b,'A running batter has two actual hands without a bat');
  assert.equal(a.parent,i===0?early.le:early.re);assert.deepEqual(geometryPoints(a),geometryPoints(b),'Equipping before/after body customization has the same shape');
  const normal=V(0,0,1).applyQuaternion(a.quaternion);assert.equal(Math.sign(normal.x),i===0?-1:1,'Running palms face inward');
  assert(Math.abs(normal.z)<1e-9);assert.equal(a.scale.x,i===0?-1:1,'Opposite thumb geometry for the two hands');
  a.traverse(mesh=>{if(mesh.isMesh){assert(mesh.geometry.userData.restShape);assert([...mesh.geometry.attributes.position.array].every(Number.isFinite));}});
 }
 for(const [mesh,geometry] of original)assert.equal(mesh.geometry,geometry,'Adding runner hands never replaces an existing body surface');
 [early.le,early.re,early.left,early.right].forEach((pivot,i)=>{assert(pivot.position.equals(pivots[i][0]));assert(pivot.quaternion.equals(pivots[i][1]));});
 const beforeGloves=gloves(early),beforeGeometry=beforeGloves.map(geometryPoints);equipRunnerHands(early);
 assert.deepEqual(gloves(early),beforeGloves,'Repeated equipment setup keeps the same hand objects');assert.deepEqual(gloves(early).map(geometryPoints),beforeGeometry);
 dressPlayer(early,playerAppearance('LG','주자','27',{bodyType:'power'}));dressPlayer(early,look);assert.deepEqual(gloves(early).map(geometryPoints),beforeGeometry,'Body edits restore original hand positions without accumulating scale');
 setPlayerDetail(early,80,true);for(const glove of gloves(early))glove.traverse(mesh=>{if(mesh.isMesh)assert(mesh.visible,'Hands retain their silhouette in broadcast views');});
 let triangles=0;early.root.traverse(mesh=>{if(mesh.isMesh)triangles+=(mesh.geometry.index?.count??mesh.geometry.attributes.position.count)/3;});maxTriangles=Math.max(maxTriangles,triangles);assert(triangles<=50000,'The complete running avatar remains within the existing near model budget');
 for(const model of [early,late]){
  const geometries=new Set(),materials=new Set(),textures=new Set(),skeletons=new Set();model.root.traverse(mesh=>{if(mesh.isMesh){geometries.add(mesh.geometry);for(const material of Array.isArray(mesh.material)?mesh.material:[mesh.material])materials.add(material);if(mesh.isSkinnedMesh)skeletons.add(mesh.skeleton);}});
  for(const material of materials)for(const value of Object.values(material))if(value?.isTexture)textures.add(value);
  const resources=[...geometries,...materials,...textures],counts=new Map(resources.map(resource=>[resource,0]));for(const resource of resources)resource.addEventListener('dispose',()=>counts.set(resource,counts.get(resource)+1));
  resources.forEach(resource=>resource.dispose());skeletons.forEach(skeleton=>skeleton.dispose());assert([...counts.values()].every(count=>count===1),'Shared cloth textures and new hand surfaces are disposed once');
 }
}
console.log(`PASS runner hands: bilateral inward palms, stable arm pivots, equipment order/body edits, idempotency, full silhouettes and shared resource cleanup; ${maxTriangles} triangles < 50000`);
