import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three');
const {createStadiumWorld}=load('lib/stadium-world.ts');
for(const compact of[true,false]){
 const scene=new THREE.Scene(),world=createStadiumWorld(scene,{compact});world.root.updateMatrixWorld(true);
 assert.equal(world.field.mound.y,.254);assert.equal(world.field.mound.z,-18.44);
 for(const [a,b]of[['home','first'],['first','second'],['second','third'],['third','home']])assert(Math.abs(world.field[a].distanceTo(world.field[b])-27.432)<1e-10);
 const seats=world.root.getObjectByName('Stadium seating'),concrete=[],materials=new Set(),textures=new Set(),geometries=new Set();let draws=0,triangles=0;
 world.root.traverse(o=>{if(!o.isMesh)return;draws++;triangles+=(o.geometry.index?.count??o.geometry.attributes.position.count)/3*(o.isInstancedMesh?o.count:1);geometries.add(o.geometry);materials.add(o.material);for(const value of Object.values(o.material))if(value?.isTexture)textures.add(value);if(o.material.color?.getHexString()==='66717b')concrete.push(o);
  const p=o.geometry.getAttribute('position'),n=o.geometry.getAttribute('normal');for(let i=0;i<p.count;i++){assert(Number.isFinite(p.getX(i)+p.getY(i)+p.getZ(i)));if(n)assert(Number.isFinite(n.getX(i)+n.getY(i)+n.getZ(i)));}
 });
 assert(draws<=20,`Stadium architecture must stay batched: ${draws}`);assert(triangles<(compact?550000:900000),'Crowd detail must retain its wide-view budget');
 const turf=[...materials].find(m=>m.name==='Stadium turf'),shader={vertexShader:'#include <begin_vertex>',fragmentShader:'#include <color_fragment>'};turf.onBeforeCompile(shader,{});
 assert(!/\b(?:float|vec[234]|int|bool)\s+(?:patch|sample|input|output|common|partition|active|filter|superp|resource)\b/.test(shader.fragmentShader),'Custom grass shader may not declare GLSL reserved words');
 for(const [x,z]of[[0,-10],[0,-28],[8,-19.4]]){const surfaceRay=new THREE.Raycaster(new THREE.Vector3(x,3,z),new THREE.Vector3(0,-1,0));const hit=surfaceRay.intersectObject(world.root,true)[0];assert.equal(hit?.object.material.name,'Stadium turf','The infield grass must be a real cutout, free from a dirt sheet above it');}
 const matrix=new THREE.Matrix4(),point=new THREE.Vector3(),ray=new THREE.Raycaster();let supports=0;
 for(let i=5;i<seats.count;i+=41){seats.getMatrixAt(i,matrix);point.setFromMatrixPosition(matrix);ray.set(point,new THREE.Vector3(0,-1,0));ray.near=.01;ray.far=.5;assert(ray.intersectObjects(concrete,false).length,`Seat ${i} has no terrace under it`);supports++;}
 const disposals=new Map();for(const asset of[...geometries,...materials,...textures]){disposals.set(asset,0);asset.addEventListener('dispose',()=>disposals.set(asset,disposals.get(asset)+1));}world.dispose();world.dispose();assert(!scene.children.includes(world.root));assert([...disposals.values()].every(n=>n===1),'World lifetime disposes GPU resources once');
 console.log(`PASS ${compact?'compact':'desktop'} stadium: ${supports} seat supports, regulation geometry, ${draws} draws / ${triangles} triangles, finite normals and disposal`);
}
