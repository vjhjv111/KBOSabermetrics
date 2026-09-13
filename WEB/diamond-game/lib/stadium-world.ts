import * as THREE from "three";

export const FIELD_DIMENSIONS={basePath:27.432,moundDistance:18.44,moundHeight:.254,moundRadius:2.7432,centreField:122,foulLine:99} as const;
export type StadiumScore={home?:string;away?:string;homeScore?:number;awayScore?:number;inning?:number;half?:string};
export type StadiumWorld={root:THREE.Group;field:{home:THREE.Vector3;mound:THREE.Vector3;first:THREE.Vector3;second:THREE.Vector3;third:THREE.Vector3};update:(seconds:number)=>void;setScoreboard:(score:StadiumScore)=>void;dispose:()=>void};
const TAU=Math.PI*2;
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
function seeded(seed:number){return()=>{seed=(Math.imul(seed,1664525)+1013904223)|0;return (seed>>>0)/4294967296;};}
function mat(color:string,roughness=.85){return new THREE.MeshStandardMaterial({color,roughness});}
function groundUV(geometry:THREE.BufferGeometry,metres:number){
 const p=geometry.getAttribute("position"),uv=new Float32Array(p.count*2);
 for(let i=0;i<p.count;i++){uv[i*2]=p.getX(i)/metres;uv[i*2+1]=p.getZ(i)/metres;}
 geometry.setAttribute("uv",new THREE.BufferAttribute(uv,2));return geometry;
}
/** Small seamless material maps use metres for UVs, so their scale survives camera changes. */
function turfTextures(compact:boolean){
 const size=compact?128:256,random=seeded(9031),height=new Float32Array(size*size),diffuse=new Uint8Array(size*size*4),normal=new Uint8Array(size*size*4),rough=new Uint8Array(size*size*4);
 for(let y=0;y<size;y++)for(let x=0;x<size;x++){
  const i=y*size+x,n=random(),blade=Math.pow(Math.max(0,Math.sin(x*.75+Math.sin(y*.085)*1.6)),7);
  height[i]=n*.35+blade*.4+Math.sin(y*.3+x*.12)*.08;
  const light=.82+n*.3+blade*.14;diffuse.set([Math.round(63*light),Math.round(103*light),Math.round(32*light),255],i*4);
  const r=Math.round(211+n*39);rough.set([r,r,r,255],i*4);
 }
 for(let y=0;y<size;y++)for(let x=0;x<size;x++){
  const at=(dx:number,dy:number)=>height[((y+dy+size)%size)*size+(x+dx+size)%size];
  const n=V((at(-1,0)-at(1,0))*.55,(at(0,-1)-at(0,1))*.55,1).normalize();
  normal.set([Math.round(n.x*127+128),Math.round(n.y*127+128),Math.round(n.z*127+128),255],(y*size+x)*4);
 }
 const texture=(data:Uint8Array,color=false)=>{const t=new THREE.DataTexture(data,size,size);t.wrapS=t.wrapT=THREE.RepeatWrapping;t.colorSpace=color?THREE.SRGBColorSpace:THREE.NoColorSpace;t.magFilter=THREE.LinearFilter;t.minFilter=THREE.LinearMipmapLinearFilter;t.generateMipmaps=true;t.anisotropy=compact?4:8;t.needsUpdate=true;return t;};
 return {map:texture(diffuse,true),normalMap:texture(normal),roughnessMap:texture(rough)};
}
function surfaceShape(points:[number,number][],metres=1,holes:[number,number][][]=[]){
 const shape=new THREE.Shape();points.forEach(([x,z],i)=>i?shape.lineTo(x,-z):shape.moveTo(x,-z));shape.closePath();
 for(const points of holes){const hole=new THREE.Path();points.forEach(([x,z],i)=>i?hole.lineTo(x,-z):hole.moveTo(x,-z));hole.closePath();shape.holes.push(hole);}
 return groundUV(new THREE.ShapeGeometry(shape).rotateX(-Math.PI/2),metres);
}
function radialPoint(angle:number,offset=0){
 const a=Math.abs(Math.atan2(Math.sin(angle),Math.cos(angle)));
 const radius=a<=Math.PI/4?122-23*Math.pow(a/(Math.PI/4),1.6):99*Math.exp(-.65*(a-Math.PI/4));
 return V(Math.sin(angle)*(radius+offset),0,-Math.cos(angle)*(radius+offset));
}
function seatingAngles(count:number){
 const samples=2048,distances=[0];let previous=radialPoint(-Math.PI,15);
 for(let i=1;i<=samples;i++){const p=radialPoint(-Math.PI+i/samples*TAU,15);distances.push(distances[i-1]+p.distanceTo(previous));previous=p;}
 const result:number[]=[];let sample=1;
 for(let i=0;i<count;i++){const distance=(i+.5)/count*distances[samples];while(distances[sample]<distance)sample++;const fraction=(distance-distances[sample-1])/(distances[sample]-distances[sample-1]);result.push(-Math.PI+(sample-1+fraction)/samples*TAU);}
 return result;
}

/** Static architecture is batched by material: hundreds of panels become a few draws. */
function batchArchitecture(root:THREE.Group,owned:Set<THREE.BufferGeometry>){
 root.updateMatrixWorld(true);const batches=new Map<THREE.Material,THREE.Mesh[]>();
 root.traverse(o=>{if(!(o instanceof THREE.Mesh)||o instanceof THREE.InstancedMesh||Array.isArray(o.material))return;const list=batches.get(o.material)??[];list.push(o);batches.set(o.material,list);});
 for(const [material,meshes] of batches){if(meshes.length<2)continue;const positions:number[]=[],normals:number[]=[],uvs:number[]=[],indices:number[]=[],p=new THREE.Vector3(),n=new THREE.Vector3();
  for(const mesh of meshes){const geometry=mesh.geometry,position=geometry.getAttribute("position"),normal=geometry.getAttribute("normal"),uv=geometry.getAttribute("uv"),offset=positions.length/3,normalMatrix=new THREE.Matrix3().getNormalMatrix(mesh.matrixWorld);
   for(let i=0;i<position.count;i++){p.fromBufferAttribute(position,i).applyMatrix4(mesh.matrixWorld);n.set(normal?.getX(i)??0,normal?.getY(i)??1,normal?.getZ(i)??0).applyMatrix3(normalMatrix).normalize();positions.push(p.x,p.y,p.z);normals.push(n.x,n.y,n.z);uvs.push(uv?.getX(i)??0,uv?.getY(i)??0);}
   if(geometry.index)for(let i=0;i<geometry.index.count;i++)indices.push(offset+geometry.index.getX(i));else for(let i=0;i<position.count;i++)indices.push(offset+i);
   mesh.removeFromParent();
  }
  const merged=new THREE.BufferGeometry();merged.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));merged.setAttribute("normal",new THREE.Float32BufferAttribute(normals,3));merged.setAttribute("uv",new THREE.Float32BufferAttribute(uvs,2));merged.setIndex(indices);merged.computeBoundingSphere();owned.add(merged);
  const mesh=new THREE.Mesh(merged,material);mesh.name="Batched stadium architecture";mesh.receiveShadow=true;root.add(mesh);
  for(const old of meshes){owned.delete(old.geometry);old.geometry.dispose();}
 }
}

/** A complete world in metres. No camera-facing stadium image is used. */
export function createStadiumWorld(scene:THREE.Scene,{compact=false,assetBase="/diamond/assets/stadium/"}:{compact?:boolean;assetBase?:string}={}):StadiumWorld{
 const root=new THREE.Group();root.name="Regulation baseball stadium";scene.add(root);
 const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>(),textures=new Set<THREE.Texture>();
 const ownMaterial=<T extends THREE.Material>(m:T)=>{materials.add(m);return m;};
 const add=(g:THREE.BufferGeometry,m:THREE.Material,x=0,y=0,z=0,parent:THREE.Object3D=root)=>{geometries.add(g);materials.add(m);const mesh=new THREE.Mesh(g,m);mesh.position.set(x,y,z);mesh.receiveShadow=true;parent.add(mesh);return mesh;};
 const box=(w:number,h:number,d:number,m:THREE.Material,x:number,y:number,z:number)=>add(new THREE.BoxGeometry(w,h,d),m,x,y,z);
 const segment=(a:THREE.Vector3,b:THREE.Vector3,width:number,height:number,m:THREE.Material)=>{const delta=b.clone().sub(a),mid=a.clone().add(b).multiplyScalar(.5),mesh=box(width,height,delta.length(),m,mid.x,mid.y,mid.z);mesh.quaternion.setFromUnitVectors(V(0,0,1),delta.normalize());return mesh;};
 const floor=(points:[number,number][],m:THREE.Material,y=.008,scale=1,holes:[number,number][][]=[])=>add(surfaceShape(points,scale,holes),m,0,y);
 const grassMaps=turfTextures(compact);Object.values(grassMaps).forEach(t=>textures.add(t));
 const grass=ownMaterial(new THREE.MeshStandardMaterial({...grassMaps,roughness:1,normalScale:new THREE.Vector2(.65,.65)}));
 grass.name="Stadium turf";
 grass.onBeforeCompile=shader=>{
  shader.vertexShader="varying vec3 turfWorld;\n"+shader.vertexShader.replace("#include <begin_vertex>","#include <begin_vertex>\nturfWorld=(modelMatrix*vec4(position,1.0)).xyz;");
  shader.fragmentShader=`varying vec3 turfWorld;
float turfHash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
float turfNoise(vec2 p){vec2 cell=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);return mix(mix(turfHash(cell),turfHash(cell+vec2(1.0,0.0)),f.x),mix(turfHash(cell+vec2(0.0,1.0)),turfHash(cell+vec2(1.0,1.0)),f.x),f.y);}
`+shader.fragmentShader.replace("#include <color_fragment>",`#include <color_fragment>
float stripe=smoothstep(-0.28,0.28,sin((turfWorld.z+turfWorld.x*0.22)*0.52));
float turfPatch=turfNoise(turfWorld.xz*0.7)-0.5;
float fine=(turfNoise(turfWorld.xz*8.0)-0.5)/(1.0+length(fwidth(turfWorld.xz))*8.0);
diffuseColor.rgb *= mix(0.95,1.045,stripe)+turfPatch*0.23+fine*0.28;`);
 };grass.customProgramCacheKey=()=>"stadium-mown-turf-v2";
 const clay=ownMaterial(new THREE.MeshPhysicalMaterial({color:"#ffffff",roughness:1,specularIntensity:.22,normalScale:new THREE.Vector2(.24,.24)}));
 clay.name="Stadium clay";
 clay.onBeforeCompile=shader=>{
  shader.fragmentShader=shader.fragmentShader.replace('#include <color_fragment>','#include <color_fragment>\ndiffuseColor.rgb *= vec3(1.6,1.04,0.8);').replace('#include <roughnessmap_fragment>','#include <roughnessmap_fragment>\nroughnessFactor=0.88+roughnessFactor*0.12;');
 };clay.customProgramCacheKey=()=>"dry-infield-clay-v2";
 // Poly Haven Brown Mud Dry, CC0. Bundled locally; no runtime third-party request.
 if(typeof document!=="undefined"){
  const loader=new THREE.TextureLoader();
  for(const [slot,url] of [["map","clay-diff.jpg"],["normalMap","clay-normal.jpg"],["roughnessMap","clay-rough.jpg"]] as const){
   const t=loader.load(`${assetBase.replace(/\/?$/,"/")}${url}`);t.wrapS=t.wrapT=THREE.RepeatWrapping;t.colorSpace=slot==="map"?THREE.SRGBColorSpace:THREE.NoColorSpace;t.anisotropy=compact?4:8;clay[slot]=t;textures.add(t);
  }
 }
 const warning=ownMaterial(mat("#9a6751")),chalk=ownMaterial(mat("#eee9dc",.96)),concrete=ownMaterial(mat("#66717b")),dark=ownMaterial(mat("#182938")),steel=ownMaterial(mat("#87939c",.47)),padding=ownMaterial(mat("#164333",.77)),gold=ownMaterial(mat("#e8c251",.53));
 const field={home:V(),mound:V(0,.254,-18.44),first:V(27.432/Math.SQRT2,0,-27.432/Math.SQRT2),second:V(0,0,-27.432*Math.SQRT2),third:V(-27.432/Math.SQRT2,0,-27.432/Math.SQRT2)};
 const turf=add(groundUV(new THREE.PlaneGeometry(330,330).rotateX(-Math.PI/2),1.5),grass,0,-.025,-45);turf.name="Mown natural turf";
 const outline:[number,number][]=[[-4,4],[-29,-18.44]];
 for(let i=0;i<=64;i++){const a=-Math.PI/2+i/64*Math.PI;outline.push([Math.sin(a)*29,-18.44-Math.cos(a)*29]);}outline.push([4,4]);
 // Cut the grass diamond out of the dirt mesh. Coplanar layered sheets fight
 // for depth precision in a ground-level camera, hiding grass and mound detail.
 floor(outline,clay,.006,1.3,[[[0,-4.3],[-15.9,-19.4],[0,-34.5],[15.9,-19.4]]]);
 add(groundUV(new THREE.CircleGeometry(5.6,64).rotateX(-Math.PI/2),1.3),clay,0,.016,0);
 const moundGeo=new THREE.CircleGeometry(FIELD_DIMENSIONS.moundRadius,64,0,TAU);moundGeo.rotateX(-Math.PI/2);
 // A real 10-inch mound, flattened near the rubber and gently sloping toward home.
 const moundPositions=moundGeo.getAttribute("position");for(let i=0;i<moundPositions.count;i++){const r=Math.hypot(moundPositions.getX(i),moundPositions.getZ(i));moundPositions.setY(i,r<.1?.254:.012);}
 moundGeo.computeVertexNormals();add(groundUV(moundGeo,1.3),clay,0,0,-18.44).name="Pitching mound";
 box(.6096,.025,.1524,chalk,0,.26,-18.44);
 // 18-inch square bases; coordinates identify each bag's centre for running paths.
 for(const [name,base] of [["First",field.first],["Second",field.second],["Third",field.third]] as const){const bag=box(.4572,.075,.4572,chalk,base.x,.06,base.z);bag.rotation.y=Math.PI/4;bag.name=`${name} base`;}
 floor([[-.216,-.216],[.216,-.216],[.216,0],[0,.216],[-.216,0]],chalk,.036);
 for(const side of [-1,1]){
  const x=side*.94;for(const z of [-.91,.91])segment(V(x-.61,.031,z),V(x+.61,.031,z),.047,.008,chalk);
  for(const edge of [-.61,.61])segment(V(x+edge,.031,-.91),V(x+edge,.031,.91),.047,.008,chalk);
  const end=radialPoint(side*Math.PI/4);segment(V(0,.025,0),end.clone().setY(.025),.075,.009,chalk);
  const foul=add(new THREE.CylinderGeometry(.11,.13,20,8),gold,end.x,10,end.z);foul.name="Foul pole";
  for(let i=0;i<7;i++)box(.65,.045,.04,gold,end.x-side*.33,13+i*.85,end.z);
 }
 // Warning track and padded fence follow the same perimeter as seating.
 const border:number[]=[],indices:number[]=[],uv:number[]=[];
 const wallSegments=112;
 for(let i=0;i<=wallSegments;i++){
  const a=-Math.PI+i/wallSegments*TAU,p=radialPoint(a),q=radialPoint(a,-5);
  border.push(p.x,.005,p.z,q.x,.005,q.z);uv.push(i,0,i,1);
  if(i<wallSegments){const n=i*2;indices.push(n,n+1,n+2,n+1,n+3,n+2);}
  if(i<wallSegments){const b=-Math.PI+(i+1)/wallSegments*TAU,next=radialPoint(b),fair=Math.abs(a)<=Math.PI/4,height=fair?3.25:2.55;
   segment(p.clone().setY(height/2),next.clone().setY(height/2),.26,height,padding);
   segment(p.clone().setY(height),next.clone().setY(height),.22,.13,fair?gold:dark);
  }
 }
 const trackGeo=new THREE.BufferGeometry();trackGeo.setAttribute("position",new THREE.Float32BufferAttribute(border,3));trackGeo.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));trackGeo.setIndex(indices);trackGeo.computeVertexNormals();add(trackGeo,warning);
 // Two dugouts: concrete shells, open field-facing fronts, benches and handrails.
 for(const side of [-1,1]){
  const centre=V(side*21.5,0,-5.5),group=new THREE.Group();group.position.copy(centre);group.rotation.y=side*.58;root.add(group);
  const local=(w:number,h:number,d:number,m:THREE.Material,x:number,y:number,z:number)=>add(new THREE.BoxGeometry(w,h,d),m,x,y,z,group);
  local(15,.22,3.7,concrete,0,2.45,0);local(15,2.5,.23,dark,0,1.2,1.6);local(.25,2.5,3.4,concrete,-7.4,1.2,0);local(.25,2.5,3.4,concrete,7.4,1.2,0);
  local(13,.18,.55,concrete,0,.5,.84);local(13,.65,.16,dark,0,.77,1.05);local(14,.08,.09,steel,0,1,-1.6);
  for(let i=-6;i<=6;i+=3)local(.065,1.05,.07,steel,i,.5,-1.6);
 }
 // Bullpen dirt lanes sit beyond the foul-line ends, inside the seating envelope.
 for(const side of [-1,1]){const p=radialPoint(side*1.04,-4);floor([[p.x-3,p.z-11],[p.x+3,p.z-11],[p.x+3,p.z+11],[p.x-3,p.z+11]],clay,.018,1.3);box(.61,.035,.15,chalk,p.x,.04,p.z-8);}
 // Entire crowd and seat rows are instanced, keeping wide-angle views affordable.
 const rowCount=compact?20:24,rowDepth=compact?1.03:.85,rowRise=compact?.57:.47,sections=compact?624:832,count=rowCount*sections,angles=seatingAngles(sections*2);
 const seatGeo=new THREE.BoxGeometry(.46,.34,.46),seatMat=ownMaterial(mat("#28506c",.82));geometries.add(seatGeo);
 const seats=new THREE.InstancedMesh(seatGeo,seatMat,count);seats.name="Stadium seating";root.add(seats);
 const torsoGeo=new THREE.CylinderGeometry(.18,.16,.5,5,1),headGeo=new THREE.OctahedronGeometry(.112,0);geometries.add(torsoGeo);geometries.add(headGeo);
 const crowdMat=ownMaterial(mat("#ffffff",.9)),crowd=new THREE.InstancedMesh(torsoGeo,crowdMat,count),heads=new THREE.InstancedMesh(headGeo,crowdMat,count);crowd.name="Instanced crowd";heads.name="Crowd heads";root.add(crowd,heads);
 const transform=new THREE.Object3D(),random=seeded(53226),palette=["#ac3b36","#d2d4cd","#1c344e","#536d8b","#c6a76d","#2b4848"],skinPalette=["#bd9679","#ad7e5c","#d1ac8e","#916849"];
 let audience=0,seatIndex=0;
 for(let row=0;row<rowCount;row++)for(let section=0;section<sections;section++){
  const angle=angles[section*2+row%2],aisle=section%26<2,p=radialPoint(angle,5+row*rowDepth+(row>11?2.1:0)),y=2.1+row*rowRise+(row>11?1.1:0);
  transform.position.set(p.x,y,p.z);transform.rotation.set(0,Math.PI-angle,0);transform.scale.set(aisle?.02:1,1,1);transform.updateMatrix();seats.setMatrixAt(seatIndex++,transform.matrix);seats.setColorAt(seatIndex-1,new THREE.Color(row>11?"#244458":"#34536c"));
  if(aisle||random()<.14)continue;
  transform.position.y=y+.39+random()*.07;transform.scale.set(1,.88+random()*.22,1);transform.updateMatrix();crowd.setMatrixAt(audience,transform.matrix);crowd.setColorAt(audience,new THREE.Color(palette[Math.floor(random()*palette.length)]));
  transform.position.y+=.37;transform.scale.setScalar(1);transform.updateMatrix();heads.setMatrixAt(audience,transform.matrix);heads.setColorAt(audience,new THREE.Color(skinPalette[Math.floor(random()*skinPalette.length)]));audience++;
 }
 crowd.count=heads.count=audience;
 // Continuous stepped terraces support every seat. Narrow concourse rails alone
 // leave the spectators floating against the sky when viewed from the mound.
 const terraces:number[]=[],terraceIndices:number[]=[];
 const quad=(a:THREE.Vector3,b:THREE.Vector3,c:THREE.Vector3,d:THREE.Vector3)=>{const n=terraces.length/3;for(const p of[a,b,c,d])terraces.push(p.x,p.y,p.z);terraceIndices.push(n,n+1,n+2,n,n+2,n+3);};
 for(let row=0;row<rowCount;row++){
  const offset=5+row*rowDepth+(row>11?2.1:0),y=1.9+row*rowRise+(row>11?1.1:0),inner=row===12?offset-2.1-rowDepth*.5:offset-rowDepth*.5,outer=offset+rowDepth*.5;
  for(let i=0;i<112;i++){
   const a=-Math.PI+i/112*TAU,b=-Math.PI+(i+1)/112*TAU,p=radialPoint(a,inner),q=radialPoint(b,inner),r=radialPoint(b,outer),t=radialPoint(a,outer);
   quad(p.clone().setY(y),q.clone().setY(y),r.clone().setY(y),t.clone().setY(y));
   quad(p.clone().setY(y-rowRise-(row===12?1.1:0)),q.clone().setY(y-rowRise-(row===12?1.1:0)),q.clone().setY(y),p.clone().setY(y));
  }
 }
 const terraceGeometry=new THREE.BufferGeometry();terraceGeometry.setAttribute('position',new THREE.Float32BufferAttribute(terraces,3));terraceGeometry.setIndex(terraceIndices);terraceGeometry.computeVertexNormals();groundUV(terraceGeometry,3);add(terraceGeometry,concrete).name='Continuous concrete seating terraces';
 // Tier decks, concourses, aisle rails and a light roof are full 3D architecture.
 const architectureSegments=56;
 for(let i=0;i<architectureSegments;i++){
  const a=-Math.PI+i/architectureSegments*TAU,b=-Math.PI+(i+1)/architectureSegments*TAU;
  for(const [offset,y,h] of [[4.5,1.05,2.1],[15.4,7.1,.55],[28,14.3,.55]] as const){
   segment(radialPoint(a,offset).setY(y),radialPoint(b,offset).setY(y),offset===4.5?1.2:3.5,h,concrete);
   segment(radialPoint(a,offset-.8).setY(y+1),radialPoint(b,offset-.8).setY(y+1),.06,.08,steel);
  }
  const p=radialPoint((a+b)/2,27.8);box(.38,14.5,.38,steel,p.x,7.3,p.z);
  if(Math.abs((a+b)/2)>Math.PI/4){const roof=segment(radialPoint(a,26).setY(17.5),radialPoint(b,26).setY(17.5),10,.25,dark);roof.receiveShadow=false;}
 }
 // Backstop mesh is actual geometry and remains correctly occluded as the camera moves.
 const netPositions:number[]=[];for(let i=-15;i<=15;i++){netPositions.push(i*.65,2.5,24,i*.65,11.5,24);}for(let y=2.5;y<11.6;y+=.55)netPositions.push(-9.75,y,24,9.75,y,24);
 const netGeo=new THREE.BufferGeometry().setAttribute("position",new THREE.Float32BufferAttribute(netPositions,3));geometries.add(netGeo);const netMaterial=ownMaterial(new THREE.LineBasicMaterial({color:"#596573",transparent:true,opacity:.23}));const net=new THREE.LineSegments(netGeo,netMaterial);root.add(net);
 // Four banks are aligned with the actual lighting directions in scene-lighting.ts.
 const floodMat=ownMaterial(new THREE.MeshBasicMaterial({color:"#e3eeff",toneMapped:false})),lampGeo=new THREE.PlaneGeometry(.7,.46);geometries.add(lampGeo);
 const lamps=new THREE.InstancedMesh(lampGeo,floodMat,4*48);lamps.name="LED floodlight banks";root.add(lamps);let lamp=0;
 for(const [x,z] of [[-57,7],[57,7],[-86,-84],[86,-84]]){
  add(new THREE.CylinderGeometry(.48,.9,38,8),steel,x,19,z);const bank=new THREE.Group();bank.position.set(x,38,z);bank.lookAt(0,2,-35);root.add(bank);add(new THREE.BoxGeometry(7.4,4.4,.4),dark,0,0,-.2,bank);
  bank.updateMatrixWorld(true);for(let row=0;row<6;row++)for(let col=0;col<8;col++){transform.position.set((col-3.5)*.87,(row-2.5)*.66,.04);transform.rotation.set(0,0,0);transform.scale.setScalar(1);transform.updateMatrix();lamps.setMatrixAt(lamp++,new THREE.Matrix4().multiplyMatrices(bank.matrixWorld,transform.matrix));}
 }
 // A restrained night sky, with distant city silhouettes and a handful of bright stars.
 scene.background=new THREE.Color("#09182b");scene.fog=new THREE.Fog("#16263a",145,390);
 const starPositions:number[]=[],starRandom=seeded(938);
 for(let i=0;i<110;i++){const a=starRandom()*TAU,h=.22+starRandom()*.7,r=Math.sqrt(1-h*h);starPositions.push(Math.cos(a)*r*340,80+h*240,Math.sin(a)*r*340-50);}
 const starsGeo=new THREE.BufferGeometry().setAttribute("position",new THREE.Float32BufferAttribute(starPositions,3));geometries.add(starsGeo);const starsMat=ownMaterial(new THREE.PointsMaterial({color:"#bfd5f0",size:.32,transparent:true,opacity:.64,fog:false}));root.add(new THREE.Points(starsGeo,starsMat));
 const cityMat=ownMaterial(mat("#172537")),cityWindows:THREE.Matrix4[]=[];
 for(let i=0;i<30;i++){
  const angle=i/30*TAU,p=radialPoint(angle,115+starRandom()*45),height=12+starRandom()*28,width=9+starRandom()*10,depth=9+starRandom()*8;
  box(width,height,depth,cityMat,p.x,height/2-1,p.z);box(width*.7,2.1,depth*.64,cityMat,p.x,height+.05,p.z);
  for(let side=0;side<4;side++)for(let floor=1;floor<height/3-1;floor++)for(let column=0;column<4;column++){
   if(starRandom()<.48)continue;const across=(column-1.5)*(side%2?depth:width)/4;
   transform.position.set(p.x+(side===0?across:side===1?width/2+.01:side===2?-across:-width/2-.01),floor*3,p.z+(side===0?depth/2+.01:side===1?-across:side===2?-depth/2-.01:across));
   transform.rotation.set(0,side*Math.PI/2,0);transform.scale.set(1,1,1);transform.updateMatrix();cityWindows.push(transform.matrix.clone());
  }
 }
 const windowGeo=new THREE.PlaneGeometry(.65,1.0);geometries.add(windowGeo);const windowMat=ownMaterial(new THREE.MeshBasicMaterial({color:'#c1ab7c',transparent:true,opacity:.45})),windows=new THREE.InstancedMesh(windowGeo,windowMat,cityWindows.length);windows.name='Distant city windows';cityWindows.forEach((matrix,i)=>windows.setMatrixAt(i,matrix));root.add(windows);
 // Canvas is used only for the scoreboard face; it is attached to a physical screen.
 const boardMat=ownMaterial(new THREE.MeshBasicMaterial({color:"#102536",toneMapped:false}));box(27,13,.8,dark,0,17,-129);add(new THREE.PlaneGeometry(25.8,11.6),boardMat,0,17,-128.55);
 let boardContext:CanvasRenderingContext2D|null=null,boardTexture:THREE.CanvasTexture|null=null,scoreKey="";
 if(typeof document!=="undefined"){const canvas=document.createElement("canvas");canvas.width=1024;canvas.height=512;boardContext=canvas.getContext("2d");if(boardContext){boardTexture=new THREE.CanvasTexture(canvas);boardTexture.colorSpace=THREE.SRGBColorSpace;textures.add(boardTexture);boardMat.map=boardTexture;boardMat.color.set("#ffffff");}}
 const setScoreboard=(score:StadiumScore)=>{
  const key=JSON.stringify(score);if(key===scoreKey||!boardContext)return;scoreKey=key;const c=boardContext;c.fillStyle="#091824";c.fillRect(0,0,1024,512);c.fillStyle="#61c1a7";c.font="700 43px Arial";c.textAlign="left";c.fillText("DIAMOND  /  NIGHT GAME",50,75);c.fillStyle="#edf1e8";c.font="700 65px Arial";c.fillText((score.away??"AWAY").slice(0,13),52,202);c.fillText((score.home??"HOME").slice(0,13),52,328);c.textAlign="right";c.font="700 94px Arial";c.fillText(String(score.awayScore??0),950,211);c.fillText(String(score.homeScore??0),950,337);c.textAlign="left";c.fillStyle="#a6bbc7";c.font="32px Arial";c.fillText(`${score.inning??1} ${score.half??"INNING"}`,54,455);boardTexture!.needsUpdate=true;
 };setScoreboard({});
 batchArchitecture(root,geometries);
 let disposed=false;
 return {root,field,setScoreboard,update:(_seconds:number)=>{},dispose:()=>{if(disposed)return;disposed=true;root.removeFromParent();geometries.forEach(g=>g.dispose());materials.forEach(m=>m.dispose());textures.forEach(t=>t.dispose());root.clear();}};
}
