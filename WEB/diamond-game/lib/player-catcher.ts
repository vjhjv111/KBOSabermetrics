import * as THREE from 'three';
import type {PlayerModel} from './player-model';
import {add,joined,loft,material,tube,v,restoreSeamNormals} from './player-geometry';
import {batchStaticPlayerMeshes} from './player-batching';
import {playerDimensions} from './player-appearance';
import {setMittStyle} from './player-equipment';

type GuardRow=readonly [y:number,width:number,front:number,curve:number];
const equipped=new WeakSet<PlayerModel>();

/** A thin, closed curved shell rather than a complete tube around the limb. */
function shield(profile:GuardRow[],columns=10,thickness=.006){
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],rows=profile.length,stride=columns+1,layer=rows*stride;
 for(let back=0;back<2;back++)for(const [y,width,front,curve] of profile)for(let col=0;col<=columns;col++){
  const u=col/columns*2-1;positions.push(u*width,y,front-curve*u*u-back*thickness);uv.push(col/columns,(y-profile[0][0])/(profile.at(-1)![0]-profile[0][0]));
 }
 for(let row=0;row<rows-1;row++)for(let col=0;col<columns;col++){
  const a=row*stride+col,b=a+1,c=b+stride,d=a+stride;
  indices.push(a,b,d,b,c,d,a+layer,d+layer,b+layer,b+layer,d+layer,c+layer);
 }
 const edge:number[]=[];
 for(let col=0;col<=columns;col++)edge.push(col);
 for(let row=1;row<rows;row++)edge.push(row*stride+columns);
 for(let col=columns-1;col>=0;col--)edge.push((rows-1)*stride+col);
 for(let row=rows-2;row>0;row--)edge.push(row*stride);
 for(let i=0;i<edge.length;i++){const a=edge[i],b=edge[(i+1)%edge.length];indices.push(a,a+layer,b,a+layer,b+layer,b);}
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}

/** Bevelled pads have broad faces and narrow channels between them. */
function pad(outline:number[][],surface:(x:number,y:number)=>number,depth=.013){
 const centre=outline.reduce((sum,p)=>[sum[0]+p[0]/outline.length,sum[1]+p[1]/outline.length],[0,0]);
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],n=outline.length;
 for(const [scale,raised] of [[1,0],[1,.003],[.82,depth]])for(const p of outline){const x=centre[0]+(p[0]-centre[0])*scale,y=centre[1]+(p[1]-centre[1])*scale;positions.push(x,y,surface(x,y)+raised);uv.push(x*4,y*4);}
 const back=positions.length/3;positions.push(centre[0],centre[1],surface(...centre as [number,number]));uv.push(.5,.5);
 const front=positions.length/3;positions.push(centre[0],centre[1],surface(...centre as [number,number])+depth+.001);uv.push(.5,.5);
 for(let i=0;i<n;i++){
  const next=(i+1)%n;indices.push(back,next,i,front,n*2+i,n*2+next);
  for(let ring=0;ring<2;ring++){const a=ring*n+i,b=ring*n+next;indices.push(a,b,a+n,b,b+n,a+n);}
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}
function skinSurface(profile:GuardRow[],x:number,y:number){
 let i=1;while(i<profile.length-1&&profile[i][0]<y)i++;
 const a=profile[i-1],b=profile[i],t=THREE.MathUtils.clamp((y-a[0])/(b[0]-a[0]),0,1),w=THREE.MathUtils.lerp(a[1],b[1],t);
 return THREE.MathUtils.lerp(a[2],b[2],t)-THREE.MathUtils.lerp(a[3],b[3],t)*(x/w)**2;
}
/** The binding follows the exact polygon border, with no spline overshoot. */
function boundEdge(profile:GuardRow[],columns=12){
 const points:number[][]=[],last=profile.length-1;
 for(let col=0;col<=columns;col++){const u=col/columns*2-1,[y,w,z,curve]=profile[0];points.push([u*w,y,z-curve*u*u]);}
 for(let row=1;row<=last;row++){const [y,w,z,curve]=profile[row];points.push([w,y,z-curve]);}
 for(let col=columns-1;col>=0;col--){const u=col/columns*2-1,[y,w,z,curve]=profile[last];points.push([u*w,y,z-curve*u*u]);}
 for(let row=last-1;row>0;row--){const [y,w,z,curve]=profile[row];points.push([-w,y,z-curve]);}
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],n=points.length;
 for(let ring=0;ring<4;ring++)for(let i=0;i<n;i++){
  let [x,y,z]=points[i];if(ring%2){const d=Math.hypot(x,y-.39);x-=x/d*.007;y-=(y-.39)/d*.007;z=skinSurface(profile,x,y);}
  positions.push(x,y,z+(ring<2?.0025:.0007));uv.push(i/n,ring%2);
 }
 for(let a=0;a<n;a++){const b=(a+1)%n;
  indices.push(a,b,n+a,b,n+b,n+a,2*n+a,3*n+a,2*n+b,2*n+b,3*n+a,3*n+b);
  indices.push(a,2*n+a,b,b,2*n+a,2*n+b,n+a,n+b,3*n+a,n+b,3*n+b,3*n+a);
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}
function fittedShoulder(shoulder:THREE.Object3D,profile:GuardRow[]){
 const triangles:THREE.Vector3[][]=[];
 shoulder.traverse(object=>{
  if(!(object instanceof THREE.Mesh))return;
  const geometry=object.geometry,position=geometry.getAttribute('position'),rest=geometry.userData.restShape as Float32Array|undefined;
  const parts=object.userData.playerBatchParts as {name:string;indexStart:number;indexCount:number}[]|undefined;
  const ranges=parts?.filter(part=>part.name==='Fitted jersey sleeve')??(object.name==='Fitted jersey sleeve'?[{indexStart:0,indexCount:geometry.index!.count}]:[]);
  object.updateMatrix();for(const part of ranges)for(let i=part.indexStart;i<part.indexStart+part.indexCount;i+=3){
   triangles.push([0,1,2].map(offset=>{const index=geometry.index!.getX(i+offset);return (rest?v(rest[index*3],rest[index*3+1],rest[index*3+2]):v().fromBufferAttribute(position,index)).applyMatrix4(object.matrix);}));
  }
 });
 const geometry=shield(profile,8,.005),position=geometry.getAttribute('position'),frontCount=position.count/2,ray=new THREE.Ray(),hit=v();
 for(let i=0;i<position.count;i++){
  const x=position.getX(i),y=position.getY(i);ray.set(v(x,y,1),v(0,0,-1));let z=-Infinity;
  for(const [a,b,c] of triangles)if(ray.intersectTriangle(a,b,c,false,hit))z=Math.max(z,hit.z);
  if(Number.isFinite(z))position.setZ(i,z+(i<frontCount?.009:.004));
 }
 geometry.computeVertexNormals();return geometry;
}
function ribbon(points:number[][],width:number){
 return joined(points.slice(1).map((p,i)=>{
  const a=v(...points[i]),b=v(...p),delta=b.clone().sub(a);
  return new THREE.BoxGeometry(width,delta.length(),.004).applyQuaternion(new THREE.Quaternion().setFromUnitVectors(v(0,1,0),delta.normalize())).translate(...a.add(b).multiplyScalar(.5).toArray());
 }));
}

/** Add catcher protection beneath the original motion pivots; no rig edits. */
export function equipCatcher(model:PlayerModel){
 if(equipped.has(model))return;equipped.add(model);
 const originalMeshes=new Set<THREE.Object3D>();model.root.traverse(object=>originalMeshes.add(object));
 const shell=material(model.appearance.equipmentColor,.73,model.jersey.bumpMap,.0018),padding=material(model.appearance.equipmentColor,.94,model.jersey.bumpMap,.0025);
 shell.userData.playerSurface='equipment';padding.userData.playerSurface='equipment';
 const binding=material('#151e24',.92,model.jersey.bumpMap,.0013),metal=new THREE.MeshStandardMaterial({color:'#525e62',roughness:.36,metalness:.76});
 const chest:GuardRow[]=[[.13,.098,.174,.027],[.205,.17,.19,.067],[.365,.211,.193,.085],[.52,.208,.18,.076],[.604,.176,.153,.055],[.651,.105,.128,.017]];
 const chestSurface=(x:number,y:number)=>skinSurface(chest,x,y)+.003;
 add(shield(chest,12,.008),binding,model.torso).name='Catcher fitted chest backing';
 const pads:THREE.BufferGeometry[]=[];
 // A central sternum piece and independent left/right pectoral and rib pads.
 pads.push(pad([[-.029,.365],[.029,.365],[.034,.56],[.024,.608],[-.024,.608],[-.034,.56]],chestSurface,.013));
 for(const sign of [-1,1]){
  const outline=(points:number[][])=>sign>0?points:points.map(([x,y])=>[-x,y]).reverse();
  pads.push(pad(outline([[.043,.49],[.181,.505],[.163,.586],[.09,.632],[.039,.595]]),chestSurface,.015));
  for(const [y0,y1,w0,w1] of [[.218,.28,.146,.174],[.288,.358,.176,.194],[.369,.433,.195,.194],[.442,.482,.194,.186]]){
   pads.push(pad(outline([[.008,y0],[w0,y0+.013],[w1,y1-.009],[.008,y1]]),chestSurface,.012));
  }
 }
 pads.push(pad([[-.079,.146],[.079,.146],[.134,.198],[0,.211],[-.134,.198]],chestSurface,.011));
 add(joined(pads),padding,model.torso).name='Catcher segmented sternum and rib pads';
 add(boundEdge(chest),shell,model.torso).name='Catcher chest bound edge';
 const straps=[-1,1].map(sign=>ribbon([[sign*.133,.606,.119],[sign*.165,.668,.047],[sign*.169,.644,-.082],[sign*.104,.38,-.159]],.028));
 straps.push(ribbon([[-.16,.296,.105],[-.21,.304,-.025],[-.155,.312,-.139],[0,.314,-.162],[.155,.312,-.139],[.21,.304,-.025],[.16,.296,.105]],.027));
 add(joined(straps),binding,model.torso).name='Catcher shoulder and torso harness';
 for(const shoulder of [model.left,model.right]){
  const profile:GuardRow[]=[[-.133,.045,.085,.022],[-.056,.08,.107,.043],[.021,.077,.106,.034],[.057,.04,.07,.014]];
  add(fittedShoulder(shoulder,profile),shell,shoulder).name='Catcher curved shoulder cap';
 }
 for(const knee of [model.lk,model.rk]){
  const shin:GuardRow[]=[[-.354,.038,.067,.018],[-.308,.05,.093,.03],[-.195,.062,.117,.047],[-.1,.067,.119,.041],[-.069,.054,.105,.025]];
  const kneecap:GuardRow[]=[[-.083,.049,.115,.026],[-.039,.077,.141,.052],[.02,.079,.126,.047],[.051,.042,.088,.017]];
  add(joined([shield(shin,10,.007),shield(kneecap,10,.008)]),shell,knee).name='Catcher articulated knee and shin shells';
  const shinSurface=(x:number,y:number)=>skinSurface(shin,x,y)+.002;
  add(pad([[-.022,-.328],[.022,-.328],[.036,-.119],[.025,-.092],[-.025,-.092],[-.036,-.119]],shinSurface,.004),padding,knee).name='Catcher raised tibia ridge';
  const bands=[[-.283,.068,.074],[-.1,.079,.091]].map(([y,w,d])=>loft([[y-.01,w,d],[y+.01,w,d]],16,1));
  add(joined(bands),binding,knee).name='Catcher fitted calf straps';
  const hinges=[-1,1].map(sign=>new THREE.CylinderGeometry(.011,.011,.005,8).rotateZ(Math.PI/2).translate(sign*.075,-.076,.069));
  add(joined(hinges),metal,knee).name='Catcher knee side hinges';
 }
 // A low brow cage fits below the existing cap bill. Nothing crosses either
 // eye at y=.061; the centre bridge begins below the nose rather than at brow.
 const cage:number[][][]=[
  [[-.127,.068,.137],[-.139,.008,.133],[-.122,-.07,.139],[-.075,-.109,.151],[0,-.119,.157],[.075,-.109,.151],[.122,-.07,.139],[.139,.008,.133],[.127,.068,.137]],
  [[-.127,.068,.137],[-.067,.073,.168],[0,.074,.18],[.067,.073,.168],[.127,.068,.137]],
  [[-.132,.007,.138],[-.071,.007,.173],[0,.006,.185],[.071,.007,.173],[.132,.007,.138]],
  [[-.12,-.066,.142],[-.063,-.067,.172],[0,-.068,.181],[.063,-.067,.172],[.12,-.066,.142]],
  [[-.105,.071,.152],[-.108,.007,.154],[-.101,-.068,.156],[-.075,-.109,.151]],
  [[.105,.071,.152],[.108,.007,.154],[.101,-.068,.156],[.075,-.109,.151]],
  [[0,.006,.185],[0,-.068,.181],[0,-.119,.157]],
 ];
 add(joined(cage.map((path,i)=>tube(path,i<2?.0045:.0036,i===0?24:12))),metal,model.head).name='Catcher open sightline face cage';
 const foam:THREE.BufferGeometry[]=[];
 for(const sign of [-1,1]){
  const points=[[.095,-.065],[.12,-.04],[.126,.029],[.115,.055],[.096,.044],[.099,-.029]];
  foam.push(pad(sign>0?points:points.map(([x,y])=>[-x,y]).reverse(),()=>.103,.018));
 }
 foam.push(pad([[-.061,-.096],[.061,-.096],[.083,-.075],[.065,-.049],[-.065,-.049],[-.083,-.075]],(x)=>.118-Math.abs(x)*.24,.02));
 add(joined(foam),padding,model.head).name='Catcher cheek and chin foam';
 const headStraps=[-1,1].map(sign=>ribbon([[sign*.124,.033,.111],[sign*.137,.026,.003],[sign*.113,.027,-.093],[0,.034,-.122]],.021));
 add(joined(headStraps),binding,model.head).name='Catcher occipital mask harness';
 // The helper is also safe when a previously customized player becomes catcher.
 // Keep immutable local originals so later dressPlayer edits do not accumulate.
 const dims=playerDimensions(model.appearance);
 model.root.traverse(object=>{
  if(!(object instanceof THREE.Mesh)||originalMeshes.has(object))return;
  const geometry=object.geometry,p=geometry.getAttribute('position'),rest=Float32Array.from(p.array);geometry.userData.restShape=rest;
  const head=object.parent===model.head,sx=head?1+(dims.widthScale-1)*.18:dims.widthScale,sz=head?1+(dims.depthScale-1)*.12:dims.depthScale;
  for(let i=0;i<p.count;i++)p.setXYZ(i,rest[i*3]*sx,rest[i*3+1],rest[i*3+2]*sz);
  geometry.computeVertexNormals();restoreSeamNormals(geometry);geometry.computeBoundingSphere();
 });
 const glove=model.le.children.find(child=>{
  let found=false;child.traverse(object=>{
   if(object.name==='Deep leather mitt pocket'||(object.userData.playerBatchParts as {name:string}[]|undefined)?.some(part=>part.name==='Deep leather mitt pocket'))found=true;
  });return found;
 });
 if(glove)setMittStyle(glove,'catcher',model.appearance);
 batchStaticPlayerMeshes(model.root,model.detailMeshes,model.lettering.map(slot=>slot.decal));
}
