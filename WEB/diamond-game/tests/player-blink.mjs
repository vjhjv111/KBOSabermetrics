import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const T=createRequire(import.meta.url)('three');
const {createPlayer,dressPlayer,equipCatcher,equipRunnerHands,setPlayerDetail}=load('lib/player-model.ts');
const {playerAppearance,playerDimensions}=load('lib/player-appearance.ts');
const {setPlayerBlink,isPlayerBlinkMesh}=load('lib/player-blink.ts');
const {batchStaticPlayerMeshes}=load('lib/player-batching.ts');
let checks=0,maxTriangles=0;const check=(v,label)=>{checks++;assert(v,label);};
const meshes=root=>{const result=[];root.traverse(o=>{if(o.isMesh)result.push(o);});return result;};
const dynamic=model=>meshes(model.root).filter(isPlayerBlinkMesh);
const shape=mesh=>Array.from(mesh.geometry.attributes.position.array.slice(0,mesh.geometry.drawRange.count*3));
const transforms=model=>[model.root,model.hips,model.torso,model.head,model.left,model.right,model.le,model.re,model.ll,model.rl,model.lk,model.rk,model.lf,model.rf].map(o=>[...o.position.toArray(),...o.quaternion.toArray(),...o.scale.toArray()]);
function resources(model){const geometries=new Set(),materials=new Set(),textures=new Set(),skeletons=new Set();model.root.traverse(o=>{if(!o.isMesh)return;geometries.add(o.geometry);if(o.isSkinnedMesh)skeletons.add(o.skeleton);for(const m of Array.isArray(o.material)?o.material:[o.material]){materials.add(m);for(const v of Object.values(m))if(v?.isTexture)textures.add(v);}});return {geometries,materials,textures,skeletons};}
function dispose(model){for(const set of Object.values(resources(model)))for(const item of set)item.dispose();}
function onDispose(resource,callback){if(resource.addEventListener)resource.addEventListener('dispose',callback);else{const original=resource.dispose;resource.dispose=function(...args){callback();return original.apply(this,args);};}}
function staticHead(model){return meshes(model.head).filter(m=>!isPlayerBlinkMesh(m)).map(m=>({mesh:m,positions:Array.from(m.geometry.attributes.position.array),index:Array.from(m.geometry.index.array)}));}
const counts={};
for(const role of ['batter','pitcher','catcher','runner']){
 const model=createPlayer('#e7e7df',role==='batter'||role==='runner');
 if(role==='catcher')equipCatcher(model);if(role==='runner')equipRunnerHands(model);
 const parts=dynamic(model);check(parts.length===2,role+': two independently animated eyelid surfaces');
 check(parts.every(m=>m.parent===model.head.getObjectByName('Player blink')),role+': animated surfaces remain in the head graph after equipment batching');
 check(parts.every(m=>!model.detailMeshes.includes(m)),role+': automatic small-part LOD cannot override closure visibility');
 const refs=parts.map(m=>({geometry:m.geometry,position:m.geometry.attributes.position,normal:m.geometry.attributes.normal,uv:m.geometry.attributes.uv,index:m.geometry.index}));
 const original=staticHead(model),pose=transforms(model);let unwantedDisposals=0;
 for(const set of Object.values(resources(model)))for(const resource of set)onDispose(resource,()=>unwantedDisposals++);
 setPlayerBlink(model.head,.65);const neutral=parts.map(shape),ranges=parts.map(m=>m.geometry.drawRange.count);
 for(const amount of [.25,1,.5,0,.9,.65]){
  setPlayerBlink(model.head,amount);assert.deepEqual(transforms(model),pose,'Blink never changes skeleton, gaze or root transforms');checks++;
  const versions=parts.map(m=>Object.values(m.geometry.attributes).map(a=>a.version));setPlayerBlink(model.head,amount);
  assert.deepEqual(parts.map(m=>Object.values(m.geometry.attributes).map(a=>a.version)),versions,'An unchanged amount does not request uploads');checks++;
  for(const [i,m]of parts.entries()){for(const key of ['position','normal','uv'])check(m.geometry.attributes[key]===refs[i][key],'No per-frame attribute replacement');check(m.geometry===refs[i].geometry&&m.geometry.index===refs[i].index,'No per-frame geometry/index replacement');}
  for(const distance of [5,40,5]){const visibility=parts.map(m=>m.visible);setPlayerDetail(model,distance);assert.deepEqual(parts.map(m=>m.visible),visibility,'LOD preserves the animated cover state');checks++;}
 }
 assert.deepEqual(parts.map(shape),neutral,'Backward closure seek exactly restores the same active vertices');checks++;
 for(const {mesh,positions,index}of original){assert.deepEqual(Array.from(mesh.geometry.attributes.position.array),positions,'Static face/eyes/UV support do not move during blinking');assert.deepEqual(Array.from(mesh.geometry.index.array),index);checks+=2;}
 check(unwantedDisposals===0,'Animating and LOD switching never dispose resources');
 for(const bodyType of ['lean','power','athletic'])for(const heightCm of [155,185,215])for(const sign of [-1,1]){
  const look=playerAppearance('LG','눈깜빡임 검사',27,{bodyType,heightCm,skinTone:bodyType==='power'?'#865b40':'#c89675'}),dims=playerDimensions(look);
  // Edit appearance while the eyes are partly shut, then inspect the actual buffers.
  dressPlayer(model,look,sign);model.root.scale.set(sign*dims.heightScale,dims.heightScale,dims.heightScale);
  const sx=1+(dims.widthScale-1)*.18,sz=1+(dims.depthScale-1)*.12;
  for(const [j,m]of parts.entries()){
   check(m.geometry.drawRange.count===ranges[j],'Body edits retain the current closure topology');const p=shape(m);
   for(let i=0;i<p.length;i++){const factor=i%3===0?sx:i%3===2?sz:1;check(Math.abs(p[i]-neutral[j][i]*factor)<2e-8,'Body fitting uses open-eye originals without capturing or double-scaling a blink');}
   check(m.geometry.userData.restShape===undefined,'Animated vertices never enter the static rest-shape cache');
  }
  check(parts[0].material.color.getHexString()===look.skinTone.slice(1),'Cover immediately follows skin tone');
 }
 const before=dynamic(model);batchStaticPlayerMeshes(model.root,model.detailMeshes,model.lettering.map(s=>s.decal));assert.deepEqual(dynamic(model),before,'Additional batching preserves the rig and its buffers');checks++;
 let triangles=0;for(const mesh of meshes(model.root))triangles+=(mesh.geometry.index?.count??mesh.geometry.attributes.position.count)/3;
 maxTriangles=Math.max(maxTriangles,triangles);check(triangles<=50000,'Every allocated triangle stays within the existing player budget');counts[role]={meshes:meshes(model.root).length,triangles};
 dispose(model);
}
const first=createPlayer('#eee'),second=createPlayer('#eee'),a=resources(first),b=resources(second);
for(const key of Object.keys(a))for(const item of a[key])check(!b[key].has(item),'Different players own independent '+key);
const bParts=dynamic(second);setPlayerBlink(second.head,.5);const expected=bParts.map(shape),events=new Map();
for(const set of Object.values(a))for(const item of set){events.set(item,0);onDispose(item,()=>events.set(item,events.get(item)+1));}
dispose(first);for(const count of events.values())check(count===1,'Existing traversal cleanup releases each owned resource exactly once');
setPlayerBlink(second.head,1);setPlayerBlink(second.head,.5);assert.deepEqual(bParts.map(shape),expected,'Disposing another model leaves this animation intact');checks++;dispose(second);
console.log('PASS player blink integration '+JSON.stringify({checks,maxTriangles,counts}));
