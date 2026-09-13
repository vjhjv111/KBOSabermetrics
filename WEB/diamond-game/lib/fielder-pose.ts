import * as THREE from 'three';
import type {PlayerModel} from './player-model';
import type {FieldPlayTimeline} from './field-play-timeline';
import {poseArm,poseLeg} from './player-pose';
import {playerDimensions} from './player-appearance';

export type FieldPoint={x:number;y:number;z:number};
export type RunningOptions={now:number;travel:number;speed:number;nominalSpeed?:number;stoppedFor?:number;samplePath?:(travel:number)=>FieldPoint;groundY?:number};
export type FieldingOptions={groundY?:number;ballRadius?:number;movement?:RunningOptions};
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z),clamp=THREE.MathUtils.clamp;
const ease=(n:number,a=0,b=1)=>THREE.MathUtils.smoothstep(n,a,Math.max(a+.0001,b));
type RigState={key:string;feet:[number,number];glove:THREE.Object3D|null};
const states=new WeakMap<PlayerModel,RigState>();
function footHeight(foot:THREE.Object3D){
 foot.updateWorldMatrix(true,true);const inverse=foot.matrixWorld.clone().invert(),p=V();let low=Infinity;
 foot.traverse(mesh=>{if(!(mesh instanceof THREE.Mesh)||mesh instanceof THREE.SkinnedMesh)return;
  const matrix=inverse.clone().multiply(mesh.matrixWorld),a=mesh.geometry.getAttribute('position');
  for(let i=0;i<a.count;i++)low=Math.min(low,p.fromBufferAttribute(a,i).applyMatrix4(matrix).y);
 });return Number.isFinite(low)?-low:.039;
}
function stateFor(model:PlayerModel){
 let state=states.get(model);if(state?.key===model.appearanceKey)return state;
 const glove=model.le.children.find(child=>{let found=false;child.traverse(o=>{if(o.name==='Deep leather mitt pocket'||(o.userData.playerBatchParts as {name:string}[]|undefined)?.some(part=>part.name==='Deep leather mitt pocket'))found=true;});return found;})??null;
 state={key:model.appearanceKey,feet:[footHeight(model.lf),footHeight(model.rf)],glove};states.set(model,state);return state;
}
function groundRoot(model:PlayerModel,groundY:number,updateChildren=true){
 model.root.updateWorldMatrix(true,false);const p=model.root.getWorldPosition(V());p.y=groundY;
 model.root.position.copy(model.root.parent?model.root.parent.worldToLocal(p):p);model.root.updateWorldMatrix(true,updateChildren);
}
function direction(model:PlayerModel,v:THREE.Vector3){return v.transformDirection(model.root.matrixWorld);}
function world(model:PlayerModel,v:THREE.Vector3){return model.root.localToWorld(v);}
function setBody(model:PlayerModel,hip:number,lean:number,twist=0,shift=0,back=0){
 model.hips.position.set(shift,hip,back);model.hips.rotation.set(0,twist*.3,0);
 model.torso.position.set(shift,hip+.02,back+.015);model.torso.rotation.set(lean,twist,0);
 model.head.rotation.set(-lean*.65,-twist*.65,0);
 const x=Math.cos(twist*.3)*.115,z=Math.sin(twist*.3)*.115;
 model.ll.position.set(shift+x,hip-.02,back-z);model.rl.position.set(shift-x,hip-.02,back+z);
}
function resetGlove(model:PlayerModel,state:RigState){
 if(!state.glove)return;const depth=playerDimensions(model.appearance).depthScale;
 state.glove.rotation.set(0,0,Math.PI);state.glove.position.set(0,-.459,.042*depth);
}

/** Distance-driven stance: during support the foot samples a fixed position
 * on the path, so advancing the root cannot slide it along the ground.
 * samplePath is world-space and should extrapolate beyond the path endpoints.
 */
export function poseRunning(model:PlayerModel,options:RunningOptions){
 const groundY=options.groundY??0;groundRoot(model,groundY,false);const state=stateFor(model);
 const scale=Math.abs(model.root.getWorldScale(V()).y)||1,hand=Math.sign(model.root.getWorldScale(V()).x)||1;
 const travel=options.travel||0,stopping=options.speed<=.1&&travel>0&&(options.nominalSpeed??0)>.1&&Number.isFinite(options.stoppedFor)&&options.stoppedFor!>=0;
 const settle=stopping?ease(options.stoppedFor!,0,350):options.speed>.1?0:1,speed=Math.max(0,stopping?options.nominalSpeed!:options.speed),moving=speed>.1&&settle<1;
 const period=clamp(1.4+speed/scale*.25,1.7,3.35)*scale,duty=.24,phase=((travel/period)%1+1)%1;
 const rootPosition=model.root.getWorldPosition(V()),forward=direction(model,V(0,0,1));
 const path=options.samplePath??((d:number)=>rootPosition.clone().addScaledVector(forward,d-travel));
 const footTargets:[THREE.Vector3,THREE.Vector3]=[V(),V()],yaws=[0,0],planted=[true,true];
 for(let i=0;i<2;i++){
  const side=i===0?1:-1;
  if(!moving){footTargets[i].copy(world(model,V(side*.155,state.feet[i],i===0?.035:-.035)));continue;}
  const offset=i*.5,cycle=travel/period+offset,index=Math.floor(cycle),t=cycle-index,start=(index-offset)*period;
  const stance=start+period*duty*.5,u=clamp((t-duty)/(1-duty),0,1),amount=ease(u),at=t<duty?stance:stance+period*amount;
  const center=V().copy(path(at)),a=V().copy(path(at-.025)),b=V().copy(path(at+.025)),tangent=b.sub(a);tangent.y=0;
  if(tangent.lengthSq()<1e-12)tangent.copy(forward);else tangent.normalize();
  const lateral=V(tangent.z,0,-tangent.x).multiplyScalar(side*.145*scale*hand);
  footTargets[i].copy(center).add(lateral);planted[i]=t<duty;
  footTargets[i].y=groundY+state.feet[i]*scale+(planted[i]?0:Math.sin(Math.PI*u)*.22*scale);
  const local=tangent.transformDirection(model.root.matrixWorld.clone().invert());yaws[i]=Math.atan2(local.x,local.z);
  if(stopping){
   const rest=world(model,V(side*.155,state.feet[i],i===0?.035:-.035));footTargets[i].lerp(rest,settle);footTargets[i].y+=Math.sin(Math.PI*settle)*.045*scale;
   yaws[i]*=1-settle;if(settle>0)planted[i]=false;
  }
 }
 const local=footTargets.map(p=>model.root.worldToLocal(p.clone()));
 const restHip=.855+Math.sin(options.now/1300)*.0015;
 let hip=moving?THREE.MathUtils.lerp(.82+.012*Math.max(0,Math.sin(phase*Math.PI*4)),restHip,settle):restHip;
 // Keep both original .43 m bones inside their actual reach at the largest
 // stride and around a corner; do not let the IK clamp lift a planted foot.
 for(let i=0;i<2;i++){
  const dx=local[i].x-(i===0?.115:-.115),dz=local[i].z;
  hip=Math.min(hip,local[i].y+.02+Math.sqrt(Math.max(.01,.853**2-dx*dx-dz*dz)));
 }
 setBody(model,hip,moving?THREE.MathUtils.lerp(.19,.07,settle):.07);resetGlove(model,state);
 poseLeg(model.ll,model.lk,model.lf,local[0],0,yaws[0]);poseLeg(model.rl,model.rk,model.rf,local[1],0,yaws[1]);
 const swing=Math.sin(phase*Math.PI*2);
 model.left.rotation.set(moving?THREE.MathUtils.lerp(-.32-swing*.53,-.27,settle):-.27,0,.08);model.right.rotation.set(moving?THREE.MathUtils.lerp(-.32+swing*.53,-.27,settle):-.27,0,-.08);
 model.le.rotation.set(moving?THREE.MathUtils.lerp(-.95,-.65,settle):-.65,0,0);model.re.rotation.set(moving?THREE.MathUtils.lerp(-.95,-.65,settle):-.65,0,0);model.root.updateWorldMatrix(true,true);
 return {leftFoot:model.lf.getWorldPosition(V()),rightFoot:model.rf.getWorldPosition(V()),leftPlanted:planted[0],rightPlanted:planted[1],phase,periodDistance:period};
}

function plantedBody(model:PlayerModel,state:RigState,target:THREE.Vector3,amount:number,twist=0){
 const low=1-ease(target.y,.25,1.15),hip=.845-low*.455*amount,lean=.08+low*.97*amount;
 // Hinge back over the planted shoes instead of squatting onto the heels.
 // This keeps the shoulders behind the gather while preserving arm reach.
 const back=clamp(target.z-.52,-.28,-.12)*low*amount;
 setBody(model,hip,lean,twist,(clamp(target.x*.18,-.12,.12)-low*.10)*amount,back);
 poseLeg(model.ll,model.lk,model.lf,V(.16,state.feet[0],.055));poseLeg(model.rl,model.rk,model.rf,V(-.16,state.feet[1],-.045));
 model.root.updateWorldMatrix(true,true);return low;
}
function gloveFrame(model:PlayerModel,state:RigState,radius:number,desired:THREE.Quaternion){
 const depth=playerDimensions(model.appearance).depthScale,scale=Math.abs(model.root.getWorldScale(V()).y)||1;
 const wrist=V(0,-.341,.006*depth),cuff=V(0,-.118,-.036*depth),anchor=V(0,-.02,-.052*depth+radius/scale);
 if(state.glove){
  const relative=model.torso.quaternion.clone().multiply(model.left.quaternion).multiply(model.le.quaternion);
  state.glove.quaternion.copy(relative.invert().multiply(desired));state.glove.position.copy(wrist).sub(cuff.clone().multiply(state.glove.scale).applyQuaternion(state.glove.quaternion));state.glove.updateWorldMatrix(false,true);
 }return {anchor,cuff};
}
function pocketAt(model:PlayerModel,state:RigState,target:THREE.Vector3,radius:number,low:number){
 // Gather a rolling ball from above with the fingers forward. An upward-
 // facing cup would put its backing below the fixed low ball/ground plane.
 const scale=Math.abs(model.root.getWorldScale(V()).y)||1,desired=new THREE.Quaternion().setFromEuler(new THREE.Euler(1.25*low,0,0));
 const frame=gloveFrame(model,state,radius,desired),offset=frame.anchor.clone().sub(frame.cuff).applyQuaternion(desired);
 const local=model.root.worldToLocal(target.clone()),guess=world(model,local.sub(offset)),shoulder=model.left.getWorldPosition(V()),point=V();
 for(let i=0;i<6;i++){
  const d=guess.clone().sub(shoulder);if(d.length()>.665*scale)guess.copy(shoulder).addScaledVector(d,.665*scale/d.length());
  poseArm(model.left,model.le,guess,direction(model,V(1,-.35,.08)));gloveFrame(model,state,radius,desired);
  point.copy(state.glove?state.glove.localToWorld(frame.anchor.clone()):model.le.localToWorld(V(0,-.34,0)));guess.add(target.clone().sub(point));
 }return point;
}
function handAt(model:PlayerModel,target:THREE.Vector3){
 // The curved finger pads end around y=-.38; the ball centre is outside
 // their front surface. The actual wrist remains on the original .34 m bone.
 const depth=playerDimensions(model.appearance).depthScale,anchor=V(0,-.378,.088*depth),scale=Math.abs(model.root.getWorldScale(V()).y)||1;
 const upper=model.right,lower=model.re;upper.updateWorldMatrix(true,true);
 const shoulder=upper.getWorldPosition(V()),delta=target.clone().sub(shoulder),l1=.34*scale,l2=anchor.length()*scale;
 const length=clamp(delta.length(),Math.abs(l1-l2)+.001,l1+l2-.001),along=delta.normalize(),bend=direction(model,V(-1,.1,-.25));
 bend.addScaledVector(along,-bend.dot(along)).normalize();
 const projection=(l1*l1-l2*l2+length*length)/(2*length),elbow=shoulder.clone().addScaledVector(along,projection).addScaledVector(bend,Math.sqrt(Math.max(0,l1*l1-projection*projection)));
 const reached=shoulder.clone().addScaledVector(along,length),localElbow=upper.parent!.worldToLocal(elbow.clone()),localTarget=upper.parent!.worldToLocal(reached.clone());
 const y=upper.position.clone().sub(localElbow).normalize(),x=localElbow.clone().sub(upper.position).cross(localTarget.clone().sub(localElbow)).normalize(),z=x.clone().cross(y).normalize();
 upper.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(x,y,z));upper.updateWorldMatrix(false,true);
 // The effective second segment ends at the finger/ball anchor, not at the
 // wrist. Map its full local vector, preserving both original .34 m bones.
 const tip=lower.parent!.worldToLocal(reached),localShoulder=lower.parent!.worldToLocal(shoulder),a=anchor.clone().normalize(),d=tip.clone().sub(lower.position).normalize();
 const normal=lower.position.clone().sub(localShoulder).cross(tip.clone().sub(lower.position)).normalize(),source=new THREE.Matrix4().makeBasis(V(1,0,0),a,V(1,0,0).cross(a)),dest=new THREE.Matrix4().makeBasis(normal,d,normal.clone().cross(d));
 lower.quaternion.setFromRotationMatrix(dest.multiply(source.transpose()));lower.updateWorldMatrix(false,true);
 return lower.localToWorld(anchor);
}

/** Existing field-ball clocks and world targets remain authoritative. This
 * articulates the mesh to their contact/release anchors; it never moves a ball.
 */
export function poseFielding(model:PlayerModel,timeline:FieldPlayTimeline,now:number,receiving=false,options:FieldingOptions={}){
 groundRoot(model,options.groundY??0);const state=stateFor(model),radius=options.ballRadius??.078;
 const target=V().copy(receiving?timeline.throwTarget??timeline.fieldTarget:timeline.fieldTarget),localTarget=model.root.worldToLocal(target.clone());
 const at=receiving?timeline.throwArrivesAt??timeline.fieldedAt:timeline.fieldedAt,prepare=ease(now,at-450,at),recover=ease(now,at,at+350);
 const movement=options.movement,arrival=movement&&Number.isFinite(movement.stoppedFor)?now-movement.stoppedFor!:now;
 const handoff=movement&&movement.travel>0&&(movement.nominalSpeed??0)>.1?ease(movement.stoppedFor??0,0,clamp(at-arrival,60,180)):1;
 const parts:THREE.Object3D[]=[model.hips,model.torso,model.head,model.left,model.right,model.le,model.re,model.ll,model.rl,model.lk,model.rk,model.lf,model.rf,...(state.glove?[state.glove]:[])];
 let incoming:{position:THREE.Vector3;rotation:THREE.Quaternion}[]|null=null,incomingFeet:THREE.Vector3[]=[],incomingYaw:number[]=[];
 if(handoff<1&&movement){
  poseRunning(model,{...movement,now,speed:movement.nominalSpeed!,stoppedFor:undefined,groundY:options.groundY??movement.groundY});
  incoming=parts.map(p=>({position:p.position.clone(),rotation:p.quaternion.clone()}));
  incomingFeet=[model.lf.getWorldPosition(V()),model.rf.getWorldPosition(V())];
  incomingYaw=[model.lf,model.rf].map(foot=>{const d=foot.getWorldDirection(V()).transformDirection(model.root.matrixWorld.clone().invert());return Math.atan2(d.x,d.z);});
 }
 const low=plantedBody(model,state,localTarget,1),catchPocket=pocketAt(model,state,target,radius,low),reachError=catchPocket.distanceTo(target);
 const releaseTarget=V(timeline.fieldTarget.x,1.25,timeline.fieldTarget.z),throwAt=!receiving?timeline.throwAt:undefined;
 const throwDirection=timeline.throwTarget?V().copy(timeline.throwTarget).sub(releaseTarget):V(0,0,1),localDirection=throwDirection.transformDirection(model.root.matrixWorld.clone().invert());
 const throwYaw=clamp(Math.atan2(localDirection.x,localDirection.z),-1.15,1.15),coil=throwAt==null?0:ease(now,throwAt-250,throwAt),follow=throwAt==null?0:ease(now,throwAt,throwAt+400);
 const action=prepare*(1-recover),twist=throwYaw*(coil*.75+follow*.2);
 plantedBody(model,state,localTarget,action,twist);
 const rest=world(model,V(.22,1.10,.27)),actualTarget=rest.clone().lerp(target,action);
 const pocket=pocketAt(model,state,actualTarget,radius,low*action);
 const support=pocket.clone().add(world(model,V(-.035,.10,.018)).sub(world(model,V())));
 let handTarget=support;
 if(throwAt!=null){
  const cocked=world(model,V(-.28,1.37,-.08));
  if(now<throwAt-160)handTarget=support.clone().lerp(cocked,ease(now,Math.max(at+80,throwAt-250),throwAt-160));
  else if(now<=throwAt)handTarget=cocked.lerp(releaseTarget,ease(now,throwAt-160,throwAt));
  else handTarget=releaseTarget.clone().lerp(world(model,V(.23,.95,.32)),follow);
 }
 let releasePoint=handAt(model,handTarget);model.root.updateWorldMatrix(true,true);
 if(incoming){
  const targets=[model.lf.getWorldPosition(V()),model.rf.getWorldPosition(V())],scale=Math.abs(model.root.getWorldScale(V()).y)||1;
  parts.forEach((part,i)=>{part.position.lerpVectors(incoming![i].position,part.position.clone(),handoff);part.quaternion.slerpQuaternions(incoming![i].rotation,part.quaternion.clone(),handoff);});
  model.root.updateWorldMatrix(true,true);
  for(let i=0;i<2;i++){
   const target=incomingFeet[i].clone().lerp(targets[i],handoff);target.y+=Math.sin(Math.PI*handoff)*.035*scale;
   poseLeg(i===0?model.ll:model.rl,i===0?model.lk:model.rk,i===0?model.lf:model.rf,model.root.worldToLocal(target),0,incomingYaw[i]*(1-handoff));
  }
  const depth=playerDimensions(model.appearance).depthScale;
  if(state.glove){state.glove.position.copy(V(0,-.341,.006*depth)).sub(V(0,-.118,-.036*depth).multiply(state.glove.scale).applyQuaternion(state.glove.quaternion));state.glove.updateWorldMatrix(true,true);pocket.copy(state.glove.localToWorld(V(0,-.02,-.052*depth+radius/scale)));}
  releasePoint=model.re.localToWorld(V(0,-.378,.088*depth));model.root.updateWorldMatrix(true,true);
 }
 return {pocket,catchPocket,releasePoint,releaseTarget,reachError,catchable:!!state.glove&&reachError<=.022*(Math.abs(model.root.getWorldScale(V()).y)||1)};
}
