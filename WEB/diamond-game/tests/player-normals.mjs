import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const THREE=createRequire(import.meta.url)('three');
const {loft,joined,oval,tube,restoreSeamNormals}=load('lib/player-geometry.ts');
const {createPlayer,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance,playerDimensions}=load('lib/player-appearance.ts');
const {hairGeometry}=load('lib/player-head.ts');
const profile=[[-.4,.04,.03,.004],[-.25,.065,.05,-.003],[0,.06,.047,.002],[.1,.02,.023,0]];
const pairs=geometry=>geometry.userData.normalSeamPairs??[];
function gap(geometry){
 const normal=geometry.getAttribute('normal');let distance=0,angle=0;
 for(const [a,b] of pairs(geometry)){
  const first=[normal.getX(a),normal.getY(a),normal.getZ(a)],last=[normal.getX(b),normal.getY(b),normal.getZ(b)];
  distance=Math.max(distance,Math.hypot(...first.map((value,i)=>value-last[i])));
  const dot=first.reduce((sum,value,i)=>sum+value*last[i],0)/Math.hypot(...first)/Math.hypot(...last);
  angle=Math.max(angle,Math.acos(Math.max(-1,Math.min(1,dot)))*180/Math.PI);
 }
 return {distance,angle};
}
function fixed(geometry,label){assert(gap(geometry).distance<1e-7,`${label}: declared seam normals must agree`);}
function invariantSnapshot(geometry){
 const attributes=Object.fromEntries(Object.entries(geometry.attributes).filter(([name])=>name!=='normal').map(([name,attribute])=>[name,Array.from(attribute.array)]));
 return {attributes,index:geometry.index&&Array.from(geometry.index.array),groups:structuredClone(geometry.groups),drawRange:{...geometry.drawRange},positionCount:geometry.getAttribute('position').count,pairs:structuredClone(pairs(geometry))};
}
function repairWithInvariants(geometry,label){
 const before=invariantSnapshot(geometry),normal=geometry.getAttribute('normal'),old=Float32Array.from(normal.array),members=new Set(pairs(geometry).flat()),refs={...geometry.attributes};
 const rest=geometry.userData.restShape,bounds=geometry.boundingBox;
 assert.equal(restoreSeamNormals(geometry),geometry,'Repair is an in-place normal-only operation');
 assert.deepEqual(invariantSnapshot(geometry),before,`${label}: repair preserves positions, UVs, skin bindings, indices and groups`);
 assert.equal(geometry.userData.restShape,rest);assert.equal(geometry.boundingBox,bounds);
 for(const [name,attribute] of Object.entries(refs))assert.equal(geometry.getAttribute(name),attribute,`${label}: no attribute allocation/replacement`);
 for(let index=0;index<normal.count;index++)if(!members.has(index))for(let component=0;component<3;component++)assert.equal(normal.getComponent(index,component),old[index*3+component],`${label}: non-seam normals stay unchanged`);
 fixed(geometry,label);
 return gap(geometry);
}

// Exact topology metadata is recorded once, including the intentionally split U coordinates.
const source=loft(profile,24,3,(y,a)=>.002*Math.sin(3*a+y*9));
assert.equal(pairs(source).length,10);fixed(source,'Fresh loft');
for(const [a,b] of pairs(source)){assert.equal(b-a,24);assert.equal(source.getAttribute('uv').getX(a),0);assert.equal(source.getAttribute('uv').getX(b),1);}
const sourcePairs=structuredClone(pairs(source)),shaped=source.clone();assert.deepEqual(pairs(shaped),pairs(source));
shaped.scale(1.13,1,1.15);shaped.computeVertexNormals();assert(gap(shaped).angle>5,'Plain recomputation reproduces the split-lighting defect');
shaped.userData.restShape=Float32Array.from(source.getAttribute('position').array);shaped.computeBoundingBox();
repairWithInvariants(shaped,'Scaled cloned loft');
assert.deepEqual(pairs(source),sourcePairs,'Repair never mutates metadata shared by a Three.js clone');
for(const style of ['short','buzz','flow']){
 const hair=hairGeometry(style);assert.equal(pairs(hair).length,13);fixed(hair,`${style} hair after hairline deformation`);
 hair.computeVertexNormals();repairWithInvariants(hair,`${style} hair regenerated normals`);
}

// Primitive metadata follows Three.js's exact generated rows, without changing
// the sphere's pole fans or the open tube's separate longitudinal ends.
const primitiveMeasurements=[];
for(const requestedSegments of [2,8,12,12.7,16]){
 const geometry=oval(.03,.045,.02,.01,-.02,.03,requestedSegments),around=Math.max(3,Math.floor(requestedSegments)),rowWidth=around+1;
 const original=new THREE.SphereGeometry(1,requestedSegments,8).scale(.03,.045,.02).translate(.01,-.02,.03);
 for(const name of ['position','uv'])assert.deepEqual(geometry.getAttribute(name).array,original.getAttribute(name).array,'Adding sphere metadata preserves the original primitive');
 assert.deepEqual(geometry.index.array,original.index.array);assert(geometry instanceof THREE.SphereGeometry);
 assert.equal(pairs(geometry).length,7);fixed(geometry,`Sphere ${requestedSegments} initial`);
 for(let row=1;row<8;row++){
  assert.deepEqual(pairs(geometry)[row-1],[row*rowWidth,row*rowWidth+around]);
  assert.equal(geometry.getAttribute('uv').getX(row*rowWidth),0);assert.equal(geometry.getAttribute('uv').getX(row*rowWidth+around),1);
 }
 const poleIds=[...Array.from({length:rowWidth},(_,i)=>i),...Array.from({length:rowWidth},(_,i)=>8*rowWidth+i)];
 assert(pairs(geometry).flat().every(index=>!poleIds.includes(index)),'No sphere pole radial sectors are marked as a seam');
 geometry.scale(1.13,1,1.15);geometry.computeVertexNormals();const broken=gap(geometry);
 const poleNormals=poleIds.map(index=>[0,1,2].map(component=>geometry.getAttribute('normal').getComponent(index,component)));
 const repaired=repairWithInvariants(geometry,`Sphere ${requestedSegments}`);
 assert.deepEqual(poleIds.map(index=>[0,1,2].map(component=>geometry.getAttribute('normal').getComponent(index,component))),poleNormals,'Sphere pole normals remain untouched');
 if(requestedSegments===12)primitiveMeasurements.push({kind:'oval',pairs:7,oldMaxDegrees:+broken.angle.toFixed(4),restoredMaxDegrees:+repaired.angle.toFixed(6)});
}
for(const segments of [4,12,24]){
 const points=[[0,0,0],[.03,.08,.02],[-.02,.18,.06]],geometry=tube(points,.002,segments),original=new THREE.TubeGeometry(new THREE.CatmullRomCurve3(points.map(point=>new THREE.Vector3(...point))),segments,.002,5,false);
 for(const name of ['position','uv'])assert.deepEqual(geometry.getAttribute(name).array,original.getAttribute(name).array,'Adding tube metadata preserves the original primitive');
 assert.deepEqual(geometry.index.array,original.index.array);assert(geometry instanceof THREE.TubeGeometry);
 assert.equal(pairs(geometry).length,segments+1);fixed(geometry,`Tube ${segments} initial`);
 for(let ring=0;ring<=segments;ring++){
  const [first,last]=pairs(geometry)[ring];assert.deepEqual([first,last],[ring*6,ring*6+5]);
  const uv=geometry.getAttribute('uv');assert.equal(uv.getX(first),uv.getX(last));assert.equal(uv.getY(first),0);assert.equal(uv.getY(last),1);
 }
 geometry.scale(1.13,1,1.15);geometry.computeVertexNormals();const broken=gap(geometry),repaired=repairWithInvariants(geometry,`Tube ${segments}`);
 if(segments===12)primitiveMeasurements.push({kind:'tube',pairs:segments+1,oldMaxDegrees:+broken.angle.toFixed(4),restoredMaxDegrees:+repaired.angle.toFixed(6)});
}
const spherePart=oval(.012,.02,.01),tubePart=tube([[0,0,0],[0,.05,.02],[.01,.08,.03]]),sphereCount=spherePart.getAttribute('position').count;
const primitivePairs=[...pairs(spherePart),...pairs(tubePart).map(pair=>pair.map(index=>index+sphereCount))],primitives=joined([spherePart,tubePart]);
assert.deepEqual(pairs(primitives),primitivePairs);primitives.scale(.91,1,.94);primitives.computeVertexNormals();repairWithInvariants(primitives,'Joined sphere and tube primitives');

// Joined geometry carries per-part offsets. A neighbouring hard-edged box remains hard.
const a=loft(profile,16,2).translate(-.2,0,0),b=loft(profile,20,3).rotateY(.6).translate(.2,0,0),box=new THREE.BoxGeometry(.1,.1,.1);
const aCount=a.getAttribute('position').count,bCount=b.getAttribute('position').count,boxCount=box.getAttribute('position').count;
const expected=[...pairs(a).map(pair=>[...pair]),...pairs(b).map(pair=>pair.map(index=>index+aCount))];
const combined=joined([a,b,box]);assert.deepEqual(pairs(combined),expected);
combined.scale(1.13,1,.94);combined.computeVertexNormals();
const hardStart=aCount+bCount,hardBefore=Array.from(combined.getAttribute('normal').array).slice(hardStart*3,(hardStart+boxCount)*3);
repairWithInvariants(combined,'Joined lofts and box');
assert.deepEqual(Array.from(combined.getAttribute('normal').array).slice(hardStart*3,(hardStart+boxCount)*3),hardBefore,'No positional welding or smoothing of hard box corners');
const extra=loft(profile,12,1),combinedCount=combined.getAttribute('position').count,expectedNested=[...pairs(combined),...pairs(extra).map(pair=>pair.map(index=>index+combinedCount))];
const nested=joined([combined,extra]);assert.deepEqual(pairs(nested),expectedNested);nested.computeVertexNormals();repairWithInvariants(nested,'Nested join');
const plainBox=new THREE.BoxGeometry(.1,.1,.1);plainBox.computeVertexNormals();const plainBefore=Array.from(plainBox.getAttribute('normal').array);restoreSeamNormals(plainBox);assert.deepEqual(Array.from(plainBox.getAttribute('normal').array),plainBefore,'Unmarked coincident hard-edge vertices are untouched');

// A later topology/binding edit invalidates smoothing for that precise pair, without welding it.
for(const scenario of ['separated position','different skin weight','different skin index','stale index']){
 const geometry=loft(profile,16,2),[first,last]=pairs(geometry)[2],count=geometry.getAttribute('position').count;
 geometry.userData.normalSeamPairs=[[first,last]];
 if(scenario==='separated position')geometry.getAttribute('position').setX(last,geometry.getAttribute('position').getX(last)+.002);
 if(scenario.startsWith('different skin')){
  const indices=new Uint16Array(count*4),weights=new Float32Array(count*4);for(let i=0;i<count;i++)weights[i*4]=1;
  if(scenario==='different skin index')indices[last*4]=1;else{weights[last*4]=.7;weights[last*4+1]=.3;}
  geometry.setAttribute('skinIndex',new THREE.Uint16BufferAttribute(indices,4));geometry.setAttribute('skinWeight',new THREE.Float32BufferAttribute(weights,4));
 }
 if(scenario==='stale index')geometry.userData.normalSeamPairs=[[first,count+4]];
 geometry.computeVertexNormals();const before=Array.from(geometry.getAttribute('normal').array);restoreSeamNormals(geometry);assert.deepEqual(Array.from(geometry.getAttribute('normal').array),before,`${scenario}: unsafe/stale metadata is skipped`);
}

// Exercise the real customization path, not merely a duplicate scale implementation.
const measurements=[],modelCoverage=[];
for(const isBatter of [false,true]){
 const model=createPlayer('#25465b',isBatter),arms=[];model.root.traverse(object=>{if(object.name==='Continuous anatomical arm')arms.push(object);});assert.equal(arms.length,2);
 const originalArms=arms.map(arm=>invariantSnapshot(arm.geometry));
 for(const arm of arms){assert.equal(pairs(arm.geometry).length,34);fixed(arm.geometry,'Initial real arm');}
 for(const bodyType of ['power','lean','athletic','power']){
  dressPlayer(model,playerAppearance('HH','fixture',undefined,{bodyType}));const dimensions=playerDimensions({bodyType});
  let markedMeshes=0,markedPairs=0;
  model.root.traverse(object=>{if(object.isMesh&&pairs(object.geometry).length){fixed(object.geometry,`${bodyType} ${object.name||'declared smooth surface'}`);markedMeshes++;markedPairs+=pairs(object.geometry).length;}});
  modelCoverage.push({role:isBatter?'batter':'pitcher',bodyType,markedMeshes,markedPairs});
  for(let armIndex=0;armIndex<arms.length;armIndex++){
   const arm=arms[armIndex],geometry=arm.geometry,original=originalArms[armIndex],current=invariantSnapshot(geometry);
   for(const name of ['uv','skinIndex','skinWeight','color'])assert.deepEqual(current.attributes[name],original.attributes[name],`${bodyType}: customization retains ${name}`);
   assert.deepEqual(current.index,original.index);assert.deepEqual(current.pairs,original.pairs);
   for(let vertex=0;vertex<geometry.getAttribute('position').count;vertex++){
    const p=geometry.getAttribute('position'),rest=original.attributes.position;
    assert(Math.abs(p.getX(vertex)-rest[vertex*3]*dimensions.widthScale)<1e-7);assert.equal(p.getY(vertex),rest[vertex*3+1]);assert(Math.abs(p.getZ(vertex)-rest[vertex*3+2]*dimensions.depthScale)<1e-7);
   }
   fixed(geometry,`${isBatter?'Batter':'Pitcher'} ${bodyType} arm after dressPlayer`);
   // Measured control: exactly the old recomputation loses the seam. The helper restores it.
   const raw=geometry.clone();raw.computeVertexNormals();const broken=gap(raw);assert(broken.angle>10);const repaired=repairWithInvariants(raw,'Real arm control');
   if(armIndex===0)measurements.push({role:isBatter?'batter':'pitcher',bodyType,pairs:pairs(raw).length,oldMaxDegrees:+broken.angle.toFixed(4),restoredMaxDegrees:+repaired.angle.toFixed(6)});
   // Skinning poses and the actual vertex locations remain bitwise unchanged by normal repair.
   const elbow=arm.skeleton.bones[1].parent;elbow.rotation.x=.61;model.root.updateMatrixWorld(true);arm.skeleton.update();
   const before=[];for(let vertex=0;vertex<geometry.getAttribute('position').count;vertex++)before.push(arm.getVertexPosition(vertex,new THREE.Vector3()).toArray());
   restoreSeamNormals(geometry);
   for(let vertex=0;vertex<before.length;vertex++)assert.deepEqual(arm.getVertexPosition(vertex,new THREE.Vector3()).toArray(),before[vertex]);
   elbow.rotation.x=0;
  }
 }
}
console.log('PASS player seam normals: exact loft/sphere/tube/clone/join metadata, body-type round trips, UV/index/skin/pose invariants, hard-edge/pole preservation and stale-pair guards');
console.log(JSON.stringify(measurements));
console.log(JSON.stringify({primitives:primitiveMeasurements,modelCoverage}));
