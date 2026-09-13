import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const THREE=createRequire(import.meta.url)('three');
const {createPlayer,equipCatcher,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts');
const V=(...a)=>new THREE.Vector3(...a);
function inventory(root){let meshes=0,triangles=0;root.traverse(o=>{if(o.isMesh){meshes++;triangles+=(o.geometry.index?.count??o.geometry.attributes.position.count)/3;}});return {meshes,triangles};}
function parts(root,name){
 const results=[];root.traverse(mesh=>{
  if(!mesh.isMesh)return;
  const source=mesh.userData.playerBatchParts??[{name:mesh.name,vertexStart:0,vertexCount:mesh.geometry.attributes.position.count,indexStart:0,indexCount:mesh.geometry.index?.count??mesh.geometry.attributes.position.count}];
  for(const part of source)if(part.name===name)results.push({mesh,...part});
 });return results;
}
function geometryOf(part){
 const {mesh,vertexStart,vertexCount,indexStart,indexCount}=part,source=mesh.geometry;
 const geometry=new THREE.BufferGeometry(),indices=[];
 for(const name of ['position','normal','uv'])if(source.attributes[name]){
  const attr=source.attributes[name];geometry.setAttribute(name,new THREE.BufferAttribute(attr.array.slice(vertexStart*attr.itemSize,(vertexStart+vertexCount)*attr.itemSize),attr.itemSize));
 }
 for(let i=indexStart;i<indexStart+indexCount;i++)indices.push((source.index?source.index.getX(i):i)-vertexStart);
 geometry.setIndex(indices);return geometry;
}
function points(root,name){return parts(root,name).flatMap(part=>Array.from(geometryOf(part).attributes.position.array));}
function capMeshes(model){const meshes=[];model.head.traverse(mesh=>{if(mesh.isMesh&&mesh.material===model.cap)meshes.push(mesh);});return meshes;}
function headMesh(part,head){
 const geometry=geometryOf(part),transform=head.matrixWorld.clone().invert().multiply(part.mesh.matrixWorld);geometry.applyMatrix4(transform);
 const mesh=new THREE.Mesh(geometry,new THREE.MeshBasicMaterial());mesh.updateMatrixWorld(true);return mesh;
}

const model=createPlayer('#265571'),before=inventory(model.root),footNames=['Shaped cleat upper','Cleat outsole','Cleat contact studs'],feet=footNames.map(name=>points(model.root,name));
const pivotNames=['root','hips','torso','head','left','right','le','re','ll','rl','lk','rk','lf','rf'];
const pivots=pivotNames.map(name=>({object:model[name],position:model[name].position.toArray(),quaternion:model[name].quaternion.toArray(),scale:model[name].scale.toArray()}));
const face=points(model.root,'Sculpted face'),hair=points(model.root,'Custom hair'),lettering=[...model.lettering];
equipCatcher(model);const after=inventory(model.root);assert(after.meshes<=170);assert(after.triangles<=50000);assert(after.triangles-before.triangles<=5270);
assert.equal(after.triangles,49664,'Reviewed catcher including the fixed-capacity eye covers and lash ribbons stays below 50k triangles');
for(const [i,name] of footNames.entries())assert.deepEqual(points(model.root,name),feet[i],`${name}: original contacts and shape are unchanged`);
for(const [i,name] of pivotNames.entries()){
 assert.equal(model[name],pivots[i].object);assert.deepEqual(model[name].position.toArray(),pivots[i].position);assert.deepEqual(model[name].quaternion.toArray(),pivots[i].quaternion);assert.deepEqual(model[name].scale.toArray(),pivots[i].scale);
}
assert.deepEqual(points(model.root,'Sculpted face'),face);assert.deepEqual(points(model.root,'Custom hair'),hair);assert.deepEqual(model.lettering,lettering);
assert(capMeshes(model).every(mesh=>mesh.visible),'The catcher keeps the original cap and printed lettering ownership');
equipCatcher(model);assert.deepEqual(inventory(model.root),after,'Equipping twice must not duplicate geometry or resources');

// Every protection panel is a real, closed, outward-facing shell, including
// narrow bevels and backs; no DoubleSide workaround or open tube endpoints.
const closedNames=['Catcher fitted chest backing','Catcher segmented sternum and rib pads','Catcher chest bound edge','Catcher curved shoulder cap','Catcher articulated knee and shin shells','Catcher raised tibia ridge','Catcher cheek and chin foam'];
let closedParts=0;
for(const name of closedNames)for(const part of parts(model.root,name)){
 const geometry=geometryOf(part),p=geometry.attributes.position,index=geometry.index,edges=new Map();let volume=0;
 for(let i=0;i<index.count;i+=3){
  const ids=[index.getX(i),index.getX(i+1),index.getX(i+2)],a=V().fromBufferAttribute(p,ids[0]),b=V().fromBufferAttribute(p,ids[1]),c=V().fromBufferAttribute(p,ids[2]);
  assert(b.clone().sub(a).cross(c.clone().sub(a)).lengthSq()>1e-17,`${name}: no degenerate face`);volume+=a.dot(b.clone().cross(c))/6;
  for(let e=0;e<3;e++){const x=ids[e],y=ids[(e+1)%3],key=x<y?`${x}:${y}`:`${y}:${x}`;edges.set(key,(edges.get(key)??0)+1);}
 }
 assert([...edges.values()].every(count=>count===2),`${name}: every shell edge has two incident faces`);
 assert(volume>0,`${name}: closed faces point outward`);assert.equal(part.mesh.material.side,THREE.FrontSide);closedParts++;
}

// Binding stays over the actual backing triangles rather than curving outside
// its edge and exposing a saw-toothed line of white shirt through the gap.
const backing=new THREE.Mesh(geometryOf(parts(model.root,'Catcher fitted chest backing')[0]),new THREE.MeshBasicMaterial());backing.updateMatrixWorld(true);
const border=geometryOf(parts(model.root,'Catcher chest bound edge')[0]),bp=border.attributes.position,bi=border.index;let bindingSamples=0;
for(let i=0;i<bi.count;i+=3){
 const a=V().fromBufferAttribute(bp,bi.getX(i)),b=V().fromBufferAttribute(bp,bi.getX(i+1)),c=V().fromBufferAttribute(bp,bi.getX(i+2));
 if(b.clone().sub(a).cross(c.clone().sub(a)).normalize().z<.5)continue;
 const centre=a.add(b).add(c).multiplyScalar(1/3),ray=new THREE.Raycaster(centre,V(0,0,-1)),hit=ray.intersectObject(backing,false)[0];
 assert(hit&&hit.distance>=.0004&&hit.distance<.004,'Every front binding triangle must sit directly on the backing');bindingSamples++;
}
assert(bindingSamples>=60);
let shoulderSamples=0;
for(const part of parts(model.root,'Catcher curved shoulder cap')){
 const sleevePart=parts(model.root,'Fitted jersey sleeve').find(candidate=>candidate.mesh.parent===part.mesh.parent);
 const sleeve=new THREE.Mesh(geometryOf(sleevePart),new THREE.MeshBasicMaterial());sleeve.updateMatrixWorld(true);
 const cap=geometryOf(part),p=cap.attributes.position;
 for(let i=0;i<p.count/2;i++){
  const point=V().fromBufferAttribute(p,i),hit=new THREE.Raycaster(point,V(0,0,-1)).intersectObject(sleeve,false)[0];
  assert(hit&&Math.abs(hit.distance-.009)<1e-6,'Shoulder protection follows the existing sleeve surface with 9mm padding, including edge vertices');shoulderSamples++;
 }
}

model.root.updateMatrixWorld(true);
const cagePart=parts(model.root,'Catcher open sightline face cage')[0],cage=headMesh(cagePart,model.head),foam=headMesh(parts(model.root,'Catcher cheek and chin foam')[0],model.head);
// Both pupil centres, plus a small visual window around each, see through the cage.
let sightRays=0;
for(const sign of [-1,1])for(const dx of [-.012,0,.012])for(const dy of [-.006,0,.006]){
 const ray=new THREE.Raycaster(V(sign*.036+dx,.061+dy,.105),V(0,0,1));
 assert.equal(ray.intersectObjects([cage,foam],false).length,0,'Cage and foam must leave each eye window open');sightRays++;
}
// Compute actual cage-to-cap triangle distance, not merely intersecting AABBs.
const triangles=[];
for(const mesh of capMeshes(model)){
 const transform=model.head.matrixWorld.clone().invert().multiply(mesh.matrixWorld),p=mesh.geometry.attributes.position,index=mesh.geometry.index;
 for(let i=0;i<index.count;i+=3)triangles.push(new THREE.Triangle(...[0,1,2].map(offset=>V().fromBufferAttribute(p,index.getX(i+offset)).applyMatrix4(transform))));
}
let capGap=Infinity;const closest=V();
for(let i=0;i<cage.geometry.attributes.position.count;i++){
 const p=V().fromBufferAttribute(cage.geometry.attributes.position,i);
 for(const triangle of triangles)capGap=Math.min(capGap,triangle.closestPointToPoint(p,closest).distanceTo(p));
}
assert(capGap>.002,'Mask wires must keep measurable clearance from the original cap/bill');

// Equipping before or after customization must produce identical protection.
// Subsequent body edits always start from immutable local restShape values.
const equipmentNames=[...closedNames,'Catcher open sightline face cage','Catcher fitted calf straps','Catcher shoulder and torso harness'];
for(const bodyType of ['lean','power','athletic']){
 const appearance=playerAppearance('HH','catcher',undefined,{bodyType,equipmentColor:'#6c2941'});
 const early=createPlayer('#265571'),late=createPlayer('#265571');equipCatcher(early);dressPlayer(early,appearance);dressPlayer(late,appearance);equipCatcher(late);
 for(const name of equipmentNames)assert.deepEqual(points(early.root,name),points(late.root,name),`${bodyType}: ${name} has the same shape regardless of equip timing`);
 for(const object of [early,late])object.root.traverse(mesh=>{if(mesh.isMesh&&mesh.material.userData.playerSurface==='equipment')assert.equal(mesh.material.color.getHexString(),'6c2941');});
 dressPlayer(early,playerAppearance('HH','catcher',undefined,{bodyType:'power',equipmentColor:'#253746'}));dressPlayer(early,appearance);
 for(const name of equipmentNames)assert.deepEqual(points(early.root,name),points(late.root,name),`${bodyType}: no accumulated scaling after a body-type round trip`);
 assert(inventory(early.root).triangles<=50000);
}
console.log('PASS catcher equipment: fixed rig/feet/head, closed outward panels, clear eye windows/cap gap, idempotent setup, equipment colors and body-shape ordering/round trips');
console.log(JSON.stringify({before,after,equipmentTriangles:after.triangles-before.triangles,closedParts,bindingSamples,shoulderSamples,sightRays,capGapMm:capGap*1000}));
