import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const T=createRequire(import.meta.url)('three'),V=(...p)=>new T.Vector3(...p);
const {headDetails}=load('lib/player-head.ts'),{createPlayer,dressPlayer,equipCatcher,equipPitchGrip}=load('lib/player-model.ts'),{playerAppearance}=load('lib/player-appearance.ts');
const meshes=root=>{const out=[];root.traverse(o=>{if(o.isMesh)out.push(o);});return out;};
const count=root=>meshes(root).reduce((n,m)=>n+m.geometry.index.count/3,0);
function faces(geometry,part={vertexStart:0,indexStart:0,indexCount:geometry.index.count}){const p=geometry.attributes.position,index=geometry.index,out=[];for(let i=part.indexStart;i<part.indexStart+part.indexCount;i+=3)out.push(new T.Triangle(V().fromBufferAttribute(p,index.getX(i)),V().fromBufferAttribute(p,index.getX(i+1)),V().fromBufferAttribute(p,index.getX(i+2))));return out;}
function frontSampler(faces){const ray=new T.Ray(),hit=V();return(x,y)=>{ray.set(V(Math.abs(x)<1e-12?0:x,y,1),V(0,0,-1));let z=-Infinity;for(const face of faces)if(ray.intersectTriangle(face.a,face.b,face.c,false,hit))z=Math.max(z,hit.z);assert(Number.isFinite(z),'Surface samples stay inside the rendered triangles');return z;};}
function cleanup(root){const all=new Set();root.traverse(o=>{if(o.geometry)all.add(o.geometry);if(o.skeleton)all.add(o.skeleton);for(const material of Array.isArray(o.material)?o.material:o.material?[o.material]:[]){all.add(material);for(const value of Object.values(material))if(value?.isTexture)all.add(value);}});const counts=new Map();for(const r of all){counts.set(r,0);if(r.addEventListener)r.addEventListener('dispose',()=>counts.set(r,counts.get(r)+1));else{const original=r.dispose.bind(r);r.dispose=()=>{counts.set(r,counts.get(r)+1);original();};}}for(const r of all)r.dispose();assert([...counts.values()].every(n=>n===1),'Current geometry/material/texture/skeleton cleanup runs once');}
function isNostrilPart(mesh,part){const p=mesh.geometry.attributes.position;let minY=Infinity,maxY=-Infinity,minZ=Infinity;for(let i=part.vertexStart;i<part.vertexStart+part.vertexCount;i++){minY=Math.min(minY,p.getY(i));maxY=Math.max(maxY,p.getY(i));minZ=Math.min(minZ,p.getZ(i));}return minY>-.002&&maxY<.002&&minZ>.10;}
function parts(root){return meshes(root).flatMap(mesh=>(mesh.userData.playerBatchParts??[{name:mesh.name,vertexStart:0,vertexCount:mesh.geometry.attributes.position.count,indexStart:0,indexCount:mesh.geometry.index.count}]).map(part=>({mesh,...part})));}
const raw=new T.Group();headDetails(raw,new T.MeshPhysicalMaterial({color:'#c89675'}),new T.MeshPhysicalMaterial(),null,false);
const rawParts=parts(raw),face=rawParts.find(p=>p.name==='Sculpted face'),nose=rawParts.find(p=>p.name==='Nasal bridge and tip'),nostrils=rawParts.find(p=>isNostrilPart(p.mesh,p));assert(face&&nose&&nostrils);
assert.equal(nose.indexCount/3,1008,'The original nose tessellation budget is retained');assert.equal(nostrils.indexCount/3,72,'Clipped nostril surfaces replace the former 336-triangle ellipsoids');
for(const mesh of meshes(raw)){for(const attr of Object.values(mesh.geometry.attributes))assert([...attr.array].every(Number.isFinite));assert([...mesh.geometry.index.array].every(i=>i>=0&&i<mesh.geometry.attributes.position.count));}
const noseFront=frontSampler(faces(nose.mesh.geometry,nose)),np=nostrils.mesh.geometry.attributes.position,ni=nostrils.mesh.geometry.index;let samples=0,minPatchGap=Infinity,maxPatchGap=-Infinity;
for(let i=nostrils.indexStart;i<nostrils.indexStart+nostrils.indexCount;i+=3){const a=V().fromBufferAttribute(np,ni.getX(i)),b=V().fromBufferAttribute(np,ni.getX(i+1)),c=V().fromBufferAttribute(np,ni.getX(i+2)),normal=new T.Triangle(a,b,c).getNormal(V());assert(normal.z>.20,'Nostril triangles face out from the underside of the nose');
 for(const weights of[[1,0,0],[0,1,0],[0,0,1],[1/3,1/3,1/3],[.5,.5,0],[0,.5,.5],[.5,0,.5]]){const p=a.clone().multiplyScalar(weights[0]).addScaledVector(b,weights[1]).addScaledVector(c,weights[2]),gap=p.z-noseFront(p.x,p.y);assert(Math.abs(gap-.00015)<1e-8,'Every shadow face follows its exact supporting skin triangle');minPatchGap=Math.min(minPatchGap,gap);maxPatchGap=Math.max(maxPatchGap,gap);samples++;}
}
cleanup(raw);
let models=0,maxTriangles=0,minAttachment=Infinity,maxAttachment=-Infinity;
for(const role of['batter','pitcher','catcher']){
 const model=createPlayer('#eeeeee',role==='batter');if(role==='catcher')equipCatcher(model);if(role==='pitcher')equipPitchGrip(model);
 const pivots=[model.root,model.head,model.torso],parents=pivots.map(o=>o.parent),headPosition=model.head.position.clone();
 for(const bodyType of['lean','athletic','power'])for(const heightCm of[155,185,215])for(const sign of[-1,1]){
  dressPlayer(model,playerAppearance('LG','QA',27,{bodyType,heightCm,skinTone:'#ac7959'}),sign);const scale=heightCm/185;model.root.scale.set(sign*scale,scale,scale);
  const all=parts(model.head),face=all.find(p=>p.name==='Sculpted face'),nose=all.find(p=>p.name==='Nasal bridge and tip'),shadow=all.find(p=>isNostrilPart(p.mesh,p));assert(face&&nose&&shadow);
  const faceZ=frontSampler(faces(face.mesh.geometry,face)),noseZ=frontSampler(faces(nose.mesh.geometry,nose)),p=nose.mesh.geometry.attributes.position;
  for(let row=0;row<10;row++){const i=nose.vertexStart+row*25+12,y=p.getY(i),gap=(p.getZ(i)-faceZ(p.getX(i),y))*scale;assert(gap<-.0002&&gap>-.001,'Lower posterior nose stays seated through head width/depth/height and mirroring');minAttachment=Math.min(minAttachment,gap);maxAttachment=Math.max(maxAttachment,gap);}
  for(let i=0;i<25;i++){const j=nose.vertexStart+i,gap=(p.getZ(j)-faceZ(p.getX(j),p.getY(j)))*scale;assert(gap<-.0002&&gap>-.0005,`Whole lower nose boundary is connected: ${role}/${bodyType}/${heightCm}/${sign}/${i} gap=${gap}`);}
  const q=shadow.mesh.geometry.attributes.position;for(let i=shadow.vertexStart;i<shadow.vertexStart+shadow.vertexCount;i++){const gap=(q.getZ(i)-noseZ(q.getX(i),q.getY(i)))*scale;assert(gap>.0001&&gap<.0002,'Nostril skin spacing follows the exact same body transform');}
  assert.equal(shadow.mesh.material.side,T.FrontSide);assert.equal(nose.mesh.material.userData.playerSurface,'skin');assert.equal(nose.mesh.material.color.getHexString(),'ac7959');
  pivots.forEach((pivot,i)=>assert.equal(pivot.parent,parents[i]));assert(model.head.position.equals(headPosition));assert(count(model.root)<=50000);maxTriangles=Math.max(maxTriangles,count(model.root));models++;
 }
 cleanup(model.root);
}
console.log('PASS nasal attachment, exact conforming nostril triangles, outward faces, finite geometry, body/height/mirror contracts, unchanged rig/material ownership and cleanup');
console.log(JSON.stringify({models,samples,nostrilGapMm:[minPatchGap*1000,maxPatchGap*1000],attachmentMm:[minAttachment*1000,maxAttachment*1000],maxTriangles}));
