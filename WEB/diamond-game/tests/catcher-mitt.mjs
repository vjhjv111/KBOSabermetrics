import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const T=createRequire(import.meta.url)('three'),V=(...a)=>new T.Vector3(...a);
const {mitt,setMittStyle}=load('lib/player-equipment.ts');
const {createPlayer,equipCatcher,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance,playerDimensions}=load('lib/player-appearance.ts');
const {poseCatcher}=load('lib/catcher-pose.ts');
const meshes=root=>{const out=[];root.traverse(o=>{if(o.isMesh)out.push(o);});return out;};
const triangles=root=>meshes(root).reduce((n,m)=>n+m.geometry.index.count/3,0);
const gloveFor=model=>model.le.children.find(root=>meshes(root).some(mesh=>mesh.name==='Deep leather mitt pocket'||mesh.userData.playerBatchParts?.some(part=>part.name==='Deep leather mitt pocket')));
const snapshot=root=>meshes(root).map(mesh=>({positions:Array.from(mesh.geometry.attributes.position.array),uv:Array.from(mesh.geometry.attributes.uv.array),index:Array.from(mesh.geometry.index.array)}));
function valid(root){
 for(const mesh of meshes(root)){
  const g=mesh.geometry,p=g.attributes.position;for(const attr of Object.values(g.attributes))assert([...attr.array].every(Number.isFinite));
  assert([...g.index.array].every(index=>index>=0&&index<p.count));
  for(const part of mesh.userData.playerBatchParts??[]){assert(part.vertexStart>=0&&part.vertexStart+part.vertexCount<=p.count);assert(part.indexStart>=0&&part.indexStart+part.indexCount<=g.index.count);for(let i=part.indexStart;i<part.indexStart+part.indexCount;i++){const index=g.index.getX(i);assert(index>=part.vertexStart&&index<part.vertexStart+part.vertexCount);}}
  for(const [a,b] of g.userData.normalSeamPairs??[])assert(V().fromBufferAttribute(g.attributes.normal,a).distanceTo(V().fromBufferAttribute(g.attributes.normal,b))<1e-7,'New geometry keeps smooth periodic UV seams');
  assert.equal(mesh.material.side,T.FrontSide);
 }
}

// Both factory layouts (five independent parts and three post-batch slots)
// retain their exact root, mesh objects, transforms and existing material owners.
let swapCases=0,oldDisposals=0;
for(const batched of [false,true]){
 const texture=new T.Texture(),holder=new T.Group(),model=batched?createPlayer('#254968'):null,root=model?gloveFor(model):mitt(holder,1,texture),slots=meshes(root),materials=slots.map(mesh=>mesh.material),before=snapshot(root),parent=root.parent;
 const sourceIds=slots.map(mesh=>(mesh.userData.playerBatchParts??[{sourceId:mesh.uuid}]).map(part=>part.sourceId));
 const transform=[...root.position.toArray(),...root.quaternion.toArray(),...root.scale.toArray()];assert.equal(triangles(root),4030);
 let materialDisposals=0,textureDisposals=0;new Set(materials).forEach(material=>material.addEventListener('dispose',()=>materialDisposals++));texture.addEventListener('dispose',()=>textureDisposals++);
 for(const style of ['catcher','fielding','catcher']){
  const old=slots.map(mesh=>mesh.geometry),counts=old.map(()=>0);old.forEach((g,i)=>g.addEventListener('dispose',()=>counts[i]++));
  setMittStyle(root,style,{bodyType:'athletic',gloveColor:'#9e592a'});
  assert.deepEqual(meshes(root),slots);assert.equal(root.parent,parent);assert.deepEqual([...root.position.toArray(),...root.quaternion.toArray(),...root.scale.toArray()],transform);
  slots.forEach((mesh,i)=>assert.equal(mesh.material,materials[i]));assert(counts.every(count=>count===1));oldDisposals+=counts.length;
  slots.forEach((mesh,i)=>assert.deepEqual(mesh.userData.playerBatchParts.map(part=>part.sourceId),sourceIds[i],'Shape swaps retain actual slot or original batch source identities'));
  assert.equal(materialDisposals,0);assert.equal(textureDisposals,0);assert.equal(triangles(root),style==='catcher'?3214:4030);valid(root);
  if(style==='fielding')assert.deepEqual(snapshot(root),before,'Returning to fielding restores the original positions, UVs and topology');
  const current=slots.map(mesh=>mesh.geometry),versions=current.map(g=>g.attributes.position.version);
  setMittStyle(root,style,{bodyType:'athletic',gloveColor:'#9e592a'});
  slots.forEach((mesh,i)=>{assert.equal(mesh.geometry,current[i]);assert.equal(mesh.geometry.attributes.position.version,versions[i]);});
  assert.equal(materialDisposals,0);assert.equal(textureDisposals,0);swapCases++;
 }
 // Same style + changed dimensions updates the existing geometry from raw rest.
 const current=slots.map(mesh=>mesh.geometry);setMittStyle(root,'catcher',{bodyType:'power',gloveColor:'#345678'});slots.forEach((mesh,i)=>assert.equal(mesh.geometry,current[i]));
 assert(slots.filter(mesh=>mesh.material.userData.playerSurface==='glove').every(mesh=>mesh.material.color.getHexString()==='345678'));
 setMittStyle(root,'catcher',{bodyType:'lean'});setMittStyle(root,'catcher',{bodyType:'athletic',gloveColor:'#9e592a'});valid(root);
 // Final cleanup can still discover every live object. Material/texture owners
 // were retained through swaps and are disposed once only by their owner.
 const active=new Set(slots.map(mesh=>mesh.geometry)),disposed=new Map();active.forEach(g=>{disposed.set(g,0);g.addEventListener('dispose',()=>disposed.set(g,disposed.get(g)+1));g.dispose();});
 assert([...disposed.values()].every(count=>count===1));new Set(materials).forEach(material=>material.dispose());texture.dispose();assert.equal(materialDisposals,new Set(materials).size);assert.equal(textureDisposals,1);
}

// The pocket has a closed floor, correct +Z front faces and an exact centre.
const sample=createPlayer('#254968'),sampleRoot=gloveFor(sample);setMittStyle(sampleRoot,'catcher',sample.appearance);
const pocket=meshes(sampleRoot).find(mesh=>mesh.name==='Deep leather mitt pocket'),g=pocket.geometry,p=g.attributes.position,index=g.index;
const ray=new T.Raycaster(V(0,-.02,1),V(0,0,-1));sampleRoot.updateMatrixWorld(true);const localPocket=new T.Mesh(g,new T.MeshBasicMaterial());localPocket.updateMatrixWorld(true);
const hit=ray.intersectObject(localPocket,false)[0];assert(hit&&Math.abs(hit.point.z+.052)<1e-8,'Pocket floor remains exactly at the existing solver anchor');
const edges=new Map();let volume=0,frontFaces=0;
const vertexKey=i=>[p.getX(i),p.getY(i),p.getZ(i)].map(value=>Math.round(value*1e7)).join(',');
for(let i=0;i<index.count;i+=3){
 const ids=[index.getX(i),index.getX(i+1),index.getX(i+2)],a=V().fromBufferAttribute(p,ids[0]),b=V().fromBufferAttribute(p,ids[1]),c=V().fromBufferAttribute(p,ids[2]),normal=b.clone().sub(a).cross(c.clone().sub(a));
 assert(normal.lengthSq()>1e-18);volume+=a.dot(b.clone().cross(c))/6;if(normal.z>0)frontFaces++;
 for(let j=0;j<3;j++){const ends=[vertexKey(ids[j]),vertexKey(ids[(j+1)%3])].sort().join('/');edges.set(ends,(edges.get(ends)??0)+1);}
}
assert([...edges.values()].every(n=>n===2),'The leather bowl has no open radial or rim boundaries');assert(volume>0&&frontFaces>300,'The closed bowl is outward-facing');

function distanceToMitt(point,root){
 let minimum=Infinity;
 for(const mesh of meshes(root)){
  const g=mesh.geometry,p=g.attributes.position,index=g.index;
  for(let i=0;i<index.count;i+=3){const triangle=new T.Triangle(...[0,1,2].map(offset=>V().fromBufferAttribute(p,index.getX(i+offset))));minimum=Math.min(minimum,triangle.closestPointToPoint(point,V()).distanceTo(point));}
 }
 return minimum;
}
let poses=0,minBallDistance=Infinity,maxCuffGap=0;
for(const heightCm of [155,185,215])for(const bodyType of ['lean','athletic','power'])for(const sign of [-1,1]){
 const look={heightCm,bodyType,gloveColor:'#915a31'},appearance=playerAppearance('HH','Catcher mitt QA',27,look),early=createPlayer('#254968'),late=createPlayer('#254968');
 equipCatcher(early);const earlyRoot=gloveFor(early);setMittStyle(earlyRoot,'catcher',look);dressPlayer(early,appearance,sign);
 dressPlayer(late,appearance,sign);equipCatcher(late);const lateRoot=gloveFor(late);setMittStyle(lateRoot,'catcher',look);
 assert.deepEqual(snapshot(earlyRoot),snapshot(lateRoot),'Equip/dress ordering cannot change mitt shape');valid(earlyRoot);
 assert(triangles(early.root)<=50000&&meshes(early.root).length<=170);
 const scale=heightCm/185,depth=playerDimensions(look).depthScale;early.root.position.set(0,0,1.1);early.root.rotation.y=Math.PI;early.root.scale.set(sign*scale,scale,scale);
 for(const target of [{x:-.25,y:.5,z:.65},{x:0,y:1.05,z:.65},{x:.25,y:1.45,z:.65}])for(const now of [0,1000,1160]){
  const result=poseCatcher(early,{now,receiveAt:1000,prepareAt:350,target,catchBall:true});early.root.updateMatrixWorld(true);
  const cuff=earlyRoot.localToWorld(V(0,-.118,-.036*depth)),wrist=early.le.localToWorld(V(0,-.341,.006*depth)),gap=cuff.distanceTo(wrist);
  assert(gap<1e-7);maxCuffGap=Math.max(maxCuffGap,gap);
  const point=earlyRoot.worldToLocal(result.pocket.clone()),expected=V(0,-.02,-.052*depth+.065/scale);assert(point.distanceTo(expected)<1e-7,'The cached pose root still targets the same ball-centre anchor');
  const distance=distanceToMitt(point,earlyRoot)*scale;assert(distance>=.0649,`${heightCm}/${bodyType}/${sign}/${now}: mitt must contain the 65mm ball without clipping (${distance})`);minBallDistance=Math.min(minBallDistance,distance);poses++;
 }
 // A style change after pose cache initialization keeps the exact cached root.
 const rootRef=earlyRoot;setMittStyle(rootRef,'fielding',look);setMittStyle(rootRef,'catcher',look);assert.equal(gloveFor(early),rootRef);
 const result=poseCatcher(early,{now:1000,receiveAt:1000,target:{x:0,y:1.05,z:.65},catchBall:true});assert(result.pocket.toArray().every(Number.isFinite));
}
console.log('PASS catcher mitt: unchanged fielding defaults/round trips, stable roots/slots/materials, exact anchors, outward closed pocket, shape ordering, live pose caches and resource ownership');
console.log(JSON.stringify({swapCases,oldDisposals,catcherTriangles:3214,poses,minBallDistance,maxCuffGap}));
