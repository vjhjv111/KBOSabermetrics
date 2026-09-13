import * as THREE from 'three';

const clamp=(value:number,min:number,max:number)=>Math.max(min,Math.min(max,value));

/** Follow the frozen contact target without changing the bat or torso tracks. */
export function poseBattingGaze(head:THREE.Object3D,targetWorld:THREE.Vector3,weight:number){
 if(!(weight>0))return;
 const rest=head.quaternion.clone(),eye=new THREE.Vector3(0,.062,.1);
 head.parent!.updateWorldMatrix(true,false);
 const target=head.parent!.worldToLocal(targetWorld.clone()).sub(head.position);
 let yaw=0,pitch=0;
 // Account for the eyes in front of the neck, rather than aiming the neck itself.
 for(let iteration=0;iteration<3;iteration++){
  const eyeOffset=eye.clone().applyQuaternion(new THREE.Quaternion().setFromEuler(new THREE.Euler(pitch,yaw,0,'YXZ')));
  const direction=target.clone().sub(eyeOffset);
  yaw=clamp(Math.atan2(direction.x,direction.z),-1.65,1.65);
  pitch=clamp(Math.atan2(-direction.y,Math.hypot(direction.x,direction.z)),-.75,.95);
 }
 const following=new THREE.Quaternion().setFromEuler(new THREE.Euler(pitch,yaw,0,'YXZ'));
 head.quaternion.copy(rest).slerp(following,clamp(weight,0,1));
 head.updateWorldMatrix(false,true);
}
