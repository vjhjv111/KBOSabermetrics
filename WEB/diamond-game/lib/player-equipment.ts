import * as THREE from "three";
import {createPlayerSurface} from "./player-surfaces";
import {object,loft,joined,oval,tube,seams,material,add,restoreSeamNormals,type Profile} from "./player-geometry";
import {buildCatcherMitt} from './player-mitt';
import {normalizePlayerCustomization,playerDimensions,type PlayerCustomization} from './player-appearance';

export type MittStyle='fielding'|'catcher';
type MittState={style:MittStyle;leather:THREE.MeshPhysicalMaterial;trim:THREE.Material;pocket:THREE.Material};
const mittStates=new WeakMap<THREE.Object3D,MittState>();

/** Join a rounded dome to the loft's existing ring; no open leather tube ends. */
function cappedLoft(profile:Profile[],around:number,steps:number,top:number,bottom=0){
 const body=loft(profile,around,steps),p=body.getAttribute("position"),sourceUV=body.getAttribute("uv");
 const positions=Array.from(p.array),uv=Array.from(sourceUV.array),indices=Array.from(body.index!.array),rings=Array.from({length:p.count/(around+1)},(_,row)=>row*(around+1));
 const cap=(end:Profile,start:number,height:number,sign:number)=>{
  const [y,width,depth,forward=0,side=0]=end;let previous=start;
  for(let ring=1;ring<4;ring++){
   const angle=ring/4*Math.PI/2,scale=Math.cos(angle),next=positions.length/3;rings.push(next);
   for(let i=0;i<=around;i++){
    const a=i/around*Math.PI*2;positions.push(side+Math.sin(a)*width*scale,y+sign*Math.sin(angle)*height,forward+Math.cos(a)*depth*scale);uv.push(i/around,sign>0?1+ring*.025:-ring*.025);
    if(i<around){const a0=previous+i,b=a0+1,c=next+i+1,d=next+i;indices.push(...(sign>0?[a0,b,d,b,c,d]:[a0,d,b,b,d,c]));}
   }previous=next;
  }
  const centre=positions.length/3;positions.push(side,y+sign*height,forward);uv.push(.5,sign>0?1.1:-.1);
  for(let i=0;i<around;i++)indices.push(...(sign>0?[previous+i,previous+i+1,centre]:[previous+i+1,previous+i,centre]));
 };
 if(top)cap(profile.at(-1)!,p.count-around-1,top,1);if(bottom)cap(profile[0],0,bottom,-1);
 body.dispose();const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(positions,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();
 geometry.userData.normalSeamPairs=rings.map(start=>[start,start+around]);
 return restoreSeamNormals(geometry);
}

/** Elliptical finger pads, soft joint bulges and rounded, tapered fingertips. */
function digit(points:number[][],radius:number,flatten=.78,rows=14){
 const curve=new THREE.CatmullRomCurve3(points.map(p=>new THREE.Vector3(...p)),false,"centripetal"),frames=curve.computeFrenetFrames(rows,false);
 const vertices:number[]=[],uv:number[]=[],indices:number[]=[],around=8;
 for(let row=0;row<=rows;row++){
  const t=row/rows,p=curve.getPointAt(t),n=frames.normals[row],b=frames.binormals[row];
  const joint=.09*Math.exp(-Math.pow((t-.23)/.085,2))+.10*Math.exp(-Math.pow((t-.62)/.075,2));
  const end=t>.86?Math.sqrt(Math.max(.0001,1-Math.pow((t-.86)/.14,2))):1,r=radius*(1-t*.22+joint)*end;
  for(let i=0;i<=around;i++){
   const a=i/around*Math.PI*2,x=Math.cos(a)*r*flatten,y=Math.sin(a)*r;
   vertices.push(p.x+n.x*x+b.x*y,p.y+n.y*x+b.y*y,p.z+n.z*x+b.z*y);uv.push(i/around,t);
   if(row<rows&&i<around){const start=row*(around+1)+i,next=start+around+1;indices.push(start,start+1,next,start+1,next+1,next);}
  }
 }
 const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(vertices,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();geometry.userData.normalSeamPairs=Array.from({length:rows+1},(_,row)=>[row*(around+1),row*(around+1)+around]);return restoreSeamNormals(geometry);
}
function stitchMesh(parent:THREE.Object3D,paths:number[][][],mat:THREE.Material,radius=.0012,density=2){
 return add(joined(paths.map(p=>tube(p,radius,Math.max(3,Math.min(12,Math.floor(p.length*density)))))),mat,parent);
}

export function shoe(parent:THREE.Object3D,dark:THREE.Material,accent:THREE.Material,sole:THREE.Material){
 // The outsole and stud contacts retain the existing ankle/toe plant envelope.
 // A higher heel collar, medial arch and broad, low toe separate cleats from boots.
 const medial=parent.parent?.name==="Left ankle"?-1:1;
 const upper=cappedLoft([
  [-.09,.045,.043],[-.079,.062,.059],[-.039,.064,.064],
  [.007,.076,.055,0,medial*.002],[.075,.083,.043,0,medial*.003],
  [.151,.079,.031,0,medial*.004],[.189,.052,.022,0,medial*.003]
 ],16,2,.015,.013).rotateX(Math.PI/2).translate(0,-.39,.054);
 add(upper,dark,parent).name="Shaped cleat upper";
 const bottom=cappedLoft([[-.104,.036,.007],[-.098,.061,.009],[-.015,.081,.011],[.091,.086,.011],[.181,.073,.009],[.203,.034,.004]],16,2,.008,.007).rotateX(Math.PI/2).translate(0,-.443,.054);
 add(bottom,sole,parent).name="Cleat outsole";
 const panels=[-1,1].map(sign=>digit([[sign*.052,-.368,-.007],[sign*.068,-.382,.033],[sign*.077,-.399,.096],[sign*.069,-.41,.167]],.008,.45,8));
 add(joined(panels),accent,parent).name="Cleat quarter panels";
 const tongue=loft([[-.028,.03,.007],[.012,.034,.009],[.049,.028,.008],[.056,.008,.004]],12,2).rotateX(.38).translate(0,-.354,.009);
 add(tongue,dark,parent).name="Cleat padded tongue";
 // Fit the crossing lace/eyelet anchors to the actual tessellated leather surface.
 // This is construction-only work; animation retains the original foot pivots.
 const surfaceRay=new THREE.Ray(),a=new THREE.Vector3(),b=new THREE.Vector3(),c=new THREE.Vector3(),hit=new THREE.Vector3();
 const onUpper=(x:number,z:number,lift=.002)=>{
  surfaceRay.set(new THREE.Vector3(x,0,z),new THREE.Vector3(0,-1,0));let y=-Infinity;
  for(const geometry of [upper,tongue]){const p=geometry.getAttribute("position"),index=geometry.index!;for(let i=0;i<index.count;i+=3){a.fromBufferAttribute(p,index.getX(i));b.fromBufferAttribute(p,index.getX(i+1));c.fromBufferAttribute(p,index.getX(i+2));if(surfaceRay.intersectTriangle(a,b,c,false,hit))y=Math.max(y,hit.y);}}
  return [x,(Number.isFinite(y)?y:-.34)+lift,z];
 };
 const lacePaths:number[][][]=[],stitches:number[][][]=[],eyelets:number[][][]=[];
 for(let i=0;i<5;i++){
  const z=.012+i*.021;
  lacePaths.push([onUpper(-.025,z),onUpper(0,z+.006,.003),onUpper(.026,z+.012)]);
  lacePaths.push([onUpper(.025,z),onUpper(-.002,z+.008,.0035),onUpper(-.025,z+.019)]);
  for(const sign of [-1,1])eyelets.push([onUpper(sign*.028-.003,z,.001),onUpper(sign*.028,z-.003,.0025),onUpper(sign*.028+.003,z,.001)]);
 }
 // A tied bow and short loose ends sit above the instep instead of floating off it.
 lacePaths.push([[0,-.317,.023],[-.023,-.307,.006],[-.027,-.311,.033],[0,-.317,.023]],[[0,-.317,.023],[.022,-.308,.009],[.025,-.31,.034],[0,-.317,.023]],[[0,-.317,.023],[-.014,-.319,.048]],[[0,-.317,.023],[.016,-.322,.05]]);
 const studs=[-.049,.049].flatMap(x=>[-.015,.125,.205].map(z=>new THREE.CylinderGeometry(.009,.006,.016,5).translate(x,-.461,z)));
 add(joined(studs),dark,parent).name="Cleat contact studs";
 for(const sign of [-1,1]){
  stitches.push([[sign*.041,-.359,-.038],[sign*.06,-.378,-.007],[sign*.075,-.419,.048],[sign*.075,-.428,.148]]);
  stitches.push([[sign*.033,-.329,.0],[sign*.042,-.348,.061],[sign*.047,-.369,.131]]);
 }
 const toe:number[][]=[];for(let i=0;i<=12;i++){const a=-Math.PI*.47+i/12*Math.PI*.94;toe.push([Math.sin(a)*.067,-.37-Math.abs(Math.sin(a))*.031,.158+Math.cos(a)*.071]);}stitches.push(toe);
 stitchMesh(parent,lacePaths,sole,.0018).name="Crossed cleat laces";
 stitchMesh(parent,eyelets,accent,.0013,1).name="Cleat eyelets";
 stitchMesh(parent,stitches,sole,.0009,1.5).name="Cleat stitched panels";
}

export function hand(parent:THREE.Object3D,mat:THREE.Material){
 const root=object(parent);
 const palm=loft([[-.075,.023,.012,.008,-.005],[-.059,.039,.017,.004,-.003],[-.033,.043,.022],[-.003,.037,.022],[.019,.028,.025]],18,2);
 const thenar=oval(.022,.033,.014,.026,-.021,.014,12),ulnar=oval(.012,.026,.009,-.032,-.035,.012,10);
 add(joined([palm,thenar,ulnar]),mat,root).name="Anatomical hand palm";
 const fingers=[-.031,-.01,.011,.03].map((x,index)=>{
  const short=index===3?.011:index===0?.004:0;
  return digit([[x,-.055,.006],[x,-.078+short,.02],[x-.001,-.089+short,.039],[x-.002,-.079+short,.053],[x-.003,-.068+short,.047]],index===3?.0088:.0106,.75,12);
 });
 fingers.push(digit([[.028,-.012,.009],[.047,-.029,.018],[.048,-.044,.036],[.035,-.054,.042]],.013,.81,12));
 add(joined(fingers),mat,root).name="Tapered articulated fingers";
 const knuckles=[-.03,-.01,.011,.03].map((x,index)=>oval(index===3?.008:.0105,.01,.005,x,-.053,.016,8));
 add(joined(knuckles),mat,root).name="Soft hand knuckles";return root;
}

/** Cupped mitt mesh, with separate finger channels and a woven web. */
export function mitt(parent:THREE.Object3D,size:number,bump:THREE.Texture|null){
 const root=object(parent);root.scale.setScalar(size);
 const leather=new THREE.MeshPhysicalMaterial({color:"#915b31",roughness:.74,clearcoat:.12,clearcoatRoughness:.58,bumpMap:bump,bumpScale:.0034});leather.userData.playerSurface="glove";
 const trim=material("#c69b66",.85,bump,.002),pocket=material("#5f3e29",.89,bump,.003);
 buildFieldingMitt(root,leather,trim,pocket);mittStates.set(root,{style:'fielding',leather,trim,pocket});return root;
}
function buildFieldingMitt(root:THREE.Object3D,leather:THREE.Material,trim:THREE.Material,pocket:THREE.Material){
 const cup=(inner:boolean)=>{
  const front:number[]=[],uv:number[]=[],indices:number[]=[],around=28,rows=7;
  for(let row=0;row<=rows;row++)for(let i=0;i<=around;i++){
   const r=Math.max(.001,row/rows),angle=i/around*Math.PI*2,s=Math.sin(angle),width=s<0?.103:.11;
   front.push(s*r*width,Math.cos(angle)*r*.118-.02,(inner?-.052:-.073)+(inner?.068:.074)*r*r);uv.push(i/around,r);
   if(row<rows&&i<around){const a=row*(around+1)+i,b=a+1,c=b+around+1,d=a+around+1;indices.push(...(inner?[a,b,d,b,c,d]:[a,d,b,b,d,c]));}
  }
  const geometry=new THREE.BufferGeometry();geometry.setAttribute("position",new THREE.Float32BufferAttribute(front,3));geometry.setAttribute("uv",new THREE.Float32BufferAttribute(uv,2));geometry.setIndex(indices);geometry.computeVertexNormals();return geometry;
 };
 add(cup(true),pocket,root).name="Deep leather mitt pocket";
 const channels=[-.073,-.025,.026,.073].map((x,index)=>cappedLoft([[.022,.018,.021,-.017],[.065,.025,.025,-.003],[.108,.025,.023,.008],[.149-Math.abs(index-1.5)*.011,.022,.02,.012]],12,2,.018).rotateZ(-x*1.2).translate(x,0,-.014));
 channels.push(cappedLoft([[-.079,.014,.019],[-.022,.034,.031],[.038,.036,.032,.002],[.079,.024,.022,.009]],14,2,.017).rotateZ(-.53).translate(.092,.01,-.005));
 channels.push(cup(false),loft([[-.139,.047,.026,-.035],[-.118,.05,.029,-.036],[-.098,.043,.026,-.037]],16,2));
 const welt:number[][]=[];for(let i=0;i<=40;i++){const a=i/40*Math.PI*2;welt.push([Math.sin(a)*(Math.sin(a)<0?.103:.11),Math.cos(a)*.118-.02,.008]);}
 channels.push(tube(welt,.0078,40));
 add(joined(channels),leather,root).name="Mitt shell and padded finger channels";
 const straps:THREE.BufferGeometry[]=[],webPaths:number[][][]=[];
 const strap=(a:number[],b:number[],width:number)=>{const delta=new THREE.Vector3(...b).sub(new THREE.Vector3(...a)),length=Math.hypot(delta.x,delta.y);straps.push(new THREE.BoxGeometry(length,width,.005).rotateZ(Math.atan2(delta.y,delta.x)).translate((a[0]+b[0])/2,(a[1]+b[1])/2,(a[2]+b[2])/2));};
 for(let i=0;i<3;i++){
  const a=[.036+i*.015,.06,.026],b=[.078+i*.016,.13-i*.012,.032];strap(a,b,.011);
  webPaths.push([[a[0],a[1],a[2]+.004],[(a[0]+b[0])/2,(a[1]+b[1])/2,.036],[b[0],b[1],b[2]+.004]]);
 }
 for(let i=0;i<2;i++)strap([.036,.076+i*.026,.033],[.119,.052+i*.026,.033],.013);
 add(joined(straps),leather,root).name="Woven leather mitt web";
 const rim:number[][]=[];for(let i=0;i<=32;i++){const angle=i/32*Math.PI*2;rim.push([Math.sin(angle)*.104,Math.cos(angle)*.118-.02,.015]);}
 const lacing:number[][][]=[rim,...webPaths];
 for(const x of [-.073,-.025,.026,.073])lacing.push([[x-.012,.043,.014],[x-.014,.123,.014],[x,.153-Math.abs(x)*.22,.008],[x+.013,.124,.012]]);
 for(let i=0;i<20;i++){
  const a=i/20*Math.PI*2,b=a+.08;
  lacing.push([[Math.sin(a)*.099,Math.cos(a)*.112-.02,.018],[Math.sin(b)*.108,Math.cos(b)*.122-.02,.018],[Math.sin(b+.035)*.108,Math.cos(b+.035)*.122-.02,-.002]]);
 }
 lacing.push([[-.039,-.12,-.007],[0,-.113,-.004],[.039,-.12,-.007]],[[.081,-.089,.0],[.101,-.108,.003],[.092,-.127,-.002]],[[.081,-.089,.0],[.106,-.081,.008],[.119,-.093,.003]]);
 stitchMesh(root,lacing,trim,.0022,1.5).name="Mitt rolled welt and leather lacing";
 const creases:number[][][]=[[[-.064,-.046,-.031],[-.043,-.009,-.043],[-.018,.024,-.04]],[[.046,-.074,-.025],[.022,-.054,-.041],[-.014,-.04,-.047]]];
 stitchMesh(root,creases,leather,.0011,1.5).name="Mitt pocket creases";
 return root;
}

/** Swap only owned geometry. Rig roots, post-batch mesh slots and materials stay live. */
export function setMittStyle(root:THREE.Object3D,style:MittStyle,appearance:PlayerCustomization={}){
 const state=mittStates.get(root);if(!state)throw new Error('Mitt style requires a root created by mitt or createMitt.');
 const look=normalizePlayerCustomization(appearance),dims=playerDimensions(look);state.leather.color.set(look.gloveColor);
 if(state.style===style){
  root.traverse(object=>{if(!(object instanceof THREE.Mesh))return;const geometry=object.geometry,p=geometry.getAttribute('position'),rest=geometry.userData.restShape??(geometry.userData.restShape=Float32Array.from(p.array));let changed=false;
   for(let i=0;i<p.count;i++){const x=Math.fround(rest[i*3]*dims.widthScale),z=Math.fround(rest[i*3+2]*dims.depthScale);if(p.getX(i)!==x||p.getZ(i)!==z){p.setXYZ(i,x,rest[i*3+1],z);changed=true;}}
   if(changed){p.needsUpdate=true;geometry.computeVertexNormals();restoreSeamNormals(geometry);geometry.computeBoundingBox();geometry.computeBoundingSphere();}
  });return;
 }
 for(const [material,max] of [[state.pocket,1],[state.leather,3],[state.trim,1]] as const){const count=root.children.filter(child=>child instanceof THREE.Mesh&&child.material===material).length;if(!count||count>max)throw new Error('Unsupported mitt surface slots.');}
 const staging=new THREE.Group();
 (style==='catcher'?buildCatcherMitt:buildFieldingMitt)(staging,state.leather,state.trim,state.pocket);
 const replacements:{mesh:THREE.Mesh;geometry:THREE.BufferGeometry;parts:{sourceId:string;name:string;vertexStart:number;vertexCount:number;indexStart:number;indexCount:number}[]}[]=[];
 for(const material of [state.pocket,state.leather,state.trim]){
  const slots=root.children.filter((child):child is THREE.Mesh=>child instanceof THREE.Mesh&&child.material===material);
  const pieces=staging.children.filter((child):child is THREE.Mesh=>child instanceof THREE.Mesh&&child.material===material);
  // Current unbatched fielding roots have 1/3/1 slots; model batching has 1/1/1.
  for(let slot=0;slot<slots.length;slot++){
   const group=pieces.filter((_,index)=>Math.min(index,slots.length-1)===slot),parts=[];let vertexStart=0,indexStart=0;
   for(const [partIndex,piece] of group.entries()){const vertexCount=piece.geometry.getAttribute('position').count,indexCount=piece.geometry.index!.count;
    // Geometry changes belong to the retained slot (or its original batched
    // sources), never to the temporary meshes used to build the new shape.
    const sourceId=slots[slot].userData.playerBatchParts?.[partIndex]?.sourceId??slots[slot].uuid;
    parts.push({sourceId,name:piece.name,vertexStart,vertexCount,indexStart,indexCount});vertexStart+=vertexCount;indexStart+=indexCount;
   }
   const geometry=group.length===1?group[0].geometry:joined(group.map(piece=>piece.geometry)),p=geometry.getAttribute('position'),rest=Float32Array.from(p.array);geometry.userData.restShape=rest;
   for(let i=0;i<p.count;i++)p.setXYZ(i,rest[i*3]*dims.widthScale,rest[i*3+1],rest[i*3+2]*dims.depthScale);
   geometry.computeVertexNormals();restoreSeamNormals(geometry);geometry.computeBoundingBox();geometry.computeBoundingSphere();
   replacements.push({mesh:slots[slot],geometry,parts});
  }
 }
 const old=new Set<THREE.BufferGeometry>();
 for(const {mesh,geometry,parts} of replacements){old.add(mesh.geometry);mesh.geometry=geometry;mesh.userData.playerBatchParts=parts;mesh.name=parts.length===1?parts[0].name:'Player static batch';}
 old.forEach(geometry=>geometry.dispose());state.style=style;
}

export function createMitt(parent:THREE.Object3D,size=1){return mitt(parent,size,createPlayerSurface("leather"));}

/** The cuff centres are the outer palm-loft rings after its rotation/offset.
 * Arm IK must meet these actual modeled wrist openings, not the bat's axis. */
export const BATTING_WRIST_CUFFS={left:[.071,-.055,-.011],right:[.071,.065,-.011]} as const;
const BATTING_PALM_OFFSET_X=.028;

export function createBat(parent:THREE.Object3D){
 const root=object(parent),wood=createPlayerSurface("wood"),cloth=createPlayerSurface("cloth");
 const barrel=new THREE.MeshPhysicalMaterial({color:"#bc8b52",roughness:.42,clearcoat:.35,clearcoatRoughness:.3,bumpMap:wood,bumpScale:.0015});
 barrel.userData.playerSurface="bat";
 const grip=material("#212a2b",.83,cloth,.0025),glove=material("#e3e3d9",.8,cloth,.0013),trim=material("#848c89",.88);
 // These grip heights belong to the hands; their wrist openings sit off-axis.
 const body=loft([[-.154,.024,.024],[-.146,.031,.031],[-.138,.032,.032],[-.129,.031,.031],[-.117,.021,.021],[.16,.022,.022],[.35,.028,.028],[.55,.035,.035],[.88,.038,.038],[.961,.037,.037],[.975,.033,.033],[.98,.029,.029]],24,3);
 const knob=new THREE.CircleGeometry(.024,24).rotateX(Math.PI/2).translate(0,-.154,0);
 add(joined([body,knob]),barrel,root).name="Wood bat barrel and knob";
 // Rim-to-centre gives this concave interior upward-facing front faces.
 const cup=new THREE.LatheGeometry([[0,.961],[.009,.9615],[.019,.966],[.026,.975],[.029,.98]].reverse().map(([x,y])=>new THREE.Vector2(x,y)),24);
 const cupColors=new Float32Array(cup.getAttribute("position").count*3);cupColors.fill(.72);cup.setAttribute("color",new THREE.BufferAttribute(cupColors,3));
 const endGrain=barrel.clone();endGrain.vertexColors=true;endGrain.roughness=.64;endGrain.clearcoat=.08;
 add(cup,endGrain,root).name="Recessed bat end cup";
 add(loft([[-.123,.0228,.0228],[.175,.0245,.0245]],16,1),grip,root).name="Bat handle wrap";
 const wrap:number[][]=[];for(let i=0;i<130;i++){const a=i/129*Math.PI*2*15;wrap.push([Math.sin(a)*.0242,-.117+i/129*.282,Math.cos(a)*.0242]);}
 seams(root,[wrap],trim,.0008);
 const palms:THREE.BufferGeometry[]=[],fingers:THREE.BufferGeometry[]=[],gloveSeams:number[][][]=[];
 for(const cuff of Object.values(BATTING_WRIST_CUFFS)){
  const y=cuff[1];
  const palm=loft([[-.05,.022,.018],[-.019,.043,.027,-.003],[.009,.043,.028,-.004],[.033,.033,.024],[cuff[0]-BATTING_PALM_OFFSET_X,.016,.018]],18,2).rotateZ(-Math.PI/2).translate(BATTING_PALM_OFFSET_X,y,cuff[2]);
  palms.push(palm,oval(.013,.03,.008,.055,y,-.034,12));
  for(let i=0;i<4;i++){
   const at=y-.036+i*.022,radius=i===3?.0083:.0097;
   fingers.push(digit([[.061,at,-.022],[.058,at+.001,.004],[.046,at+.002,.025],[.023,at,.034],[-.003,at-.001,.027],[-.02,at,.01]],radius,.76,16));
   palms.push(oval(.01,.008,.004,.059,at,-.022,10));
   // Paired seams follow the bend on the back of each phalanx, leaving a pad below.
   gloveSeams.push([[.063,at-.005,-.013],[.065,at-.005,.002],[.057,at-.005,.018]]);
  }
  fingers.push(digit([[.043,y+.027,-.023],[.022,y+.036,-.03],[-.005,y+.029,-.027],[-.025,y+.008,-.01]],.012,.78,14));
  gloveSeams.push([[.045,y-.027,-.035],[.061,y-.023,-.036],[.068,y,-.027],[.062,y+.024,-.032],[.045,y+.032,-.03]]);
  gloveSeams.push([[.005,y-.023,-.031],[.019,y-.015,-.038],[.035,y+.004,-.038]]);
 }
 add(joined(palms),glove,root).name="Batting glove palms and knuckle pads";
 add(joined(fingers),glove,root).name="Batting glove tapered finger grip";
 stitchMesh(root,gloveSeams,trim,.0008).name="Batting glove stitched panels";
 return root;
}
