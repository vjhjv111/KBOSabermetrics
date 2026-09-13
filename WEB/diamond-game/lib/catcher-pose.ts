import * as THREE from 'three';
import type {PlayerModel} from './player-model';
import {poseArm,poseLeg} from './player-pose';
import {playerDimensions} from './player-appearance';

export type CatcherPoint={x:number;y:number;z:number};
export type CatcherPoseOptions={now:number;target?:CatcherPoint;receiveAt?:number;prepareAt?:number;catchBall?:boolean;cancelAt?:number;groundY?:number;ballRadius?:number};
export type CatcherPhase='ready'|'prepare'|'receive'|'absorb'|'recover';
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z),clamp=THREE.MathUtils.clamp;
const ease=(value:number,start:number,end:number)=>THREE.MathUtils.smoothstep(value,start,Math.max(start+.001,end));
type PoseState={key:string;footHeights:[number,number];glove:THREE.Object3D|null;catchKey:string;catchPocket:THREE.Vector3;reachError:number;catchable:boolean;blocking:boolean};
const states=new WeakMap<PlayerModel,PoseState>();

function footHeight(foot:THREE.Object3D){
 foot.updateWorldMatrix(true,true);const inverse=foot.matrixWorld.clone().invert(),point=V();let bottom=Infinity;
 foot.traverse(object=>{if(!(object instanceof THREE.Mesh)||object instanceof THREE.SkinnedMesh)return;
  const positions=object.geometry.getAttribute('position'),matrix=inverse.clone().multiply(object.matrixWorld);
  for(let i=0;i<positions.count;i++){point.fromBufferAttribute(positions,i).applyMatrix4(matrix);bottom=Math.min(bottom,point.y);}
 });
 return Number.isFinite(bottom)?-bottom:.039;
}
function findGlove(model:PlayerModel){
 return model.le.children.find(child=>{let found=false;child.traverse(object=>{
  if(object.name==='Deep leather mitt pocket'||(object.userData.playerBatchParts as {name:string}[]|undefined)?.some(part=>part.name==='Deep leather mitt pocket'))found=true;
 });return found;})??null;
}
function stateFor(model:PlayerModel){
 let state=states.get(model);
 if(!state||state.key!==model.appearanceKey){
  state={key:model.appearanceKey,footHeights:[footHeight(model.lf),footHeight(model.rf)],glove:findGlove(model),catchKey:'',catchPocket:V(),reachError:Infinity,catchable:false,blocking:false};states.set(model,state);
 }
 return state;
}

// Solve in an outward-turned knee plane, then return to the original root
// frame. The existing pivots, lengths and planted ankle targets are unchanged.
function spreadLeg(thigh:THREE.Object3D,knee:THREE.Object3D,foot:THREE.Object3D,target:THREE.Vector3,spread:number,yaw:number){
 const original=thigh.position.clone(),rotation=new THREE.Quaternion().setFromAxisAngle(V(0,1,0),spread),inverse=rotation.clone().invert();
 thigh.position.applyQuaternion(inverse);
 poseLeg(thigh,knee,foot,target.clone().applyQuaternion(inverse),0,yaw-spread);
 thigh.position.copy(original);thigh.quaternion.premultiply(rotation);
}
function rootDirection(model:PlayerModel,direction:THREE.Vector3){return direction.transformDirection(model.root.matrixWorld);}
function rootPoint(model:PlayerModel,point:THREE.Vector3){return model.root.localToWorld(point);}
function setBody(model:PlayerModel,state:PoseState,target:THREE.Vector3,breath=0){
 const low=1-ease(target.y,.48,.86),high=ease(target.y,1.02,1.85),shift=clamp(target.x*.40,-.25,.25);
 // Stand straighter for a mask-height receive. An overhead extension above
 // the mask can lean into the reach again without crowding the held ball.
 const lean=.30+low*.16-high*.24+.14*ease(high,.7,1);
 const hip=.36-low*.105+high*.38+breath,z=-.10+high*.025;
 model.hips.position.set(shift,hip,z);model.hips.rotation.set(0,0,0);
 model.torso.position.set(shift,hip+.02,z+.03);model.torso.rotation.set(lean,clamp(target.x*.11,-.12,.12),clamp(-target.x*.40,-.23,.23));
 model.head.rotation.set(-lean*.85,-model.torso.rotation.y*.7,0);
 model.ll.position.set(shift+.115,hip-.02,z);model.rl.position.set(shift-.115,hip-.02,z);
 const spread=.55-low*.04-high*.08;
 spreadLeg(model.ll,model.lk,model.lf,V(.255,state.footHeights[0],.11),spread,.28);
 spreadLeg(model.rl,model.rk,model.rf,V(-.255,state.footHeights[1],.11),-spread,-.28);
 model.root.updateWorldMatrix(true,true);
 return {low,high,hip};
}

function gloveFrame(model:PlayerModel,state:PoseState,ballRadius:number,roll:number){
 const depth=playerDimensions(model.appearance).depthScale,scale=Math.abs(model.root.getWorldScale(V()).y)||1;
 const wrist=V(0,-.341,.006*depth),cuff=V(0,-.118,-.036*depth),anchor=V(0,-.02,-.052*depth+ballRadius/scale);
 const desired=new THREE.Quaternion().setFromEuler(new THREE.Euler(0,0,roll));
 // Keep the opening toward the pitcher while the wrist articulates, and seat
 // the actual forearm endpoint in the cuff even after a body-depth change.
 const relative=model.torso.quaternion.clone().multiply(model.left.quaternion).multiply(model.le.quaternion);
 if(state.glove){
  state.glove.quaternion.copy(relative.invert().multiply(desired));
  state.glove.position.copy(wrist).sub(cuff.clone().multiply(state.glove.scale).applyQuaternion(state.glove.quaternion));
  state.glove.updateWorldMatrix(false,true);
 }
 return {wrist,cuff,anchor,desired};
}
function posePocket(model:PlayerModel,state:PoseState,localTarget:THREE.Vector3,ballRadius:number){
 const scale=Math.abs(model.root.getWorldScale(V()).y)||1,roll=clamp(-localTarget.x*.16,-.20,.20);
 const frame=gloveFrame(model,state,ballRadius,roll),offset=frame.anchor.clone().sub(frame.cuff).applyQuaternion(frame.desired);
 const target=rootPoint(model,localTarget.clone()),guess=rootPoint(model,localTarget.clone().sub(offset)),shoulder=model.left.getWorldPosition(V());
 const bend=rootDirection(model,V(1,-.32,.08)),maxReach=.665*scale,pocket=V();
 for(let iteration=0;iteration<4;iteration++){
  const delta=guess.clone().sub(shoulder),length=delta.length();
  if(length>maxReach)guess.copy(shoulder).addScaledVector(delta,maxReach/length);
  if(length<.07*scale)guess.copy(shoulder).addScaledVector(delta.normalize(),.07*scale);
  poseArm(model.left,model.le,guess,bend);gloveFrame(model,state,ballRadius,roll);
  if(state.glove)pocket.copy(state.glove.localToWorld(frame.anchor.clone()));else pocket.copy(model.le.localToWorld(V(0,-.34,0)));
  guess.add(target.clone().sub(pocket));
 }
 return pocket;
}
function poseAt(model:PlayerModel,state:PoseState,target:THREE.Vector3,ballRadius:number,breath=0){
 setBody(model,state,target,breath);
 const pocket=posePocket(model,state,target,ballRadius);
 const protectedHand=model.torso.localToWorld(V(-.07,.30,.24));
 poseArm(model.right,model.re,protectedHand,rootDirection(model,V(-1,-.35,.1)));
 model.root.updateWorldMatrix(true,true);return pocket;
}

/** A deterministic receiving pose. Input and returned ball-centre anchors are
 * world coordinates; the caller keeps ownership of the pitch and ball physics.
 * Callers retain the previous target + cancelAt for a smooth cancelled-pitch return.
 */
export function poseCatcher(model:PlayerModel,options:CatcherPoseOptions){
 const groundY=Number.isFinite(options.groundY)?options.groundY!:0,ballRadius=Number.isFinite(options.ballRadius)?Math.max(0,options.ballRadius!):.065;
 model.root.updateWorldMatrix(true,false);const origin=model.root.getWorldPosition(V());origin.y=groundY;
 model.root.position.copy(model.root.parent?model.root.parent.worldToLocal(origin):origin);
 model.root.updateWorldMatrix(true,true);
 const state=stateFor(model),ready=V(.09,.80,.46),target=options.target&&[options.target.x,options.target.y,options.target.z].every(Number.isFinite)?V(options.target.x,options.target.y,options.target.z):null;
 const receiveAt=options.receiveAt,hasReceive=!!target&&Number.isFinite(receiveAt),localTarget=target?model.root.worldToLocal(target.clone()):ready.clone();
 const scale=Math.abs(model.root.getWorldScale(V()).y)||1,catchKey=JSON.stringify([target?.toArray(),groundY,ballRadius,model.root.matrixWorld.toArray(),model.appearance.bodyType]);
 if(catchKey!==state.catchKey){
  state.catchKey=catchKey;state.blocking=!!target&&localTarget.y<.38;
  const safe=localTarget.clone();safe.y=Math.max(.17,safe.y);
  state.catchPocket.copy(poseAt(model,state,safe,ballRadius));
  state.reachError=target?state.catchPocket.distanceTo(target):0;
  // A wide/off-the-ground pitch remains a miss/block. Do not teleport a ball
  // to an unreachable pocket or change baseball outcomes to fit the animation.
  state.catchable=!!target&&!!state.glove&&localTarget.y>=.17&&state.reachError<=.022*scale;
 }
 let blend=0,absorb=0,phase:CatcherPhase='ready';
 if(hasReceive){
  const at=receiveAt!,prepare=Number.isFinite(options.prepareAt)?options.prepareAt!:at-650;
  const cancelled=Number.isFinite(options.cancelAt),cancelAt=options.cancelAt!;
  blend=ease(options.now,prepare,at-35);
  if(cancelled&&options.now>=cancelAt){blend=ease(cancelAt,prepare,at-35)*(1-ease(options.now,cancelAt,cancelAt+300));phase=blend>0?'recover':'ready';}
  else if(options.now<at)phase=blend>0?'prepare':'ready';
  else{
   const age=options.now-at,recovery=ease(age,320,950);blend*=1-recovery;
   if(options.catchBall!==false&&state.catchable&&!cancelled)absorb=ease(age,0,160)*(1-ease(age,300,850));
   phase=age<40?'receive':age<320&&absorb>0?'absorb':blend>0?'recover':'ready';
  }
 }
 const actual=ready.clone().lerp(localTarget,blend);actual.y=Math.max(.17,actual.y);
 // Receive with a small downward give. A deep backward pull would drive a
 // held 65 mm ball into the mask on high/central pitches.
 actual.z-=.012*absorb;actual.y-=.025*absorb;actual.x-=clamp(actual.x,-.2,.2)*.12*absorb;
 const pocket=poseAt(model,state,actual,ballRadius,Math.sin(options.now/1100)*.002*(1-blend));
 return {pocket,catchPocket:state.catchPocket.clone(),reachError:state.reachError,catchable:state.catchable&&options.catchBall!==false,blocking:state.blocking,phase,receiveProgress:blend,absorption:absorb};
}
