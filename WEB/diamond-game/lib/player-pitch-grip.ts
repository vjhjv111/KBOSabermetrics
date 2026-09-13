import * as THREE from 'three';
import {add,loft,joined,oval,restoreSeamNormals} from './player-geometry';
import {normalizePlayerCustomization,playerDimensions,type PlayerCustomization} from './player-appearance';

/** Held ball visual radius in WORLD metres; never a collision/flight constant. */
export const PITCH_GRIP_BALL_RADIUS=.0365;
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
type Part={name:string;vertexStart:number;vertexCount:number;indexStart:number;indexCount:number};
export type PitchGrip={root:THREE.Group;mesh:THREE.Mesh;ballCenter:THREE.Vector3;ballRadius:number;parts:Part[]};
const states=new WeakMap<PitchGrip,{key:string;balls:Map<THREE.Object3D,number>}>();
const heldBallMeshes=new WeakSet<THREE.Object3D>();
export const isPitchGripBallMesh=(object:THREE.Object3D)=>heldBallMeshes.has(object);

/** Oval phalanges, soft joint ridges and a genuinely closed rounded fingertip. */
function finger(points:THREE.Vector3[],radius:number){
 const rows=18,around=10,curve=new THREE.CatmullRomCurve3(points,false,'centripetal'),frames=curve.computeFrenetFrames(rows,false);
 const positions:number[]=[],uv:number[]=[],index:number[]=[];
 for(let row=0;row<=rows;row++){
  const t=row/rows,point=curve.getPointAt(t),n=frames.normals[row],b=frames.binormals[row];
  const joints=1+.085*Math.exp(-(((t-.28)/.09)**2))+.08*Math.exp(-(((t-.61)/.07)**2));
  const cap=t>.83?Math.sqrt(Math.max(0,1-((t-.83)/.17)**2)):1;
  const r=radius*(1-t*.16)*joints*cap;
  for(let i=0;i<=around;i++){
   const angle=i/around*Math.PI*2;positions.push(point.x+n.x*Math.cos(angle)*r*.84+b.x*Math.sin(angle)*r,point.y+n.y*Math.cos(angle)*r*.84+b.y*Math.sin(angle)*r,point.z+n.z*Math.cos(angle)*r*.84+b.z*Math.sin(angle)*r);uv.push(i/around,t);
   if(row<rows&&i<around){const a=row*(around+1)+i,c=a+around+1;index.push(a,a+1,c,a+1,c+1,c);}
  }
 }
 const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));g.setAttribute('uv',new THREE.Float32BufferAttribute(uv,2));g.setIndex(index);g.computeVertexNormals();
 g.userData.normalSeamPairs=Array.from({length:rows},(_,row)=>[row*(around+1),row*(around+1)+around]);return restoreSeamNormals(g);
}

function shapes(appearance:PlayerCustomization,radius:number){
 const dims=playerDimensions(appearance),r=radius/dims.heightScale;
 // Keep the palm behind the ball, rather than placing the ball in the wrist.
 const center=V(0,-.092,r+.024*dims.depthScale),width=dims.widthScale,depth=dims.depthScale;
 const palm=loft([[-.069,.030,.011,.004],[-.051,.041,.020,-.003],[-.024,.036,.021,-.003],[0,.027,.021,.006],[.010,.026,.020,.006]],18,3).scale(width,1,depth);
 const pads=joined([oval(.019*width,.027,.014*depth,.026*width,-.031,.007*depth,12),oval(.010*width,.023,.008*depth,-.030*width,-.041,.005*depth,10)]);
 const on=(x:number,y:number,z:number,fingerRadius:number)=>V(x,y,z).normalize().multiplyScalar(r+fingerRadius*.9).add(center);
 const indexRadius=.0103*Math.sqrt(width*depth),middleRadius=.0108*Math.sqrt(width*depth),thumbRadius=.0124*Math.sqrt(width*depth);
 const fingers:[string,THREE.BufferGeometry][]=[
  ['Index finger',finger([V(.020*width,-.057,.002),on(.40,.48,-.81,indexRadius),on(.43,-.18,-.88,indexRadius),on(.39,-.76,-.51,indexRadius),on(.33,-.93,.13,indexRadius)],indexRadius)],
  ['Middle finger',finger([V(-.003*width,-.062,.002),on(-.13,.43,-.89,middleRadius),on(-.16,-.27,-.95,middleRadius),on(-.18,-.86,-.46,middleRadius),on(-.14,-.985,.05,middleRadius)],middleRadius)],
  ['Ring finger',finger([V(-.023*width,-.059,.003),on(-.64,.39,-.67,.0095),on(-.72,-.18,-.65,.0095),on(-.73,-.56,-.39,.0095),on(-.71,-.68,.12,.0095)],.0095*Math.sqrt(width*depth))],
  ['Little finger',finger([V(-.037*width,-.052,.001),on(-.90,.40,-.34,.0082),on(-.97,.01,-.22,.0082),on(-.91,-.38,-.15,.0082),on(-.83,-.47,.12,.0082)],.0082*Math.sqrt(width*depth))],
  ['Thumb',finger([V(.026*width,-.015,.003),V(.044*width,-.038,.018*depth),on(.95,.39,-.24,thumbRadius),on(.99,.02,.14,thumbRadius),on(.69,-.07,.72,thumbRadius)],thumbRadius)],
 ];
 const pieces:[string,THREE.BufferGeometry][]=[['Palm',joined([palm,pads])],...fingers];
 const parts:Part[]=[];let vertexStart=0,indexStart=0;
 for(const [name,g] of pieces){
  const position=g.getAttribute('position');
  // A small radial fitting pass accommodates height/body changes. It is local
  // to this grip; ordinary hands and the skeleton retain their original shape.
  // The clearance also allows the polygon chords to touch without deep burial.
  const clearance=.0008/dims.heightScale;
  for(let i=0;i<position.count;i++){
   const point=V().fromBufferAttribute(position,i),offset=point.clone().sub(center),distance=offset.length();
   if(distance<r+clearance)point.copy(center).addScaledVector(offset,(r+clearance)/Math.max(distance,1e-9));
   position.setXYZ(i,point.x,point.y,point.z);
  }
  g.computeVertexNormals();restoreSeamNormals(g);
  parts.push({name,vertexStart,vertexCount:position.count,indexStart,indexCount:g.index!.count});vertexStart+=position.count;indexStart+=g.index!.count;
 }
 const geometry=joined(pieces.map(([,g])=>g));geometry.userData.restShape=Float32Array.from(geometry.getAttribute('position').array);geometry.computeBoundingBox();geometry.computeBoundingSphere();
 return {geometry,center,parts};
}

/** Skin material/samplers are borrowed, exactly as the existing hand factory. */
export function createPitchGrip(parent:THREE.Object3D,skin:THREE.Material,appearance:PlayerCustomization={},radius=PITCH_GRIP_BALL_RADIUS):PitchGrip{
 const root=new THREE.Group();root.name='Pitcher grip hand';root.position.set(0,-.34,0);parent.add(root);
 const built=shapes(appearance,radius),mesh=add(built.geometry,skin,root);mesh.name='Pitcher grip skin';
 const grip:PitchGrip={root,mesh,ballCenter:built.center,ballRadius:radius,parts:built.parts};
 states.set(grip,{key:shapeKey(appearance,radius),balls:new Map()});return grip;
}
const shapeKey=(appearance:PlayerCustomization,radius:number)=>{const {heightCm,bodyType}=normalizePlayerCustomization(appearance);return `${heightCm}/${bodyType}/${radius}`;};

/** Call after dressPlayer only when height/body changes, never in the hot loop.
 * Refit from the shape recipe, so generic body scaling cannot bury the fingers.
 * Existing mesh/material/texture identity and external cleanup remain valid. */
export function fitPitchGrip(grip:PitchGrip,appearance:PlayerCustomization,force=false){
 const state=states.get(grip)!;const key=shapeKey(appearance,grip.ballRadius);
 if(key===state.key&&!force)return;
 const built=shapes(appearance,grip.ballRadius),old=grip.mesh.geometry;grip.mesh.geometry=built.geometry;grip.ballCenter.copy(built.center);grip.parts=built.parts;state.key=key;old.dispose();syncPitchGripBalls(grip);
}

/** Exact two-link IK whose endpoint is the ball centre, not the wrist.
 * The .34m forearm, wrist pivot and motion/engine release point stay unchanged.
 * The elbow-to-ball virtual link includes the off-axis hand offset. */
export function posePitchGripArm(upper:THREE.Object3D,lower:THREE.Object3D,ballTarget:THREE.Vector3,bend:THREE.Vector3,grip:PitchGrip){
 upper.updateWorldMatrix(true,true);
 const scale=Math.abs(upper.getWorldScale(V()).y),shoulder=upper.getWorldPosition(V()),delta=ballTarget.clone().sub(shoulder),requested=delta.length();
 // A close set position naturally cocks the wrist. Keeping a completely stiff
 // wrist makes the longer elbow-to-ball link unable to fold close to the chest.
 grip.root.rotation.x=-.95*(1-THREE.MathUtils.smoothstep(requested/scale,.12,.26));
 const localBall=grip.ballCenter.clone().applyQuaternion(grip.root.quaternion).add(grip.root.position),a=.34*scale,b=localBall.length()*scale;
 const length=THREE.MathUtils.clamp(requested,Math.abs(a-b)+.00001,a+b-.00001),direction=delta.normalize();
 const along=(a*a-b*b+length*length)/(2*length),height=Math.sqrt(Math.max(0,a*a-along*along)),perpendicular=bend.clone().addScaledVector(direction,-bend.dot(direction)).normalize();
 const elbow=shoulder.clone().addScaledVector(direction,along).addScaledVector(perpendicular,height);
 const frame=(bone:THREE.Object3D,tip:THREE.Vector3,normal:THREE.Vector3)=>{const y=bone.position.clone().sub(tip).normalize(),x=normal.clone().addScaledVector(y,-normal.dot(y)).normalize(),z=x.clone().cross(y).normalize();bone.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(x,y,z));};
 const localElbow=upper.parent!.worldToLocal(elbow.clone()),localTarget=upper.parent!.worldToLocal(ballTarget.clone()),normal=localElbow.clone().sub(upper.position).cross(localTarget.clone().sub(localElbow)).normalize();
 frame(upper,localElbow,normal);upper.updateWorldMatrix(false,true);
 const lowerTarget=lower.parent!.worldToLocal(ballTarget.clone()),localShoulder=lower.parent!.worldToLocal(shoulder.clone()),lowerNormal=lower.position.clone().sub(localShoulder).cross(lowerTarget.clone().sub(lower.position)).normalize();
 frame(lower,lowerTarget,lowerNormal);
 // Rotate the real forearm inside the virtual link's plane so its displaced
 // ball anchor, rather than local -Y alone, points exactly at the target.
 lower.quaternion.multiply(new THREE.Quaternion().setFromAxisAngle(V(1,0,0),Math.atan2(localBall.z,-localBall.y)));lower.updateWorldMatrix(false,true);syncPitchGripBalls(grip);
 return {target:ballTarget,actual:grip.root.localToWorld(grip.ballCenter.clone()),ballLocal:localBall,reachError:Math.abs(requested-length)};
}

/** Borrow the visual ball. Its normal scene owner still disposes its resources.
 * geometryRadius is the radius used to construct the ball mesh/group. */
export function attachPitchGripBall(grip:PitchGrip,ball:THREE.Object3D,geometryRadius=PITCH_GRIP_BALL_RADIUS){
 if(!Number.isFinite(geometryRadius)||geometryRadius<=0)throw new Error('Positive ball geometry radius required');
 grip.root.add(ball);states.get(grip)!.balls.set(ball,geometryRadius);
 ball.traverse(object=>{if(object instanceof THREE.Mesh)heldBallMeshes.add(object);});
 syncPitchGripBalls(grip);return ball;
}
/** Pose/dress calls keep the prop spherical and at the same world radius even
 * when the whole actor changes height or is mirrored. No geometry recreation. */
function syncPitchGripBalls(grip:PitchGrip){
 const balls=states.get(grip)!.balls;if(!balls.size)return;
 grip.root.updateWorldMatrix(true,false);const scale=Math.abs(grip.root.getWorldScale(V()).y);
 for(const [ball,radius] of balls){
  if(ball.parent!==grip.root){balls.delete(ball);continue;}
  ball.position.copy(grip.ballCenter);ball.scale.setScalar(grip.ballRadius/(radius*Math.max(scale,1e-8)));
 }
}
