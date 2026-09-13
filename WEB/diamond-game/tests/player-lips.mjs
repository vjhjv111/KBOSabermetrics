import fs from 'node:fs';import path from 'node:path';import {fileURLToPath} from 'node:url';import {createRequire} from 'node:module';import assert from 'node:assert/strict';
import {load} from './load-ts.mjs';
const web=fileURLToPath(new URL('..',import.meta.url)),require=createRequire(import.meta.url),T=require('three'),ts=require('typescript'),V=(...p)=>new T.Vector3(...p);
const headPath=path.join(web,'lib/player-head.ts'),headSource=fs.readFileSync(headPath,'utf8');
// Fixed original 48mm outline and 21 column colors; no source snapshot needed.
const originalFixture={"bounds":{"min":[-0.024000000208616257,-0.030075399205088615],"max":[0.024000000208616257,-0.02157028205692768]},"columns":[[-0.024000000208616257,1,0.8899999856948853,0.8700000047683716],[-0.02160000056028366,1,0.8809999823570251,0.8600000143051147],[-0.019200000911951065,1,0.871999979019165,0.8500000238418579],[-0.01679999940097332,1,0.8629999756813049,0.8399999737739563],[-0.014399999752640724,1,0.8539999723434448,0.8299999833106995],[-0.012000000104308128,1,0.8450000286102295,0.8199999928474426],[-0.009600000455975533,1,0.8360000252723694,0.8100000023841858],[-0.007199999876320362,1,0.8270000219345093,0.800000011920929],[-0.004800000227987766,1,0.8180000185966492,0.7900000214576721],[-0.002400000113993883,1,0.8090000152587891,0.7799999713897705],[0,1,0.800000011920929,0.7699999809265137],[0.002400000113993883,1,0.8090000152587891,0.7799999713897705],[0.004800000227987766,1,0.8180000185966492,0.7900000214576721],[0.007199999876320362,1,0.8270000219345093,0.800000011920929],[0.009600000455975533,1,0.8360000252723694,0.8100000023841858],[0.012000000104308128,1,0.8450000286102295,0.8199999928474426],[0.014399999752640724,1,0.8539999723434448,0.8299999833106995],[0.01679999940097332,1,0.8629999756813049,0.8399999737739563],[0.019200000911951065,1,0.871999979019165,0.8500000238418579],[0.02160000056028366,1,0.8809999823570251,0.8600000143051147],[0.024000000208616257,1,0.8899999856948853,0.8700000047683716]]};
function compile(source,file){const mod={exports:{}};new Function('require','module','exports',ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText)(id=>id.startsWith('.')?load(path.resolve(path.dirname(file),id+'.ts')):require(id),mod,mod.exports);return mod.exports;}
const original=compile(headSource+'\nexport {headGeometry,faceZ};',headPath),{createLipGeometries}=load(path.join(web,'lib/player-lips.ts')),patch=createLipGeometries(original.faceZ),head=original.headGeometry();
let assertions=0;const check=(value,message)=>{assertions++;assert(value,message);};
function faces(g){const p=g.attributes.position,index=g.index,out=[];for(let i=0;i<index.count;i+=3)out.push([V().fromBufferAttribute(p,index.getX(i)),V().fromBufferAttribute(p,index.getX(i+1)),V().fromBufferAttribute(p,index.getX(i+2))]);return out;}
const cross=(a,b,p)=>(b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x);
const area=poly=>Math.abs(poly.reduce((sum,p,i)=>sum+p.x*poly[(i+1)%poly.length].y-p.y*poly[(i+1)%poly.length].x,0))*.5;
function plane(face,p){const [a,b,c]=face,d=cross(a,b,c),u=((p.x-a.x)*(c.y-a.y)-(p.y-a.y)*(c.x-a.x))/d,w=((b.x-a.x)*(p.y-a.y)-(b.y-a.y)*(p.x-a.x))/d;return a.z+u*(b.z-a.z)+w*(c.z-a.z);}
function inside(face,p){return face.every((a,i)=>cross(a,face[(i+1)%3],p)>=-1e-13);}
function front(list,p){let z=-Infinity;for(const f of list){const determinant=cross(f[0],f[1],f[2]);if(determinant<=1e-16||!inside(f,p))continue;z=Math.max(z,plane(f,p));}return z;}
function clip(subject,boundary){let polygon=subject.map(v=>({x:v.x,y:v.y}));for(let side=0;side<3;side++){
 const a=boundary[side],b=boundary[(side+1)%3],result=[];for(let i=0;i<polygon.length;i++){
  const p=polygon[i],q=polygon[(i+1)%polygon.length],dp=cross(a,b,p),dq=cross(a,b,q),pin=dp>=-1e-14,qin=dq>=-1e-14;
  if(pin)result.push(p);if(pin!==qin){const t=dp/(dp-dq);result.push({x:p.x+(q.x-p.x)*t,y:p.y+(q.y-p.y)*t});}
 }polygon=result;if(polygon.length<3)return [];}
 return polygon;
}
const stats={};
for(const [name,g]of Object.entries(patch)){
 const p=g.attributes.position,n=g.attributes.normal;g.computeBoundingBox();let symmetryPosition=0,symmetryNormal=0,minNormalZ=Infinity,minArea=Infinity;
 for(const a of Object.values(g.attributes))check([...a.array].every(Number.isFinite),`${name}: finite attributes`);
 for(let i=0;i<p.count;i++){
  const point=V().fromBufferAttribute(p,i),normal=V().fromBufferAttribute(n,i);check(Math.abs(normal.length()-1)<1e-6,`${name}: unit vertex normal`);minNormalZ=Math.min(minNormalZ,normal.z);
  let match=-1,error=Infinity;for(let j=0;j<p.count;j++){const d=Math.hypot(p.getX(j)+point.x,p.getY(j)-point.y,p.getZ(j)-point.z);if(d<error){error=d;match=j;}}
  symmetryPosition=Math.max(symmetryPosition,error);symmetryNormal=Math.max(symmetryNormal,V(-normal.x,normal.y,normal.z).distanceTo(V().fromBufferAttribute(n,match)));
 }
 for(const face of faces(g)){const normal=V().subVectors(face[1],face[0]).cross(V().subVectors(face[2],face[0]));minArea=Math.min(minArea,normal.length()*.5);check(normal.length()>1e-12&&normal.z>0,`${name}: nondegenerate front-facing triangles`);}
 check(symmetryPosition<1e-8,`${name}: symmetric positions`);check(symmetryNormal<2e-5,`${name}: symmetric normals`);check(minNormalZ>0,`${name}: outward vertex normals`);
 stats[name]={vertices:p.count,triangles:g.index.count/3,bounds:{min:g.boundingBox.min.toArray(),max:g.boundingBox.max.toArray()},symmetryPosition,symmetryNormal,minNormalZ,minTriangleArea:minArea};
}
check(stats.lips.triangles+stats.mouth.triangles<=360,'Combined triangle budget never exceeds the existing 360');
check(stats.lips.triangles===304&&stats.mouth.triangles===40,'Reviewed geometry saves 16 triangles from the original 240+120');
for(const [i,axis]of ['x','y'].entries())for(const end of ['min','max'])check(Math.abs(patch.lips.boundingBox[end][axis]-originalFixture.bounds[end][i])<1e-8,'Original lip width and height');
const p=patch.lips.attributes.position,c=patch.lips.attributes.color;
for(let i=0;i<p.count;i++){const column=originalFixture.columns.find(column=>Math.abs(column[0]-p.getX(i))<1e-9);check(!!column,'Every column retains the original x sample');for(let ch=0;ch<3;ch++)check(c.getComponent(i,ch)===column[ch+1],'Original lip tint is unchanged');}
const profile=[];for(let i=0;i<p.count;i++)if(Math.abs(p.getX(i))<1e-9)profile.push({y:p.getY(i),reliefMm:(p.getZ(i)-original.faceZ(p.getX(i),p.getY(i)))*1000});profile.sort((a,b)=>a.y-b.y);
check(profile.length===9&&profile[1].reliefMm>profile[4].reliefMm+.8&&profile[7].reliefMm>profile[4].reliefMm+.8,'Two shallow lip ridges separated by a valley');
const headFaces=faces(head);let minFaceGap=Infinity;for(let i=0;i<p.count;i++){const point=V().fromBufferAttribute(p,i),z=front(headFaces,point);check(Number.isFinite(z),'Candidate stays inside the original face coverage');minFaceGap=Math.min(minFaceGap,point.z-z);}check(minFaceGap>0,'Lip surface stays in front of the unchanged rendered face');
const scaled=[];
for(const [body,height,sx,sz]of [['athletic',185,1,1],['lean',155,1+(.91-1)*.18,1+(.94-1)*.12],['power',215,1+(1.13-1)*.18,1+(1.15-1)*.12]]){
 const scale=height/185,lips=patch.lips.clone().scale(sx*scale,scale,sz*scale),mouth=patch.mouth.clone().scale(sx*scale,scale,sz*scale),lipFaces=faces(lips),mouthFaces=faces(mouth);
 let minGap=Infinity,maxGap=-Infinity,coverageError=0,intersections=0,sampledPoints=0;
 for(const ribbon of mouthFaces){
  // Triangular overlap vertices are the extrema of the piecewise-linear gap.
  // This catches crossings between mesh vertices as well as centroid samples.
  let covered=0;for(const support of lipFaces){const polygon=clip(ribbon,support);if(polygon.length<3||area(polygon)<1e-17)continue;covered+=area(polygon);intersections++;
   for(const point of polygon){const gap=plane(ribbon,point)-plane(support,point);minGap=Math.min(minGap,gap);maxGap=Math.max(maxGap,gap);}
  }
  coverageError=Math.max(coverageError,Math.abs(covered-area(ribbon)));check(Math.abs(covered-area(ribbon))<1e-11,'Ribbon triangle is completely supported by lip triangles');
  const [a,b,c]=ribbon,probes=[...ribbon,a.clone().add(b).multiplyScalar(.5),b.clone().add(c).multiplyScalar(.5),c.clone().add(a).multiplyScalar(.5),a.clone().add(b).add(c).multiplyScalar(1/3)];
  for(const point of probes){const z=front(lipFaces,point);check(Number.isFinite(z),'Vertex/edge/face midpoint lies on the lip patch');const gap=point.z-z;check(gap>0&&gap<.0002,'Ribbon neither intersects nor floats far from the real supporting triangle');sampledPoints++;}
 }
 check(minGap>0&&maxGap<.0002,'Exact overlap extrema preserve a narrow positive gap');scaled.push({body,height,minGapMm:minGap*1000,maxGapMm:maxGap*1000,coverageError,intersections,sampledPoints});lips.dispose();mouth.dispose();
}
// Exercise the public factory call site, including existing mesh order,
// names, brown feature material reuse and independent lip-material ownership.
for(const helmet of [false,true]){
 const root=new T.Group(),skin=new T.MeshPhysicalMaterial({color:'#c89675'}),cap=new T.MeshPhysicalMaterial();
 original.headDetails(root,skin,cap,null,helmet);
 const index=root.children.findIndex(object=>object.name==='Natural lip planes');
 check(index>=1,'Public head factory exposes the named lip planes');
 const lips=root.children[index],mouth=root.children[index+1],nostrils=root.children[index-1];
 check(mouth.name==='','Existing mouth mesh name and insertion order are preserved');
 check(mouth.material===nostrils.material,'Mouth reuses the existing brown facial-feature material');
 check(lips.material!==skin&&lips.material.color.equals(skin.color)&&lips.material.vertexColors&&lips.material.roughness===.7,'Lip tint material remains independently owned');
 check(lips.castShadow&&lips.receiveShadow&&mouth.castShadow&&mouth.receiveShadow,'Existing rendering flags are retained');
 for(const [mesh,geometry]of [[lips,patch.lips],[mouth,patch.mouth]]){
  check(mesh.geometry!==geometry,'Each head owns its geometry');
  check(JSON.stringify(Array.from(mesh.geometry.index.array))===JSON.stringify(Array.from(geometry.index.array)),'Public head uses the reviewed triangle topology');
  for(const name of Object.keys(geometry.attributes))check(JSON.stringify(Array.from(mesh.geometry.attributes[name].array))===JSON.stringify(Array.from(geometry.attributes[name].array)),'Public head uses reviewed '+name+' attributes');
 }
 const resources=new Set([skin,cap]);root.traverse(object=>{if(object.geometry)resources.add(object.geometry);for(const material of Array.isArray(object.material)?object.material:object.material?[object.material]:[]){resources.add(material);for(const value of Object.values(material))if(value?.isTexture)resources.add(value);}});for(const resource of resources)resource.dispose();
}
console.log('PASS player lips: '+assertions+' assertions; 304 lip + 40 mouth triangles; unchanged outline/tint, symmetric outward surfaces, exact supported ribbon and public head integration');
console.log(JSON.stringify({minRenderedFaceGapMm:minFaceGap*1000,scaled}));
for(const geometry of [head,patch.lips,patch.mouth])geometry.dispose();
