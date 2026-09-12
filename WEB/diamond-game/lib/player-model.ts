import * as THREE from "three";
import {PlayerAppearance} from "./player-appearance";
import {createPlayerSurface} from "./player-surfaces";

const v=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
type Profile=[y:number,width:number,depth:number,forward?:number,side?:number];
type Relief=(height:number,angle:number)=>number;
type SurfaceMaterial=THREE.MeshStandardMaterial|THREE.MeshPhysicalMaterial;

/** Named pivots are the existing motion and collision rig, in metres. */
export type PlayerModel={
 root:THREE.Group;hips:THREE.Group;torso:THREE.Group;head:THREE.Group;
 left:THREE.Group;right:THREE.Group;le:THREE.Group;re:THREE.Group;
 ll:THREE.Group;rl:THREE.Group;lk:THREE.Group;rk:THREE.Group;
 jersey:SurfaceMaterial;cap:SurfaceMaterial;accent:SurfaceMaterial;
 lettering:{area:"front"|"back"|"cap";decal:THREE.Mesh;material:THREE.MeshStandardMaterial}[];
 appearanceKey:string;
};

function object(parent:THREE.Object3D,x=0,y=0,z=0){
 const result=new THREE.Group();result.position.set(x,y,z);parent.add(result);return result;
}
function add(geometry:THREE.BufferGeometry,material:THREE.Material,parent:THREE.Object3D,x=0,y=0,z=0){
 const result=new THREE.Mesh(geometry,material);result.position.set(x,y,z);
 result.castShadow=true;result.receiveShadow=true;parent.add(result);return result;
}
function material(color:string,roughness:number,bumpMap:THREE.Texture|null=null,bumpScale=0){
 return new THREE.MeshStandardMaterial({color,roughness,bumpMap,bumpScale});
}
function interpolate(a:number,b:number,c:number,d:number,t:number){
 return .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
}
function profileAt(profile:Profile[],height:number){
 let index=1;while(index<profile.length-1&&profile[index][0]<height)index++;
 const a=profile[Math.max(0,index-2)],b=profile[index-1],c=profile[index],d=profile[Math.min(profile.length-1,index+1)];
 const t=THREE.MathUtils.clamp((height-b[0])/(c[0]-b[0]),0,1);
 return [height,...[1,2,3,4].map(k=>interpolate(a[k]??0,b[k]??0,c[k]??0,d[k]??0,t))] as Profile;
}

/** Elliptical, individually shaped cross sections avoid a tube-shaped body. */
function loft(profile:Profile[],around=24,steps=3,relief?:Relief){
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
 return geometry;
}

function joined(geometries:THREE.BufferGeometry[]){
 const positions:number[]=[],normals:number[]=[],uvs:number[]=[],indices:number[]=[];
 for(const geometry of geometries){
  const position=geometry.getAttribute("position"),normal=geometry.getAttribute("normal"),uv=geometry.getAttribute("uv"),offset=positions.length/3;
  for(let i=0;i<position.count;i++){
   positions.push(position.getX(i),position.getY(i),position.getZ(i));
   normals.push(normal?.getX(i)??0,normal?.getY(i)??1,normal?.getZ(i)??0);uvs.push(uv?.getX(i)??0,uv?.getY(i)??0);
  }
  if(geometry.index)for(let i=0;i<geometry.index.count;i++)indices.push(offset+geometry.index.getX(i));
  else for(let i=0;i<position.count;i++)indices.push(offset+i);
  geometry.dispose();
 }
 const result=new THREE.BufferGeometry();result.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));
 result.setAttribute("normal",new THREE.Float32BufferAttribute(normals,3));result.setAttribute("uv",new THREE.Float32BufferAttribute(uvs,2));result.setIndex(indices);return result;
}
function oval(width:number,height:number,depth:number,x=0,y=0,z=0,segments=12){
 return new THREE.SphereGeometry(1,segments,8).scale(width,height,depth).translate(x,y,z);
}
function tube(points:number[][],radius=.002,segments=12){
 return new THREE.TubeGeometry(new THREE.CatmullRomCurve3(points.map(point=>v(...point))),segments,radius,5,false);
}
function seams(parent:THREE.Object3D,paths:number[][][],mat:THREE.Material,radius=.002){
 return add(joined(paths.map(path=>tube(path,radius))),mat,parent);
}
function gauss(value:number,centre:number,width:number){return Math.exp(-(((value-centre)/width)**2));}

const shirtProfile:Profile[]=[
 [.012,.171,.122,.008],[.047,.188,.131,.008],[.13,.197,.14,.008],
 [.27,.207,.15,.006],[.4,.225,.151,.002],[.51,.236,.146,-.001],
 [.605,.237,.134,-.007],[.651,.208,.115,-.009],[.706,.116,.077,-.01],[.731,.083,.068,-.009]
];
function jerseyRelief(height:number,angle:number){
 return .006*gauss(height,.13,.12)*Math.sin(angle*7+height*24)
  +.004*gauss(height,.34,.19)*Math.sin(angle*5-height*13)
  +.0045*gauss(height,.535,.075)*Math.sin(angle*6+height*22);
}
function jerseyDetails(torso:THREE.Object3D,pants:THREE.Material,belt:THREE.Material,accent:THREE.Material,button:THREE.Material){
 // The narrow, flatter belt sits at the waist instead of a circular waist ring.
 add(loft([[.027,.185,.133,.008],[.032,.192,.138,.008],[.06,.193,.139,.008],[.066,.183,.131,.008]],28,1),belt,torso);
 const loops=[-.145,-.083,.083,.145].map(x=>new THREE.BoxGeometry(.011,.045,.011).translate(x,.046,.008+.137*Math.sqrt(1-(x/.193)**2)));
 add(joined(loops),pants,torso);
 add(new THREE.BoxGeometry(.033,.027,.009),button,torso,0,.047,.153);
 const paths:number[][][]=[];
 for(const side of [-1,1]){
  paths.push([[side*.072,.723,.035],[side*.078,.715,.07],[side*.041,.677,.1],[0,.668,.106]]);
  paths.push([[side*.156,.558,-.107],[side*.201,.546,-.087],[side*.228,.506,-.035]]);
 }
 seams(torso,paths,accent,.0028);
 const front:number[][]=[];
 for(let y=.096;y<=.646;y+=.025){const [, ,depth,z=0]=profileAt(shirtProfile,y);front.push([.005,y,z+depth+jerseyRelief(y,0)+.002]);}
 seams(torso,[front],pants,.0022);
 const buttons=[.138,.244,.35,.456,.56,.64].map(y=>{
  const [, ,depth,z=0]=profileAt(shirtProfile,y);
  return new THREE.CylinderGeometry(.0043,.0043,.002,7).rotateX(Math.PI/2).translate(.006,y,z+depth+jerseyRelief(y,0)+.004);
 });
 add(joined(buttons),button,torso);
}

function headGeometry(){
 const geometry=loft([
  [-.07,.035,.042,.04],[-.055,.061,.053,.034],[-.027,.091,.071,.017],
  [.014,.111,.095,.002],[.049,.114,.104,-.002],[.084,.112,.104,-.005],
  [.124,.106,.101,-.006],[.158,.089,.085,-.006],[.186,.048,.044,-.006],[.193,.002,.002,-.006]
 ],32,3,(height,angle)=>{
  const front=Math.max(0,Math.cos(angle));
  const socket=gauss(height,.064,.018)*gauss(Math.abs(Math.sin(angle)),.43,.18);
  const cheek=gauss(height,.015,.03)*gauss(Math.abs(Math.sin(angle)),.6,.24);
  return front*(-.0055*socket+.004*cheek);
 });
 // A very slight warm cheek and cooler jaw shade supplies face planes without a painted-on smile.
 const positions=geometry.getAttribute("position"),colors:number[]=[],color=new THREE.Color();
 for(let i=0;i<positions.count;i++){
  const y=positions.getY(i),x=positions.getX(i),z=positions.getZ(i);
  const cheek=gauss(y,.015,.025)*gauss(Math.abs(x),.076,.028)*Math.max(0,z/.1);
  const jaw=gauss(y,-.035,.026)*Math.max(0,z/.1);
  color.setRGB(1,.985-cheek*.035-jaw*.018,.976-cheek*.045-jaw*.012);colors.push(color.r,color.g,color.b);
 }
 geometry.setAttribute("color",new THREE.Float32BufferAttribute(colors,3));return geometry;
}

function noseGeometry(){
 const positions=[
  -.012,.077,.097, .012,.077,.097, -.017,.02,.106, .017,.02,.106,
  -.019,.004,.116, .019,.004,.116, -.013,-.004,.107, .013,-.004,.107,
  0,.063,.119, 0,.013,.143, 0,-.007,.122
 ];
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));
 geometry.setIndex([0,2,8,0,8,1,1,8,3,8,2,9,8,9,3,2,4,9,3,9,5,4,10,9,5,9,10,4,6,10,5,10,7,6,7,10]);geometry.computeVertexNormals();return geometry;
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

function headDetails(head:THREE.Object3D,skin:SurfaceMaterial,cap:SurfaceMaterial,cloth:THREE.Texture|null,isBatter:boolean){
 const faceMaterial=skin.clone();faceMaterial.vertexColors=true;
 add(headGeometry(),faceMaterial,head);add(noseGeometry(),skin,head);
 const hair=material("#242421",.97),features=material("#684f42",.91),eyeWhite=material("#dcd7ca",.7),iris=material("#252923",.59);
 const ears=[-1,1].map(sign=>loft([[-.006,.005,.01],[.003,.009,.017],[.026,.012,.021],[.047,.009,.016],[.053,.002,.005]],12,2).translate(sign*.111,0,-.014));
 add(joined(ears),skin,head);
 add(joined([-1,1].map(sign=>oval(.003,.019,.01,sign*.122,.025,-.003,10))),features,head);
 add(loft([[.045,.11,.098,-.018],[.115,.11,.104,-.019],[.166,.083,.078,-.02],[.195,.01,.01,-.019]],24,2),hair,head);
 // Facial details stay restrained: the eyes are almond-shaped, set under the brow.
 const eyes=[-1,1].map(sign=>oval(.017,.0045,.0027,sign*.047,.06,.0945,12));
 add(joined(eyes),eyeWhite,head);
 add(joined([-1,1].map(sign=>oval(.0041,.0043,.0018,sign*.047,.06,.0975,10))),iris,head);
 const featuresPaths:number[][][]=[];
 for(const sign of [-1,1]){
  featuresPaths.push([[sign*.029,.063,.095],[sign*.047,.067,.098],[sign*.064,.063,.089]]);
  featuresPaths.push([[sign*.027,.079,.1],[sign*.044,.083,.103],[sign*.067,.079,.093]]);
 }
 featuresPaths.push([[-.026,-.023,.107],[-.013,-.025,.115],[0,-.026,.118],[.013,-.025,.115],[.026,-.023,.107]]);
 seams(head,featuresPaths,features,.0015);
 const lips=material("#a87763",.83);
 seams(head,[[[-.019,-.029,.112],[0,-.031,.117],[.019,-.029,.112]]],lips,.0022);
 const shell=loft([[.066,.116,.116,-.009],[.091,.126,.121,-.009],[.13,.126,.12,-.01],[.18,.107,.102,-.01],[.218,.062,.061,-.01],[.235,.002,.002,-.01]],28,3);
 add(shell,cap,head);
 const bill=add(brimGeometry(),cap,head);bill.material.side=THREE.DoubleSide;
 const underside=material("#28312d",.86);add(brimGeometry().translate(0,-.002,0),underside,head).material.side=THREE.BackSide;
 if(isBatter){
  const guards=[-1,1].map(sign=>loft([[-.049,.002,.009],[.0,.014,.036],[.051,.017,.046],[.091,.009,.031]],12,3).translate(sign*.117,0,-.009));
  add(joined(guards),cap,head);
  const vents=[-1,1].flatMap(sign=>[
   oval(.0018,.011,.017,sign*.132,.023,-.002,10),
   oval(.017,.003,.002,sign*.074,.178,.077,10),
   oval(.009,.003,.002,sign*.036,.203,.062,10)
  ]);add(joined(vents),underside,head);
 }else{
  const stitch=material("#81868a",.94,cloth,.002);
  seams(head,[-1,1].map(sign=>[[0,.236,-.011],[sign*.064,.218,.033],[sign*.11,.166,.053],[sign*.12,.098,.019]]),stitch,.001);
  add(oval(.012,.005,.012,0,.236,-.01,10),cap,head);
 }
}

function cuff(parent:THREE.Object3D,at:number,width:number,depth:number,mat:THREE.Material){
 return add(loft([[at-.012,width,depth],[at+.012,width,depth]],20,1),mat,parent);
}

function arm(parent:THREE.Object3D,side:number,jersey:THREE.Material,skin:THREE.Material,accent:THREE.Material){
 const upper=object(parent,side*.27,.58),lower=object(upper,0,-.34);
 add(loft([[-.377,.025,.026,.005],[-.346,.049,.052,.002],[-.29,.058,.06,-.002],[-.204,.072,.071,-.006],[-.11,.074,.069,-.005],[.008,.064,.061],[.055,.025,.025]],20,3),skin,upper);
 add(loft([[-.192,.068,.069,-.004],[-.166,.079,.073,-.003],[-.085,.091,.078,-.005],[.004,.087,.076,-.005],[.052,.055,.052],[.07,.011,.014]],24,2,(y,a)=>.004*gauss(y,-.117,.08)*Math.sin(a*5+y*20)),jersey,upper);
 cuff(upper,-.184,.073,.072,accent);
 add(loft([[-.341,.026,.028,.006],[-.315,.035,.034,.005],[-.263,.04,.038,.008],[-.166,.057,.05,.006],[-.091,.064,.057],[.0,.05,.051],[.028,.022,.026]],20,3,(y,a)=>.0015*Math.sin(a*3)*gauss(y,-.12,.12)),skin,lower);
 return {upper,lower};
}

function shoe(parent:THREE.Object3D,dark:THREE.Material,accent:THREE.Material,sole:THREE.Material){
 // Shoe sections run heel-to-toe; the flat sole and raised heel read as footwear.
 const upper=loft([
  [-.103,.035,.025],[-.082,.071,.049],[.0,.079,.056],[.09,.084,.04],[.17,.073,.027],[.204,.015,.012]
 ],20,3,(z,a)=>.0018*Math.sin(a*5+z*37)).rotateX(Math.PI/2).translate(0,-.39,.054);
 add(upper,dark,parent);
 const bottom=loft([[-.111,.016,.002],[-.098,.061,.009],[-.015,.081,.011],[.091,.086,.011],[.181,.073,.009],[.211,.015,.001]],20,2).rotateX(Math.PI/2).translate(0,-.443,.054);
 add(bottom,sole,parent);
 const panels=[-1,1].map(sign=>tube([[sign*.071,-.399,-.01],[sign*.081,-.394,.059],[sign*.076,-.409,.15]],.009));
 add(joined(panels),accent,parent);
 const lacePaths:number[][][]=[];
 for(let i=0;i<4;i++)lacePaths.push([[-.032,-.34-i*.009,.018+i*.023],[.03,-.347-i*.009,.034+i*.023]]);
 seams(parent,lacePaths,sole,.0022);
 const studs=[-.049,.049].flatMap(x=>[-.015,.125,.205].map(z=>new THREE.CylinderGeometry(.009,.006,.016,5).translate(x,-.461,z)));
 add(joined(studs),dark,parent);
}

function leg(root:THREE.Object3D,side:number,accent:THREE.Material,dark:THREE.Material,sole:THREE.Material){
 const thigh=object(root,side*.115,.91),knee=object(thigh,0,-.43);
 seams(thigh,[[[side*.104,.012,.027],[side*.116,-.12,.027],[side*.111,-.247,.028],[side*.078,-.414,.026]]],accent,.0025);
 seams(knee,[[[side*.073,-.01,.025],[side*.088,-.143,.012],[side*.07,-.266,.026],[side*.057,-.381,.026]]],accent,.0025);
 shoe(knee,dark,accent,sole);return {thigh,knee};
}

function trousers(root:THREE.Group,hips:THREE.Group,left:{thigh:THREE.Group;knee:THREE.Group},right:{thigh:THREE.Group;knee:THREE.Group},mat:THREE.Material){
 const positions:number[]=[],uvs:number[]=[],indices:number[]=[],boneIndices:number[]=[],weights:number[]=[];
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
 // One waist branches into two legs with a shared crotch surface; there is no separate pelvis shell.
 const leftTop=ring(.811,.11,.118,.112,0,1),rightTop=ring(.811,.11,.118,-.112,0,-1);
 connect(upper,leftTop,0,count/2);connect(upper,rightTop,count/2,count);
 // Recess the fly into the two thigh surfaces; a single flat triangle reads like a flap when crouched.
 const fly=vertex(0,.852,.111,0,0),crotch=vertex(0,.803,.096,0,0);
 indices.push(upper[0],rightTop[0],fly,upper[0],fly,leftTop[0]);
 indices.push(fly,rightTop[0],crotch,fly,crotch,leftTop[0]);
 indices.push(upper[count/2],leftTop[count/2],rightTop[count/2]);
 for(let i=0;i<count/2;i++){
  const a=leftTop[(count/2+i)%count],b=leftTop[(count/2+i+1)%count];
  const c=rightTop[(count/2-i+count)%count],d=rightTop[(count/2-i-1+count)%count];
  if(i===count/2-1)indices.push(a,b,crotch,a,crotch,c,c,crotch,d);
  else indices.push(a,b,c,c,b,d);
 }
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
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));
 geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uvs,2));geometry.setAttribute("skinIndex",new THREE.Uint16BufferAttribute(boneIndices,4));
 geometry.setAttribute("skinWeight",new THREE.Float32BufferAttribute(weights,4));geometry.setIndex(indices);geometry.computeVertexNormals();
 const bones=[hips,left.thigh,right.thigh,left.knee,right.knee].map((pivot,index)=>{
  const bone=new THREE.Bone();bone.name=["Pants waist","Left thigh","Right thigh","Left knee","Right knee"][index];pivot.add(bone);return bone;
 });
 root.updateMatrixWorld(true);
 const result=new THREE.SkinnedMesh(geometry,mat);result.name="Continuous tailored trousers";
 result.castShadow=true;result.receiveShadow=true;root.add(result);result.bind(new THREE.Skeleton(bones));
 // Its bones remain attached to the existing animation pivots, including mirrored left-handed rigs.
 result.frustumCulled=false;return result;
}

function hand(parent:THREE.Object3D,mat:THREE.Material){
 const root=object(parent);
 add(loft([[-.078,.019,.014,.004],[-.059,.042,.02],[-.012,.045,.023],[.019,.031,.025]],16,3),mat,root);
 const fingers=[-.031,-.01,.01,.03].map((x,index)=>tube([[x,-.05,.003],[x,-.078,.018],[x,-.089,.039],[x,-.073,.052]],.011-(index===3?.002:0),8));
 fingers.push(tube([[.039,-.007,.003],[.056,-.028,.017],[.043,-.049,.036]],.014,8));
 add(joined(fingers),mat,root);return root;
}

/** Cupped mitt mesh, with separate finger channels and a woven web. */
function mitt(parent:THREE.Object3D,size:number,bump:THREE.Texture|null){
 const root=object(parent);root.scale.setScalar(size);
 const leather=new THREE.MeshPhysicalMaterial({color:"#915b31",roughness:.74,clearcoat:.07,bumpMap:bump,bumpScale:.0045});
 const trim=material("#c69b66",.85,bump,.002),pocket=material("#5f3e29",.89,bump,.003);
 const front:number[]=[],uv:number[]=[],indices:number[]=[],around=28,rows=8;
 for(let row=0;row<=rows;row++)for(let i=0;i<=around;i++){
  const r=Math.max(.001,row/rows),angle=i/around*Math.PI*2;
  front.push(Math.sin(angle)*r*.105,Math.cos(angle)*r*.119-.02,.013-.063*(1-r*r));uv.push(i/around,r);
  if(row<rows&&i<around){const a=row*(around+1)+i,b=a+1,c=b+around+1,d=a+around+1;indices.push(a,d,b,b,d,c);}
 }
 const bowl=new THREE.BufferGeometry();bowl.setAttribute("position",new THREE.Float32BufferAttribute(front,3));bowl.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));bowl.setIndex(indices);bowl.computeVertexNormals();
 pocket.side=THREE.DoubleSide;add(bowl,pocket,root);
 add(oval(.107,.12,.027,0,-.02,-.08,20),leather,root);
 const channels=[-.073,-.025,.026,.073].map((x,index)=>loft([[.034,.023,.023],[.091,.026,.025],[.145-Math.abs(index-1.5)*.011,.024,.022],[.166-Math.abs(index-1.5)*.011,.008,.012]],12,2).rotateZ(-x*1.2).translate(x,0,-.014));
 channels.push(loft([[-.079,.013,.019],[-.022,.035,.03],[.046,.035,.028],[.083,.022,.018],[.092,.006,.009]],12,2).rotateZ(-.53).translate(.092,.01,-.005));
 add(joined(channels),leather,root);
 const rim:number[][]=[];for(let i=0;i<=32;i++){const angle=i/32*Math.PI*2;rim.push([Math.sin(angle)*.104,Math.cos(angle)*.118-.02,.015]);}
 const lacing:number[][][]=[rim];
 for(const x of [-.073,-.025,.026,.073])lacing.push([[x-.012,.043,.014],[x-.014,.123,.014],[x,.153-Math.abs(x)*.22,.008],[x+.013,.124,.012]]);
 for(let i=0;i<4;i++){
  lacing.push([[.028+i*.015,.034,.025],[.076+i*.014,.105-i*.011,.025]]);
  lacing.push([[.023,.051+i*.013,.024],[.11,.014+i*.02,.024]]);
 }
 seams(root,lacing,trim,.0026);
 add(loft([[-.139,.047,.025,-.035],[-.118,.048,.027,-.036],[-.1,.047,.025,-.037]],16,1),leather,root);
 seams(root,[[[-.039,-.12,-.011],[0,-.113,-.008],[.039,-.12,-.011]]],trim,.003);
 return root;
}

export function createMitt(parent:THREE.Object3D,size=1){return mitt(parent,size,createPlayerSurface("leather"));}

function uniformTexture(appearance:PlayerAppearance,area:"front"|"back"|"cap"){
 if(typeof document==="undefined")return null;
 const canvas=document.createElement("canvas");canvas.width=512;canvas.height=512;
 const context=canvas.getContext("2d");if(!context)return null;
 context.textAlign="center";context.textBaseline="middle";context.fillStyle=appearance.letter;
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
   const [,width,depth,forward=0]=profileAt([[.091,.126,.121,-.009],[.13,.126,.12,-.01],[.18,.107,.102,-.01],[.218,.062,.061,-.01]],y);
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
 const cloth=createPlayerSurface("cloth"),skinTexture=createPlayerSurface("skin"),leather=createPlayerSurface("leather");
 const jersey=material(color,.92,cloth,.0045),pants=material("#e5e3dc",.95,cloth,.004);
 const skin=new THREE.MeshPhysicalMaterial({color:"#bb8967",roughness:.72,clearcoat:.025,bumpMap:skinTexture,bumpScale:.0015});
 const dark=material("#172027",.72,leather,.0025),white=material("#dfdfd5",.86),accent=material("#dedccf",.9,cloth,.002);
 const cap=new THREE.MeshPhysicalMaterial({color,roughness:isBatter?.26:.87,clearcoat:isBatter?.66:0,clearcoatRoughness:.2,bumpMap:isBatter?null:cloth,bumpScale:.003});
 const hips=object(root,0,.93),torso=object(root,0,.95),head=object(torso,0,.82);
 add(loft(shirtProfile,32,4,jerseyRelief),jersey,torso);
 add(loft([[.681,.057,.058,-.01],[.726,.074,.067,-.013],[.788,.063,.057,-.009],[.821,.05,.052,.003]],20,3),skin,torso);
 jerseyDetails(torso,pants,dark,accent,white);headDetails(head,skin,cap,cloth,isBatter);
 const leftArm=arm(torso,1,jersey,skin,accent),rightArm=arm(torso,-1,jersey,skin,accent);
 const left=leftArm.upper,right=rightArm.upper,le=leftArm.lower,re=rightArm.lower;
 for(const lower of [le,re])cuff(lower,-.303,.0355,.0345,isBatter?accent:dark);
 if(isBatter){
  const elbow=loft([[-.098,.042,.025,.033],[-.062,.058,.038,.024],[-.019,.054,.034,.025],[.013,.025,.018,.018]],16,2);
  add(elbow,accent,le);cuff(le,-.061,.06,.054,dark);
 }else{
  const glove=mitt(le,1,leather);glove.position.set(0,-.34,.025);
  const throwingHand=hand(re,skin);throwingHand.position.set(0,-.303,-.012);
 }
 const leftLeg=leg(root,1,accent,dark,white),rightLeg=leg(root,-1,accent,dark,white);
 trousers(root,hips,leftLeg,rightLeg,pants);
 const lettering=(["front","back","cap"] as const).map(area=>{
  const material=new THREE.MeshStandardMaterial({transparent:true,depthWrite:false,roughness:.86,polygonOffset:true,polygonOffsetFactor:-1,alphaTest:.02});
  const {geometry,centre}=decalGeometry(area),decal=add(geometry,material,area==="cap"?head:torso,0,centre);
  if(area==="back")decal.rotation.y=Math.PI;
  decal.castShadow=false;decal.visible=false;return {area,decal,material};
 });
 return {root,hips,torso,head,left,right,le,re,ll:leftLeg.thigh,rl:rightLeg.thigh,lk:leftLeg.knee,rk:rightLeg.knee,jersey,cap,accent,lettering,appearanceKey:""};
}

export function dressPlayer(model:PlayerModel,appearance:PlayerAppearance,handedness=1){
 const key=JSON.stringify(appearance);
 if(key!==model.appearanceKey){
  model.appearanceKey=key;model.jersey.color.set(appearance.jersey);model.cap.color.set(appearance.cap);model.accent.color.set(appearance.accent);
  for(const {area,decal,material} of model.lettering){
   const previous=material.map;material.map=uniformTexture(appearance,area);material.needsUpdate=true;decal.visible=!!material.map;previous?.dispose();
  }
 }
 // Mirroring the skeleton for a left-handed player must never mirror printed lettering.
 for(const {decal} of model.lettering)decal.scale.x=handedness;
}

export function equipCatcher(model:PlayerModel){
 const shell=material("#243443",.62),pad=material("#3f505e",.94,model.jersey.bumpMap,.003),metal=new THREE.MeshStandardMaterial({color:"#899398",roughness:.37,metalness:.72});
 const chest=loft([[.147,.094,.023,.153],[.2,.165,.033,.151],[.361,.2,.038,.144],[.522,.192,.03,.128],[.625,.094,.025,.119]],24,3);
 add(chest,shell,model.torso);
 const ribs=[.215,.28,.345,.41,.475,.536].map(y=>tube([[-.147,y,.19],[0,y-.009,.193],[.147,y,.187]],.018));
 add(joined(ribs),pad,model.torso);
 for(const shoulder of [model.left,model.right])add(loft([[-.14,.065,.057],[-.042,.096,.082],[.044,.071,.064],[.07,.005,.012]],16,2),shell,shoulder);
 for(const knee of [model.lk,model.rk]){
  add(loft([[-.35,.042,.019,.058],[-.27,.062,.025,.068],[-.13,.07,.024,.082],[-.005,.062,.042,.082],[.043,.023,.02,.065]],18,3),shell,knee);
  for(const y of [-.05,-.275])cuff(knee,y,.083,.079,pad);
 }
 const mask=object(model.head,0,.03,.018),bars:number[][][]=[];
 for(const y of [-.095,-.026,.048,.124]){
  const width=y<-.08?.075:y>.1?.096:.129;
  bars.push([[-width,y,.102],[0,y,.158],[width,y,.102]]);
 }
 for(const x of [-.104,0,.104])bars.push([[x*.7,-.095,.115],[x,-.026,.154-Math.abs(x)*.18],[x*.86,.124,.115]]);
 seams(mask,bars,metal,.005);
 add(oval(.08,.032,.036,0,-.092,.083,14),pad,mask);
}

export function createBat(parent:THREE.Object3D){
 const root=object(parent),wood=createPlayerSurface("wood"),cloth=createPlayerSurface("cloth");
 const barrel=new THREE.MeshPhysicalMaterial({color:"#bc8b52",roughness:.42,clearcoat:.35,clearcoatRoughness:.3,bumpMap:wood,bumpScale:.0015});
 const grip=material("#212a2b",.83,cloth,.0025),glove=material("#dfdfd4",.82,cloth,.0018),trim=material("#868e8e",.84);
 // Grip anchors at -0.055 and +0.065 are shared with two-bone arm IK.
 add(loft([[-.154,.028,.028],[-.143,.038,.038],[-.129,.038,.038],[-.117,.022,.022],[.16,.023,.023],[.35,.033,.033],[.55,.049,.049],[.88,.055,.055],[.94,.051,.051],[.976,.026,.026],[.98,.001,.001]],20,3),barrel,root);
 add(loft([[-.123,.0228,.0228],[.175,.0245,.0245]],16,1),grip,root);
 const wrap:number[][]=[];for(let i=0;i<130;i++){const a=i/129*Math.PI*2*15;wrap.push([Math.sin(a)*.0242,-.117+i/129*.282,Math.cos(a)*.0242]);}
 seams(root,[wrap],trim,.0008);
 for(const y of [-.055,.065]){
  const palm=loft([[-.05,.023,.022],[.0,.048,.032],[.037,.034,.023],[.043,.016,.018]],16,2).rotateZ(-Math.PI/2).translate(.028,y,-.011);
  add(palm,glove,root);
  const fingers:number[][][]=[];
  for(let i=0;i<4;i++){
   const at=y-.038+i*.023;
   fingers.push([[.06,at,-.027],[.05,at,.023],[.021,at,.04],[-.009,at,.027],[-.019,at,.012]]);
  }
  fingers.push([[.047,y+.033,-.027],[.008,y+.03,-.034],[-.026,y+.007,-.01]]);
  seams(root,fingers,glove,.0096);
  seams(root,[[[.065,y-.029,-.022],[.069,y,.004],[.061,y+.028,-.022]]],trim,.0012);
 }
 return root;
}
