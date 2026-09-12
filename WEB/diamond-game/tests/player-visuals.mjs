import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import * as THREE from 'three';
import {load} from './load-ts.mjs';
const {playerAppearance}=load('lib/player-appearance.ts'),{scenePointerControls}=load('lib/scene-pointer.ts');
assert.equal(playerAppearance('한화','노시환').team,'HH');
assert.equal(playerAppearance('SSG','김광현').team,'SK');
assert.equal(playerAppearance('키움','이주형').team,'WO');
assert.equal(playerAppearance('HH','노시환').number,undefined);
assert.equal(playerAppearance('HH','노시환','2026:12345').number,undefined);
assert.equal(playerAppearance('HH','노시환','08').number,'08');
assert.equal(playerAppearance('unknown','선수').wordmark,'unknown');
let side='batter',aim=null,calls=[];
const pointer=(id=1,type='touch',primary=true,button=0,x=.25)=>({pointerId:id,pointerType:type,isPrimary:primary,button,x,preventDefault(){calls.push('prevent')}});
const controls=scenePointerControls({side:()=>side,aim:e=>{aim=e.x;calls.push('aim')},swing:()=>calls.push('swing:'+aim),chargeStart:()=>calls.push('charge'),chargeEnd:()=>calls.push('release'),focus:()=>{},capture:()=>{},release:()=>{}});
controls.down(pointer());
assert.deepEqual(calls,['prevent','aim','swing:0.25'],'Touch location must reach swing before it freezes the aim');
controls.move(pointer(1,'touch',true,0,.9));controls.down(pointer(2,'touch',false));controls.up(pointer(2,'touch',false));controls.down(pointer(3,'touch',true));
assert.equal(calls.filter(c=>c.startsWith('swing')).length,1,'Dragging and additional fingers must not swing twice');
assert.equal(aim,.25,'Dragging after touch-down must not move the frozen swing target');
controls.up(pointer());controls.down(pointer(3,'touch',true,0,-.8));assert.equal(calls.at(-1),'swing:-0.8');controls.up(pointer(3));
calls=[];side='pitcher';controls.down(pointer());controls.move(pointer(1,'touch',true,0,.7));controls.up(pointer());
assert.equal(aim,.7);assert(!calls.includes('charge')&&!calls.includes('release'),'Touch pitching continues to aim; its existing throw button controls release');
calls=[];controls.down(pointer(1,'mouse'));controls.move(pointer(1,'mouse',true,0,.4));controls.up(pointer(1,'mouse'));
assert.deepEqual(calls,['aim','charge','aim','release']);
calls=[];side='batter';controls.down(pointer(1,'mouse',true,2));controls.down(pointer(1,'touch',false));assert.equal(calls.length,0);
controls.down(pointer(1,'mouse',true,0,-.3));assert.deepEqual(calls,['aim','swing:-0.3']);controls.up(pointer(1,'mouse'));

const source=fs.readFileSync('app/action-scene.tsx','utf8');
const helpers=ts.transpileModule(source.slice(source.indexOf('const V='),source.indexOf('export default function')),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText;
const {player,dressPlayer}=new Function('THREE','clamp',helpers+';return {player,dressPlayer};')(THREE,(v,a,b)=>Math.max(a,Math.min(b,v)));
const model=player('#224466',true);dressPlayer(model,playerAppearance('HH','노시환'),-1);
assert.equal(model.jersey.color.getHexString(),'f15c22');assert.equal(model.cap.color.getHexString(),'20252c');
for(const {decal} of model.lettering)assert.equal(decal.scale.x,-1,'Hand mirroring must not reverse uniform lettering');
assert.equal(model.head.position.y,.82);assert.deepEqual(model.left.position.toArray(),[.27,.58,0]);assert.deepEqual(model.right.position.toArray(),[-.27,.58,0]);assert.equal(model.le.position.y,-.34);assert.equal(model.re.position.y,-.34);
let meshes=0,triangles=0;model.root.traverse(o=>{if(o.isMesh){meshes++;triangles+=(o.geometry.index?.count??o.geometry.attributes.position.count)/3;}});
assert(meshes<=140,`Player must stay within a modest draw-call budget: ${meshes}`);assert(triangles<18000,`Player geometry must remain suitable for mobile: ${triangles}`);

let canvases=0,disposedMaps=0,painted=[];
globalThis.document={createElement(tag){assert.equal(tag,'canvas');canvases++;return {getContext(){return {fillText(text){painted.push(text)},fillRect(){}}}}}};
dressPlayer(model,playerAppearance('HH','노시환',8));assert.equal(canvases,3);assert(painted.includes('8'));
model.lettering.forEach(slot=>slot.material.map.addEventListener('dispose',()=>disposedMaps++));
dressPlayer(model,playerAppearance('HH','노시환',8),-1);assert.equal(canvases,3,'Steady frames and handedness changes reuse existing textures');
painted=[];dressPlayer(model,playerAppearance('SS','이승현'));
assert.equal(canvases,6);assert.equal(disposedMaps,3,'Changing players releases the previous uniform maps');assert(!painted.some(text=>/^\d+$/.test(text)),'A missing jersey number stays absent');
delete globalThis.document;

const {FIELD_CAMERAS,fieldFov}=load('lib/field-camera.ts');
const pointerSource=source.slice(source.indexOf('const aimEvent='),source.indexOf('const controls='));
for(const side of ['batter','pitcher'])for(const [width,height] of [[367,420],[390,610],[1100,650],[1800,800]]){
 const config=FIELD_CAMERAS[side],camera=new THREE.PerspectiveCamera(fieldFov(side,width/height),width/height,.03,300);
 camera.position.set(...config.position);camera.lookAt(...config.target);camera.updateProjectionMatrix();camera.updateMatrixWorld(true);
 const rect={left:29.5,top:145.75,width,height},node={getBoundingClientRect:()=>rect},ray=new THREE.Raycaster(),plane=new THREE.Plane(new THREE.Vector3(0,0,1),0),point=new THREE.Vector3(),frameProps={current:{aim:{current:null}}};
 const aimEvent=new Function('node','ray','plane','point','camera','frameProps','THREE','clamp',ts.transpileModule(pointerSource,{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText+';return aimEvent;')(node,ray,plane,point,camera,frameProps,THREE,(v,a,b)=>Math.max(a,Math.min(b,v)));
 for(const x of [-1,0,1])for(const y of [-1,0,1]){
  const projected=new THREE.Vector3(x*.5,1.05+y*.55,0).project(camera);
  aimEvent({clientX:rect.left+(projected.x*.5+.5)*width,clientY:rect.top+(-projected.y*.5+.5)*height});
  assert(Math.abs(frameProps.current.aim.current.x-x)<1e-10&&Math.abs(frameProps.current.aim.current.y-y)<1e-10,`Portrait/desktop touch must map to the visible field: ${side} ${width}x${height}`);
 }
}
console.log(`PASS appearance/real-number handling, reused/disposed uniform textures, mirrored lettering, fixed skeleton, portrait/desktop projected aim, tap-before-swing, drag/multi-touch guards and desktop/pitcher controls; player ${meshes} meshes / ${triangles} triangles`);
