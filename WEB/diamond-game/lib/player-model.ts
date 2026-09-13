import * as THREE from "three";
import {v,object,add,material,profileAt,loft,joined,oval,tube,seams,gauss,restoreSeamNormals,type Profile,type SurfaceMaterial} from "./player-geometry";
import {capProfile,hairGeometry,headDetails} from "./player-head";
import {shoe,hand,mitt,mittPalette} from "./player-equipment";
export {createBat,createMitt} from "./player-equipment";
import {PlayerAppearance,PlayerCustomization,normalizePlayerCustomization,playerDimensions} from "./player-appearance";
import {createPlayerSurface,createPlayerRoughness,createPlayerAlbedo} from "./player-surfaces";
import {bindUniform} from "./player-uniform";
import {batchStaticPlayerMeshes} from "./player-batching";
import {equipCatcher as fitCatcherEquipment} from "./player-catcher";
import {createPitchGrip,fitPitchGrip,isPitchGripBallMesh,type PitchGrip} from './player-pitch-grip';
import {fitPlayerBlink,isPlayerBlinkMesh} from './player-blink';

/** Named pivots are the existing motion and collision rig, in metres. */
export type PlayerModel={
 root:THREE.Group;hips:THREE.Group;torso:THREE.Group;head:THREE.Group;
 left:THREE.Group;right:THREE.Group;le:THREE.Group;re:THREE.Group;
 ll:THREE.Group;rl:THREE.Group;lk:THREE.Group;rk:THREE.Group;lf:THREE.Group;rf:THREE.Group;
 jersey:SurfaceMaterial;cap:SurfaceMaterial;accent:SurfaceMaterial;
 lettering:{area:"front"|"back"|"cap";decal:THREE.Mesh;material:THREE.MeshStandardMaterial}[];
 appearanceKey:string;
 appearance:Required<PlayerCustomization>;
 detailMeshes:THREE.Mesh[];
};

const shirtProfile:Profile[]=[
 [.012,.171,.122,.008],[.047,.188,.131,.008],[.13,.197,.14,.008],
 [.27,.207,.15,.006],[.4,.225,.151,.002],[.51,.236,.146,-.001],
 [.605,.237,.134,-.007],[.651,.208,.115,-.009],[.706,.116,.077,-.01],[.731,.083,.068,-.009]
];
function jerseyTailoring(height:number,angle:number){
 const side=Math.abs(Math.sin(angle)),back=Math.max(0,-Math.cos(angle));
 // Broad tension folds stay inside the garment. The collar and tucked edge
 // retain their original positions, as do every attachment and motion pivot.
 const interior=THREE.MathUtils.smoothstep(height,.07,.105)*(1-THREE.MathUtils.smoothstep(height,.6,.645));
 const lateral=THREE.MathUtils.smoothstep(side,.16,.4);
 const waist=.0036*side*lateral*(gauss(height,.145+.032*Math.cos(angle*2),.026)-.72*gauss(height,.179+.032*Math.cos(angle*2),.026));
 const underarm=-.0032*side*side*lateral*gauss(height,.465-.052*side,.029);
 const shoulder=.0024*back*gauss(height,.566-.035*side,.038);
 return interior*(waist+underarm+shoulder);
}
function jerseyRelief(height:number,angle:number){
 const front=Math.max(0,Math.cos(angle)),back=Math.max(0,-Math.cos(angle));
 return .007*gauss(height,.13,.12)*Math.sin(angle*7+height*24)
  +.0035*gauss(height,.34,.19)*Math.sin(angle*5-height*13)
  +.005*gauss(height,.535,.075)*Math.sin(angle*6+height*22)
  +.007*front*gauss(height,.515,.1)*gauss(Math.abs(Math.sin(angle)),.48,.3)
  -.0035*back*gauss(Math.sin(angle),0,.22)*gauss(height,.43,.19)
  +jerseyTailoring(height,angle);
}
function jerseyGeometry(){
 const geometry=loft(shirtProfile,32,4,jerseyRelief),positions=geometry.getAttribute('position');
 for(let i=0;i<positions.count;i++){
  const angle=(i%33)/32*Math.PI*2,y=positions.getY(i),front=Math.max(0,Math.cos(angle));
  // Cut the actual neckline to the collar. A painted trim over a closed shell
  // otherwise leaves a white bib up to the throat when viewed face-on.
  const cut=(.008+.057*Math.pow(front,6))*THREE.MathUtils.smoothstep(y,.63,.731),height=y-cut;
  const [,width,depth,z=0]=profileAt(shirtProfile,height),relief=jerseyRelief(height,angle);
  positions.setXYZ(i,Math.sin(angle)*(width+relief),height,z+Math.cos(angle)*(depth+relief));
 }
 geometry.computeVertexNormals();restoreSeamNormals(geometry);return geometry;
}
function fittedJerseySeams(torso:THREE.Object3D,paths:number[][][],mat:THREE.Material,radius:number){
 const pieces=paths.map(path=>{
  const geometry=tube(path,radius),positions=geometry.getAttribute('position');
  // Translate each existing tube ring onto the cloth, retaining its round
  // cross-section, vertex heights and therefore its exact garment skin weights.
  for(let start=0;start<positions.count;start+=6){
   let x=0,y=0,z=0;for(let i=0;i<5;i++){x+=positions.getX(start+i)/5;y+=positions.getY(start+i)/5;z+=positions.getZ(start+i)/5;}
   const [,width,depth,forward=0]=profileAt(shirtProfile,y),angle=Math.atan2(x/width,(z-forward)/depth);
   const relief=jerseyRelief(y,angle)+radius+.001;
   const dx=Math.sin(angle)*(width+relief)-x,dz=forward+Math.cos(angle)*(depth+relief)-z;
   for(let i=0;i<6;i++)positions.setXYZ(start+i,positions.getX(start+i)+dx,positions.getY(start+i),positions.getZ(start+i)+dz);
  }
  geometry.computeVertexNormals();restoreSeamNormals(geometry);return geometry;
 });
 return add(joined(pieces),mat,torso);
}
function jerseyDetails(torso:THREE.Object3D,jersey:THREE.Material,pants:THREE.Material,belt:THREE.Material,accent:THREE.Material,button:THREE.Material){
 // The narrow, flatter belt sits at the waist instead of a circular waist ring.
 add(loft([[.027,.185,.133,.008],[.032,.192,.138,.008],[.06,.193,.139,.008],[.066,.183,.131,.008]],28,1),belt,torso);
 const loops=[-.145,-.083,.083,.145].map(x=>new THREE.BoxGeometry(.011,.045,.011).translate(x,.046,.008+.137*Math.sqrt(1-(x/.193)**2)));
 add(joined(loops),pants,torso);
 add(new THREE.BoxGeometry(.033,.027,.009),button,torso,0,.047,.153);
 const paths:number[][][]=[];
 for(const side of [-1,1]){
  paths.push([[side*.156,.558,-.107],[side*.201,.546,-.087],[side*.228,.506,-.035]]);
 }
 fittedJerseySeams(torso,paths,accent,.0028);
 const collarPositions:number[]=[],collarUV:number[]=[],collarIndices:number[]=[],segments=40;
 for(let row=0;row<2;row++)for(let i=0;i<=segments;i++){
  const angle=i/segments*Math.PI*2,y=.723-.057*Math.pow(Math.max(0,Math.cos(angle)),6)+(row-.5)*.009;
  const [,w,d,z=0]=profileAt(shirtProfile,y),relief=jerseyRelief(y,angle)+.0018;
  collarPositions.push(Math.sin(angle)*(w+relief),y,z+Math.cos(angle)*(d+relief));collarUV.push(i/segments,row);
  if(row===0&&i<segments){const a=i,b=i+1,c=b+segments+1,d=a+segments+1;collarIndices.push(a,b,d,b,c,d);}
 }
 const collar=new THREE.BufferGeometry();collar.setAttribute("position",new THREE.Float32BufferAttribute(collarPositions,3));
 collar.setAttribute("uv",new THREE.Float32BufferAttribute(collarUV,2));collar.setIndex(collarIndices);collar.computeVertexNormals();add(collar,accent,torso);
 // A sewn, overlapping fabric placket has thickness and two stitched edges.
 // It is part of the same weighted garment so the buttons never float off it.
 const placketPositions:number[]=[],placketUV:number[]=[],placketIndices:number[]=[],placketRows=30;
 const frontEdges:number[][][]=[[],[]];
 for(let row=0;row<=placketRows;row++){
  const y=THREE.MathUtils.lerp(.087,.67,row/placketRows),[,width,depth,z=0]=profileAt(shirtProfile,y);
  for(let col=0;col<3;col++){
   const x=-.012+col*.015,angle=Math.asin(x/width),pz=z+Math.cos(angle)*(depth+jerseyRelief(y,angle))+.0026;
   placketPositions.push(x,y,pz+(col===1?.0007:0));placketUV.push(col/2,row/placketRows);
   if(col!==1)frontEdges[col/2].push([x,y,pz+.0006]);
   if(row<placketRows&&col<2){const a=row*3+col,b=a+1,c=b+3,d=a+3;placketIndices.push(a,b,d,b,c,d);}
  }
 }
 const placket=new THREE.BufferGeometry();placket.setAttribute('position',new THREE.Float32BufferAttribute(placketPositions,3));placket.setAttribute('uv',new THREE.Float32BufferAttribute(placketUV,2));placket.setIndex(placketIndices);placket.computeVertexNormals();
 add(placket,jersey,torso).name='Sewn jersey placket';
 const stitch=(jersey as SurfaceMaterial).clone();stitch.color.multiplyScalar(.85);stitch.userData.playerSurface='jerseyStitch';
 seams(torso,frontEdges,stitch,.0008).name='Placket edge stitching';
 const buttons=[.138,.244,.35,.456,.56,.64].map(y=>{
  const [, ,depth,z=0]=profileAt(shirtProfile,y);
  return new THREE.CylinderGeometry(.0043,.0043,.0022,10).rotateX(Math.PI/2).translate(.006,y,z+depth+jerseyRelief(y,0)+.0056);
 });
 add(joined(buttons),button,torso);
 const stitchPaths:number[][][]=[];
 for(const sign of [-1,1]){
  stitchPaths.push([[sign*.08,.707,-.068],[sign*.142,.675,-.094],[sign*.204,.609,-.105]]);
  stitchPaths.push([[sign*.171,.09,-.075],[sign*.198,.24,-.055],[sign*.215,.42,-.045]]);
 }
 fittedJerseySeams(torso,stitchPaths,stitch,.0011).name='Jersey panel stitching';
 const backYoke:number[][]=[];for(let i=0;i<=20;i++){
  const angle=(i/20-.5)*1.9+Math.PI,y=.584-Math.cos(angle-Math.PI)*.018;
  const [,w,d,z=0]=profileAt(shirtProfile,y),relief=jerseyRelief(y,angle)+.002;
  backYoke.push([Math.sin(angle)*(w+relief),y,z+Math.cos(angle)*(d+relief)]);
 }
 seams(torso,[backYoke],stitch,.0014).name='Back yoke stitching';
}

function cuff(parent:THREE.Object3D,at:number,width:number,depth:number,mat:THREE.Material){
 return add(loft([[at-.012,width,depth],[at+.012,width,depth]],20,1),mat,parent);
}

function arm(parent:THREE.Object3D,side:number,jersey:THREE.Material,skin:THREE.Material,accent:THREE.Material){
 const upper=object(parent,side*.27,.58),lower=object(upper,0,-.34);
 const anatomy=loft([[-.681,.024,.026,.006],[-.656,.032,.032,.006],[-.603,.038,.036,.008],
  [-.51,.053,.047,.004],[-.44,.061,.055,-.002],[-.38,.051,.05,.0],[-.341,.047,.049,.005],
  [-.295,.057,.059,.002],[-.204,.07,.068,-.005],[-.11,.075,.068,-.004],
  [.008,.065,.061],[.055,.025,.025]],24,3,(y,a)=>
   .003*Math.sin(a*2+.5)*gauss(y,-.16,.11)+.0025*Math.cos(a*3)*gauss(y,-.46,.09));
 const positions=anatomy.getAttribute("position"),indices:number[]=[],weights:number[]=[],colors:number[]=[];
 for(let i=0;i<positions.count;i++){
  const y=positions.getY(i),blend=THREE.MathUtils.smoothstep(y,-.4,-.275);
  indices.push(0,1,0,0);weights.push(blend,1-blend,0,0);
  const elbow=gauss(y,-.34,.065),warm=gauss(y,-.22,.12);
  colors.push(1-elbow*.026,.99-elbow*.038-warm*.01,.975-elbow*.037-warm*.016);
 }
 anatomy.setAttribute("skinIndex",new THREE.Uint16BufferAttribute(indices,4));anatomy.setAttribute("skinWeight",new THREE.Float32BufferAttribute(weights,4));
 anatomy.setAttribute("color",new THREE.Float32BufferAttribute(colors,3));
 const armSkin=skin.clone() as SurfaceMaterial;armSkin.vertexColors=true;
 const bones=[upper,lower].map((pivot,i)=>{const bone=new THREE.Bone();bone.name=`${side>0?"Left":"Right"} ${i?"elbow":"shoulder"}`;pivot.add(bone);return bone;});
 upper.updateWorldMatrix(true,true);
 const body=new THREE.SkinnedMesh(anatomy,armSkin);body.name="Continuous anatomical arm";body.castShadow=body.receiveShadow=true;body.frustumCulled=false;
 upper.add(body);body.bind(new THREE.Skeleton(bones));
 const sleeveProfile:Profile[]=[[-.195,.08,.076,-.004],[-.174,.083,.078,-.003],[-.085,.091,.08,-.005],[.004,.087,.076,-.005],[.052,.055,.052],[.07,.011,.014]];
 const sleeveRelief=(y:number,a:number)=>{
  const interior=THREE.MathUtils.smoothstep(y,-.174,-.145)*(1-THREE.MathUtils.smoothstep(y,.005,.04));
  const tension=.0025*(gauss(y,-.105+.024*Math.cos(a),.025)-.7*gauss(y,-.074+.024*Math.cos(a),.025));
  return .003*gauss(y,-.117,.08)*Math.sin(a*5+y*20)+interior*tension;
 };
 add(loft(sleeveProfile,24,2,sleeveRelief),jersey,upper).name='Fitted jersey sleeve';
 const hem=[-.195,-.188,-.181].map(y=>{const [,w,d,z=0]=profileAt(sleeveProfile,y);return [y,w+.0012,d+.0012,z] as Profile;});
 add(loft(hem,24,1,sleeveRelief),accent,upper).name='Sewn sleeve cuff';
 const stitches:number[][]=[],[,sw,sd,sz=0]=profileAt(sleeveProfile,-.177);
 for(let i=0;i<=28;i++){const a=i/28*Math.PI*2,r=sleeveRelief(-.177,a)+.0009;stitches.push([Math.sin(a)*(sw+r),-.177,sz+Math.cos(a)*(sd+r)]);}
 seams(upper,[stitches],jersey,.00065);
 return {upper,lower};
}

function leg(root:THREE.Object3D,side:number,accent:THREE.Material,dark:THREE.Material,sole:THREE.Material){
 const thigh=object(root,side*.115,.91),knee=object(thigh,0,-.43);
 const ankle=object(knee,0,-.43),shoeOrigin=object(ankle,0,.43);ankle.name=side>0?"Left ankle":"Right ankle";
 shoe(shoeOrigin,dark,accent,sole);return {thigh,knee,ankle};
}

function trousers(root:THREE.Group,hips:THREE.Group,left:{thigh:THREE.Group;knee:THREE.Group},right:{thigh:THREE.Group;knee:THREE.Group},mat:SurfaceMaterial,piping:THREE.Color){
 let positions:number[]=[],uvs:number[]=[],indices:number[]=[],boneIndices:number[]=[],weights:number[]=[];
 const count=32;
 const vertex=(x:number,height:number,z:number,u:number,side:number)=>{
  const index=positions.length/3;positions.push(x,height,z);uvs.push(u,(height-.08)/.93);
  const hip=THREE.MathUtils.smoothstep(height,.79,.995);
  const knee=1-THREE.MathUtils.smoothstep(height,.427,.555);
  const leftWeight=side===0?THREE.MathUtils.smoothstep(x,-.065,.065):side>0?1:0;
  const influences=[
   [0,hip],[1,(1-hip)*(1-knee)*leftWeight],[2,(1-hip)*(1-knee)*(1-leftWeight)],
   [3,(1-hip)*knee*leftWeight],[4,(1-hip)*knee*(1-leftWeight)]
  ].filter(([,weight])=>weight>0).sort((a,b)=>b[1]-a[1]).slice(0,4);
  while(influences.length<4)influences.push([0,0]);
  const sum=influences.reduce((total,[,weight])=>total+weight,0);
  boneIndices.push(...influences.map(([bone])=>bone));weights.push(...influences.map(([,weight])=>weight/sum));
  return index;
 };
 const ring=(height:number,width:number,depth:number,centre=0,forward=0,side=0)=>{
  const vertices:number[]=[];
  for(let i=0;i<count;i++){
   const angle=i/count*Math.PI*2;
   const fold=.0025*gauss(height,.54,.085)*Math.sin(angle*5+height*43)
    +.0028*gauss(height,.125,.05)*Math.sin(angle*6-height*62)
    +.0014*gauss(height,.87,.08)*Math.sin(angle*5+height*25);
   const x=centre+Math.sin(angle)*(width+fold),z=forward+Math.cos(angle)*(depth+fold);
   vertices.push(vertex(x,height,z,i/count,side));
  }
  return vertices;
 };
 const connect=(upper:number[],lower:number[],start=0,end=count)=>{
  for(let i=start;i<end;i++){
   const next=(i+1)%count,a=upper[i],b=upper[next],c=lower[next],d=lower[i];indices.push(a,d,b,b,d,c);
  }
 };
 let upper=ring(1.015,.182,.124,0,.007);
 for(const [y,width,depth,forward] of [[.982,.19,.13,.007],[.941,.198,.136,.003],[.897,.207,.137,-.003]]){
  const lower=ring(y,width,depth,0,forward);connect(upper,lower);upper=lower;
 }
 // Both leg openings share a curved inseam from the fly, under the crotch, to the seat.
 // Welding that saddle into the waist removes the horizontal bridge between separate thigh rings.
 const leftTop:number[]=[],rightTop:number[]=[];
 for(let i=0;i<=count/2;i++){
  const angle=i/count*Math.PI*2,x=Math.sin(angle)*.222;
  const height=.822+.054*Math.cos(angle)**2,z=-.002+Math.cos(angle)*.126;
  const index=vertex(x,height,z,i/count,i===0||i===count/2?0:1);leftTop[i]=index;
  rightTop[(count-i)%count]=i===0||i===count/2?index:vertex(-x,height,z,(count-i)/count,-1);
 }
 for(let i=count/2+1;i<count;i++){
  const angle=i/count*Math.PI*2;
  const index=vertex(0,.876+.082*Math.sin(angle),-.002+Math.cos(angle)*.126,i/count,0);
  leftTop[i]=index;rightTop[count-i]=index;
 }
 connect(upper,leftTop,0,count/2);connect(upper,rightTop,count/2,count);
 const sections=[
  [.754,.111,.105,.0],[.685,.109,.103,.0],[.604,.1,.095,.003],
  [.553,.091,.087,.007],[.513,.085,.081,.011],[.478,.081,.079,.01],
  [.44,.08,.075,.005],[.389,.083,.08,-.006],[.326,.082,.079,-.013],
  [.251,.073,.068,-.007],[.177,.062,.06,.0],[.116,.057,.057,.003],[.083,.057,.058,.004]
 ];
 for(const [side,top] of [[1,leftTop],[-1,rightTop]] as const){
  let previous:number[]=[...top];
  for(let section=0;section<sections.length;section++){
   const [height,width,depth,forward]=sections[section];
   const next=ring(height,width,depth,side*.115,forward,side);connect(previous,next);previous=next;
  }
 }
 // One Loop subdivision rounds the branching surface and carries the existing side-specific
 // skin weights with it. The only boundary edges are the waist and the two trouser cuffs.
 const neighbours=Array.from({length:positions.length/3},()=>new Set<number>());
 const edges=new Map<string,{a:number;b:number;opposites:number[];index:number}>();
 const edgeKey=(a:number,b:number)=>a<b?`${a}:${b}`:`${b}:${a}`;
 for(let i=0;i<indices.length;i+=3){
  const face=indices.slice(i,i+3);
  for(let j=0;j<3;j++){
   const a=face[j],b=face[(j+1)%3],opposite=face[(j+2)%3],key=edgeKey(a,b);
   neighbours[a].add(b);neighbours[b].add(a);
   if(!edges.has(key))edges.set(key,{a,b,opposites:[],index:0});
   edges.get(key)!.opposites.push(opposite);
  }
 }
 const boundaries=Array.from({length:neighbours.length},()=>[] as number[]);
 for(const edge of edges.values())if(edge.opposites.length===1){boundaries[edge.a].push(edge.b);boundaries[edge.b].push(edge.a);}
 const smoothPositions:number[]=[],smoothUvs:number[]=[],smoothBones:number[]=[],smoothWeights:number[]=[];
 const blend=(vertices:number[],factors:number[])=>{
  const index=smoothPositions.length/3,point=[0,0,0],uv=[0,0],skin=[0,0,0,0,0];
  for(let i=0;i<vertices.length;i++){
   const at=vertices[i],factor=factors[i];
   for(let k=0;k<3;k++)point[k]+=positions[at*3+k]*factor;
   for(let k=0;k<2;k++)uv[k]+=uvs[at*2+k]*factor;
   for(let k=0;k<4;k++)skin[boneIndices[at*4+k]]+=weights[at*4+k]*factor;
  }
  const influences=skin.map((weight,bone)=>[bone,weight]).filter(([,weight])=>weight>0).sort((a,b)=>b[1]-a[1]).slice(0,4);
  while(influences.length<4)influences.push([0,0]);
  const sum=influences.reduce((total,[,weight])=>total+weight,0);
  smoothPositions.push(...point);smoothUvs.push(...uv);
  smoothBones.push(...influences.map(([bone])=>bone));smoothWeights.push(...influences.map(([,weight])=>weight/sum));return index;
 };
 for(let i=0;i<neighbours.length;i++){
  const boundary=boundaries[i];
  if(boundary.length===2)blend([i,...boundary],[.75,.125,.125]);
  else{
   const adjacent=[...neighbours[i]],n=adjacent.length,beta=(.625-(.375+.25*Math.cos(2*Math.PI/n))**2)/n;
   blend([i,...adjacent],[1-n*beta,...adjacent.map(()=>beta)]);
  }
 }
 for(const edge of edges.values())edge.index=edge.opposites.length===2
  ?blend([edge.a,edge.b,...edge.opposites],[.375,.375,.125,.125]):blend([edge.a,edge.b],[.5,.5]);
 const smoothIndices:number[]=[];
 for(let i=0;i<indices.length;i+=3){
  const [a,b,c]=indices.slice(i,i+3),ab=edges.get(edgeKey(a,b))!.index,bc=edges.get(edgeKey(b,c))!.index,ca=edges.get(edgeKey(c,a))!.index;
  smoothIndices.push(a,ab,ca,b,bc,ab,c,ca,bc,ab,bc,ca);
 }
 positions=smoothPositions;uvs=smoothUvs;boneIndices=smoothBones;weights=smoothWeights;indices=smoothIndices;
 // Form shallow knee and ankle creases after subdivision. Only the existing
 // rest vertices move: topology, skin weights, waist and open hems are stable.
 const clothColors:number[]=[];
 for(let i=0;i<positions.length;i+=3){
  const x=positions[i],height=positions[i+1],z=positions[i+2],side=x>=0?1:-1;
  const angle=Math.atan2(x-side*.115,z),front=Math.max(0,Math.cos(angle)),back=Math.max(0,-Math.cos(angle));
  const interior=THREE.MathUtils.smoothstep(height,.116,.145)*(1-THREE.MathUtils.smoothstep(height,.74,.79));
  const knee=front*(.0028*gauss(height,.534-.018*Math.sin(angle),.043)-.003*gauss(height,.482-.015*Math.sin(angle),.025))
   -.0028*back*gauss(height,.484,.027);
  const ankle=(.0028*gauss(height,.157+.009*Math.sin(angle),.024)-.0018*gauss(height,.19+.009*Math.sin(angle),.019))*(.45+.55*Math.abs(Math.sin(angle)));
  const thigh=.0022*front*(gauss(height,.711-.028*Math.sin(angle),.038)-.65*gauss(height,.666-.028*Math.sin(angle),.031));
  const fold=interior*(knee+ankle+thigh);
  positions[i]+=Math.sin(angle)*fold;positions[i+2]+=Math.cos(angle)*fold;
  const inseam=Math.max(0,-side*Math.sin(angle));
  const shade=1-.055*inseam*inseam*gauss(height,.67,.19)-.055*Math.max(0,-fold)/.0032-.035*gauss(height,.974,.03);
  clothColors.push(shade,shade,shade);
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));
 geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uvs,2));geometry.setAttribute("skinIndex",new THREE.Uint16BufferAttribute(boneIndices,4));
 geometry.setAttribute("color",new THREE.Float32BufferAttribute(clothColors,3));
 geometry.setAttribute("skinWeight",new THREE.Float32BufferAttribute(weights,4));geometry.setIndex(indices);geometry.computeVertexNormals();
 const bones=[hips,left.thigh,right.thigh,left.knee,right.knee].map((pivot,index)=>{
  const bone=new THREE.Bone();bone.name=["Pants waist","Left thigh","Right thigh","Left knee","Right knee"][index];pivot.add(bone);return bone;
 });
 root.updateMatrixWorld(true);
 const tailored=mat.clone();tailored.vertexColors=true;
 // Sew the colored piping into the same cloth surface. Separate rigid tubes
 // drift away from the hip and shin when their neighbouring skin weights bend.
 tailored.onBeforeCompile=shader=>{
  shader.uniforms.uniformPiping={value:piping};
  shader.vertexShader='varying vec2 tailoredUV;\nvarying float tailoredSide;\n'+shader.vertexShader.replace('#include <begin_vertex>','#include <begin_vertex>\ntailoredUV=uv;tailoredSide=position.x;');
  shader.fragmentShader='uniform vec3 uniformPiping;\nvarying vec2 tailoredUV;\nvarying float tailoredSide;\n'+shader.fragmentShader.replace('#include <color_fragment>',`#include <color_fragment>
float seamDistance=abs(tailoredUV.x-(tailoredSide>=0.0?0.25:0.75));
float seamAA=max(fwidth(seamDistance),0.001);
float seamCoverage=1.0-smoothstep(0.005-seamAA,0.005+seamAA,seamDistance);
seamCoverage*=smoothstep(0.14,0.155,abs(tailoredSide));
diffuseColor.rgb=mix(diffuseColor.rgb,uniformPiping,seamCoverage);`);
 };
 tailored.customProgramCacheKey=()=> 'tailored-trouser-piping-v1';
 const result=new THREE.SkinnedMesh(geometry,tailored);result.name="Continuous tailored trousers";
 result.castShadow=true;result.receiveShadow=true;root.add(result);result.bind(new THREE.Skeleton(bones));
 // Its bones remain attached to the existing animation pivots, including mirrored left-handed rigs.
 result.frustumCulled=false;return result;
}

function uniformTexture(appearance:PlayerAppearance,area:"front"|"back"|"cap"){
 if(typeof document==="undefined")return null;
 const canvas=document.createElement("canvas");canvas.width=512;canvas.height=512;
 const context=canvas.getContext("2d");if(!context)return null;
 let letter=appearance.letter;
 if(area==="cap"){
  // Shirt lettering may be dark on white uniforms; cap printing needs its own contrast.
  const luminance=(hex:string)=>{const c=new THREE.Color(hex);return c.r*.2126+c.g*.7152+c.b*.0722;};
  const background=luminance(appearance.cap),contrast=(hex:string)=>{const foreground=luminance(hex);return (Math.max(background,foreground)+.05)/(Math.min(background,foreground)+.05);};
  if(contrast(letter)<3)letter=contrast("#f5f2e8")>=3?"#f5f2e8":"#172535";
 }
 context.textAlign="center";context.textBaseline="middle";context.fillStyle=letter;
 context.lineJoin="round";context.strokeStyle=appearance.cap;context.lineWidth=3;
 const text=(value:string,x:number,y:number,width:number)=>{context.strokeText(value,x,y,width);context.fillText(value,x,y,width);};
 if(area==="front"){
  context.font="italic 800 160px Arial, sans-serif";text(appearance.wordmark,256,256,462);
 }else if(area==="back"){
  context.font="700 76px 'Malgun Gothic', sans-serif";text(appearance.name,256,90,449);
  if(appearance.number){context.font="900 274px Arial, sans-serif";text(appearance.number,256,311,410);}
 }else{context.font="italic 900 183px Arial, sans-serif";text(appearance.team,256,271,432);}
 const texture=new THREE.CanvasTexture(canvas);texture.colorSpace=THREE.SRGBColorSpace;texture.anisotropy=4;return texture;
}

function decalGeometry(area:"front"|"back"|"cap"){
 const centre=area==="cap"?.146:area==="back"?.437:.475;
 const height=area==="cap"?.065:area==="back"?.36:.142;
 const angle=area==="cap"?1.18:2.05,columns=24,rows=12,positions:number[]=[],uvs:number[]=[],indices:number[]=[];
 for(let row=0;row<=rows;row++)for(let col=0;col<=columns;col++){
  const y=centre+(row/rows-.5)*height,theta=(col/columns-.5)*angle;
  let x:number,z:number;
  if(area==="cap"){
   const [,width,depth,forward=0]=profileAt(capProfile,y);
   x=Math.sin(theta)*(width+.0017);z=forward+Math.cos(theta)*(depth+.0017);
  }else{
   const [,width,depth,forward=0]=profileAt(shirtProfile,y),surfaceAngle=theta+(area==="back"?Math.PI:0),relief=jerseyRelief(y,surfaceAngle)+.0022;
   x=Math.sin(theta)*(width+relief);z=Math.cos(theta)*(depth+relief)+(area==="back"?-forward:forward);
  }
  positions.push(x,y-centre,z);uvs.push(col/columns,row/rows);
  if(row<rows&&col<columns){const a=row*(columns+1)+col,b=a+1,c=b+columns+1,d=a+columns+1;indices.push(a,b,d,b,c,d);}
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uvs,2));geometry.setIndex(indices);geometry.computeVertexNormals();return {geometry,centre};
}

export function createPlayer(color:string,isBatter=false):PlayerModel{
 const root=new THREE.Group();root.name=isBatter?"Athletic batter":"Athletic pitcher";
 const cloth=createPlayerSurface("cloth"),clothAlbedo=createPlayerAlbedo("cloth"),skinTexture=createPlayerSurface("skin"),leather=createPlayerSurface("leather");
 const clothRough=createPlayerRoughness("cloth"),skinRough=createPlayerRoughness("skin"),leatherRough=createPlayerRoughness("leather");
 const jersey=new THREE.MeshPhysicalMaterial({color,map:clothAlbedo,roughness:.92,roughnessMap:clothRough,bumpMap:cloth,bumpScale:.0015,sheen:.42,sheenRoughness:.9,sheenColor:"#a7acb2"});
 const pants=new THREE.MeshPhysicalMaterial({color:"#e5e3dc",map:clothAlbedo,roughness:.96,roughnessMap:clothRough,bumpMap:cloth,bumpScale:.0014,sheen:.23,sheenRoughness:.94});
 const skin=new THREE.MeshPhysicalMaterial({color:"#c89675",roughness:.72,roughnessMap:skinRough,clearcoat:.07,clearcoatRoughness:.52,bumpMap:skinTexture,bumpScale:.0006});skin.userData.playerSurface="skin";
 const dark=material("#172027",.72,leather,.0025),white=material("#dfdfd5",.86),accent=material("#dedccf",.9,cloth,.002);
 accent.map=clothAlbedo;
 dark.roughnessMap=leatherRough;dark.userData.playerSurface="cleats";
 const cap=new THREE.MeshPhysicalMaterial({color,map:isBatter?null:clothAlbedo,roughness:isBatter?.26:.87,clearcoat:isBatter?.66:0,clearcoatRoughness:.2,bumpMap:isBatter?null:cloth,bumpScale:.003});
 const hips=object(root,0,.93),torso=object(root,0,.95),head=object(torso,0,.82);
 add(jerseyGeometry(),jersey,torso).name="Flexible tailored jersey";
 jerseyDetails(torso,jersey,pants,dark,accent,white);
 const uniformPanels=torso.children.filter((child):child is THREE.Mesh=>child instanceof THREE.Mesh);
 const undershirt=material('#25313a',.96,cloth,.001);undershirt.userData.playerSurface='undershirt';
 undershirt.map=clothAlbedo;
 add(loft([[.662,.113,.091,-.011],[.68,.099,.083,-.011],[.699,.089,.078,-.012],[.713,.084,.075,-.012]],28,2),undershirt,torso).name='Fitted undershirt neckline';
 add(loft([[.657,.108,.09,-.01],[.693,.086,.074,-.011],[.738,.072,.064,-.012],[.788,.063,.057,-.009],[.821,.05,.052,.003]],24,3,(y,a)=>{
  const front=Math.max(0,Math.cos(a));
  return front*(.0038*gauss(y,.755,.035)*gauss(Math.abs(Math.sin(a)),.58,.22)-.0025*gauss(y,.713,.018)*gauss(Math.sin(a),0,.3));
 }),skin,torso).name='Neck and collarbone transition';
 headDetails(head,skin,cap,cloth,isBatter);
 const leftArm=arm(torso,1,jersey,skin,accent),rightArm=arm(torso,-1,jersey,skin,accent);
 const left=leftArm.upper,right=rightArm.upper,le=leftArm.lower,re=rightArm.lower;
 for(const lower of [le,re])cuff(lower,-.303,.0355,.0345,isBatter?accent:dark);
 if(isBatter){
  const elbow=loft([[-.098,.042,.025,.033],[-.062,.058,.038,.024],[-.019,.054,.034,.025],[.013,.025,.018,.018]],16,2);
  add(elbow,accent,le);cuff(le,-.061,.06,.054,dark);
 }else{
  const glove=mitt(le,1,leather);
  // Seat the wrist in the cuff, with the fingers extending away from the elbow.
  // The mitt keeps its own coordinates; animation still targets the same wrist.
  glove.rotation.z=Math.PI;glove.position.set(0,-.459,.042);
  const throwingHand=hand(re,skin);throwingHand.position.set(0,-.303,-.012);
 }
 const leftLeg=leg(root,1,accent,dark,white),rightLeg=leg(root,-1,accent,dark,white);
 trousers(root,hips,leftLeg,rightLeg,pants,accent.color);
 if(isBatter){
  // A fitted lead-leg guard follows the shin. Mirroring the whole rig keeps it on the lead side.
  const guard=new THREE.MeshPhysicalMaterial({color:"#27313b",roughness:.6,clearcoat:.14});
  guard.userData.playerSurface="equipment";
  add(loft([[-.344,.029,.017,.058],[-.302,.054,.022,.061],[-.174,.071,.025,.072],[-.084,.058,.02,.081],[-.067,.021,.01,.074]],20,3),guard,leftLeg.knee);
  for(const y of [-.108,-.296])cuff(leftLeg.knee,y,y>-.2?.084:.071,y>-.2?.087:.07,dark);
  seams(leftLeg.knee,[[[-.038,-.304,.087],[-.045,-.179,.102],[-.035,-.096,.104]],[[.038,-.304,.087],[.045,-.179,.102],[.035,-.096,.104]]],accent,.0025);
 }
 const lettering:PlayerModel['lettering']=(["front","back","cap"] as const).map(area=>{
  const material=new THREE.MeshStandardMaterial({transparent:true,depthWrite:false,roughness:.86,polygonOffset:true,polygonOffsetFactor:-1,alphaTest:.02});
  const {geometry,centre}=decalGeometry(area),decal=add(geometry,material,area==="cap"?head:torso,0,centre);
  if(area==="back")decal.rotation.y=Math.PI;
  decal.castShadow=false;decal.visible=false;return {area,decal,material};
 });
 const clothLettering=lettering.filter(slot=>slot.area!=="cap");
 const bound=bindUniform(root,hips,torso,[...uniformPanels,...clothLettering.map(slot=>slot.decal)]);
 clothLettering.forEach((slot,index)=>{slot.decal=bound[uniformPanels.length+index];});
 // Low-frequency cloth occlusion remains visible at playing distance. Sharing
 // this attribute across every jersey surface keeps the static batches intact.
 jersey.vertexColors=true;
 root.traverse(object=>{
  if(!(object instanceof THREE.Mesh)||object.material!==jersey)return;
  const geometry=object.geometry,positions=geometry.getAttribute('position'),colors:number[]=[];
  const sleeve=object.parent===left||object.parent===right;
  for(let i=0;i<positions.count;i++){
   const x=positions.getX(i),y=positions.getY(i)+object.position.y,z=positions.getZ(i),angle=Math.atan2(x,z);
   const shade=sleeve
    ?1-.06*gauss(y,-.08,.085)*Math.pow(Math.abs(Math.sin(angle)),2)-.035*gauss(y,-.176,.013)
    :1-.065*Math.pow(Math.abs(Math.sin(angle)),4)*gauss(y,.438,.09)-.052*Math.max(0,-jerseyTailoring(y,angle))/.0038-.026*gauss(y,.078,.023);
   colors.push(shade,shade,shade);
  }
  geometry.setAttribute('color',new THREE.Float32BufferAttribute(colors,3));
 });
 const detailMeshes:THREE.Mesh[]=[];
 root.traverse(o=>{if(!(o instanceof THREE.Mesh)||isPlayerBlinkMesh(o)||(o instanceof THREE.SkinnedMesh&&!o.userData.uniformPanel))return;o.geometry.computeBoundingBox();const size=o.geometry.boundingBox!.getSize(v());if(Math.max(size.x,size.y,size.z)<.085&&!lettering.some(slot=>slot.decal===o))detailMeshes.push(o);});
 batchStaticPlayerMeshes(root,detailMeshes,lettering.map(slot=>slot.decal));
 return {root,hips,torso,head,left,right,le,re,ll:leftLeg.thigh,rl:rightLeg.thigh,lk:leftLeg.knee,rk:rightLeg.knee,lf:leftLeg.ankle,rf:rightLeg.ankle,jersey,cap,accent,lettering,appearanceKey:"",appearance:normalizePlayerCustomization(),detailMeshes};
}

const pitchGrips=new WeakMap<PlayerModel,PitchGrip>();
export function getPitchGrip(model:PlayerModel){return pitchGrips.get(model);}

/** Only the featured pitcher opts into this grip; ordinary fielders keep their hands. */
export function equipPitchGrip(model:PlayerModel){
 const existing=getPitchGrip(model);if(existing)return existing;
 const old=model.re.children.find(child=>{let found=false;child.traverse(object=>{if(object instanceof THREE.Mesh&&(object.name==='Anatomical hand palm'||object.userData.playerBatchParts?.some((part:{name:string})=>part.name==='Anatomical hand palm')))found=true;});return found;});
 if(!old)throw new Error('Pitch grip requires an ordinary throwing hand');
 const meshes:THREE.Mesh[]=[];old.traverse(object=>{if(object instanceof THREE.Mesh)meshes.push(object);});
 const grip=createPitchGrip(model.re,meshes[0].material as THREE.Material,model.appearance);
 old.removeFromParent();new Set(meshes.map(mesh=>mesh.geometry)).forEach(geometry=>geometry.dispose());
 model.detailMeshes=model.detailMeshes.filter(mesh=>!meshes.includes(mesh));pitchGrips.set(model,grip);return grip;
}

export function dressPlayer(model:PlayerModel,appearance:PlayerAppearance,handedness=1){
 if(appearance.jerseyNumber&&/^\d{1,2}$/.test(appearance.jerseyNumber))appearance={...appearance,number:appearance.jerseyNumber};
 const key=JSON.stringify(appearance);
 if(key!==model.appearanceKey){
  model.appearanceKey=key;model.jersey.color.set(appearance.jersey);model.cap.color.set(appearance.cap);model.accent.color.set(appearance.accent);
  const look=normalizePlayerCustomization(appearance),previousLook=model.appearance;model.appearance=look;
  const colors:Record<string,string>={skin:look.skinTone,hair:look.hairColor,...mittPalette(look.gloveColor),cleats:look.cleatColor,equipment:look.equipmentColor,jerseyStitch:appearance.jersey,undershirt:appearance.cap};
  model.root.traverse(o=>{const mesh=o as THREE.Mesh;if(!mesh.isMesh)return;
   for(const material of Array.isArray(mesh.material)?mesh.material:[mesh.material]){const surface=material as SurfaceMaterial,color=colors[material.userData.playerSurface];if(color&&surface.color)surface.color.set(color).multiplyScalar(material.userData.playerSurface==='jerseyStitch'?.85:material.userData.skinToneFactor??1);}
   if(mesh.name==="Custom hair"){mesh.visible=look.hairStyle!=="bald";if(previousLook.hairStyle!==look.hairStyle){const old=mesh.geometry;mesh.geometry=hairGeometry(look.hairStyle);old.dispose();}}
  });
  if(previousLook.bodyType!==look.bodyType){
   const dims=playerDimensions(look);
   // Shape the rest vertices, not the animation pivots or bone lengths. Each mesh
   // retains its originals so successive edits do not accumulate scale errors.
   model.root.traverse(o=>{const mesh=o as THREE.Mesh;if(!mesh.isMesh||mesh.name==="Custom hair"||isPitchGripBallMesh(mesh)||isPlayerBlinkMesh(mesh))return;
    const belongsToHead=(()=>{let p:THREE.Object3D|null=mesh;while(p&&p!==model.root){if(p===model.head)return true;p=p.parent;}return false;})();
    const p=mesh.geometry.getAttribute("position");if(!p)return;
    const data=mesh.geometry.userData,original=data.restShape??(data.restShape=Float32Array.from(p.array));
    const sx=belongsToHead?1+(dims.widthScale-1)*.18:dims.widthScale,sz=belongsToHead?1+(dims.depthScale-1)*.12:dims.depthScale;
    for(let i=0;i<p.count;i++){const x=original[i*3],y=original[i*3+1],z=original[i*3+2];
     // The trouser legs thicken around their own femur axes, preserving stance.
     const centre=mesh instanceof THREE.SkinnedMesh&&mesh.skeleton.bones.length>2&&y<.79?(x>=0?.115:-.115):0;
     p.setXYZ(i,centre+(x-centre)*sx,y,z*sz);
    }p.needsUpdate=true;mesh.geometry.computeVertexNormals();restoreSeamNormals(mesh.geometry);mesh.geometry.computeBoundingSphere();
   });
   // The eyelids keep their own open-eye originals, even during a blink.
   fitPlayerBlink(model.head,1+(dims.widthScale-1)*.18,1+(dims.depthScale-1)*.12);
  }
  const pitchGrip=getPitchGrip(model);if(pitchGrip)fitPitchGrip(pitchGrip,look);
  for(const {area,decal,material} of model.lettering){
   const previous=material.map;material.map=uniformTexture(appearance,area);material.needsUpdate=true;decal.visible=!!material.map;previous?.dispose();
  }
 }
 // Mirroring the skeleton for a left-handed player must never mirror printed lettering.
 for(const {decal,material} of model.lettering){
  decal.scale.x=decal instanceof THREE.SkinnedMesh?1:handedness;
  // Skinning already places cloth in the mirrored bone frame. Reverse the print
  // in UV space, retaining a fitted wordmark during waist/chest separation.
  if(decal instanceof THREE.SkinnedMesh&&material.map){material.map.repeat.x=handedness;material.map.offset.x=handedness<0?1:0;}
 }
}

/** Keep full silhouettes but cull sub-pixel seams/features in wide broadcast views. */
export function setPlayerDetail(model:PlayerModel,distance:number,compact=false){
 const visible=distance<(compact?10:17);for(const mesh of model.detailMeshes)mesh.visible=visible;
}

/** Bat is shared by two IK hands, so color it without rebuilding its grip rig. */
export function dressBat(bat:THREE.Object3D,appearance:PlayerCustomization){
 const look=normalizePlayerCustomization(appearance);bat.traverse(o=>{if(!(o instanceof THREE.Mesh))return;for(const material of Array.isArray(o.material)?o.material:[o.material])if(material.userData.playerSurface==="bat")(material as SurfaceMaterial).color.set(look.batColor);});
}

export function equipCatcher(model:PlayerModel){ fitCatcherEquipment(model); }

const runnersWithHands=new WeakSet<PlayerModel>();
/** Batter hands belong to the bat rig. A separate running avatar needs its own
 * two gloves when that bat is no longer rendered. Keep the same arm pivots. */
export function equipRunnerHands(model:PlayerModel){
 if(runnersWithHands.has(model))return;runnersWithHands.add(model);
 const gloveMaterial=material('#e3e3d9',.8,model.jersey.bumpMap,.0013),dims=playerDimensions(model.appearance);
 gloveMaterial.userData.playerSurface='battingGlove';
 for(const [arm,sign] of [[model.le,-1],[model.re,1]] as const){
  const glove=hand(arm,gloveMaterial);glove.name=sign<0?'Runner left batting glove':'Runner right batting glove';
  // Turn each relaxed palm inward around the forearm, preserving the cuff's
  // centre instead of presenting both palms upward while running.
  glove.rotation.y=sign*Math.PI/2;glove.position.set(-sign*.019,-.303,.007);glove.scale.x=sign;
  add(loft([[0,.039,.036,.019],[.024,.040,.037,.019]],20,1),gloveMaterial,glove).name='Runner batting glove cuff';
  glove.traverse(object=>{
   if(!(object instanceof THREE.Mesh))return;
   const geometry=object.geometry,positions=geometry.getAttribute('position'),rest=Float32Array.from(positions.array);geometry.userData.restShape=rest;
   for(let i=0;i<positions.count;i++)positions.setXYZ(i,rest[i*3]*dims.widthScale,rest[i*3+1],rest[i*3+2]*dims.depthScale);
   geometry.computeVertexNormals();restoreSeamNormals(geometry);geometry.computeBoundingSphere();
  });
 }
 batchStaticPlayerMeshes(model.root,model.detailMeshes,model.lettering.map(slot=>slot.decal));
}
