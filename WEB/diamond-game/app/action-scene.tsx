"use client";
import {useEffect,useRef,MutableRefObject} from "react";
import * as THREE from "three";
import {ActionView,Vec,PitchResult,incomingBallPosition,clamp,playerProfile,batterStats,pitcherStats,throwsLeft,batsLeft,isUnderhand,evaluatePitch} from "../lib/action-engine";
import {swingPose,pitchingPose,SWING_CONTACT_MS} from "../lib/player-motion";
import {FIELD_CAMERAS,fieldFov} from "../lib/field-camera";
import {localNow} from "../lib/game-clock";
import {lastPitchMarker} from "../lib/pitch-feedback";
import {battedBallPosition} from "../lib/batted-ball";
import {playerAppearance,PlayerAppearance} from "../lib/player-appearance";
import {scenePointerControls} from "../lib/scene-pointer";

export type LocalSwing={code:string;pitchId:number;at:number;aim:Vec};
type Props={view:ActionView|null;batterId:string;pitcherId:string;side:"batter"|"pitcher";aim:MutableRefObject<Vec>;clock:MutableRefObject<number>;swingTime:number;localSwing:LocalSwing|null;charging:boolean;chargeStarted:number;onAim?:(p:Vec)=>void;onSwing:()=>void;onContact:(key:string)=>void;onChargeStart:()=>void;onChargeEnd:()=>void;onReady:(ok:boolean)=>void};
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
function mat(color:string,roughness=.7){return new THREE.MeshStandardMaterial({color,roughness})}
function mesh(geo:THREE.BufferGeometry,material:THREE.Material,parent:THREE.Object3D,x=0,y=0,z=0){const m=new THREE.Mesh(geo,material);m.position.set(x,y,z);parent.add(m);return m}
function capsule(parent:THREE.Object3D,r:number,length:number,material:THREE.Material,x=0,y=0,z=0){return mesh(new THREE.CapsuleGeometry(r,length,4,10),material,parent,x,y,z)}
function segment(parent:THREE.Object3D,length:number,r:number,material:THREE.Material,x:number,y:number,z:number){const group=new THREE.Group();group.position.set(x,y,z);parent.add(group);capsule(group,r,length-2*r,material,0,-length/2);return group}
function stitch(parent:THREE.Object3D,points:number[][],color:string){const line=new THREE.Line(new THREE.BufferGeometry().setFromPoints(points.map(p=>V(...p))),new THREE.LineBasicMaterial({color}));parent.add(line);return line}
function ring(parent:THREE.Object3D,radius:number,tube:number,material:THREE.Material,x:number,y:number,z=0){const m=mesh(new THREE.TorusGeometry(radius,tube,4,18),material,parent,x,y,z);m.rotation.x=Math.PI/2;return m}
function mittRig(parent:THREE.Object3D,size=1){
 const root=new THREE.Group();root.scale.setScalar(size);parent.add(root);
 const leather=mat("#935e36",.94),edge=mat("#cc9b61",.86),pocket=mat("#583925",.95);
 const palm=mesh(new THREE.SphereGeometry(.13,12,10),leather,root);palm.scale.set(.9,1.14,.6);
 const hollow=mesh(new THREE.SphereGeometry(.098,12,8),pocket,root,0,-.01,.059);hollow.scale.set(.92,1.03,.2);
 for(let i=0;i<4;i++){const finger=capsule(root,.026,.087,leather,-.077+i*.046,.076,.008);finger.rotation.z=(1.5-i)*.08;stitch(finger,[[-.014,-.053,.022],[-.014,.05,.022],[.012,.05,.022]],"#dab488");}
 const thumb=capsule(root,.035,.1,leather,.097,-.018,.014);thumb.rotation.z=-.58;
 for(let i=0;i<3;i++)stitch(root,[[.022+i*.018,.024,.065],[.063+i*.013,.095,.041]],"#dab488");
 const wrist=capsule(root,.037,.072,edge,0,-.109,-.004);wrist.rotation.z=Math.PI/2;
 return root;
}
function cleat(parent:THREE.Object3D,shoe:THREE.Material,accent:THREE.Material,sole:THREE.Material){
 const heel=mesh(new THREE.SphereGeometry(1,12,8),shoe,parent,0,-.387,-.008);heel.scale.set(.081,.065,.117);
 const toe=mesh(new THREE.SphereGeometry(1,12,8),shoe,parent,0,-.407,.113);toe.scale.set(.083,.046,.087);
 const bottom=mesh(new THREE.CapsuleGeometry(.082,.116,3,10),sole,parent,0,-.443,.053);bottom.rotation.x=Math.PI/2;bottom.scale.y=1;bottom.scale.z=.19;
 for(const x of [-1,1]){const panel=mesh(new THREE.BoxGeometry(.008,.032,.099),accent,parent,x*.076,-.39,.04);panel.rotation.x=-.12;}
 for(let i=0;i<3;i++)stitch(parent,[[-.034,-.342-i*.012,.007+i*.027],[.034,-.342-i*.012,.027+i*.027]],"#eeeae2");
 for(const x of [-.049,.049])for(const z of [-.035,.13])mesh(new THREE.CylinderGeometry(.012,.008,.018,5),shoe,parent,x,-.458,z);
}
function uniformTexture(appearance:PlayerAppearance,area:"front"|"back"|"cap"){
 if(typeof document==="undefined")return null;
 const canvas=document.createElement("canvas");canvas.width=256;canvas.height=256;const context=canvas.getContext("2d");if(!context)return null;
 context.textAlign="center";context.textBaseline="middle";context.fillStyle=appearance.letter;
 if(area==="front"){context.font="italic 800 41px Arial, sans-serif";context.fillText(appearance.wordmark,128,119,237);context.fillRect(35,153,186,4);}
 else if(area==="back"){context.font="700 35px 'Malgun Gothic', sans-serif";context.fillText(appearance.name,128,55,234);if(appearance.number){context.font="900 148px Arial, sans-serif";context.fillText(appearance.number,128,162,218);}else{context.font="700 25px Arial, sans-serif";context.fillText(appearance.wordmark,128,143,212);}}
 else{context.font="italic 900 104px Arial, sans-serif";context.fillText(appearance.team,128,133,224);}
 const texture=new THREE.CanvasTexture(canvas);texture.colorSpace=THREE.SRGBColorSpace;texture.anisotropy=2;return texture;
}
function player(color:string,isBatter=false){
 const root=new THREE.Group(),jersey=mat(color,.94),pants=mat("#e8e9e2",.95),skin=mat("#c99670",.87),dark=mat("#192537",.63),white=mat("#e8e7df",.9),accent=mat("#eee8db",.8),cap=mat(color,isBatter?.33:.82);
 const hips=new THREE.Group();hips.position.y=.93;root.add(hips);capsule(hips,.2,.12,pants);
 const torso=new THREE.Group();torso.position.y=.95;root.add(torso);
 // The original rig, shoulder anchors and maximum torso radius are unchanged.
 const outline=[[.172,.015],[.19,.1],[.211,.26],[.224,.4],[.23,.55],[.205,.64],[.11,.715],[.087,.747]].map(([r,y])=>new THREE.Vector2(r,y));
 mesh(new THREE.LatheGeometry(outline,20),jersey,torso);capsule(torso,.079,.098,skin,0,.735);
 ring(torso,.101,.012,accent,0,.718);ring(torso,.194,.016,dark,0,.049);
 mesh(new THREE.BoxGeometry(.039,.03,.009),white,torso,0,.05,.211);
 for(const x of [-.135,.135])mesh(new THREE.BoxGeometry(.018,.047,.01),pants,torso,x,.048,.153);
 stitch(torso,[[0,.086,.194],[0,.29,.219],[0,.48,.232],[0,.615,.214],[0,.695,.133]],"#d9dad5");
 for(const y of [.14,.255,.375,.49,.594])mesh(new THREE.SphereGeometry(.006,5,4),white,torso,0,y,y<.2?.206:y>.56?.228:.232);
 const head=new THREE.Group();head.position.y=.82;torso.add(head);
 const face=mesh(new THREE.SphereGeometry(.145,16,12),skin,head,0,.06);face.scale.set(.96,1,.94);
 const jaw=mesh(new THREE.SphereGeometry(.086,12,8),skin,head,0,-.012,.038);jaw.scale.set(.98,.73,.91);
 const hair=mesh(new THREE.SphereGeometry(.143,14,10,Math.PI,Math.PI),dark,head,0,.073,-.004);hair.scale.y=.91;
 mesh(new THREE.SphereGeometry(.151,20,12,0,Math.PI*2,0,Math.PI*(isBatter?.59:.52)),cap,head,0,.1);
 const brim=mesh(new THREE.SphereGeometry(1,16,6,0,Math.PI*2,0,Math.PI/2),cap,head,0,.106,.119);brim.scale.set(.13,.014,.079);
 mesh(new THREE.SphereGeometry(.013,8,6),cap,head,0,.249,0);
 const nose=mesh(new THREE.SphereGeometry(.035,10,8),skin,head,0,.05,.131);nose.scale.set(.64,.9,1);
 for(const x of [-.057,.057]){
  const eye=mesh(new THREE.SphereGeometry(.013,8,6),white,head,x,.083,.124);eye.scale.set(1,.62,.42);
  mesh(new THREE.SphereGeometry(.007,6,5),dark,head,x,.083,.132);
  stitch(head,[[x-.015,.107,.13],[x,.111,.132],[x+.013,.107,.128]],"#493727");
  const ear=mesh(new THREE.SphereGeometry(.026,8,6),skin,head,Math.sign(x)*.134,.048,-.003);ear.scale.set(.42,1,.64);
  if(isBatter){const guard=mesh(new THREE.SphereGeometry(.041,10,8),cap,head,Math.sign(x)*.129,.041,-.006);guard.scale.set(.32,1.4,.89);mesh(new THREE.SphereGeometry(.012,6,6),dark,head,Math.sign(x)*.143,.041,.004);}
 }
 stitch(head,[[-.026,-.007,.13],[0,-.014,.134],[.026,-.007,.13]],"#795645");
 if(!isBatter)for(const sign of [-1,1])stitch(head,[[0,.252,0],[sign*.086,.218,.031],[sign*.131,.158,.058]],"#929aa4");
 // The model faces +z: its anatomical right shoulder is on -x.
 const left=segment(torso,.37,.079,skin,.27,.58,0),right=segment(torso,.37,.079,skin,-.27,.58,0);
 for(const shoulder of [left,right]){capsule(shoulder,.079,.064,jersey,0,-.11);ring(shoulder,.071,.009,accent,0,-.207);}
 const le=segment(left,.35,.067,skin,0,-.34,0),re=segment(right,.35,.067,skin,0,-.34,0);
 for(const arm of [le,re]){ring(arm,.055,.011,dark,0,-.284);if(isBatter){const pad=mesh(new THREE.SphereGeometry(.059,10,8),accent,arm,0,-.044,.025);pad.scale.set(1,.95,.73);}}
 const glove=mittRig(le);glove.position.set(0,-.34,.025);glove.visible=!isBatter;
 const hand=mesh(new THREE.SphereGeometry(.06,10,8),skin,re,0,-.34,0);hand.visible=!isBatter;
 const ll=segment(root,.46,.105,pants,.115,.91,0),rl=segment(root,.46,.105,pants,-.115,.91,0),lk=segment(ll,.43,.084,pants,0,-.43,0),rk=segment(rl,.43,.084,pants,0,-.43,0);
 for(const [thigh,sign] of [[ll,1],[rl,-1]] as const){stitch(thigh,[[sign*.092,-.065,.025],[sign*.105,-.23,.025],[sign*.09,-.375,.024]],"#a3a8a8");}
 for(const knee of [lk,rk]){ring(knee,.071,.012,accent,0,-.303);cleat(knee,dark,accent,white);}
 const lettering=(["front","back","cap"] as const).map(area=>{
  const material=new THREE.MeshStandardMaterial({transparent:true,depthWrite:false,roughness:.92,polygonOffset:true,polygonOffsetFactor:-1});
  const radius=area==="cap"?.147:.234,height=area==="back"?.39:area==="cap"?.067:.19,arc=area==="cap"?.85:1.64,y=area==="cap"?.151:area==="back"?.43:.46;
  const geometry=new THREE.CylinderGeometry(radius,radius,height,16,4,true,-arc/2,arc),positions=geometry.attributes.position;
  for(let i=0;i<positions.count;i++){
   const surfaceY=positions.getY(i)+y;
   let surfaceRadius=Math.sqrt(.151**2-(surfaceY-.1)**2);
   if(area!=="cap"){const next=outline.findIndex(point=>point.y>=surfaceY),a=outline[Math.max(0,next-1)],b=outline[Math.max(0,next)];surfaceRadius=a.x+(b.x-a.x)*(surfaceY-a.y)/(b.y-a.y||1);}
   const scale=(surfaceRadius+.001)/radius;positions.setXYZ(i,positions.getX(i)*scale,positions.getY(i),positions.getZ(i)*scale);
  }
  geometry.computeVertexNormals();
  const decal=mesh(geometry,material,area==="cap"?head:torso,0,y);
  if(area==="back")decal.rotation.y=Math.PI;
  decal.visible=false;return {area,decal,material};
 });
 return {root,hips,torso,head,left,right,le,re,ll,rl,lk,rk,jersey,cap,accent,lettering,appearanceKey:""};
}
function dressPlayer(model:ReturnType<typeof player>,appearance:PlayerAppearance,hand=1){
 const key=JSON.stringify(appearance);
 if(key!==model.appearanceKey){model.appearanceKey=key;model.jersey.color.set(appearance.jersey);model.cap.color.set(appearance.cap);model.accent.color.set(appearance.accent);
  for(const {area,decal,material} of model.lettering){const old=material.map;material.map=uniformTexture(appearance,area);material.needsUpdate=true;decal.visible=!!material.map;old?.dispose();}
 }
 for(const {decal} of model.lettering)decal.scale.x=hand;
}
function catcherEquipment(model:ReturnType<typeof player>){
 const shell=mat("#253343",.64),pad=mat("#465567",.94),metal=mat("#9ca8b2",.33);
 const protector=mesh(new THREE.SphereGeometry(1,12,10),shell,model.torso,0,.394,.221);protector.scale.set(.198,.258,.057);
 for(const y of [.235,.305,.375,.445,.515]){const rib=capsule(model.torso,.026,.26,pad,0,y,.27);rib.rotation.z=Math.PI/2;}
 for(const shoulder of [model.left,model.right]){const cap=mesh(new THREE.SphereGeometry(.088,10,6),shell,shoulder,0,-.05,0);cap.scale.set(1,1.15,.92);}
 for(const knee of [model.lk,model.rk]){const shield=mesh(new THREE.SphereGeometry(1,10,8),shell,knee,0,-.16,.071);shield.scale.set(.079,.204,.041);for(const y of [-.045,-.275]){const strap=ring(knee,.081,.013,pad,0,y);strap.scale.z=1.12;}}
 const mask=new THREE.Group();model.head.add(mask);mask.position.set(0,.048,.039);
 const bar=(a:THREE.Vector3,b:THREE.Vector3)=>{const tube=mesh(new THREE.CylinderGeometry(.007,.007,1,5),metal,mask);between(tube,a,b);};
 for(const y of [-.105,-.025,.06,.132]){const width=y<-.08?.088:y>.1?.101:.144;bar(V(-width,y,.101),V(0,y,.151));bar(V(0,y,.151),V(width,y,.101));}
 for(const x of [-.11,0,.11]){bar(V(x*.81,-.105,.115),V(x,-.025,.151-Math.abs(x)*.25));bar(V(x,-.025,.151-Math.abs(x)*.25),V(x*.85,.132,.109));}
 const chin=mesh(new THREE.SphereGeometry(1,10,6),pad,mask,0,-.096,.077);chin.scale.set(.087,.038,.064);
}
// Solve each elbow so both wrists stay attached to the bat instead of swinging a single rigid arm.
function poseArm(upper:THREE.Object3D,lower:THREE.Object3D,wrist:THREE.Vector3,bend:THREE.Vector3){
 upper.updateWorldMatrix(true,true);const boneLength=.34*Math.abs(upper.getWorldScale(V()).y),shoulder=upper.getWorldPosition(V()),delta=wrist.clone().sub(shoulder),length=Math.min(boneLength*2-.001,Math.max(.02,delta.length())),direction=delta.normalize();
 const perpendicular=bend.clone().addScaledVector(direction,-bend.dot(direction)).normalize();
 const elbow=shoulder.clone().addScaledVector(direction,length/2).addScaledVector(perpendicular,Math.sqrt(boneLength**2-(length/2)**2));
 const localElbow=upper.parent!.worldToLocal(elbow.clone()).sub(upper.position).normalize();upper.quaternion.setFromUnitVectors(V(0,-1,0),localElbow);upper.updateWorldMatrix(false,true);
 const localWrist=lower.parent!.worldToLocal(wrist.clone()).sub(lower.position).normalize();lower.quaternion.setFromUnitVectors(V(0,-1,0),localWrist);lower.updateWorldMatrix(false,true);
}
function between(m:THREE.Mesh,a:THREE.Vector3,b:THREE.Vector3){const d=b.clone().sub(a);m.position.copy(a).add(b).multiplyScalar(.5);m.quaternion.setFromUnitVectors(V(0,1,0),d.clone().normalize());m.scale.y=d.length();}
function batRig(scene:THREE.Scene){
 const root=new THREE.Group();scene.add(root);const wood=mat("#c99553",.38),grip=mat("#222b34"),glove=mat("#f3f4ef");
 mesh(new THREE.CylinderGeometry(.055,.03,.72,16),wood,root,0,.59);mesh(new THREE.SphereGeometry(.055,16,8),wood,root,0,.95);
 mesh(new THREE.CylinderGeometry(.022,.025,.36,12),grip,root,0,.05);mesh(new THREE.CylinderGeometry(.039,.039,.022,12),wood,root,0,-.14);
 for(const y of [-.055,.065]){const palm=mesh(new THREE.SphereGeometry(.064,14,10),glove,root,.012,y,0);palm.scale.set(1,.8,.8);for(let i=0;i<3;i++){const finger=capsule(root,.013,.072,glove,.042,y-.025+i*.022,.012);finger.rotation.z=Math.PI/2}}
 return root;
}
function poseBatter(batter:ReturnType<typeof player>,mirror:THREE.Group,bat:THREE.Group,pose:ReturnType<typeof swingPose>){
 bat.position.set(...pose.grip);bat.quaternion.setFromUnitVectors(V(0,1,0),V(...pose.axis));bat.updateWorldMatrix(true,true);
 batter.root.position.x=-.92+pose.bodyShift;batter.root.rotation.y=Math.PI/2+pose.turn*.25;batter.hips.position.y=.93-pose.crouch;batter.torso.position.y=.95-pose.crouch;batter.hips.rotation.y=pose.turn*.35;batter.torso.rotation.set(.12+pose.reach*.18,pose.turn*.8,-.04);batter.head.rotation.y=Math.PI/2-pose.turn*1.05;
 const kneeBend=Math.acos(clamp(1-pose.crouch/.91,0,1));batter.ll.position.y=batter.rl.position.y=.91-pose.crouch;batter.ll.rotation.x=-kneeBend-pose.load*.16;batter.lk.rotation.x=kneeBend*2+pose.load*.28;batter.rl.rotation.x=-kneeBend;batter.rl.rotation.y=pose.turn*.35;batter.rk.rotation.x=kneeBend*2;mirror.updateWorldMatrix(true,true);
 const leftWrist=bat.localToWorld(V(0,-.055,0)),rightWrist=bat.localToWorld(V(0,.065,0));
 poseArm(batter.left,batter.le,leftWrist,V(0,-1,-.2));poseArm(batter.right,batter.re,rightWrist,V(0,-1,.3));
 return {leftWrist,rightWrist};
}
function baseball(parent:THREE.Object3D,r=.065){const group=new THREE.Group();mesh(new THREE.SphereGeometry(r,20,16),new THREE.MeshStandardMaterial({color:"#fffbe7",roughness:.55,emissive:"#fffbe0",emissiveIntensity:.22}),group);const red=new THREE.LineBasicMaterial({color:"#bc3324"});for(const sign of [-1,1]){const points=[];for(let i=0;i<=64;i++){const a=i/64*Math.PI*2;points.push(V(Math.cos(a)*r*.82,Math.sin(a)*r*.82,sign*r*.56))}group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points),red))}parent.add(group);return group}
export default function ActionScene(props:Props){
 const mount=useRef<HTMLDivElement>(null),target=useRef<HTMLDivElement>(null),marker=useRef<HTMLDivElement>(null),frameProps=useRef(props);frameProps.current=props;
 useEffect(()=>{if(!mount.current)return;const node=mount.current;let disposed=false,frame=0;let renderer:THREE.WebGLRenderer;
 try{renderer=new THREE.WebGLRenderer({antialias:true,alpha:true,powerPreference:"high-performance"});}catch{props.onReady(false);return}
 renderer.setPixelRatio(Math.min(window.devicePixelRatio,2));renderer.setClearColor(0,0);renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.25;node.appendChild(renderer.domElement);
 const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(47,1,.03,300);scene.add(camera);scene.add(new THREE.HemisphereLight("#d9eaff","#84714b",2.4));const light=new THREE.DirectionalLight("#ffefd0",3.6);light.position.set(-5,9,3);scene.add(light);const rim=new THREE.DirectionalLight("#9abfff",2);rim.position.set(5,4,-20);scene.add(rim);
 const pitcher=player("#e76c25"),batter=player("#2060b0",true),catcher=player("#263c54"),batterMirror=new THREE.Group();scene.add(pitcher.root,batterMirror,catcher.root);batterMirror.add(batter.root);pitcher.root.position.set(0,0,-18.44);batter.root.position.set(-.92,0,.06);catcher.root.position.set(0,-.66,1.1);catcher.root.scale.setScalar(.9);catcher.root.rotation.y=Math.PI;catcher.ll.rotation.x=-1.65;catcher.rl.rotation.x=-1.65;catcher.lk.rotation.x=2.1;catcher.rk.rotation.x=2.1;
 const heldBall=baseball(pitcher.re,.055);heldBall.position.set(0,-.34,0);
 catcherEquipment(catcher);
 const zone=new THREE.Group();scene.add(zone);const lineMat=new THREE.LineBasicMaterial({color:"#f0f7e5",transparent:true,opacity:.36,depthTest:false});const gridMat=new THREE.LineBasicMaterial({color:"#e5f5df",transparent:true,opacity:.14,depthTest:false});
 for(let i=0;i<=3;i++){const x=-.5+i/3,y=.5+i/3*1.1;zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(x,.5,0),V(x,1.6,0)]),i===0||i===3?lineMat:gridMat));zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(-.5,y,0),V(.5,y,0)]),i===0||i===3?lineMat:gridMat))}
 const ball=baseball(scene),trail=Array.from({length:9},(_,i)=>{const m=mesh(new THREE.SphereGeometry(.035-i*.002,8,6),new THREE.MeshBasicMaterial({color:"#f5f5b9",transparent:true,opacity:.33*(1-i/9),depthWrite:false}),scene);m.visible=false;return m});
 const bat=batRig(scene);
 const batTrail=Array.from({length:5},(_,i)=>{const m=mesh(new THREE.CylinderGeometry(.045,.023,1,10),new THREE.MeshBasicMaterial({color:"#f2d3a0",transparent:true,opacity:.12-i*.018,depthWrite:false}),scene);m.visible=false;return m});
 const fpGlove=mittRig(camera,1.46),fpBall=baseball(camera,.066);
 const fpThrowHand=mesh(new THREE.SphereGeometry(.063,14,10),mat("#c99670"),camera),fpThrowArm=mesh(new THREE.CylinderGeometry(.04,.065,1,10),mat("#c99670"),camera);
 let width=1,height=1,lastSide="",lastSwingStart=-Infinity,frozenAim={x:0,y:0},predictionKey="",prediction:PitchResult|undefined;
 const configureCamera=()=>{const side=frameProps.current.side,config=FIELD_CAMERAS[side];camera.position.set(config.position[0],config.position[1],config.position[2]);camera.lookAt(config.target[0],config.target[1],config.target[2]);camera.aspect=width/height;camera.fov=fieldFov(side,camera.aspect);camera.updateProjectionMatrix();camera.updateMatrixWorld(true)};
 const resize=()=>{width=node.clientWidth;height=node.clientHeight;renderer.setSize(width,height,false);configureCamera()};const observer=new ResizeObserver(resize);observer.observe(node);resize();
 const ray=new THREE.Raycaster(),plane=new THREE.Plane(V(0,0,1),0),point=V();
 const aimEvent=(event:PointerEvent)=>{const rect=node.getBoundingClientRect();ray.setFromCamera(new THREE.Vector2((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1),camera);if(ray.ray.intersectPlane(plane,point)){const a={x:clamp(point.x/.5,-2,2),y:clamp((point.y-1.05)/.55,-2,2)};frameProps.current.aim.current=a;frameProps.current.onAim?.(a)}};
 const controls=scenePointerControls({side:()=>frameProps.current.side,aim:event=>aimEvent(event as PointerEvent),swing:()=>frameProps.current.onSwing(),chargeStart:()=>frameProps.current.onChargeStart(),chargeEnd:()=>frameProps.current.onChargeEnd(),focus:()=>node.focus({preventScroll:true}),capture:id=>node.setPointerCapture(id),release:id=>{if(node.hasPointerCapture(id))node.releasePointerCapture(id)}});
 node.addEventListener("pointermove",controls.move,{passive:false});node.addEventListener("pointerdown",controls.down,{passive:false});node.addEventListener("pointerup",controls.up);node.addEventListener("pointercancel",controls.up);node.addEventListener("lostpointercapture",controls.up);node.tabIndex=0;node.style.touchAction="none";node.setAttribute("aria-label","야구 플레이 화면. 타자는 화면의 스윙할 위치를 터치하거나 마우스로 클릭하세요. 투수는 조준 후 투구 버튼 또는 스페이스를 사용하세요.");
 let appearanceKey="";
 const refreshAppearance=(pr:Props,battingSign:number,throwSign:number)=>{
  const key=pr.batterId+":"+pr.pitcherId+":"+(pr.view?.roster?.revision??"")+":"+battingSign+":"+throwSign;
  if(key===appearanceKey)return;appearanceKey=key;
  const selected=(id:string,side:"batter"|"pitcher")=>{try{const record=side==="batter"?batterStats(id):pitcherStats(id),profile=playerProfile(id,side) as {jerseyNumber?:unknown}|undefined;return playerAppearance(record.team,record.name,profile?.jerseyNumber);}catch{return playerAppearance("","");}};
  const batterLook=selected(pr.batterId,"batter"),pitcherLook=selected(pr.pitcherId,"pitcher");
  dressPlayer(batter,batterLook,battingSign);dressPlayer(pitcher,pitcherLook,throwSign);dressPlayer(catcher,{...pitcherLook,name:"",number:undefined});
 };
 const render=()=>{if(disposed)return;const pr=frameProps.current,now=localNow()+pr.clock.current,p=pr.view?.pitch,side=pr.side;
 const battingSign=batsLeft(pr.batterId,pr.pitcherId)?-1:1,throwSign=throwsLeft(pr.pitcherId)?-1:1,underhand=isUnderhand(pr.pitcherId);
 refreshAppearance(pr,battingSign,throwSign);
 if(side!==lastSide){lastSide=side;configureCamera();pitcher.root.visible=side==="batter";batter.root.visible=true;catcher.root.visible=side==="pitcher";fpGlove.visible=side==="pitcher"}
 const bodyScale=(playerProfile(pr.pitcherId,"pitcher")?.heightCm??185)/185;pitcher.root.scale.set(throwSign*bodyScale,bodyScale,bodyScale);batterMirror.scale.x=battingSign;
 const idle=Math.sin(now/750)*.008,relative=p?now-p.releaseAt:-2000,delivery=pitchingPose(relative,underhand);
 pitcher.torso.rotation.set(delivery.lean,delivery.lift*.32,underhand?-.14:0);pitcher.ll.rotation.x=-delivery.lift*1.2;pitcher.lk.rotation.x=delivery.lift*1.3;pitcher.rl.rotation.x=-delivery.lean*.5;pitcher.root.position.y=idle;
 pitcher.root.updateWorldMatrix(true,true);
 const releaseHand=pitcher.root.localToWorld(V(...delivery.hand));poseArm(pitcher.right,pitcher.re,releaseHand,V(-throwSign,.1,-.15));poseArm(pitcher.left,pitcher.le,pitcher.root.localToWorld(V(.08,1.26-delivery.lean*.3,.28)),V(throwSign,-.3,.1));heldBall.visible=!!p&&relative>=-900&&relative<0;
 const localSwing=p&&pr.localSwing&&pr.localSwing.code===pr.view?.code&&pr.localSwing.pitchId===p.id?pr.localSwing:null;
 const knownSwing=localSwing??p?.aiBatterSwing??(p?.reaction?.swingAt!=null&&p.reaction.swingAim?{at:p.reaction.swingAt,aim:p.reaction.swingAim}:null);
 const key=pr.view&&p?pr.view.code+":"+p.id+":"+(knownSwing?.at??"take"):"";
 if(key!==predictionKey){predictionKey=key;prediction=pr.view&&p?evaluatePitch(pr.view,knownSwing,now):undefined;}
 const reaction=p?.reaction??prediction;
 const contactAt=knownSwing?.at??p?.reaction?.swingAt;
 const swingStart=side==="batter"?pr.swingTime:contactAt!=null?contactAt-SWING_CONTACT_MS:-Infinity;
 if(pr.swingTime!==lastSwingStart){lastSwingStart=pr.swingTime;frozenAim={...pr.aim.current}}
 const swingAge=now-swingStart,swingAim=side==="batter"?localSwing?.aim??frozenAim:knownSwing?.aim??p?.target??{x:0,y:0};
 const pose=swingPose(swingAge,swingAim,battingSign);
 poseBatter(batter,batterMirror,bat,pose);
 for(const [i,ghost] of batTrail.entries()){ghost.visible=swingAge>40&&swingAge<240;if(!ghost.visible)continue;const prev=swingPose(swingAge-(i+1)*9,swingAim,battingSign);const a=V(...prev.grip).addScaledVector(V(...prev.axis),.25),b=V(...prev.grip).addScaledVector(V(...prev.axis),.95);between(ghost,a,b)}
 const wind=relative>=-900&&relative<0?Math.sin(Math.PI*(relative+900)/900):0,follow=relative>=0&&relative<400?Math.sin(Math.PI*relative/400):0;
 fpGlove.position.set(-.23*throwSign,-.19+(pr.charging?.04:0),-.85);fpGlove.scale.setScalar(.83);fpGlove.rotation.set(-.2,.4*throwSign,-.2*throwSign);
 fpBall.position.set((.25+wind*.07-follow*.22)*throwSign,-.16-wind*.09-follow*.12,-.82+wind*.14-follow*.13);fpBall.scale.setScalar(.7);fpBall.visible=side==="pitcher"&&(!p||p.resolved||relative<0);
 fpThrowHand.visible=fpThrowArm.visible=side==="pitcher";fpThrowHand.position.copy(fpBall.position).add(V(0,-.042,.032));between(fpThrowArm,V(.40*throwSign,-.62,-.5),fpThrowHand.position);
 ball.visible=false;for(const t of trail)t.visible=false;
 if(p&&now>=p.releaseAt){const u=(now-p.releaseAt)/p.flightMs;
 if(reaction?.contact&&now>=reaction.contact.at){const pos=battedBallPosition(reaction,now)!;ball.position.set(pos.x,pos.y,pos.z);ball.visible=now-reaction.contact.at<7000;ball.scale.setScalar(1.2);if(now-reaction.contact.at<1200)pr.onContact(pr.view!.code+":"+p.id+":bat");}
 else if(reaction?.bodyHit&&now>=reaction.bodyHit.at){const hit=reaction.bodyHit,elapsed=(now-hit.at)/1000;ball.position.set(hit.position.x,Math.max(.065,hit.position.y-4.9*elapsed*elapsed),hit.position.z-.25*Math.min(elapsed,1));ball.visible=elapsed<1.8;ball.scale.setScalar(1);}
 else if(u<1.24){
  const incoming=(time:number)=>incomingBallPosition(p,reaction,time,side);
  const pos=incoming(now);ball.position.set(pos.x,pos.y,pos.z);ball.visible=true;ball.scale.setScalar(side==="batter"?1.3:1.05);for(let i=0;i<trail.length;i++){const old=incoming(now-(i+1)*9);trail[i].position.set(old.x,old.y,old.z);trail[i].visible=now-(i+1)*9>=p.releaseAt}}
 ball.rotation.x=now/50;ball.rotation.z=now/75;}
 const aim=pr.aim.current,screen=V(aim.x*.5,1.05+aim.y*.55,0).project(camera);if(target.current){target.current.style.left=(screen.x*.5+.5)*width+"px";target.current.style.top=(-screen.y*.5+.5)*height+"px";target.current.style.setProperty("--reticle-size",side==="batter"?"54px":"32px")}
 const mark=lastPitchMarker(pr.view,now,reaction?.bodyHit??null);if(marker.current){marker.current.hidden=!mark;if(mark){const point=V(mark.position.x,mark.position.y,mark.position.z).project(camera);marker.current.style.left=(point.x*.5+.5)*width+"px";marker.current.style.top=(-point.y*.5+.5)*height+"px";marker.current.className="last-pitch-marker"+(mark.body?" body-hit-marker":"");marker.current.setAttribute("aria-label",mark.body?"직전 공 몸에 맞은 위치":"직전 공 통과 위치");marker.current.querySelector("span")!.textContent=mark.body?"몸에 맞음":"직전 공";}}
 renderer.render(scene,camera);frame=requestAnimationFrame(render);
 };frame=requestAnimationFrame(render);props.onReady(true);
 return()=>{disposed=true;cancelAnimationFrame(frame);observer.disconnect();node.removeEventListener("pointermove",controls.move);node.removeEventListener("pointerdown",controls.down);node.removeEventListener("pointerup",controls.up);node.removeEventListener("pointercancel",controls.up);node.removeEventListener("lostpointercapture",controls.up);const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>(),textures=new Set<THREE.Texture>();scene.traverse(o=>{const m=o as THREE.Mesh;if(m.geometry)geometries.add(m.geometry);if(m.material)for(const material of Array.isArray(m.material)?m.material:[m.material])materials.add(material)});for(const material of materials){const map=(material as THREE.MeshStandardMaterial).map;if(map)textures.add(map);material.dispose()}textures.forEach(texture=>texture.dispose());geometries.forEach(geometry=>geometry.dispose());renderer.dispose();renderer.domElement.remove()};
 },[]);
 return <><div className="three-surface" ref={mount}/><div ref={marker} className="last-pitch-marker" hidden><i/><span>직전 공</span></div><div ref={target} className={"aim-reticle "+(props.side==="pitcher"?"pitch-reticle":"")}><i/><b/><span/></div></>;
}
