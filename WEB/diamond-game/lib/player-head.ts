import * as THREE from "three";
import {createLipGeometries} from "./player-lips";
import {createPlayerBlink} from './player-blink';
import type {PlayerHairStyle} from "./player-appearance";
import {v,loft,joined,oval,seams,material,add,profileAt,gauss,restoreSeamNormals,type Profile,type SurfaceMaterial} from "./player-geometry";

const headProfile:Profile[]=[
  [-.07,.035,.042,.04],[-.055,.061,.053,.034],[-.027,.087,.071,.017],
  [.014,.103,.095,.002],[.049,.109,.104,-.002],[.084,.11,.104,-.005],
  [.124,.106,.101,-.006],[.158,.089,.085,-.006],[.186,.048,.044,-.006],[.193,.002,.002,-.006]
];
export const capProfile:Profile[]=[
 [.066,.116,.116,-.009],[.091,.126,.121,-.009],[.13,.126,.12,-.01],
 [.18,.107,.102,-.01],[.213,.069,.068,-.01],[.23,.032,.032,-.01],[.235,.0005,.0005,-.01]
];
function faceRelief(height:number,angle:number){
 const front=Math.max(0,Math.cos(angle)),side=Math.abs(Math.sin(angle));
 const socket=gauss(height,.062,.012)*gauss(side,.40,.19);
 const brow=gauss(height,.081,.012)*gauss(side,.40,.25);
 const cheek=gauss(height,.022,.021)*gauss(side,.61,.24);
 const jaw=gauss(height,-.024,.025)*gauss(side,.76,.25);
 const muzzle=gauss(height,-.023,.017)*gauss(side,0,.39);
 const nasalWing=gauss(height,.005,.018)*gauss(side,.18,.105);
 return front*(-.0075*socket+.0023*brow+.0032*cheek-.0015*jaw+.002*muzzle+.0015*nasalWing);
}
function faceZ(x:number,y:number){
 const [,w,d,z=0]=profileAt(headProfile,y),angle=Math.asin(THREE.MathUtils.clamp(x/w,-1,1));
 return z+Math.cos(angle)*d+faceRelief(y,angle);
}
function headGeometry(){
 const geometry=loft(headProfile,48,5),position=geometry.getAttribute("position");
 for(let i=0;i<position.count;i++){
  const angle=(i%49)/48*Math.PI*2;
  position.setZ(i,position.getZ(i)+faceRelief(position.getY(i),angle));
 }
 geometry.computeVertexNormals();
 const normals=geometry.getAttribute("normal");
 for(let i=0;i<position.count;i+=49){
  const n=v(normals.getX(i)+normals.getX(i+48),normals.getY(i)+normals.getY(i+48),normals.getZ(i)+normals.getZ(i+48)).normalize();
  normals.setXYZ(i,n.x,n.y,n.z);normals.setXYZ(i+48,n.x,n.y,n.z);
 }
 // A very slight warm cheek and cooler jaw shade supplies face planes without a painted-on smile.
 const positions=geometry.getAttribute("position"),colors:number[]=[],color=new THREE.Color();
 for(let i=0;i<positions.count;i++){
  const y=positions.getY(i),x=positions.getX(i),z=positions.getZ(i);
  const cheek=gauss(y,.015,.025)*gauss(Math.abs(x),.076,.028)*Math.max(0,z/.1);
  const jaw=gauss(y,-.035,.026)*Math.max(0,z/.1),temple=gauss(y,.067,.03)*gauss(Math.abs(x),.083,.027);
  color.setRGB(1-temple*.012,.992-cheek*.035-jaw*.01-temple*.018,.985-cheek*.048-jaw*.006-temple*.017);colors.push(color.r,color.g,color.b);
 }
 geometry.setAttribute("color",new THREE.Float32BufferAttribute(colors,3));return geometry;
}

export function hairGeometry(style:PlayerHairStyle="short"){
 const geometry=loft([[0,.1,.1],[1,.1,.1]],32,12),positions=geometry.getAttribute("position");
 for(let row=0;row<=12;row++)for(let col=0;col<=32;col++){
  const angle=col/32*Math.PI*2,front=Math.max(0,Math.cos(angle));
  // A hairline follows the temples and nape, staying above the eyebrows under the bill.
  const back=Math.max(0,-Math.cos(angle)),bottom=(style==="flow"?.023-back*.064:style==="buzz"?.04:.023)+.111*front*front,y=THREE.MathUtils.lerp(bottom,.191,row/12);
  const [,w,d,z=0]=profileAt(headProfile,y);
  const layer=style==="buzz"?.0006:style==="flow"?.005+back*.003:.0018;
  positions.setXYZ(row*33+col,Math.sin(angle)*(w+layer),y,z+Math.cos(angle)*(d+layer));
 }
 geometry.computeVertexNormals();return restoreSeamNormals(geometry);
}

/** A thin inset patch follows the helmet instead of floating ellipsoids above its curved shell. */
function helmetVent(x:number,y:number,width:number,height:number){
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],segments=20;
 for(let i=0;i<=segments;i++){
  const angle=i?((i-1)/(segments-1))*Math.PI*2:0;
  const px=x+(i?Math.cos(angle)*width:0),py=y+(i?Math.sin(angle)*height:0);
  const [,w,d,z=0]=profileAt(capProfile,py),pz=z+d*Math.sqrt(Math.max(0,1-(px/w)**2))+.0008;
  positions.push(px,py,pz);uv.push(i?.5+Math.cos(angle)*.5:.5,i?.5+Math.sin(angle)*.5:.5);
  if(i>1)indices.push(0,i-1,i);
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));
 geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}

/** Fit the nasal base to the face. Sample the actual face triangles, not the
 * analytic profile, so the lower attachment follows the rendered cheek mesh. */
function frontSampler(geometry:THREE.BufferGeometry,bounds?:readonly [number,number,number,number]){
 const p=geometry.getAttribute('position'),index=geometry.index!,faces:{x:number;y:number;z:number;bx:number;by:number;bz:number;cx:number;cy:number;cz:number;den:number;minX:number;maxX:number;minY:number;maxY:number}[]=[];
 for(let i=0;i<index.count;i+=3){
  const a=index.getX(i),b=index.getX(i+1),c=index.getX(i+2),x=p.getX(a),y=p.getY(a),z=p.getZ(a),bx=p.getX(b)-x,by=p.getY(b)-y,bz=p.getZ(b)-z,cx=p.getX(c)-x,cy=p.getY(c)-y,cz=p.getZ(c)-z,den=bx*cy-by*cx;
  const minX=Math.min(x,x+bx,x+cx),maxX=Math.max(x,x+bx,x+cx),minY=Math.min(y,y+by,y+cy),maxY=Math.max(y,y+by,y+cy);
  if(bounds&&(maxX<bounds[0]||minX>bounds[1]||maxY<bounds[2]||minY>bounds[3]))continue;
  if(Math.abs(den)>1e-12)faces.push({x,y,z,bx,by,bz,cx,cy,cz,den,minX,maxX,minY,maxY});
 }
 return (x:number,y:number)=>{
  let front=-Infinity;
  for(const f of faces){if(x<f.minX-1e-8||x>f.maxX+1e-8||y<f.minY-1e-8||y>f.maxY+1e-8)continue;const dx=x-f.x,dy=y-f.y,u=(dx*f.cy-dy*f.cx)/f.den,w=(f.bx*dy-f.by*dx)/f.den;if(u>=-1e-7&&w>=-1e-7&&u+w<=1+1e-7)front=Math.max(front,f.z+u*f.bz+w*f.cz);}
  if(!Number.isFinite(front))throw new Error('Nose surface sample outside its rendered geometry');return front;
 };
}
function noseGeometry(face:THREE.BufferGeometry){
 const geometry=loft([[-.006,.003,.003,.102],[.001,.012,.008,.108],[.008,.015,.011,.112],
  [.018,.012,.014,.111],[.031,.0075,.011,.108],[.05,.006,.006,.102],
  [.07,.008,.003,.102],[.081,.001,.001,.101]],24,3),p=geometry.getAttribute('position'),surface=frontSampler(face,[-.02,.02,-.0061,.0401]);
 for(let i=0;i<p.count;i++){
  const y=p.getY(i);if(y>=.04)continue;
  const angle=(i%25)/24*Math.PI*2,front=Math.cos(angle),side=Math.abs(Math.sin(angle)),blend=1-THREE.MathUtils.smoothstep(y,.018,.04);
  const x=p.getX(i)*(1+.16*gauss(y,.004,.009)),skinZ=surface(x,y),rear=(1-THREE.MathUtils.smoothstep(front,-.2,.28))*blend;
  let z=THREE.MathUtils.lerp(p.getZ(i),skinZ-.0007,rear);
  z+=.0015*gauss(y,.005,.006)*gauss(side,.70,.20)*Math.max(0,front);
  // The columella's lower boundary sinks just into the face. The tip and
  // upper bridge remain in place instead of bringing the whole nose backward.
  z=THREE.MathUtils.lerp(z,skinZ-.00035,1-THREE.MathUtils.smoothstep(y,-.006,-.001));
  p.setXYZ(i,x,y,z);
 }
 geometry.computeVertexNormals();restoreSeamNormals(geometry);return geometry;
}
function nostrilGeometry(nose:THREE.BufferGeometry){
 // Clip the existing nose triangles to each ellipse. Every dark face remains
 // parallel to its exact supporting skin triangle, with no crossing lattice.
 const p=nose.getAttribute('position'),normals=nose.getAttribute('normal'),index=nose.index!,positions:number[]=[],normalValues:number[]=[],uv:number[]=[],indices:number[]=[],around=20;
 type Point=number[];
 for(const sign of [-1,1]){
  const x0=sign*.0088,y0=.0004,rx=.0024,ry=.00105,ellipse=Array.from({length:around},(_,i)=>[x0+Math.cos(i/around*Math.PI*2)*rx,y0+Math.sin(i/around*Math.PI*2)*ry]);
  for(let triangle=0;triangle<index.count;triangle+=3){
   const ia=index.getX(triangle),ib=index.getX(triangle+1),ic=index.getX(triangle+2),ax=p.getX(ia),ay=p.getY(ia),bx=p.getX(ib),by=p.getY(ib),cx=p.getX(ic),cy=p.getY(ic);
   if(Math.max(ax,bx,cx)<x0-rx||Math.min(ax,bx,cx)>x0+rx||Math.max(ay,by,cy)<y0-ry||Math.min(ay,by,cy)>y0+ry)continue;
   if((bx-ax)*(cy-ay)-(by-ay)*(cx-ax)<=1e-12)continue;
   let polygon:Point[]=Array.from({length:3},(_,j)=>{const i=index.getX(triangle+j);return[p.getX(i),p.getY(i),p.getZ(i),normals.getX(i),normals.getY(i),normals.getZ(i)];});
   for(let edge=0;edge<around&&polygon.length;edge++){
    const u=ellipse[edge],w=ellipse[(edge+1)%around],side=(q:Point)=>(w[0]-u[0])*(q[1]-u[1])-(w[1]-u[1])*(q[0]-u[0]),next:Point[]=[];
    for(let i=0;i<polygon.length;i++){
     const a=polygon[i],b=polygon[(i+1)%polygon.length],da=side(a),db=side(b),insideA=da>=-1e-15,insideB=db>=-1e-15;
     if(insideA)next.push(a);
     if(insideA!==insideB){const t=da/(da-db);next.push(a.map((value,j)=>THREE.MathUtils.lerp(value,b[j],t)));}
    }
    polygon=next;
   }
   if(polygon.length<3)continue;const start=positions.length/3;
   for(const q of polygon){positions.push(q[0],q[1],q[2]+.00015);const n=v(q[3],q[4],q[5]).normalize();normalValues.push(n.x,n.y,n.z);uv.push((q[0]-x0)/rx*.5+.5,(q[1]-y0)/ry*.5+.5);}
   for(let i=1;i<polygon.length-1;i++)indices.push(start,start+i,start+i+1);
  }
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute('normal',new THREE.Float32BufferAttribute(normalValues,3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);return geometry;
}

/** A small curved surface opens inside the sculpted socket, below its bony rim. */
function eyeBounds(t:number){const arch=Math.pow(Math.max(0,1-t*t),.7);return [.061-.0028*arch+.0006*t,.061+.0035*arch+.0006*t];}
function eyeZ(x:number,y:number){
 const t=(Math.abs(x)-.044)/.0145,[low,high]=eyeBounds(t),arch=Math.pow(Math.max(0,1-t*t),.7),f=THREE.MathUtils.clamp((y-low)/Math.max(.0001,high-low),0,1);
 return faceZ(x,y)+.0005+.004*arch*Math.sin(Math.PI*f);
}
function eyePigment(x:number,y:number){
 const radius=Math.hypot(x,y-.0614),iris=1-THREE.MathUtils.smoothstep(radius,.00458,.00478),pupil=1-THREE.MathUtils.smoothstep(radius,.0017,.00188);
 const edge=THREE.MathUtils.smoothstep(radius,.0039,.00455),white=[210,201,184],dark=[21,25,24],brown=[60-edge*17,51-edge*15,42-edge*12];
 return white.map((value,index)=>THREE.MathUtils.lerp(THREE.MathUtils.lerp(value,brown[index],iris),dark[index],pupil));
}
// One fixed pigment image (128 KiB), private and lazy. Every texture owns a
// fresh byte copy; caller edits and disposal never affect later players.
let eyePixelTemplate:Uint8Array|null=null;
function eyeTexture(){
 // The pigment shares the sclera surface. Separate curved fans intersected
 // the sclera's triangulation, producing black/white depth speckles.
 if(typeof document==='undefined')return null;
 const width=256,height=128;
 if(!eyePixelTemplate){
  const data=new Uint8Array(width*height*4);
  for(let row=0;row<height;row++)for(let col=0;col<width;col++){
   const color=eyePigment(((col+.5)/width*2-1)*.0145,.055+(row+.5)/height*.012),offset=(row*width+col)*4;
   for(let channel=0;channel<3;channel++)data[offset+channel]=Math.round(color[channel]);data[offset+3]=255;
  }
  eyePixelTemplate=data;
 }
 const texture=new THREE.DataTexture(eyePixelTemplate.slice(),width,height,THREE.RGBAFormat);texture.colorSpace=THREE.SRGBColorSpace;
 texture.magFilter=THREE.LinearFilter;texture.minFilter=THREE.LinearMipmapLinearFilter;texture.generateMipmaps=true;texture.needsUpdate=true;return texture;
}
function eyeGeometry(sign:number){
 const positions:number[]=[],indices:number[]=[],uv:number[]=[],colors:number[]=[],columns=24,rows=8,color=new THREE.Color();
 for(let row=0;row<=rows;row++)for(let col=0;col<=columns;col++){
  const u=col/columns,t=u*2-1,f=row/rows,[low,high]=eyeBounds(t);
  const x=sign*(.044+t*.0145),y=THREE.MathUtils.lerp(low,high,f),z=eyeZ(x,y);
  positions.push(x,y,z);uv.push(u,(y-.055)/.012);
  const pigment=eyePigment(t*.0145,y);color.setRGB(pigment[0]/255,pigment[1]/255,pigment[2]/255).convertSRGBToLinear();colors.push(color.r,color.g,color.b);
  if(row<rows&&col<columns){const a=row*(columns+1)+col,b=a+1,c=b+columns+1,d=a+columns+1;indices.push(...(sign>0?[a,b,d,b,c,d]:[a,d,b,b,d,c]));}
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute("color",new THREE.Float32BufferAttribute(colors,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}

function upperLidGeometry(sign:number){
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],columns=16,rows=3;
 for(let row=0;row<=rows;row++)for(let col=0;col<=columns;col++){
  const t=col/columns*2-1,arch=Math.pow(Math.max(0,1-t*t),.7),f=row/rows,x=sign*(.044+t*.0145),[,edge]=eyeBounds(t);
  const y=edge+f*.007*arch,z=faceZ(x,y)+.00025+arch*(.0013*(1-f)+.0014*Math.sin(Math.PI*f));
  positions.push(x,y,z);uv.push(col/columns,f);
  if(row<rows&&col<columns){const a=row*(columns+1)+col,b=a+1,c=b+columns+1,d=a+columns+1;indices.push(...(sign>0?[a,b,d,b,c,d]:[a,d,b,b,d,c]));}
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}

function browGeometry(sign:number){
 const positions:number[]=[],indices:number[]=[],uv:number[]=[],columns=16;
 for(let row=0;row<2;row++)for(let col=0;col<=columns;col++){
  const t=col/columns,x=sign*(.027+t*.041),curve=Math.sin(t*Math.PI),y=.079+.0045*curve-.001*t+(row-.5)*(.0012+curve*.003);
  positions.push(x,y,faceZ(x,y)+.0007);uv.push(t,row);
  if(row===0&&col<columns){const a=col,b=a+1,c=b+columns+1,d=a+columns+1;indices.push(...(sign>0?[a,b,d,b,c,d]:[a,d,b,b,d,c]));}
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}

function lipsGeometry(){
 const positions:number[]=[],colors:number[]=[],uv:number[]=[],indices:number[]=[],columns=20,rows=6;
 for(let row=0;row<=rows;row++)for(let col=0;col<=columns;col++){
  const t=col/columns*2-1,x=t*.024,arch=Math.max(0,1-t*t),f=row/rows;
  const crease=-.026+gauss(Math.abs(t),.3,.22)*.0008;
  const bottom=crease-.0042*arch,top=crease+(.0025+gauss(Math.abs(t),.28,.23)*.0015)*arch;
  const y=THREE.MathUtils.lerp(bottom,top,f),z=faceZ(x,y)+.0004+.0017*arch*Math.sin(Math.PI*f);
  positions.push(x,y,z);uv.push(col/columns,f);colors.push(1,.80+Math.abs(t)*.09,.77+Math.abs(t)*.10);
  if(row<rows&&col<columns){const a=row*(columns+1)+col,b=a+1,c=b+columns+1,d=a+columns+1;indices.push(a,b,d,b,c,d);}
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute("color",new THREE.Float32BufferAttribute(colors,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
}

function brimGeometry(){
 const positions:number[]=[],uv:number[]=[],indices:number[]=[],columns=20,rows=4;
 for(let row=0;row<=rows;row++)for(let col=0;col<=columns;col++){
  const t=row/rows,angle=(col/columns-.5)*2.25;
  const x=Math.sin(angle)*THREE.MathUtils.lerp(.111,.148,t);
  const z=THREE.MathUtils.lerp(Math.cos(angle)*.108,.057+Math.cos(angle)*.146,t);
  const y=.096-t*.016+Math.pow(Math.abs(x)/.148,2)*.015;
  positions.push(x,y,z);uv.push(col/columns,t);
  if(row<rows&&col<columns){const a=row*(columns+1)+col,b=a+1,c=b+columns+1,d=a+columns+1;indices.push(a,d,b,b,d,c);}
 }
 const result=new THREE.BufferGeometry();result.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));result.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));result.setIndex(indices);result.computeVertexNormals();return result;
}

export function headDetails(head:THREE.Object3D,skin:SurfaceMaterial,cap:SurfaceMaterial,cloth:THREE.Texture|null,isBatter:boolean){
 const faceMaterial=skin.clone();faceMaterial.vertexColors=true;
 const face=headGeometry(),nose=noseGeometry(face);add(face,faceMaterial,head).name="Sculpted face";add(nose,skin,head).name="Nasal bridge and tip";
 const hair=material("#242421",.94,cloth,.0008),features=material("#684f42",.91),eyeWhite=material("#ffffff",.4);hair.userData.playerSurface="hair";
 eyeWhite.map=eyeTexture();eyeWhite.vertexColors=!eyeWhite.map;
 const ears=[-1,1].map(sign=>loft([[-.006,.004,.006],[.003,.007,.013],[.025,.009,.018],[.043,.007,.014],[.05,.002,.005]],16,3).translate(sign*.109,0,-.013));
 add(joined(ears),skin,head);
 const earInset=skin.clone();earInset.color.multiplyScalar(.77);earInset.userData.skinToneFactor=.77;
 add(joined([-1,1].map(sign=>oval(.001,.01,.007,sign*.117,.024,-.009,12))),earInset,head);
 const earFolds=[-1,1].map(sign=>[[sign*.114,.003,-.004],[sign*.118,.016,.002],[sign*.118,.035,-.001],[sign*.114,.041,-.012]]);
 seams(head,earFolds,skin,.0014);
 add(hairGeometry(),hair,head).name="Custom hair";
 const eyes=[-1,1].map(eyeGeometry),eyeColors=eyes.flatMap(eye=>Array.from(eye.getAttribute("color").array)),eyesGeometry=joined(eyes);
 eyesGeometry.setAttribute("color",new THREE.Float32BufferAttribute(eyeColors,3));
 add(eyesGeometry,eyeWhite,head).name="Recessed almond eyes";
 const lid=skin.clone();lid.color.multiplyScalar(.96);lid.userData.skinToneFactor=.96;
 add(joined([-1,1].map(upperLidGeometry)),skin,head).name="Upper eyelid hoods";
 const lids:number[][][]=[],lashPaths:number[][][]=[];
 for(const sign of [-1,1]){
  for(const upper of [false,true]){
   const points:number[][]=[];
   for(let i=0;i<=8;i++){
    const t=i/4-1,x=sign*(.044+t*.0145),bounds=eyeBounds(t),y=bounds[upper?1:0];
    points.push([x,y,faceZ(x,y)+.001]);
   }
   lids.push(points);if(upper)lashPaths.push(points.map(([x,y,z])=>[x,y-.00045,z+.00025]));
  }
 }
 seams(head,lids,lid,.00085);createPlayerBlink(head,eyesGeometry,skin,features,lashPaths);
 add(joined([-1,1].map(browGeometry)),hair,head).name="Tapered brows";
 add(nostrilGeometry(nose),features,head);
 const lips=skin.clone();lips.vertexColors=true;lips.roughness=.7;
 const lipPatch=createLipGeometries(faceZ);
 add(lipPatch.lips,lips,head).name="Natural lip planes";
 add(lipPatch.mouth,features,head);
 const shell=loft(capProfile,40,4),shellPositions=shell.getAttribute("position");
 for(let i=0;i<shellPositions.count;i++){
  const angle=(i%41)/40*Math.PI*2,y=shellPositions.getY(i);
  // Raise the forehead opening above the brows; the sides still protect the temples.
  const lifted=y+.029*Math.pow(Math.max(0,Math.cos(angle)),6)*(1-THREE.MathUtils.smoothstep(y,.066,.14));
  const [,w,d,z=0]=profileAt(capProfile,lifted);
  shellPositions.setXYZ(i,Math.sin(angle)*w,lifted,z+Math.cos(angle)*d);
 }
 shell.computeVertexNormals();
 const shellNormals=shell.getAttribute("normal");
 for(let i=0;i<shellPositions.count;i+=41){
  const normal=v(shellNormals.getX(i)+shellNormals.getX(i+40),shellNormals.getY(i)+shellNormals.getY(i+40),shellNormals.getZ(i)+shellNormals.getZ(i+40)).normalize();
  shellNormals.setXYZ(i,normal.x,normal.y,normal.z);shellNormals.setXYZ(i+40,normal.x,normal.y,normal.z);
 }
 add(shell,cap,head);
 // A narrow returned shell edge provides thickness at the temple/forehead
 // opening. It follows the same lifted opening as the exterior crown.
 const rimPoints:number[]=[],rimUV:number[]=[],rimIndices:number[]=[];
 for(let row=0;row<2;row++)for(let i=0;i<=40;i++){
  const angle=i/40*Math.PI*2,y=.066+.029*Math.pow(Math.max(0,Math.cos(angle)),6),[,w,d,z=0]=profileAt(capProfile,y);
  rimPoints.push(Math.sin(angle)*(w-row*.0028),y+row*.0012,z+Math.cos(angle)*(d-row*.0028));rimUV.push(i/40,row);
  if(row===0&&i<40){const a=i,b=a+1,c=b+41,d=a+41;rimIndices.push(a,b,d,b,c,d);}
 }
 const rim=new THREE.BufferGeometry();rim.setAttribute("position",new THREE.Float32BufferAttribute(rimPoints,3));rim.setAttribute("uv",new THREE.Float32BufferAttribute(rimUV,2));rim.setIndex(rimIndices);rim.computeVertexNormals();add(rim,cap,head).name="Fitted crown rim";
 const bill=add(brimGeometry(),cap,head);bill.material.side=THREE.DoubleSide;
 const underside=material("#28312d",.86);add(brimGeometry().translate(0,-.002,0),underside,head).material.side=THREE.BackSide;
 const billEdge:number[][]=[];
 for(let i=0;i<=24;i++){const angle=(i/24-.5)*2.25,x=Math.sin(angle)*.148;billEdge.push([x,.079+Math.pow(Math.abs(x)/.148,2)*.015,.057+Math.cos(angle)*.146]);}
 seams(head,[billEdge],cap,.0012).name="Rounded bill edge";
 if(isBatter){
  const guards=[-1,1].map(sign=>loft([[-.049,.002,.009],[.0,.014,.036],[.051,.017,.046],[.091,.009,.031]],12,3).translate(sign*.117,0,-.009));
  add(joined(guards),cap,head);
  const vents=[-1,1].flatMap(sign=>[
   oval(.0018,.011,.017,sign*.132,.023,-.002,10),
   helmetVent(sign*.072,.173,.014,.003),helmetVent(sign*.035,.203,.008,.0025)
  ]);
  const ventMaterial=underside.clone();ventMaterial.side=THREE.FrontSide;add(joined(vents),ventMaterial,head);
  const edge=material("#101820",.58);
  seams(head,[-1,1].map(sign=>[[sign*.112,.093,.047],[sign*.128,.038,.03],[sign*.124,-.019,.004],[sign*.107,-.041,-.026]]),edge,.0028);
  const rivets=[-1,1].flatMap(sign=>[oval(.0028,.003,.003,sign*.1285,.0471,.0279,8),oval(.0028,.003,.003,sign*.126,-.017,.005,8)]);
  add(joined(rivets),features,head);
 }else{
  const stitch=material("#81868a",.94,cloth,.002);
  const panels:number[][][]=[];
  for(let panel=0;panel<6;panel++){
   const angle=(panel+.5)/6*Math.PI*2,points:number[][]=[];
   for(let i=0;i<=10;i++){
    const y=THREE.MathUtils.lerp(.069+.029*Math.pow(Math.max(0,Math.cos(angle)),6),.233,i/10),[,w,d,z=0]=profileAt(capProfile,y);
    points.push([Math.sin(angle)*(w+.0005),y,z+Math.cos(angle)*(d+.0005)]);
   }
   panels.push(points);
  }
  seams(head,panels,stitch,.00065);
  add(oval(.012,.005,.012,0,.236,-.01,10),cap,head);
 }
}
