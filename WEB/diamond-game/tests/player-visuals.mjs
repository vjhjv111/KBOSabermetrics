import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three');
const {playerAppearance}=load('lib/player-appearance.ts'),{scenePointerControls}=load('lib/scene-pointer.ts');
assert.equal(playerAppearance('한화','노시환').team,'HH');
assert.equal(playerAppearance('SSG','김광현').team,'SK');
assert.equal(playerAppearance('키움','이주형').team,'WO');
assert.equal(playerAppearance('HH','노시환').number,undefined);
assert.equal(playerAppearance('HH','노시환','2026:12345').number,undefined);
assert.equal(playerAppearance('HH','노시환','08').number,'08');
assert.equal(playerAppearance('unknown','선수').wordmark,'unknown');
function pointerHarness(side='batter'){
 let aim=null;
 const calls=[],captures=[];
 const pointer=(overrides={})=>({pointerId:1,pointerType:'touch',isPrimary:true,button:0,clientX:100,clientY:180,timeStamp:1000,preventDefault(){calls.push('prevent')},...overrides});
 const controls=scenePointerControls({side:()=>side,aim:e=>{aim=e.clientX;calls.push('aim:'+aim)},swing:()=>calls.push('swing:'+aim),chargeStart:()=>calls.push('charge'),chargeEnd:()=>calls.push('release'),chargeCancel:()=>calls.push('cancel-charge'),focus:()=>{},capture:id=>captures.push(id),release:()=>{}});
 return {controls,pointer,calls,captures};
}
for(const side of ['batter','pitcher']){
 const {controls,pointer,calls,captures}=pointerHarness(side);
 controls.down(pointer());
 assert.deepEqual(calls,[],`${side}: touching the field must leave scrolling available without aiming or swinging`);
 assert.deepEqual(captures,[],`${side}: touch-down must not explicitly capture a scrolling gesture`);
 controls.move(pointer({clientX:116,clientY:188,timeStamp:1040}));
 assert.deepEqual(calls,[],`${side}: small finger movement waits for the tap to finish`);
 controls.up(pointer({clientX:104,clientY:183,timeStamp:1080}));
 const expected=side==='batter'?['aim:100','swing:100']:['aim:100'];
 assert.deepEqual(calls,expected,`${side}: a tap uses its initial target, with aim set before a batter swing`);
 controls.up(pointer({timeStamp:1080}));
 assert.deepEqual(calls,expected,`${side}: one tap must perform only one action`);
}
for(const side of ['batter','pitcher']){
 for(const [duration,dx,dy] of [[450,0,0],[650,16,8],[700,12,16]]){
  const {controls,pointer,calls}=pointerHarness(side);
  controls.down(pointer());
  controls.move(pointer({clientX:100+dx,clientY:180+dy,timeStamp:1000+duration/2}));
  assert.deepEqual(calls,[],`${side}: a deliberate first tap still waits for release`);
  controls.up(pointer({clientX:100+dx,clientY:180+dy,timeStamp:1000+duration}));
  assert.deepEqual(calls,side==='batter'?['aim:100','swing:100']:['aim:100'],`${side}: a ${duration}ms first tap with normal finger drift acts exactly once`);
 }
 for(const gesture of ['701ms hold','21px release drift','21px out and back','diagonal swipe']){
  const {controls,pointer,calls}=pointerHarness(side);
  controls.down(pointer());
  if(gesture==='21px out and back'){
   controls.move(pointer({clientX:121,timeStamp:1050}));controls.move(pointer({timeStamp:1100}));
  }
  controls.up(pointer({timeStamp:gesture==='701ms hold'?1701:1150,...(gesture==='21px release drift'?{clientX:121}:gesture==='diagonal swipe'?{clientX:116,clientY:196}:{})}));
  assert.deepEqual(calls,[],`${side}: ${gesture} is not accepted as a tap`);
 }
 const {controls,pointer,calls}=pointerHarness(side);
 controls.down(pointer());controls.up(pointer({timeStamp:1450}));
 controls.down(pointer({pointerId:2,clientX:140,timeStamp:1530}));controls.up(pointer({pointerId:2,clientX:140,timeStamp:1650}));
 assert.deepEqual(calls,side==='batter'?['aim:100','swing:100','aim:140','swing:140']:['aim:100','aim:140'],`${side}: each deliberate tap works independently without requiring a second tap`);
}
for(const side of ['batter','pitcher']){
 for(const gesture of ['vertical swipe','horizontal swipe','drag back to origin','release displacement','long press']){
  const {controls,pointer,calls}=pointerHarness(side);
  controls.down(pointer());
  if(gesture==='vertical swipe')controls.move(pointer({clientY:300,timeStamp:1040}));
  if(gesture==='horizontal swipe')controls.move(pointer({clientX:250,timeStamp:1040}));
  if(gesture==='drag back to origin'){
   controls.move(pointer({clientY:300,timeStamp:1040}));
   controls.move(pointer({timeStamp:1060}));
  }
  controls.up(pointer({timeStamp:gesture==='long press'?2500:1080,...(gesture==='release displacement'?{clientY:300}:{})}));
  assert.deepEqual(calls,[],`${side}: ${gesture} must neither change aim nor swing, charge, or prevent scrolling`);
 }
 for(const eventType of ['pointercancel','lostpointercapture']){
  const {controls,pointer,calls}=pointerHarness(side);
  controls.down(pointer());controls.cancel(pointer({type:eventType,timeStamp:1040}));
  controls.move(pointer({clientY:300,timeStamp:1060}));controls.up(pointer({timeStamp:1080}));
  assert.deepEqual(calls,[],`${side}: ${eventType} discards the touch without triggering an action`);
  controls.down(pointer({pointerId:2,timeStamp:1200}));controls.up(pointer({pointerId:2,timeStamp:1280}));
  assert.deepEqual(calls,side==='batter'?['aim:100','swing:100']:['aim:100'],`${side}: a new tap works after ${eventType}`);
 }
 const {controls,pointer,calls}=pointerHarness(side);
 controls.down(pointer());controls.down(pointer({pointerId:2,isPrimary:false,timeStamp:1020}));
 controls.up(pointer({pointerId:2,isPrimary:false,timeStamp:1060}));controls.up(pointer({timeStamp:1080}));
 assert.deepEqual(calls,[],`${side}: adding a second finger invalidates the initial tap`);
 controls.down(pointer({pointerId:3,timeStamp:1200}));controls.up(pointer({pointerId:3,timeStamp:1280}));
 assert.deepEqual(calls,side==='batter'?['aim:100','swing:100']:['aim:100'],`${side}: a fresh tap works after both fingers lift`);
}
{
 const {controls,pointer,calls}=pointerHarness();
 controls.down(pointer());controls.cancel(pointer({pointerId:99}));controls.up(pointer({pointerId:99}));
 controls.up(pointer({timeStamp:1080}));
 assert.deepEqual(calls,['aim:100','swing:100'],'An unrelated pointer cannot cancel or finish the active tap');
}
{
 const {controls,pointer,calls,captures}=pointerHarness('pitcher');
 controls.move(pointer({pointerType:'mouse',clientX:90}));
 controls.down(pointer({pointerType:'mouse'}));controls.move(pointer({pointerType:'mouse',clientX:140}));controls.up(pointer({pointerType:'mouse'}));
 assert.deepEqual(calls,['aim:90','aim:100','charge','aim:140','release'],'Mouse pitching retains hover, drag aiming, and hold/release');
 assert.deepEqual(captures,[1],'Mouse pitching keeps pointer capture through a drag');
}
for(const eventType of ['pointercancel','lostpointercapture']){
 const {controls,pointer,calls}=pointerHarness('pitcher');
 controls.down(pointer({pointerType:'mouse'}));controls.cancel(pointer({pointerType:'mouse',type:eventType}));controls.up(pointer({pointerType:'mouse'}));
 assert.deepEqual(calls,['aim:100','charge','cancel-charge'],`${eventType} stops a mouse charge without pitching`);
 controls.down(pointer({pointerType:'mouse',pointerId:2}));controls.up(pointer({pointerType:'mouse',pointerId:2}));
 assert.deepEqual(calls.slice(-3),['aim:100','charge','release'],'A cancelled mouse charge does not block the next pitch');
}
{
 const {controls,pointer,calls}=pointerHarness();
 controls.down(pointer({pointerType:'mouse',button:2}));controls.down(pointer({isPrimary:false}));
 assert.deepEqual(calls,[],'Secondary buttons and lone non-primary touches do not act');
 controls.down(pointer({pointerType:'mouse',clientX:75}));
 assert.deepEqual(calls,['aim:75','swing:75'],'Mouse batting still swings immediately on pointer-down');
 controls.up(pointer({pointerType:'mouse',clientX:75}));
 assert.deepEqual(calls,['aim:75','swing:75'],'Mouse release does not swing twice');
}

const source=fs.readFileSync('app/action-scene.tsx','utf8');
const {createPlayer:player,dressPlayer,createBat,createMitt,equipCatcher}=load('lib/player-model.ts');
function resources(root){
 const geometries=new Set(),materials=new Set(),textures=new Set(),skeletons=new Set();
 root.traverse(object=>{
  if(object.geometry)geometries.add(object.geometry);
  if(object.isSkinnedMesh)skeletons.add(object.skeleton);
  if(object.material)for(const material of Array.isArray(object.material)?object.material:[object.material]){
   materials.add(material);for(const value of Object.values(material))if(value?.isTexture)textures.add(value);
  }
 });
 return {geometries,materials,textures,skeletons};
}
function inspectPlayer(model,label,maxMeshes=120){
 assert.equal(model.head.position.y,.82);assert.deepEqual(model.left.position.toArray(),[.27,.58,0]);assert.deepEqual(model.right.position.toArray(),[-.27,.58,0]);assert.equal(model.le.position.y,-.34);assert.equal(model.re.position.y,-.34);
 assert.equal(model.lf.parent,model.lk);assert.equal(model.rf.parent,model.rk);
 assert.equal(model.lf.position.y,-.43);assert.equal(model.rf.position.y,-.43);
 let meshes=0,triangles=0,skinned=0,skinnedArms=0,skinnedTrousers=0,skinnedUniform=0;
 model.root.updateMatrixWorld(true);
 model.root.traverse(object=>{
  if(!object.isMesh)return;meshes++;const geometry=object.geometry;triangles+=(geometry.index?.count??geometry.attributes.position.count)/3;
  for(const [attributeName,attribute] of Object.entries(geometry.attributes))for(const number of attribute.array)assert(Number.isFinite(number),`${label}: non-finite ${attributeName} on ${geometry.type}`);
  geometry.computeBoundingSphere();assert(Number.isFinite(geometry.boundingSphere.radius)&&geometry.boundingSphere.radius>0,`${label}: invalid mesh bounds`);
  if(object.isSkinnedMesh){
   skinned++;const weights=geometry.getAttribute('skinWeight'),indices=geometry.getAttribute('skinIndex'),positions=geometry.getAttribute('position');
   assert.equal(weights.itemSize,4);assert.equal(indices.itemSize,4);assert.equal(weights.count,positions.count);assert.equal(indices.count,positions.count);
   const hinges=[model.le,model.re,model.lk,model.rk].filter(joint=>object.skeleton.bones.some(bone=>bone.parent===joint));
   const isArm=hinges.some(joint=>joint===model.le||joint===model.re);
   if(isArm){skinnedArms++;assert.equal(object.skeleton.bones.length,2,`${label}: each continuous arm follows its shoulder and elbow`);assert.equal(hinges.length,1);}
   else if(object.userData.uniformPanel){skinnedUniform++;assert.equal(object.skeleton.bones.length,2,`${label}: uniform follows separate pelvis and chest`);assert.equal(object.skeleton.bones[0].parent,model.hips);assert.equal(object.skeleton.bones[1].parent,model.torso);}
   else{skinnedTrousers++;assert(object.skeleton.bones.length>=5,`${label}: trousers follow the hips and both leg joints`);assert.equal(hinges.length,2);}
   object.skeleton.update();const original=new THREE.Vector3(),deformed=new THREE.Vector3();
   for(let vertex=0;vertex<positions.count;vertex++){
    let sum=0;
    for(let influence=0;influence<4;influence++){
     const weight=weights.getComponent(vertex,influence),index=indices.getComponent(vertex,influence);
     assert(weight>=0&&weight<=1,`${label}: skin weights must be nonnegative and bounded`);
     assert(Number.isInteger(index)&&index>=0&&index<object.skeleton.bones.length,`${label}: skin indices must identify valid bones`);sum+=weight;
    }
    assert(Math.abs(sum-1)<1e-6,`${label}: every vertex needs normalized skin weights`);
    original.fromBufferAttribute(positions,vertex);object.getVertexPosition(vertex,deformed);
    assert(original.distanceTo(deformed)<1e-5,`${label}: inverse binds must preserve the rest shape`);
   }
   for(const joint of hinges){
    const boneIndex=object.skeleton.bones.findIndex(bone=>bone.parent===joint);
    assert(boneIndex>=0,`${label}: elbow/knee pivots drive their skinning skeleton`);
    const samples=[];
    for(let vertex=0;vertex<positions.count;vertex++)for(let influence=0;influence<4;influence++){
     if(indices.getComponent(vertex,influence)===boneIndex&&weights.getComponent(vertex,influence)>.5){samples.push([vertex,object.getVertexPosition(vertex,new THREE.Vector3())]);break;}
    }
    assert(samples.length>0,`${label}: each forearm or lower leg has vertices attached`);
    const rotation=joint.rotation.x;joint.rotation.x+=.6;model.root.updateMatrixWorld(true);object.skeleton.update();
    let movement=0;for(const [vertex,before] of samples)movement=Math.max(movement,object.getVertexPosition(vertex,deformed).distanceTo(before));
    assert(movement>.04,`${label}: bending each elbow/knee must deform its forearm/trouser leg`);
    joint.rotation.x=rotation;model.root.updateMatrixWorld(true);object.skeleton.update();
   }
  }
 });
 assert.equal(skinned,3+skinnedUniform,`${label}: arms, trousers and uniform are the only skinned parts`);
 assert(skinnedUniform>=4,`${label}: shirt, waist details and both wordmarks follow body rotation`);
 assert.equal(skinnedArms,2);assert.equal(skinnedTrousers,1);
 assert(meshes<=maxMeshes,`${label} must stay within its mobile draw-call budget: ${meshes}/${maxMeshes}`);
 assert(triangles<=50000,`${label} must stay within its mobile geometry budget: ${triangles}/50000`);
 return {meshes,triangles};
}
const fallback=player('#224466',true);dressPlayer(fallback,playerAppearance('HH','노시환'),-1);
assert.equal(resources(fallback.root).textures.size,0,'Server-side construction works without a DOM');
const budget=inspectPlayer(fallback,'batter');
const pitcher=player('#224466');inspectPlayer(pitcher,'pitcher');
const catcher=player('#224466');equipCatcher(catcher);inspectPlayer(catcher,'equipped catcher',170);

let canvases=0,painted=[];
globalThis.document={createElement(tag){
 assert.equal(tag,'canvas');canvases++;
 const canvas={width:0,height:0,pixels:null,getContext(){return context}};
 const context={fillText(text){painted.push(text)},strokeText(){},fillRect(){},clearRect(){},beginPath(){},closePath(){},moveTo(){},lineTo(){},arc(){},ellipse(){},stroke(){},fill(){},save(){},restore(){},translate(){},rotate(){},scale(){},setTransform(){},createImageData(width,height){return {width,height,data:new Uint8ClampedArray(width*height*4)}},putImageData(pixels){canvas.pixels=pixels.data}};
 return canvas;
}};
const model=player('#224466',true);dressPlayer(model,playerAppearance('HH','노시환',8),-1);
assert.equal(model.jersey.color.getHexString(),'f15c22');assert.equal(model.cap.color.getHexString(),'20252c');
assert(painted.includes('8'));
for(const {decal,material} of model.lettering){
 if(decal.isSkinnedMesh){assert.equal(decal.scale.x,1);assert.equal(material.map.repeat.x,-1);assert.equal(material.map.offset.x,1,'Skinned printing mirrors its UVs without detaching from cloth');}
 else assert.equal(decal.scale.x,-1,'Rigid cap lettering reverses its local transform');
}
const actorMaps=resources(model.root).textures,surfaceMaps=[...actorMaps].filter(texture=>texture.image.pixels),diffuseMaps=new Set(),dataMaps=new Set();
model.root.traverse(object=>{for(const material of object.material?(Array.isArray(object.material)?object.material:[object.material]):[]){if(material.map?.image.pixels)diffuseMaps.add(material.map);for(const slot of ['bumpMap','roughnessMap'])if(material[slot])dataMaps.add(material[slot]);}});
assert(dataMaps.size>=3,'Uniform cloth, skin and glove leather have their own material detail');
assert(diffuseMaps.has(model.jersey.map),'Jerseys include a neutral cloth diffuse map');
assert.equal(model.accent.map,model.jersey.map,'Uniform trim shares the same cloth diffuse map');
const trouserMesh=model.root.getObjectByName('Continuous tailored trousers');
assert.equal(trouserMesh.material.map,model.jersey.map,'Shirt and trousers share one cloth diffuse texture per player');
for(const texture of surfaceMaps){
 assert.equal(texture.colorSpace,diffuseMaps.has(texture)?THREE.SRGBColorSpace:THREE.NoColorSpace,'Diffuse maps use sRGB while height/roughness maps remain linear');
 assert(!(diffuseMaps.has(texture)&&dataMaps.has(texture)),'Pigment and scalar surface data do not share the same texture');
 assert.equal(texture.wrapS,THREE.RepeatWrapping);assert.equal(texture.wrapT,THREE.RepeatWrapping);
 assert(texture.image.width<=256&&texture.image.height<=256,'Actor surface details stay within a small mobile texture budget');
 const pixels=texture.image.pixels,values=new Set();
 for(let i=0;i<pixels.length;i+=4){assert.equal(pixels[i],pixels[i+1]);assert.equal(pixels[i],pixels[i+2]);assert.equal(pixels[i+3],255);values.add(pixels[i]);}
 assert(values.size>2,'Each surface contains visible material detail');
}
const disposed=new Map();
for(const texture of actorMaps){disposed.set(texture,0);texture.addEventListener('dispose',()=>disposed.set(texture,disposed.get(texture)+1));}
const steadyCanvases=canvases;
for(let frame=0;frame<60;frame++)dressPlayer(model,playerAppearance('HH','노시환',8),frame%2?-1:1);
assert.equal(canvases,steadyCanvases,'Animation and handedness changes do not regenerate textures');
assert([...disposed.values()].every(count=>count===0));
const oldLetters=model.lettering.map(slot=>slot.material.map);painted=[];dressPlayer(model,playerAppearance('SS','이승현'));
assert.equal(canvases,steadyCanvases+3,'Only the three uniform labels change with a player');
assert(oldLetters.every(texture=>disposed.get(texture)===1),'Changing players releases every old uniform label');
assert(surfaceMaps.every(texture=>disposed.get(texture)===0),'Player changes retain surface detail maps');
assert(!painted.some(text=>/^\d+$/.test(text)),'A missing jersey number stays absent');

const second=player('#224466');dressPlayer(second,playerAppearance('HT','김도영'));
assert.equal(second.cap.map,second.jersey.map,'Cloth caps share the uniform diffuse texture');
for(const team of ['OB','LG','SS','HH','HT','KT','LT','NC','SK','WO']){
 const appearance=playerAppearance(team,'검증선수');dressPlayer(second,appearance);
 const capLetter=second.lettering.find(slot=>slot.area==='cap').material.map.image.getContext('2d').fillStyle;
 const luminance=color=>{const c=new THREE.Color(color);return c.r*.2126+c.g*.7152+c.b*.0722;},ink=luminance(capLetter),cloth=luminance(appearance.cap);
 assert((Math.max(ink,cloth)+.05)/(Math.min(ink,cloth)+.05)>=3,`${team}: cap letters remain visible on the team cap`);
 for(const slot of second.lettering.filter(slot=>slot.area!=='cap'))assert.equal(slot.material.map.image.getContext('2d').fillStyle,appearance.letter,'Chest/back retain their original team lettering');
}
const secondMaps=resources(second.root).textures;assert([...secondMaps].every(texture=>!actorMaps.has(texture)),'Players own independent texture lifetimes');
const scene=new THREE.Scene();scene.add(model.root);createBat(scene);createMitt(scene);
const cleanupResources=resources(scene),cleanupDisposals=new Map();
for(const resource of [...cleanupResources.geometries,...cleanupResources.materials,...cleanupResources.textures]){cleanupDisposals.set(resource,0);resource.addEventListener('dispose',()=>cleanupDisposals.set(resource,cleanupDisposals.get(resource)+1));}
const skeletonDisposals=new Map(),boneTextureDisposals=new Map();
for(const skeleton of cleanupResources.skeletons){
 skeleton.computeBoneTexture();const texture=skeleton.boneTexture;boneTextureDisposals.set(texture,0);texture.addEventListener('dispose',()=>boneTextureDisposals.set(texture,boneTextureDisposals.get(texture)+1));
 skeletonDisposals.set(skeleton,0);const dispose=skeleton.dispose.bind(skeleton);skeleton.dispose=()=>{skeletonDisposals.set(skeleton,skeletonDisposals.get(skeleton)+1);dispose()};
}
const cleanupStart=source.indexOf('const geometries=new Set'),cleanupEnd=source.indexOf('renderer.dispose()',cleanupStart);
assert(cleanupStart>=0&&cleanupEnd>cleanupStart,'Scene teardown must expose its resource cleanup before renderer disposal');
const cleanup=ts.transpileModule(source.slice(cleanupStart,cleanupEnd),{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText;
let lightingDisposals=0;
new Function('scene','THREE','lighting',cleanup)(scene,THREE,{dispose(){lightingDisposals++}});
assert.equal(lightingDisposals,1,'Scene teardown releases the reflection and shadow render targets');
assert([...cleanupDisposals.values()].every(count=>count===1),'Scene teardown disposes each geometry, material and diffuse/bump texture exactly once');
assert(skeletonDisposals.size>0&&[...skeletonDisposals.values()].every(count=>count===1),'Scene teardown disposes each distinct skeleton exactly once');
assert([...boneTextureDisposals.values()].every(count=>count===1),'Skeleton disposal also releases its GPU bone texture exactly once');
assert(oldLetters.every(texture=>disposed.get(texture)===1),'Already-replaced uniform labels must not be disposed twice');
delete globalThis.document;

const {FIELD_CAMERAS,fieldFov}=load('lib/field-camera.ts');
const pointerSource=source.slice(source.indexOf('const aimEvent='),source.indexOf('const controls='));
for(const side of ['batter','pitcher'])for(const [width,height] of [[367,420],[390,610],[1100,650],[1800,800]]){
 const config=FIELD_CAMERAS[side],camera=new THREE.PerspectiveCamera(fieldFov(side,width/height),width/height,.03,300);
 camera.position.set(...config.position);camera.lookAt(...config.target);camera.updateProjectionMatrix();camera.updateMatrixWorld(true);
 const rect={left:29.5,top:145.75,width,height},node={getBoundingClientRect:()=>rect},ray=new THREE.Raycaster(),plane=new THREE.Plane(new THREE.Vector3(0,0,1),0),point=new THREE.Vector3(),frameProps={current:{aim:{current:null}}};
 const aimEvent=new Function('node','ray','plane','point','camera','frameProps','THREE','clamp','tracking',ts.transpileModule(pointerSource,{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText+';return aimEvent;')(node,ray,plane,point,camera,frameProps,THREE,(v,a,b)=>Math.max(a,Math.min(b,v)),false);
 for(const x of [-1,0,1])for(const y of [-1,0,1]){
  const projected=new THREE.Vector3(x*.5,1.05+y*.55,0).project(camera);
  aimEvent({clientX:rect.left+(projected.x*.5+.5)*width,clientY:rect.top+(-projected.y*.5+.5)*height});
  assert(Math.abs(frameProps.current.aim.current.x-x)<1e-10&&Math.abs(frameProps.current.aim.current.y-y)<1e-10,`Portrait/desktop touch must map to the visible field: ${side} ${width}x${height}`);
 }
}
console.log(`PASS athlete geometry and fixed rig, normalized skin weights and rest binds, mobile budgets, material/skeleton resource cleanup, mirrored lettering, real-number handling, portrait/desktop projected aim, tap-before-swing and pointer guards; batter ${budget.meshes} meshes / ${budget.triangles} triangles`);
