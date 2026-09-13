import * as THREE from "three";

export const v=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
export type Profile=[y:number,width:number,depth:number,forward?:number,side?:number];
export type Relief=(height:number,angle:number)=>number;
export type SurfaceMaterial=THREE.MeshStandardMaterial|THREE.MeshPhysicalMaterial;
export type NormalSeamPair=readonly [number,number];

/** Restore only explicitly declared smooth UV seams after recalculating normals.
 * Positions, topology, UVs and skin bindings remain untouched. Coincident vertices
 * elsewhere may be intentional hard edges and must never be welded by proximity.
 */
export function restoreSeamNormals(geometry:THREE.BufferGeometry){
 const pairs=geometry.userData.normalSeamPairs as NormalSeamPair[]|undefined;
 const position=geometry.getAttribute('position'),normal=geometry.getAttribute('normal');
 if(!Array.isArray(pairs)||!position||!normal||position.count!==normal.count)return geometry;
 const bindings=['skinIndex','skinWeight'].map(name=>geometry.getAttribute(name)).filter(Boolean),average=new THREE.Vector3();
 let restored=false;
 for(const pair of pairs){
  if(!Array.isArray(pair)||pair.length!==2)continue;
  const [first,last]=pair;
  if(!Number.isInteger(first)||!Number.isInteger(last)||first<0||last<0||first===last||first>=position.count||last>=position.count)continue;
  const dx=position.getX(first)-position.getX(last),dy=position.getY(first)-position.getY(last),dz=position.getZ(first)-position.getZ(last);
  if(!Number.isFinite(dx+dy+dz)||dx*dx+dy*dy+dz*dz>1e-12)continue;
  // A later edit may intentionally split a ring or give it different bone
  // influences. Stale metadata must not join those independently moving faces.
  if(bindings.some(binding=>binding.count!==position.count||Array.from({length:binding.itemSize},(_,component)=>Math.abs(binding.getComponent(first,component)-binding.getComponent(last,component))).some(delta=>!Number.isFinite(delta)||delta>1e-7)))continue;
  average.set(normal.getX(first)+normal.getX(last),normal.getY(first)+normal.getY(last),normal.getZ(first)+normal.getZ(last));
  if(!Number.isFinite(average.lengthSq())||average.lengthSq()<1e-12)continue;
  average.normalize();normal.setXYZ(first,average.x,average.y,average.z);normal.setXYZ(last,average.x,average.y,average.z);restored=true;
 }
 if(restored)normal.needsUpdate=true;
 return geometry;
}

export function object(parent:THREE.Object3D,x=0,y=0,z=0){
 const result=new THREE.Group();result.position.set(x,y,z);parent.add(result);return result;
}
export function add(geometry:THREE.BufferGeometry,material:THREE.Material,parent:THREE.Object3D,x=0,y=0,z=0){
 const result=new THREE.Mesh(geometry,material);result.position.set(x,y,z);
 result.castShadow=true;result.receiveShadow=true;parent.add(result);return result;
}
export function material(color:string,roughness:number,bumpMap:THREE.Texture|null=null,bumpScale=0){
 return new THREE.MeshStandardMaterial({color,roughness,bumpMap,bumpScale});
}
export function interpolate(a:number,b:number,c:number,d:number,t:number){
 return .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
}
export function profileAt(profile:Profile[],height:number){
 let index=1;while(index<profile.length-1&&profile[index][0]<height)index++;
 const a=profile[Math.max(0,index-2)],b=profile[index-1],c=profile[index],d=profile[Math.min(profile.length-1,index+1)];
 const t=THREE.MathUtils.clamp((height-b[0])/(c[0]-b[0]),0,1);
 return [height,...[1,2,3,4].map(k=>interpolate(a[k]??0,b[k]??0,c[k]??0,d[k]??0,t))] as Profile;
}

/** Elliptical, individually shaped cross sections avoid a tube-shaped body. */
export function loft(profile:Profile[],around=24,steps=3,relief?:Relief){
 const positions:number[]=[],uv:number[]=[],indices:number[]=[];
 const rows=(profile.length-1)*steps,min=profile[0][0],max=profile.at(-1)![0];
 for(let row=0;row<=rows;row++){
  const segment=Math.min(profile.length-2,Math.floor(row/steps));
  const height=THREE.MathUtils.lerp(profile[segment][0],profile[segment+1][0],(row-segment*steps)/steps);
  const [,width,depth,forward=0,side=0]=profileAt(profile,height);
  for(let point=0;point<=around;point++){
   const angle=point/around*Math.PI*2,detail=(relief?.(height,angle)??0)*Math.min(1,width/.035,depth/.035);
   positions.push(side+Math.sin(angle)*Math.max(.0001,width+detail),height,forward+Math.cos(angle)*Math.max(.0001,depth+detail));
   uv.push(point/around,(height-min)/(max-min));
   if(row<rows&&point<around){const a=row*(around+1)+point,b=a+1,c=b+around+1,d=a+around+1;indices.push(a,b,d,b,c,d);}
  }
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));
 geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();
 // Keep exact pairs through clones, transforms and joined geometry. Recomputing
 // normals during body customization must not recreate a visible front meridian.
 geometry.userData.normalSeamPairs=Array.from({length:rows+1},(_,row)=>[row*(around+1),row*(around+1)+around]);
 return restoreSeamNormals(geometry);
}

export function joined(geometries:THREE.BufferGeometry[]){
 const positions:number[]=[],normals:number[]=[],uvs:number[]=[],indices:number[]=[],normalSeamPairs:NormalSeamPair[]=[];
 for(const geometry of geometries){
  const position=geometry.getAttribute("position"),normal=geometry.getAttribute("normal"),uv=geometry.getAttribute("uv"),offset=positions.length/3;
  for(let i=0;i<position.count;i++){
   positions.push(position.getX(i),position.getY(i),position.getZ(i));
   normals.push(normal?.getX(i)??0,normal?.getY(i)??1,normal?.getZ(i)??0);uvs.push(uv?.getX(i)??0,uv?.getY(i)??0);
  }
  if(geometry.index)for(let i=0;i<geometry.index.count;i++)indices.push(offset+geometry.index.getX(i));
  else for(let i=0;i<position.count;i++)indices.push(offset+i);
  const pairs=geometry.userData.normalSeamPairs as NormalSeamPair[]|undefined;
  if(Array.isArray(pairs))for(const pair of pairs){
   if(Array.isArray(pair)&&pair.length===2&&pair.every(index=>Number.isInteger(index)&&index>=0&&index<position.count))normalSeamPairs.push([offset+pair[0],offset+pair[1]]);
  }
  geometry.dispose();
 }
 const result=new THREE.BufferGeometry();result.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));
 result.setAttribute("normal",new THREE.Float32BufferAttribute(normals,3));result.setAttribute("uv",new THREE.Float32BufferAttribute(uvs,2));result.setIndex(indices);
 if(normalSeamPairs.length)result.userData.normalSeamPairs=normalSeamPairs;
 return result;
}
export function oval(width:number,height:number,depth:number,x=0,y=0,z=0,segments=12){
 const geometry=new THREE.SphereGeometry(1,segments,8).scale(width,height,depth).translate(x,y,z),around=Math.max(3,Math.floor(segments));
 // Only duplicate longitude endpoints at the seven interior latitudes. The
 // pole fans have distinct UV offsets and are not a collection of ring seams.
 geometry.userData.normalSeamPairs=Array.from({length:7},(_,row)=>[(row+1)*(around+1),(row+1)*(around+1)+around]);
 restoreSeamNormals(geometry);return geometry;
}
export function tube(points:number[][],radius=.002,segments=12){
 const geometry=new THREE.TubeGeometry(new THREE.CatmullRomCurve3(points.map(point=>v(...point))),segments,radius,5,false),ringWidth=6;
 // Radial UV closure only: never join the independent ends of the open path.
 geometry.userData.normalSeamPairs=Array.from({length:geometry.getAttribute('position').count/ringWidth},(_,ring)=>[ring*ringWidth,ring*ringWidth+ringWidth-1]);
 restoreSeamNormals(geometry);return geometry;
}
export function seams(parent:THREE.Object3D,paths:number[][][],mat:THREE.Material,radius=.002){
 return add(joined(paths.map(path=>tube(path,radius))),mat,parent);
}
export function gauss(value:number,centre:number,width:number){return Math.exp(-(((value-centre)/width)**2));}
