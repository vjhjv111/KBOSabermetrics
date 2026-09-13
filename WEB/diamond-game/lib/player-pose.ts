import * as THREE from "three";
import {createPlayer as player,getPitchGrip} from "./player-model";
import {posePitchGripArm} from './player-pitch-grip';
import {swingPose,pitchingPose,MOUND_HEIGHT} from "./player-motion";
import {poseBattingGaze} from './batting-gaze';
import {playerDimensions} from './player-appearance';
import {BATTING_WRIST_CUFFS} from './player-equipment';
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
const clamp=(v:number,a:number,b:number)=>Math.max(a,Math.min(b,v));
// Two axes define the bone's roll as well as its direction. A shortest-arc
// quaternion alone flips at the upward arm position during a throw or swing.
function boneFrame(bone:THREE.Object3D,tip:THREE.Vector3,planeNormal:THREE.Vector3){
 const y=bone.position.clone().sub(tip).normalize(),x=planeNormal.clone().addScaledVector(y,-planeNormal.dot(y)).normalize(),z=x.clone().cross(y).normalize();
 bone.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(x,y,z));
}
// Solve each elbow so both wrists stay attached to the bat instead of swinging a single rigid arm.
export function poseArm(upper:THREE.Object3D,lower:THREE.Object3D,wrist:THREE.Vector3,bend:THREE.Vector3){
 upper.updateWorldMatrix(true,true);const boneLength=.34*Math.abs(upper.getWorldScale(V()).y),shoulder=upper.getWorldPosition(V()),delta=wrist.clone().sub(shoulder),length=Math.min(boneLength*2-.001,Math.max(.02,delta.length())),direction=delta.normalize();
 const perpendicular=bend.clone().addScaledVector(direction,-bend.dot(direction)).normalize();
 const elbow=shoulder.clone().addScaledVector(direction,length/2).addScaledVector(perpendicular,Math.sqrt(boneLength**2-(length/2)**2));
 const localElbow=upper.parent!.worldToLocal(elbow.clone()),localWrist=upper.parent!.worldToLocal(wrist.clone());
 const plane=localElbow.clone().sub(upper.position).cross(localWrist.clone().sub(localElbow)).normalize();
 boneFrame(upper,localElbow,plane);upper.updateWorldMatrix(false,true);
 const forearmTarget=lower.parent!.worldToLocal(wrist.clone()),localShoulder=lower.parent!.worldToLocal(shoulder.clone());
 const forearmPlane=lower.position.clone().sub(localShoulder).cross(forearmTarget.clone().sub(lower.position)).normalize();
 boneFrame(lower,forearmTarget,forearmPlane);lower.updateWorldMatrix(false,true);
}
/** Solve to the real skin wrist ring, whose centre is .341 m below the elbow
 * and .006 m forward. The original .34 m elbow pivot never moves or stretches. */
function poseCuffArm(upper:THREE.Object3D,lower:THREE.Object3D,target:THREE.Vector3,pole:THREE.Vector3,depth:number){
 const anchor=V(0,-.341,.006*depth);upper.updateWorldMatrix(true,true);
 const scale=Math.abs(upper.getWorldScale(V()).y),shoulder=upper.getWorldPosition(V()),delta=target.clone().sub(shoulder),l1=.34*scale,l2=anchor.length()*scale;
 const length=THREE.MathUtils.clamp(delta.length(),Math.abs(l1-l2)+.001,l1+l2-.001),along=delta.normalize(),bend=pole.clone().addScaledVector(along,-pole.dot(along)).normalize();
 const projection=(l1*l1-l2*l2+length*length)/(2*length),elbow=shoulder.clone().addScaledVector(along,projection).addScaledVector(bend,Math.sqrt(Math.max(0,l1*l1-projection*projection)));
 const reached=shoulder.clone().addScaledVector(along,length),localElbow=upper.parent!.worldToLocal(elbow.clone()),localTarget=upper.parent!.worldToLocal(reached.clone());
 const y=upper.position.clone().sub(localElbow).normalize(),x=localElbow.clone().sub(upper.position).cross(localTarget.clone().sub(localElbow)).normalize(),z=x.clone().cross(y).normalize();
 upper.quaternion.setFromRotationMatrix(new THREE.Matrix4().makeBasis(x,y,z));upper.updateWorldMatrix(false,true);
 const tip=lower.parent!.worldToLocal(reached),localShoulder=lower.parent!.worldToLocal(shoulder),a=anchor.clone().normalize(),d=tip.clone().sub(lower.position).normalize();
 const normal=lower.position.clone().sub(localShoulder).cross(tip.clone().sub(lower.position)).normalize(),source=new THREE.Matrix4().makeBasis(V(1,0,0),a,V(1,0,0).cross(a)),dest=new THREE.Matrix4().makeBasis(normal,d,normal.clone().cross(d));
 lower.quaternion.setFromRotationMatrix(dest.multiply(source.transpose()));lower.updateWorldMatrix(false,true);
}
// Solve knees in player-local space so mirrored players keep the same planted-foot mechanics.
export function poseLeg(thigh:THREE.Object3D,knee:THREE.Object3D,foot:THREE.Object3D,target:THREE.Vector3,footPitch=0,footYaw=0){
 const delta=target.clone().sub(thigh.position),distance=clamp(delta.length(),.025,.859),direction=delta.normalize();
 const bend=V(0,0,1).addScaledVector(direction,-direction.z).normalize();
 const elbow=thigh.position.clone().addScaledVector(direction,distance*.5).addScaledVector(bend,Math.sqrt(.43*.43-distance*distance*.25));
 thigh.quaternion.setFromUnitVectors(V(0,-1,0),elbow.clone().sub(thigh.position).normalize());
 const shin=target.clone().sub(elbow).normalize().applyQuaternion(thigh.quaternion.clone().invert());
 knee.quaternion.setFromUnitVectors(V(0,-1,0),shin);
 // Independent ankles keep a planted shoe level while the knee bends above it.
 foot.quaternion.copy(thigh.quaternion).multiply(knee.quaternion).invert().multiply(new THREE.Quaternion().setFromEuler(new THREE.Euler(footPitch,footYaw,0,"YXZ")));
}
export function poseBatter(batter:ReturnType<typeof player>,mirror:THREE.Group,bat:THREE.Group,pose:ReturnType<typeof swingPose>){
 const scale=Math.abs(mirror.scale.y),hand=Math.sign(mirror.scale.x)||1;
 // The shaft is axially symmetric, but its attached gloves must mirror with
 // the player. This preserves every point on the bat axis, including contact.
 bat.scale.set(hand*scale,scale,scale);bat.position.set(pose.grip[0]*scale,pose.grip[1]*scale,pose.grip[2]*scale);bat.quaternion.setFromUnitVectors(V(0,1,0),V(...pose.axis));bat.updateWorldMatrix(true,true);
 const crouch=Math.max(pose.crouch+.045*(1-pose.reach),.075+Math.abs(pose.bodyShift)*.18);
 batter.root.position.x=-.92+pose.bodyShift;batter.root.rotation.y=Math.PI/2+pose.rootTurn;
 // Independent absolute yaw tracks let the hips start first without dragging the shoulders open.
 batter.hips.position.set(0,.93-crouch,pose.weightShift);batter.hips.rotation.y=pose.pelvisTurn-pose.rootTurn;
 batter.torso.position.set(0,.95-crouch,pose.weightShift);
 batter.torso.rotation.set(.12+pose.reach*.18,pose.torsoTurn-pose.rootTurn,-.04);
 batter.head.rotation.set(-pose.reach*.07,Math.PI/2-pose.torsoTurn,0);
 batter.ll.position.set(.115,.91-crouch,.02+pose.weightShift);batter.rl.position.set(-.115,.91-crouch,-.02+pose.weightShift);
 mirror.updateWorldMatrix(true,true);
 // The front foot lifts in the load, then braces. The rear heel turns after contact rather than sliding.
 const front=batter.root.worldToLocal(mirror.localToWorld(V(-.925,.034+pose.frontLift,-.255+pose.stride)));
 const rearYaw=Math.PI/2+.09+pose.pelvisTurn*.4,toe=V(-.925+Math.sin(Math.PI/2+.09)*.22,.034,.355+Math.cos(Math.PI/2+.09)*.22);
 // Rotate around the rear toe: lifting the heel must not lift or slide the whole shoe.
 const rear=batter.root.worldToLocal(mirror.localToWorld(toe.add(V(-Math.sin(rearYaw)*Math.cos(pose.heel)*.22,Math.sin(pose.heel)*.22,-Math.cos(rearYaw)*Math.cos(pose.heel)*.22))));
 poseLeg(batter.ll,batter.lk,batter.lf,front,0,-.13-pose.rootTurn);
 poseLeg(batter.rl,batter.rk,batter.rf,rear,pose.heel,.09+pose.pelvisTurn*.4-pose.rootTurn);
 const leftWrist=bat.localToWorld(V(...BATTING_WRIST_CUFFS.left)),rightWrist=bat.localToWorld(V(...BATTING_WRIST_CUFFS.right)),depth=playerDimensions(batter.appearance).depthScale;
 // A short hitter can miss an outer/high cuff by a few millimetres during an
 // unloaded start. Smoothly translate the chest just enough to keep both fixed
 // arm segments reachable; pelvis, feet and bat tracks remain unchanged.
 const maxReach=(.34+V(0,-.341,.006*depth).length())*scale-.001;
 for(const [upper,target]of[[batter.left,leftWrist],[batter.right,rightWrist]] as const){
  const delta=target.clone().sub(upper.getWorldPosition(V())),distance=delta.length(),excess=distance-maxReach,band=.002*scale;
  const amount=excess<=-band?0:excess>=band?excess:(excess+band)**2/(4*band);
  if(amount>0){const origin=batter.torso.getWorldPosition(V()),next=origin.clone().addScaledVector(delta,amount/distance);batter.torso.position.add(batter.root.worldToLocal(next).sub(batter.root.worldToLocal(origin)));batter.root.updateWorldMatrix(true,true);}
 }
 poseCuffArm(batter.left,batter.le,leftWrist,V(-.86268*hand,-.2821,-.41977),depth);poseCuffArm(batter.right,batter.re,rightWrist,V(-.48159*hand,-.6925,.53714),depth);
 if(pose.gazeWeight>0){const target=V(...pose.gazeTarget).multiplyScalar(scale);if(bat.parent)bat.parent.localToWorld(target);poseBattingGaze(batter.head,target,pose.gazeWeight);}
 return {leftWrist,rightWrist};
}
export function posePitcher(pitcher:ReturnType<typeof player>,delivery:ReturnType<typeof pitchingPose>,throwSign:number,_legacyUnderhand=false,breath=0){
 pitcher.root.position.y=MOUND_HEIGHT;
 pitcher.hips.position.set(delivery.hipShift,.93-delivery.drop,delivery.forward);pitcher.hips.rotation.y=delivery.hips;
 pitcher.torso.position.set(delivery.hipShift,.95-delivery.drop+breath,delivery.forward);
 pitcher.torso.rotation.set(delivery.lean,delivery.coil,delivery.sideBend);
 pitcher.head.rotation.set(-delivery.lean*.6,-delivery.coil*.75,0);
 // Thigh roots follow the pelvis while the planted ankles remain in mound space.
 const hipX=Math.cos(delivery.hips)*.115,hipZ=Math.sin(delivery.hips)*.115;
 pitcher.ll.position.set(delivery.hipShift+hipX,.91-delivery.drop,delivery.forward-hipZ);pitcher.rl.position.set(delivery.hipShift-hipX,.91-delivery.drop,delivery.forward+hipZ);
 poseLeg(pitcher.ll,pitcher.lk,pitcher.lf,V(...delivery.lead),delivery.leadPitch,delivery.leadYaw);
 poseLeg(pitcher.rl,pitcher.rk,pitcher.rf,V(...delivery.trail),delivery.heel,delivery.trailYaw);
 pitcher.root.updateWorldMatrix(true,true);
 const releaseHand=pitcher.root.localToWorld(V(...delivery.hand));
 const grip=getPitchGrip(pitcher),bend=V(delivery.throwElbow[0]*throwSign,delivery.throwElbow[1],delivery.throwElbow[2]);
 if(grip)posePitchGripArm(pitcher.right,pitcher.re,releaseHand,bend,grip);else poseArm(pitcher.right,pitcher.re,releaseHand,bend);
 poseArm(pitcher.left,pitcher.le,pitcher.root.localToWorld(V(...delivery.glove)),V(delivery.gloveElbow[0]*throwSign,delivery.gloveElbow[1],delivery.gloveElbow[2]));
 return releaseHand;
}
