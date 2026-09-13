import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three'),V=(...a)=>new THREE.Vector3(...a);
const {createPlayer,dressPlayer}=load('lib/player-model.ts'),{playerAppearance}=load('lib/player-appearance.ts');
let checks=0,rivetsChecked=0,maxTriangles=0,minUpperEmbed=Infinity,maxUpperEmbed=0;
const ok=(condition,message)=>{checks++;assert(condition,message);};
function parts(model){const result=[];model.head.traverse(mesh=>{if(!mesh.isMesh)return;for(const part of mesh.userData.playerBatchParts??[{vertexStart:0,vertexCount:mesh.geometry.attributes.position.count,indexStart:0,indexCount:mesh.geometry.index.count}])result.push({...part,mesh});});return result;}
function triangles(part){const p=part.mesh.geometry.attributes.position,index=part.mesh.geometry.index,result=[];for(let i=part.indexStart;i<part.indexStart+part.indexCount;i+=3)result.push(new THREE.Triangle(...[0,1,2].map(k=>V().fromBufferAttribute(p,index.getX(i+k)).applyMatrix4(part.mesh.matrixWorld))));return result;}
function closest(point,faces){let distance=Infinity,nearest=V(),q=V();for(const face of faces){face.closestPointToPoint(point,q);const d=q.distanceTo(point);if(d<distance){distance=d;nearest.copy(q);}}return nearest;}
function intersects(a,b){const ray=new THREE.Ray(),delta=V(),hit=V();for(const face of a)for(const [p,q]of[[face.a,face.b],[face.b,face.c],[face.c,face.a]]){delta.copy(q).sub(p);const length=delta.length();if(length<1e-12)continue;ray.set(p,delta.divideScalar(length));for(const other of b)if(ray.intersectTriangle(other.a,other.b,other.c,false,hit)&&hit.distanceTo(p)<=length+1e-10)return true;}return false;}
function positions(part){const p=part.mesh.geometry.attributes.position;return Array.from({length:part.vertexCount},(_,i)=>V().fromBufferAttribute(p,part.vertexStart+i));}
function dispose(root){const all=new Set();root.traverse(o=>{if(o.geometry)all.add(o.geometry);if(o.skeleton)all.add(o.skeleton);for(const mat of Array.isArray(o.material)?o.material:o.material?[o.material]:[]){all.add(mat);Object.values(mat).filter(v=>v?.isTexture).forEach(v=>all.add(v));}});all.forEach(v=>v.dispose());}
for(const height of[155,185,215])for(const bodyType of['lean','athletic','power'])for(const hand of[-1,1]){
 const model=createPlayer('#183957',true),appearance=playerAppearance('LG','QA',27,{heightCm:height,bodyType,skinTone:'#e4bea0'}),scale=height/185;
 dressPlayer(model,appearance,hand);model.root.scale.set(hand*scale,scale,scale);model.root.rotation.set(.12,.3,-.05);model.root.updateWorldMatrix(true,true);
 // Batch metadata preserves the original two 12-sided loft guards and four
 // eight-sided rivets. Do not rely on their being standalone draw calls.
 const all=parts(model),guards=all.filter(p=>p.vertexCount===260&&p.indexCount===1296),rivets=all.filter(p=>p.vertexCount===324&&p.indexCount===1344);
 ok(guards.length===1&&rivets.length===1,'Both original guards and exactly four unchanged rivet primitives remain');
 const guard=guards[0],rivet=rivets[0],faces=triangles(guard),originalParts=[guard,rivet].map(part=>({part,positions:positions(part),geometry:part.mesh.geometry,material:part.mesh.material}));
 ok(rivet.mesh.material.color.getHexString()==='684f42','Rivets retain their existing brown feature material');
 for(let i=0;i<4;i++){
  const part={...rivet,vertexStart:rivet.vertexStart+i*81,vertexCount:81,indexStart:rivet.indexStart+i*336,indexCount:336},surface=triangles(part),vertices=positions(part).map(p=>p.applyMatrix4(part.mesh.matrixWorld));
  ok(intersects(surface,faces)||intersects(faces,surface),`Rivet ${i} touches actual guard triangles: ${height}/${bodyType}/${hand}`);rivetsChecked++;
  if(i%2===0){
   const center=new THREE.Box3().setFromPoints(vertices).getCenter(V()),near=closest(center,faces),normal=center.clone().sub(near).normalize(),distances=vertices.map(p=>p.clone().sub(near).dot(normal)),embed=-Math.min(...distances),outer=Math.max(...distances);
   minUpperEmbed=Math.min(minUpperEmbed,embed);maxUpperEmbed=Math.max(maxUpperEmbed,embed);
   ok(embed>.0003*scale&&embed<.0015*scale,'Upper rivet is shallowly seated, not floating or buried');
   ok(outer>.003*scale&&outer<.005*scale,'Upper rivet head stays visible above guard');
  }
 }
 // Both geometry pieces follow the same head body-shape transform. Appearance
 // round trips must not reallocate them or accumulate the attachment offset.
 dressPlayer(model,playerAppearance('LG','QA',27,{...appearance,bodyType:bodyType==='power'?'lean':'power'}),hand);dressPlayer(model,appearance,hand);
 for(const original of originalParts){ok(original.part.mesh.geometry===original.geometry&&original.part.mesh.material===original.material,'Changing body type preserves geometry/material ownership');const current=positions(original.part);current.forEach((point,i)=>ok(point.distanceTo(original.positions[i])<1e-7,'Guard/rivet rest-shape round trip is exact'));}
 let trianglesCount=0;model.root.traverse(mesh=>{if(mesh.isMesh)trianglesCount+=(mesh.geometry.index?.count??mesh.geometry.attributes.position.count)/3;});maxTriangles=Math.max(maxTriangles,trianglesCount);ok(trianglesCount<=50000,'The full batter remains within the unchanged 50k triangle budget');dispose(model.root);
}
console.log(`PASS helmet rivets: ${checks} assertions, ${rivetsChecked} actual surface contacts, 18 height/body/hand models; upper embed ${(minUpperEmbed*1000).toFixed(3)}–${(maxUpperEmbed*1000).toFixed(3)}mm; max ${maxTriangles} triangles`);
