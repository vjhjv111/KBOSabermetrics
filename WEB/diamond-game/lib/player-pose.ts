import * as THREE from "three";
import {createPlayer as player} from "./player-model";
import {swingPose,pitchingPose,MOUND_HEIGHT} from "./player-motion";
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
const clamp=(v:number,a:number,b:number)=>Math.max(a,Math.min(b,v));
// Solve each elbow so both wrists stay attached to the bat instead of swinging a single rigid arm.
export function poseArm(upper:THREE.Object3D,lower:THREE.Object3D,wrist:THREE.Vector3,bend:THREE.Vector3){
 upper.updateWorldMatrix(true,true);const boneLength=.34*Math.abs(upper.getWorldScale(V()).y),shoulder=upper.getWorldPosition(V()),delta=wrist.clone().sub(shoulder),length=Math.min(boneLength*2-.001,Math.max(.02,delta.length())),direction=delta.normalize();
 const perpendicular=bend.clone().addScaledVector(direction,-bend.dot(direction)).normalize();
 const elbow=shoulder.clone().addScaledVector(direction,length/2).addScaledVector(perpendicular,Math.sqrt(boneLength**2-(length/2)**2));
 const localElbow=upper.parent!.worldToLocal(elbow.clone()).sub(upper.position).normalize();upper.quaternion.setFromUnitVectors(V(0,-1,0),localElbow);upper.updateWorldMatrix(false,true);
 const localWrist=lower.parent!.worldToLocal(wrist.clone()).sub(lower.position).normalize();lower.quaternion.setFromUnitVectors(V(0,-1,0),localWrist);lower.updateWorldMatrix(false,true);
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
 foot.quaternion.copy(thigh.quaternion).multiply(knee.quaternion).invert().multiply(new THREE.Quaternion().setFromEuler(new THREE.Euler(footPitch,footYaw,0)));
}
export function poseBatter(batter:ReturnType<typeof player>,mirror:THREE.Group,bat:THREE.Group,pose:ReturnType<typeof swingPose>){
 const scale=Math.abs(mirror.scale.y);bat.scale.setScalar(scale);bat.position.set(pose.grip[0]*scale,pose.grip[1]*scale,pose.grip[2]*scale);bat.quaternion.setFromUnitVectors(V(0,1,0),V(...pose.axis));bat.updateWorldMatrix(true,true);
 const crouch=Math.max(pose.crouch+.045*(1-pose.reach),.075+Math.abs(pose.bodyShift)*.18);
 batter.root.position.x=-.92+pose.bodyShift;batter.root.rotation.y=Math.PI/2+pose.turn*.25;batter.hips.position.y=.93-crouch;batter.torso.position.y=.95-crouch;batter.hips.rotation.y=pose.turn*.35;batter.torso.rotation.set(.12+pose.reach*.18,pose.turn*.8,-.04);batter.head.rotation.y=Math.PI/2-pose.turn*1.05;
 batter.ll.position.set(.115,.91-crouch,.02);batter.rl.position.set(-.115,.91-crouch,-.02);
 mirror.updateWorldMatrix(true,true);
 // The front foot lifts in the load, then braces. The rear heel turns after contact rather than sliding.
 const leadLift=Math.max(0,(pose.load-.15)/.85);
 const front=batter.root.worldToLocal(mirror.localToWorld(V(-.925,.034+leadLift*.055,-.255+leadLift*.035)));
 const heelPitch=Math.max(0,pose.turn)*.17;
 const rear=batter.root.worldToLocal(mirror.localToWorld(V(-.925,.034+Math.sin(heelPitch)*.22,.355)));
 const rootTurn=pose.turn*.25;
 poseLeg(batter.ll,batter.lk,batter.lf,front,0,-.13-rootTurn);
 poseLeg(batter.rl,batter.rk,batter.rf,rear,heelPitch,.09+pose.turn*.35-rootTurn);
 const leftWrist=bat.localToWorld(V(0,-.055,0)),rightWrist=bat.localToWorld(V(0,.065,0));
 poseArm(batter.left,batter.le,leftWrist,V(0,-1,-.2));poseArm(batter.right,batter.re,rightWrist,V(0,-1,.3));
 return {leftWrist,rightWrist};
}
export function posePitcher(pitcher:ReturnType<typeof player>,delivery:ReturnType<typeof pitchingPose>,throwSign:number,_legacyUnderhand=false,breath=0){
 pitcher.root.position.y=MOUND_HEIGHT;
 pitcher.hips.position.set(0,.93-delivery.drop,delivery.forward);pitcher.hips.rotation.y=delivery.hips;
 pitcher.torso.position.set(0,.95-delivery.drop+breath,delivery.forward);
 pitcher.torso.rotation.set(delivery.lean,delivery.coil,delivery.sideBend);
 pitcher.head.rotation.set(-delivery.lean*.6,-delivery.coil*.75,0);
 pitcher.ll.position.set(.115,.91-delivery.drop,delivery.forward);pitcher.rl.position.set(-.115,.91-delivery.drop,delivery.forward);
 poseLeg(pitcher.ll,pitcher.lk,pitcher.lf,V(...delivery.lead),0,-.08);
 poseLeg(pitcher.rl,pitcher.rk,pitcher.rf,V(...delivery.trail),delivery.heel,.08+delivery.hips*.5);
 pitcher.root.updateWorldMatrix(true,true);
 const releaseHand=pitcher.root.localToWorld(V(...delivery.hand));
 poseArm(pitcher.right,pitcher.re,releaseHand,V(delivery.throwElbow[0]*throwSign,delivery.throwElbow[1],delivery.throwElbow[2]));
 poseArm(pitcher.left,pitcher.le,pitcher.root.localToWorld(V(...delivery.glove)),V(delivery.gloveElbow[0]*throwSign,delivery.gloveElbow[1],delivery.gloveElbow[2]));
 return releaseHand;
}
