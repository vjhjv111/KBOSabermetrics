import * as THREE from 'three';

// Skin and features are borrowed from this head;
// every geometry, neutral array and clipping scratch buffer belongs to one rig.
const STRIDE=11,COVER_GAP=.00015,LASH_GAP=.00022;
type DynamicPart={mesh:THREE.Mesh;position:THREE.BufferAttribute;normal:THREE.BufferAttribute;uv:THREE.BufferAttribute;capacity:number};
type BlinkRig={group:THREE.Group;cover:DynamicPart;lash:DynamicPart;source:Float64Array;triangles:Uint32Array;one:Float64Array;two:Float64Array;amount:number;sx:number;sz:number};
const rigs=new WeakMap<THREE.Object3D,BlinkRig>();
const smooth=(v:number)=>{const t=Math.max(0,Math.min(1,v));return t*t*(3-2*t);};

function part(name:string,material:THREE.Material,capacity:number):DynamicPart{
 const geometry=new THREE.BufferGeometry();
 const position=new THREE.BufferAttribute(new Float32Array(capacity*3),3).setUsage(THREE.DynamicDrawUsage);
 const normal=new THREE.BufferAttribute(new Float32Array(capacity*3),3).setUsage(THREE.DynamicDrawUsage);
 const uv=new THREE.BufferAttribute(new Float32Array(capacity*2),2).setUsage(THREE.DynamicDrawUsage);
 const index=new Uint16Array(capacity);for(let i=0;i<capacity;i++)index[i]=i;
 geometry.setAttribute('position',position);geometry.setAttribute('normal',normal);geometry.setAttribute('uv',uv);geometry.setIndex(new THREE.BufferAttribute(index,1));geometry.setDrawRange(0,0);
 geometry.boundingBox=new THREE.Box3();geometry.boundingSphere=new THREE.Sphere();
 const mesh=new THREE.Mesh(geometry,material);mesh.name=name;mesh.userData.playerBlinkDynamic=true;mesh.castShadow=true;mesh.receiveShadow=true;
 return {mesh,position,normal,uv,capacity};
}

/** Idempotent registration. The input eyes can be mutated/disposed afterwards. */
export function createPlayerBlink(head:THREE.Object3D,eyes:THREE.BufferGeometry,skin:THREE.Material,features:THREE.Material,upperLashPaths:number[][][]):void{
 if(rigs.has(head))return;
 const position=eyes.getAttribute('position'),normal=eyes.getAttribute('normal'),uv=eyes.getAttribute('uv'),index=eyes.index;
 if(!position||!normal||!uv||!index||upperLashPaths.length!==2)throw new Error('Blink requires the indexed paired eye surfaces and two upper lash paths.');
 const columns=new Map<number,{low:number;high:number}>();
 for(let i=0;i<position.count;i++){
  const x=position.getX(i),y=position.getY(i);const bounds=columns.get(x);
  if(bounds){bounds.low=Math.min(bounds.low,y);bounds.high=Math.max(bounds.high,y);}else columns.set(x,{low:y,high:y});
 }
 const paths=upperLashPaths.map(path=>path.map(point=>point.slice()).sort((a,b)=>a[0]-b[0]));
 const source=new Float64Array(position.count*STRIDE);
 for(let i=0;i<position.count;i++){
  const x=position.getX(i),bounds=columns.get(x)!,path=paths.find(path=>Math.sign(path[path.length>>1][0])===Math.sign(x));
  if(!path)throw new Error('Each eye requires its own upper lash path.');
  let segment=0;while(segment+2<path.length&&x>path[segment+1][0])segment++;
  const a=path[segment],b=path[segment+1],t=Math.max(0,Math.min(1,(x-a[0])/(b[0]-a[0]))),offset=i*STRIDE;
  source.set([x,position.getY(i),position.getZ(i),normal.getX(i),normal.getY(i),normal.getZ(i),uv.getX(i),uv.getY(i),bounds.low,bounds.high,a[1]+(b[1]-a[1])*t],offset);
 }
 const valid:number[]=[];
 for(let i=0;i<index.count;i+=3){
  const a=index.getX(i),b=index.getX(i+1),c=index.getX(i+2),j=a*STRIDE,k=b*STRIDE,l=c*STRIDE;
  const area=(source[k]-source[j])*(source[l+1]-source[j+1])-(source[k+1]-source[j+1])*(source[l]-source[j]);
  if(area>1e-16)valid.push(a,b,c); // Original closed corners contain zero-area faces.
  else if(area< -1e-16)throw new Error('Blink expects outward +Z eye triangles.');
 }
 if(!valid.length)throw new Error('Blink eye surface has no outward triangles.');
 // The paired 24-column eye grid needs at most two triangles per source face
 // for the cover. The narrow lash strip fits this same conservative capacity.
 const capacity=valid.length*2;
 const cover=part('Blink skin cover',skin,capacity),lash=part('Blink upper lash ribbon',features,capacity),group=new THREE.Group();group.name='Player blink';group.add(cover.mesh,lash.mesh);
 const rig:BlinkRig={group,cover,lash,source,triangles:new Uint32Array(valid),one:new Float64Array(8*STRIDE),two:new Float64Array(8*STRIDE),amount:0,sx:1,sz:1};
 rigs.set(head,rig);head.add(group);prime(rig);updateBounds(rig);update(rig);
}

function distance(buffer:Float64Array,offset:number,amount:number,mode:number){
 const y=buffer[offset+1],low=buffer[offset+8],high=buffer[offset+9],edge=high-(high-low)*amount;
 if(mode===0)return y-edge;
 const blend=smooth(amount/.2),width=.0009+(.00044-.0009)*blend;
 const lower=(buffer[offset+10]-width*.5)*(1-blend)+edge*blend;
 return mode===1?y-lower:lower+width-y;
}

function clip(input:Float64Array,count:number,output:Float64Array,amount:number,mode:number){
 let used=0;
 for(let i=0;i<count;i++){
  const a=i*STRIDE,b=((i+1)%count)*STRIDE,da=distance(input,a,amount,mode),db=distance(input,b,amount,mode),inside=da>=0,nextInside=db>=0;
  if(inside){for(let k=0;k<STRIDE;k++)output[used*STRIDE+k]=input[a+k];used++;}
  if(inside!==nextInside){const t=da/(da-db);for(let k=0;k<STRIDE;k++)output[used*STRIDE+k]=input[a+k]+(input[b+k]-input[a+k])*t;used++;}
 }
 return used;
}

function emit(part:DynamicPart,used:number,polygon:Float64Array,a:number,b:number,c:number,rig:BlinkRig,gap:number){
 const j=a*STRIDE,k=b*STRIDE,l=c*STRIDE;
 const area=(polygon[k]-polygon[j])*(polygon[l+1]-polygon[j+1])-(polygon[k+1]-polygon[j+1])*(polygon[l]-polygon[j]);
 if(area<1e-17)return used;
 const ax=Math.fround(polygon[j]*rig.sx),ay=Math.fround(polygon[j+1]),bx=Math.fround(polygon[k]*rig.sx),by=Math.fround(polygon[k+1]),cx=Math.fround(polygon[l]*rig.sx),cy=Math.fround(polygon[l+1]);
 if((bx-ax)*(cy-ay)-(by-ay)*(cx-ax)<=0)return used; // A clipped sliver can collapse after GPU float conversion.
 if(used+3>part.capacity)throw new Error('Blink buffer capacity exceeded by incompatible eye topology.');
 for(let point=0;point<3;point++){
  const at=(point===0?a:point===1?b:c)*STRIDE;
  part.position.setXYZ(used,polygon[at]*rig.sx,polygon[at+1],(polygon[at+2]+gap)*rig.sz);
  const nx=polygon[at+3]/rig.sx,ny=polygon[at+4],nz=polygon[at+5]/rig.sz,length=Math.hypot(nx,ny,nz)||1;
  part.normal.setXYZ(used,nx/length,ny/length,nz/length);part.uv.setXY(used,polygon[at+6],polygon[at+7]);used++;
 }
 return used;
}

// No arrays, vectors, materials or geometry objects are allocated during update.
function update(rig:BlinkRig){
 let coverUsed=0,lashUsed=0;
 const {source,triangles,one,two,amount}=rig;
 for(let triangle=0;triangle<triangles.length;triangle+=3){
  for(let vertex=0;vertex<3;vertex++){const offset=triangles[triangle+vertex]*STRIDE;for(let k=0;k<STRIDE;k++)one[vertex*STRIDE+k]=source[offset+k];}
  if(amount>0){const count=clip(one,3,two,amount,0);for(let i=1;i<count-1;i++)coverUsed=emit(rig.cover,coverUsed,two,0,i,i+1,rig,COVER_GAP);}
  const first=clip(one,3,two,amount,1),count=clip(two,first,one,amount,2);
  for(let i=1;i<count-1;i++)lashUsed=emit(rig.lash,lashUsed,one,0,i,i+1,rig,LASH_GAP);
 }
 finish(rig.cover,coverUsed);finish(rig.lash,lashUsed);rig.cover.mesh.visible=amount>0;
}
function finish(part:DynamicPart,used:number){part.mesh.geometry.setDrawRange(0,used);part.position.needsUpdate=true;part.normal.needsUpdate=true;part.uv.needsUpdate=true;}

// Keep the inactive capacity finite, shaped and bounded as well. Existing
// inventory tools inspect all vertices even when the skin cover is fully open.
function prime(rig:BlinkRig){
 let used=0;
 for(let triangle=0;triangle<rig.triangles.length;triangle+=3){
  for(let vertex=0;vertex<3;vertex++){const offset=rig.triangles[triangle+vertex]*STRIDE;for(let k=0;k<STRIDE;k++)rig.one[vertex*STRIDE+k]=rig.source[offset+k];}
  for(let repeat=0;repeat<2;repeat++){emit(rig.cover,used,rig.one,0,1,2,rig,COVER_GAP);used=emit(rig.lash,used,rig.one,0,1,2,rig,LASH_GAP);}
 }
}

function updateBounds(rig:BlinkRig){
 const box=rig.cover.mesh.geometry.boundingBox!;box.makeEmpty();
 for(let i=0;i<rig.source.length;i+=STRIDE){const x=rig.source[i]*rig.sx,y=rig.source[i+1],z=(rig.source[i+2]+LASH_GAP)*rig.sz;box.min.x=Math.min(box.min.x,x);box.min.y=Math.min(box.min.y,y);box.min.z=Math.min(box.min.z,z);box.max.x=Math.max(box.max.x,x);box.max.y=Math.max(box.max.y,y);box.max.z=Math.max(box.max.z,z);}
 box.min.z-=LASH_GAP*rig.sz;rig.lash.mesh.geometry.boundingBox!.copy(box);
 box.getBoundingSphere(rig.cover.mesh.geometry.boundingSphere!);rig.lash.mesh.geometry.boundingSphere!.copy(rig.cover.mesh.geometry.boundingSphere!);
}

/** A pure amount sample: seeking backwards does not accumulate deformation. */
export function setPlayerBlink(head:THREE.Object3D,amount:number):void{
 const rig=rigs.get(head);if(!rig)return;const value=Number.isFinite(amount)?Math.max(0,Math.min(1,amount)):0;
 if(value===rig.amount)return;rig.amount=value;update(rig);
}

/** Head-local body width/depth only; height and handedness remain parent transforms. */
export function fitPlayerBlink(head:THREE.Object3D,sx:number,sz:number):void{
 const rig=rigs.get(head);if(!rig)return;
 if(!Number.isFinite(sx)||!Number.isFinite(sz)||sx<=0||sz<=0)throw new Error('Blink head scales must be positive finite values.');
 if(sx===rig.sx&&sz===rig.sz)return;rig.sx=sx;rig.sz=sz;prime(rig);updateBounds(rig);update(rig);
}

export function isPlayerBlinkMesh(object:THREE.Object3D):boolean{return (object as THREE.Mesh).isMesh===true&&object.userData.playerBlinkDynamic===true;}
