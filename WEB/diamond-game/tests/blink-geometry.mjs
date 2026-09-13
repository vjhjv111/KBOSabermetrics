import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
import {load} from './load-ts.mjs';
const web=fileURLToPath(new URL('..',import.meta.url)),require=createRequire(import.meta.url);
const T=require('three'),ts=require('typescript'),candidatePath=path.join(web,'lib/player-blink.ts'),candidateSource=fs.readFileSync(candidatePath,'utf8');
const headPath=path.join(web,'lib/player-head.ts'),headSource=fs.readFileSync(headPath,'utf8');
let assertions=0,constructorCalls=0;
const check=(value,message)=>{assertions++;assert(value,message);};
const equal=(a,b,message)=>{assertions++;assert.deepEqual(a,b,message);};
const watched=new Map(),three=new Proxy(T,{get(target,key){const value=target[key];if(typeof value!=='function'||!/^([A-Z])/.test(String(key)))return value;if(!watched.has(key))watched.set(key,new Proxy(value,{construct(target,args){constructorCalls++;return Reflect.construct(target,args);}}));return watched.get(key);}});
function compile(source,file,overrides={}){
 const mod={exports:{}};const js=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
 new Function('require','module','exports',js)(id=>overrides[id]??(id==='three'?three:id.startsWith('.')?load(path.resolve(path.dirname(file),id+'.ts')):require(id)),mod,mod.exports);return mod.exports;
}
const api=compile(candidateSource+'\nexport const auditRig=(head:THREE.Object3D)=>rigs.get(head);',candidatePath);
const headAPI=compile(headSource+'\nexport {eyeGeometry,eyeBounds,faceZ,PLAYER_EYE_CENTER_X};',headPath);
const {joined}=load(path.join(web,'lib/player-geometry.ts'));
const eyes=joined([-1,1].map(sign=>headAPI.eyeGeometry(sign)));
const lashPaths=[-1,1].map(sign=>Array.from({length:9},(_,i)=>{const t=i/4-1,x=sign*(headAPI.PLAYER_EYE_CENTER_X+t*.0145),y=headAPI.eyeBounds(t)[1];return[x,y-.00045,headAPI.faceZ(x,y)+.00125];}));
const skin=new T.MeshPhysicalMaterial({color:'#c89675'}),features=new T.MeshPhysicalMaterial({color:'#684f42'});
function make(input=eyes){const head=new T.Group();api.createPlayerBlink(head,input,skin,features,lashPaths);return{head,rig:api.auditRig(head)};}
const first=make(),second=make(),rig=first.rig;
equal(Object.keys(api).sort(),['auditRig','createPlayerBlink','fitPlayerBlink','isPlayerBlinkMesh','setPlayerBlink'],'Requested API only, plus test-only private inspector');
equal(first.head.children.length,1,'One independently visible blink parent');
check(first.head.children[0]===rig.group,'Rig parent stays attached for scene cleanup');
check(api.isPlayerBlinkMesh(rig.cover.mesh)&&api.isPlayerBlinkMesh(rig.lash.mesh)&&!api.isPlayerBlinkMesh(rig.group),'Only the two dynamic meshes are marked');
check(rig.cover.mesh.material===skin&&rig.lash.mesh.material===features,'Materials and their textures are borrowed within this model');
check(rig.cover.mesh.geometry!==second.rig.cover.mesh.geometry&&rig.source!==second.rig.source&&rig.one!==second.rig.one,'Geometry, neutral data and clipping scratch are independently owned');
api.createPlayerBlink(first.head,new T.BufferGeometry(),skin,features,[]);equal(first.head.children.length,1,'Registration is idempotent');

function vertices(part){const g=part.mesh.geometry,p=g.attributes.position;return Array.from({length:g.drawRange.count},(_,i)=>[p.getX(i),p.getY(i),p.getZ(i)]);}
const snapshot=state=>[vertices(state.cover),vertices(state.lash),state.cover.mesh.visible];
for(const part of[rig.cover,rig.lash]){
 const g=part.mesh.geometry;check(!!g.index,'Geometry has a stable index');
 for(const attribute of Object.values(g.attributes))check(Array.from(attribute.array).every(Number.isFinite),'Entire active/inactive capacity is initialized and finite');
 g.computeBoundingSphere();check(g.boundingSphere.radius>0,'Open cover still has a nonzero neutral bounding sphere');
 check(g.attributes.position.usage===T.DynamicDrawUsage,'Dynamic vertex buffers are excluded from static batching');
}
const beforeChildren=rig.group.children.slice();load(path.join(web,'lib/player-batching.ts')).batchStaticPlayerMeshes(first.head,[]);
equal(rig.group.children,beforeChildren,'Static and repeated batching preserve the dynamic rig');
rig.group.visible=false;api.setPlayerBlink(first.head,.6);check(!rig.group.visible,'Amount updates cannot override parent LOD visibility');rig.group.visible=true;

const original=eyes.attributes.position,index=eyes.index,triangles=[];
for(let i=0;i<index.count;i+=3){const face=[0,1,2].map(k=>{const j=index.getX(i+k);return [original.getX(j),original.getY(j),original.getZ(j)];});if(cross(face[0],face[1],face[2])>1e-16)triangles.push(face);}
function cross(a,b,p){return(b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0]);}
const area=face=>cross(face[0],face[1],face[2])*.5;
function inside(face,p){return face.every((a,i)=>cross(a,face[(i+1)%3],p)>=-1e-11);}
function plane(face,p){const[a,b,c]=face,d=cross(a,b,c),u=((p[0]-a[0])*(c[1]-a[1])-(p[1]-a[1])*(c[0]-a[0]))/d,w=((b[0]-a[0])*(p[1]-a[1])-(b[1]-a[1])*(p[0]-a[0]))/d;return a[2]+u*(b[2]-a[2])+w*(c[2]-a[2]);}
let minCoverGap=Infinity,maxCoverGap=-Infinity,minLashGap=Infinity,maxLashGap=-Infinity,checkedTriangles=0,checkedPoints=0,maxUsed=0;
const sourceArea=triangles.reduce((sum,face)=>sum+area(face),0),amounts=[0,.001,.05,.199,.2,.25,.5,.75,.999,1];
for(const [body,sx,sz]of [['athletic',1,1],['lean',1+(.91-1)*.18,1+(.94-1)*.12],['power',1+(1.13-1)*.18,1+(1.15-1)*.12]]){
 api.fitPlayerBlink(first.head,sx,sz);
 for(const amount of amounts){
  api.setPlayerBlink(first.head,amount);let coverArea=0;
  for(const [kind,part,gap]of [['cover',rig.cover,.00015],['lash',rig.lash,.00022]]){
   const points=vertices(part),normals=part.mesh.geometry.attributes.normal;maxUsed=Math.max(maxUsed,points.length);
   for(let i=0;i<points.length;i++){
    const n=[normals.getX(i),normals.getY(i),normals.getZ(i)];check(Math.abs(Math.hypot(...n)-1)<1e-6&&n[2]>0,'Active normals are finite unit and outward');
    check(part.mesh.geometry.boundingSphere.containsPoint(new T.Vector3(...points[i])),'Conservative bounds contain the current face');
   }
   for(let i=0;i<points.length;i+=3){
    const face=points.slice(i,i+3).map(([x,y,z])=>[x/sx,y,z/sz]);check(area(face)>0,'Every active face has positive projected area');
    const support=triangles.find(source=>face.every(point=>inside(source,point)));check(!!support,'Whole emitted triangle is inside one real eye supporting triangle');
    const probes=[...face,face[0].map((n,j)=>(n+face[1][j])*.5),face[1].map((n,j)=>(n+face[2][j])*.5),face[2].map((n,j)=>(n+face[0][j])*.5),face[0].map((n,j)=>(n+face[1][j]+face[2][j])/3)];
    for(const point of probes){const actual=point[2]-plane(support,point);check(Math.abs(actual-gap)<2e-7,'Vertex/edge/centroid follows the exact supporting surface with a small positive gap');if(kind==='cover'){minCoverGap=Math.min(minCoverGap,actual);maxCoverGap=Math.max(maxCoverGap,actual);}else{minLashGap=Math.min(minLashGap,actual);maxLashGap=Math.max(maxLashGap,actual);}checkedPoints++;}
    if(kind==='cover')coverArea+=area(face);checkedTriangles++;
   }
   // At exact grid-row thresholds clipping can split the same surface with
   // different interior vertices. Compare the rendered reflected surface.
   const bins=new Set(points.map(([x,y,z])=>[-x/sx,y,z/sz].map(v=>Math.round(v*1e7)).join(',')));
   for(const point of points){const sameCell=bins.has([point[0]/sx,point[1],point[2]/sz].map(v=>Math.round(v*1e7)).join(','));let nearest=Infinity;if(!sameCell){const reflected=[-point[0],point[1],point[2]];for(let j=0;j<points.length;j+=3){const face=points.slice(j,j+3);if(inside(face,reflected))nearest=Math.min(nearest,Math.abs(plane(face,reflected)-reflected[2]));}}check(sameCell||nearest<2e-7,`Both eyelids have symmetric surfaces: ${body}/${amount}/${kind}, error=${nearest}`);}
  }
  if(amount===0)equal(rig.cover.mesh.geometry.drawRange.count,0,'Open eye has no covering skin triangles');
  if(amount===1)check(Math.abs(coverArea-sourceArea)<2e-10,'Fully closed skin covers the whole original eye area');
  // From .2 onward the lash strip is entirely on the already covered side.
  if(amount>=.2)for(const point of vertices(rig.lash)){
   const x=point[0]/sx,y=point[1];let left=null,right=null;
   for(let i=0;i<rig.source.length;i+=11){const px=rig.source[i];if(Math.sign(px)!==Math.sign(x))continue;if(px<=x+1e-8&&(!left||px>left[0]))left=[px,rig.source[i+8],rig.source[i+9]];if(px>=x-1e-8&&(!right||px<right[0]))right=[px,rig.source[i+8],rig.source[i+9]];}
   check(left&&right,'Lash stays within original eye columns');const t=right[0]===left[0]?0:(x-left[0])/(right[0]-left[0]);const lo=left[1]+(right[1]-left[1])*t,hi=left[2]+(right[2]-left[2])*t;check(y>=hi-(hi-lo)*amount-2e-8,'Closing lash stays over the skin cover');
  }
 }
}

// Keep the original open lash centerline/width in XY; depth is intentionally
// projected onto the real curved eye instead of preserving a floating tube.
api.fitPlayerBlink(first.head,1,1);api.setPlayerBlink(first.head,0);
const openPoints=vertices(rig.lash);let maxOpenCenterError=0,maxOpenWidthError=0,maxOpenDepthChange=0;
for(const path of lashPaths)for(const target of path.slice(1,-1)){
 let minY=Infinity,maxY=-Infinity,z=NaN;
 // Clipped Float32 edges can miss an authored x by a few nanometres. Clamp
 // edge interpolation so that tolerance never extrapolates a vertical edge.
 for(let i=0;i<openPoints.length;i+=3){const face=openPoints.slice(i,i+3);if(inside(face,target))z=plane(face,target);for(let edge=0;edge<3;edge++){const a=face[edge],b=face[(edge+1)%3];if(target[0]<Math.min(a[0],b[0])-5e-9||target[0]>Math.max(a[0],b[0])+5e-9)continue;if(Math.abs(b[0]-a[0])<1e-12){minY=Math.min(minY,a[1],b[1]);maxY=Math.max(maxY,a[1],b[1]);continue;}const t=Math.max(0,Math.min(1,(target[0]-a[0])/(b[0]-a[0]))),y=a[1]+(b[1]-a[1])*t;minY=Math.min(minY,y);maxY=Math.max(maxY,y);}}
 maxOpenCenterError=Math.max(maxOpenCenterError,Math.abs((minY+maxY)*.5-target[1]));maxOpenWidthError=Math.max(maxOpenWidthError,Math.abs(maxY-minY-.0009));maxOpenDepthChange=Math.max(maxOpenDepthChange,Math.abs(z-target[2]));check(Number.isFinite(z),'Open lash path is still inside the continuous ribbon');
}
check(maxOpenCenterError<1e-7&&maxOpenWidthError<1e-7,'Open centerline and original 0.9mm tube silhouette width are retained away from tapered corners');
api.setPlayerBlink(first.head,-2);const openSnapshot=snapshot(rig);api.setPlayerBlink(first.head,NaN);equal(snapshot(rig),openSnapshot,'Invalid amount safely opens');api.setPlayerBlink(first.head,1);const closedSnapshot=snapshot(rig);api.setPlayerBlink(first.head,2);equal(snapshot(rig),closedSnapshot,'Amount is clamped at fully closed');
for(const value of [0,-1,NaN,Infinity]){assert.throws(()=>api.fitPlayerBlink(first.head,value,1));assertions++;equal(snapshot(rig),closedSnapshot,'Invalid body scale cannot corrupt the current pose');}

// Fixed buffer identity and Three/typed-array allocation counters during amount
// changes and body round trips; no geometry/material/texture churn is hidden.
const objects=[rig.cover,rig.lash].flatMap(part=>[part.mesh.geometry,part.position.array,part.normal.array,part.uv.array,part.mesh.geometry.index.array]);
const ctorBefore=constructorCalls,typed=Object.fromEntries(['Float32Array','Float64Array','Uint16Array','Uint32Array'].map(name=>[name,globalThis[name]]));let typedCalls=0;
for(const[name,Constructor]of Object.entries(typed))globalThis[name]=new Proxy(Constructor,{construct(target,args){typedCalls++;return Reflect.construct(target,args);}});
try{for(let i=0;i<201;i++){api.setPlayerBlink(first.head,i/200);api.fitPlayerBlink(first.head,1+(i%3)*.01,1+(i%5)*.002);}}finally{for(const[name,Constructor]of Object.entries(typed))globalThis[name]=Constructor;}
const updateAllocations=constructorCalls-ctorBefore;equal(updateAllocations,0,'Animation/fitting creates no Three objects');equal(typedCalls,0,'Animation/fitting creates no typed buffers');
equal([rig.cover,rig.lash].flatMap(part=>[part.mesh.geometry,part.position.array,part.normal.array,part.uv.array,part.mesh.geometry.index.array]),objects,'All GPU buffer identities remain stable');
api.fitPlayerBlink(first.head,1,1);api.setPlayerBlink(first.head,.42);const expected=snapshot(rig);
for(const[height,hand,sx,sz]of[[155,-1,.9838,.9928],[215,1,1.0234,1.018],[185,-1,1,1]]){
 first.head.scale.set(hand*height/185,height/185,height/185);api.fitPlayerBlink(first.head,sx,sz);api.setPlayerBlink(first.head,.9);api.setPlayerBlink(first.head,.42);api.fitPlayerBlink(first.head,1,1);equal(snapshot(rig),expected,'Body/height/mirror round trips never accumulate deformation');
}
const version=rig.cover.position.version;api.setPlayerBlink(first.head,.42);equal(rig.cover.position.version,version,'Repeated amount is an upload-free no-op');
api.setPlayerBlink(second.head,.42);equal(snapshot(second.rig),expected,'Cold sample matches the previously animated model');
const copiedInput=eyes.clone(),third=make(copiedInput);copiedInput.attributes.position.array.fill(999);copiedInput.dispose();api.setPlayerBlink(third.head,.42);equal(snapshot(third.rig),expected,'Input geometry can be changed/disposed after registration');
rig.cover.position.array.fill(123);api.setPlayerBlink(first.head,.5);api.setPlayerBlink(first.head,.42);equal(snapshot(rig),expected,'Owned live vertices recover from neutral data');equal(snapshot(second.rig),expected,'Mutation never affects another head');

// Public create/dress/pose integration and model budgets are independently
// covered by player-blink.mjs. This file checks the exact generated surfaces.
let disposed=0;first.head.traverse(object=>{if(object.isMesh){object.geometry.addEventListener('dispose',()=>disposed++);object.geometry.dispose();}});equal(disposed,2,'Scene traversal owns exactly two dynamic geometry disposals');api.setPlayerBlink(second.head,1);api.setPlayerBlink(second.head,.42);equal(snapshot(second.rig),expected,'Other heads survive disposal');
for(const state of[second,third])state.head.traverse(object=>{if(object.isMesh)object.geometry.dispose();});
eyes.dispose();skin.dispose();features.dispose();
console.log('PASS blink geometry: '+assertions+' assertions; exact eye-triangle coverage, outward normals, fixed independent buffers, body/mirror/cold sampling');
console.log(JSON.stringify({checkedTriangles,checkedPoints,minCoverGapMm:minCoverGap*1000,maxCoverGapMm:maxCoverGap*1000,minLashGapMm:minLashGap*1000,maxLashGapMm:maxLashGap*1000,maxOpenCenterErrorMm:maxOpenCenterError*1000,maxOpenWidthErrorMm:maxOpenWidthError*1000,maxOpenDepthChangeMm:maxOpenDepthChange*1000,capacityTrianglesPerPart:rig.cover.capacity/3,maxActiveTrianglesPerPart:maxUsed/3,threeAllocationsDuringUpdate:updateAllocations,typedAllocationsDuringUpdate:typedCalls}));
