"use client";
import {useEffect,useRef,MutableRefObject} from "react";
import * as THREE from "three";
import {ActionView,Vec,PitchResult,incomingBallPosition,clamp,playerProfile,batterStats,pitcherStats,throwsLeft,batsLeft,isUnderhand,evaluatePitch} from "../lib/action-engine";
import {swingPose,pitchingPose,SWING_CONTACT_MS} from "../lib/player-motion";
import {FIELD_CAMERAS,fieldFov} from "../lib/field-camera";
import {localNow} from "../lib/game-clock";
import {lastPitchMarker} from "../lib/pitch-feedback";
import {battedBallPosition} from "../lib/batted-ball";
import {playerAppearance} from "../lib/player-appearance";
import {scenePointerControls} from "../lib/scene-pointer";
import {createPlayer as player,dressPlayer,equipCatcher as catcherEquipment,createBat as batRig} from "../lib/player-model";
import {stadiumLighting} from "../lib/scene-lighting";

export type LocalSwing={code:string;pitchId:number;at:number;aim:Vec};
type Props={view:ActionView|null;batterId:string;pitcherId:string;side:"batter"|"pitcher";aim:MutableRefObject<Vec>;clock:MutableRefObject<number>;swingTime:number;localSwing:LocalSwing|null;charging:boolean;chargeStarted:number;onAim?:(p:Vec)=>void;onSwing:()=>void;onContact:(key:string)=>void;onChargeStart:()=>void;onChargeEnd:()=>void;onChargeCancel:()=>void;onReady:(ok:boolean)=>void};
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
function mesh(geo:THREE.BufferGeometry,material:THREE.Material,parent:THREE.Object3D,x=0,y=0,z=0){const m=new THREE.Mesh(geo,material);m.position.set(x,y,z);parent.add(m);return m}
// Solve each elbow so both wrists stay attached to the bat instead of swinging a single rigid arm.
function poseArm(upper:THREE.Object3D,lower:THREE.Object3D,wrist:THREE.Vector3,bend:THREE.Vector3){
 upper.updateWorldMatrix(true,true);const boneLength=.34*Math.abs(upper.getWorldScale(V()).y),shoulder=upper.getWorldPosition(V()),delta=wrist.clone().sub(shoulder),length=Math.min(boneLength*2-.001,Math.max(.02,delta.length())),direction=delta.normalize();
 const perpendicular=bend.clone().addScaledVector(direction,-bend.dot(direction)).normalize();
 const elbow=shoulder.clone().addScaledVector(direction,length/2).addScaledVector(perpendicular,Math.sqrt(boneLength**2-(length/2)**2));
 const localElbow=upper.parent!.worldToLocal(elbow.clone()).sub(upper.position).normalize();upper.quaternion.setFromUnitVectors(V(0,-1,0),localElbow);upper.updateWorldMatrix(false,true);
 const localWrist=lower.parent!.worldToLocal(wrist.clone()).sub(lower.position).normalize();lower.quaternion.setFromUnitVectors(V(0,-1,0),localWrist);lower.updateWorldMatrix(false,true);
}
function between(m:THREE.Mesh,a:THREE.Vector3,b:THREE.Vector3){const d=b.clone().sub(a);m.position.copy(a).add(b).multiplyScalar(.5);m.quaternion.setFromUnitVectors(V(0,1,0),d.clone().normalize());m.scale.y=d.length();}
// Solve knees in player-local space so mirrored players keep the same planted-foot mechanics.
function poseLeg(thigh:THREE.Object3D,knee:THREE.Object3D,foot:THREE.Object3D,target:THREE.Vector3,footPitch=0,footYaw=0){
 const delta=target.clone().sub(thigh.position),distance=clamp(delta.length(),.025,.859),direction=delta.normalize();
 const bend=V(0,0,1).addScaledVector(direction,-direction.z).normalize();
 const elbow=thigh.position.clone().addScaledVector(direction,distance*.5).addScaledVector(bend,Math.sqrt(.43*.43-distance*distance*.25));
 thigh.quaternion.setFromUnitVectors(V(0,-1,0),elbow.clone().sub(thigh.position).normalize());
 const shin=target.clone().sub(elbow).normalize().applyQuaternion(thigh.quaternion.clone().invert());
 knee.quaternion.setFromUnitVectors(V(0,-1,0),shin);
 // Independent ankles keep a planted shoe level while the knee bends above it.
 foot.quaternion.copy(thigh.quaternion).multiply(knee.quaternion).invert().multiply(new THREE.Quaternion().setFromEuler(new THREE.Euler(footPitch,footYaw,0)));
}
function poseBatter(batter:ReturnType<typeof player>,mirror:THREE.Group,bat:THREE.Group,pose:ReturnType<typeof swingPose>){
 bat.position.set(...pose.grip);bat.quaternion.setFromUnitVectors(V(0,1,0),V(...pose.axis));bat.updateWorldMatrix(true,true);
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
function posePitcher(pitcher:ReturnType<typeof player>,delivery:ReturnType<typeof pitchingPose>,throwSign:number,underhand:boolean,breath=0){
 pitcher.root.position.y=0;
 pitcher.hips.position.set(0,.93-delivery.drop,delivery.forward);pitcher.hips.rotation.y=delivery.hips;
 pitcher.torso.position.set(0,.95-delivery.drop+breath,delivery.forward);
 pitcher.torso.rotation.set(delivery.lean,delivery.coil,underhand?.08+delivery.lift*.13+delivery.lean*.4:0);
 pitcher.head.rotation.set(-delivery.lean*.6,-delivery.coil*.75,0);
 pitcher.ll.position.set(.115,.91-delivery.drop,delivery.forward);pitcher.rl.position.set(-.115,.91-delivery.drop,delivery.forward);
 poseLeg(pitcher.ll,pitcher.lk,pitcher.lf,V(...delivery.lead),0,-.08);
 poseLeg(pitcher.rl,pitcher.rk,pitcher.rf,V(...delivery.trail),delivery.heel,.08+delivery.hips*.5);
 pitcher.root.updateWorldMatrix(true,true);
 const releaseHand=pitcher.root.localToWorld(V(...delivery.hand));
 poseArm(pitcher.right,pitcher.re,releaseHand,V(-throwSign,.1,-.15));
 poseArm(pitcher.left,pitcher.le,pitcher.root.localToWorld(V(...delivery.glove)),V(throwSign,-.3,.1));
 return releaseHand;
}
function baseball(parent:THREE.Object3D,r=.065){const group=new THREE.Group();mesh(new THREE.SphereGeometry(r,20,16),new THREE.MeshStandardMaterial({color:"#fffbe7",roughness:.55,emissive:"#fffbe0",emissiveIntensity:.22}),group);const red=new THREE.LineBasicMaterial({color:"#bc3324"});for(const sign of [-1,1]){const points=[];for(let i=0;i<=64;i++){const a=i/64*Math.PI*2;points.push(V(Math.cos(a)*r*.82,Math.sin(a)*r*.82,sign*r*.56))}group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points),red))}parent.add(group);return group}
export default function ActionScene(props:Props){
 const mount=useRef<HTMLDivElement>(null),target=useRef<HTMLDivElement>(null),marker=useRef<HTMLDivElement>(null),frameProps=useRef(props);frameProps.current=props;
 useEffect(()=>{if(!mount.current)return;const node=mount.current;let disposed=false,frame=0;let renderer:THREE.WebGLRenderer;
 try{renderer=new THREE.WebGLRenderer({antialias:true,alpha:true,powerPreference:"high-performance"});}catch{props.onReady(false);return}
 const compact=window.matchMedia("(any-pointer: coarse)").matches||node.clientWidth<700;
 renderer.setPixelRatio(Math.min(window.devicePixelRatio,compact?1.4:1.8));renderer.setClearColor(0,0);node.appendChild(renderer.domElement);
 const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(47,1,.03,300);scene.add(camera);const lighting=stadiumLighting(renderer,scene,compact);
 const pitcher=player("#e76c25"),batter=player("#2060b0",true),catcher=player("#263c54"),batterMirror=new THREE.Group();scene.add(pitcher.root,batterMirror,catcher.root);batterMirror.add(batter.root);pitcher.root.position.set(0,0,-18.44);batter.root.position.set(-.92,0,.06);catcher.root.position.set(0,-.66,1.1);catcher.root.scale.setScalar(.9);catcher.root.rotation.y=Math.PI;catcher.ll.rotation.x=-1.65;catcher.rl.rotation.x=-1.65;catcher.lk.rotation.x=2.1;catcher.rk.rotation.x=2.1;
 const heldBall=baseball(pitcher.re,.055);heldBall.position.set(0,-.34,0);
 catcherEquipment(catcher);
 const zone=new THREE.Group();scene.add(zone);const lineMat=new THREE.LineBasicMaterial({color:"#f0f7e5",transparent:true,opacity:.36,depthTest:false});const gridMat=new THREE.LineBasicMaterial({color:"#e5f5df",transparent:true,opacity:.14,depthTest:false});
 for(let i=0;i<=3;i++){const x=-.5+i/3,y=.5+i/3*1.1;zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(x,.5,0),V(x,1.6,0)]),i===0||i===3?lineMat:gridMat));zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(-.5,y,0),V(.5,y,0)]),i===0||i===3?lineMat:gridMat))}
 const ball=baseball(scene),trail=Array.from({length:9},(_,i)=>{const m=mesh(new THREE.SphereGeometry(.035-i*.002,8,6),new THREE.MeshBasicMaterial({color:"#f5f5b9",transparent:true,opacity:.33*(1-i/9),depthWrite:false}),scene);m.visible=false;return m});
 const bat=batRig(scene);
 const batTrail=Array.from({length:5},(_,i)=>{const m=mesh(new THREE.CylinderGeometry(.045,.023,1,10),new THREE.MeshBasicMaterial({color:"#f2d3a0",transparent:true,opacity:.12-i*.018,depthWrite:false}),scene);m.visible=false;return m});
 let width=1,height=1,lastSide="",lastSwingStart=-Infinity,frozenAim={x:0,y:0},predictionKey="",prediction:PitchResult|undefined;
 const configureCamera=()=>{const side=frameProps.current.side,config=FIELD_CAMERAS[side];camera.position.set(config.position[0],config.position[1],config.position[2]);camera.lookAt(config.target[0],config.target[1],config.target[2]);camera.aspect=width/height;camera.fov=fieldFov(side,camera.aspect);camera.updateProjectionMatrix();camera.updateMatrixWorld(true)};
 const resize=()=>{width=node.clientWidth;height=node.clientHeight;renderer.setSize(width,height,false);configureCamera()};const observer=new ResizeObserver(resize);observer.observe(node);resize();
 const ray=new THREE.Raycaster(),plane=new THREE.Plane(V(0,0,1),0),point=V();
 const aimEvent=(event:PointerEvent)=>{const rect=node.getBoundingClientRect();ray.setFromCamera(new THREE.Vector2((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1),camera);if(ray.ray.intersectPlane(plane,point)){const a={x:clamp(point.x/.5,-2,2),y:clamp((point.y-1.05)/.55,-2,2)};frameProps.current.aim.current=a;frameProps.current.onAim?.(a)}};
 const controls=scenePointerControls({side:()=>frameProps.current.side,aim:event=>aimEvent(event as PointerEvent),swing:()=>frameProps.current.onSwing(),chargeStart:()=>frameProps.current.onChargeStart(),chargeEnd:()=>frameProps.current.onChargeEnd(),chargeCancel:()=>frameProps.current.onChargeCancel(),focus:()=>node.focus({preventScroll:true}),capture:id=>node.setPointerCapture(id),release:id=>{if(node.hasPointerCapture(id))node.releasePointerCapture(id)}});
 node.addEventListener("pointermove",controls.move,{passive:false});node.addEventListener("pointerdown",controls.down,{passive:false});node.addEventListener("pointerup",controls.up);node.addEventListener("pointercancel",controls.cancel);node.addEventListener("lostpointercapture",controls.cancel);node.tabIndex=0;node.setAttribute("aria-label","야구 플레이 화면. 모바일은 짧게 탭해서 스윙 또는 조준하고 위아래로 밀어 스크롤하세요. 투수는 조준 후 투구 버튼 또는 스페이스를 사용하세요.");
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
 if(side!==lastSide){lastSide=side;configureCamera();lighting.focus(side);pitcher.root.visible=true;batter.root.visible=true;catcher.root.visible=side==="pitcher"}
 const bodyScale=(playerProfile(pr.pitcherId,"pitcher")?.heightCm??185)/185;pitcher.root.scale.set(throwSign*bodyScale,bodyScale,bodyScale);batterMirror.scale.x=battingSign;
 const idle=Math.sin(now/750)*.008,relative=p?now-p.releaseAt:-2000,delivery=pitchingPose(relative,underhand);
 posePitcher(pitcher,delivery,throwSign,underhand,relative < -900||relative>=640?idle:0);heldBall.visible=!p||p.resolved||relative<0;
 const localSwing=p&&pr.localSwing&&pr.localSwing.code===pr.view?.code&&pr.localSwing.pitchId===p.id?pr.localSwing:null;
 const knownSwing=localSwing??p?.aiBatterSwing??(p?.reaction?.swingAt!=null&&p.reaction.swingAim?{at:p.reaction.swingAt,aim:p.reaction.swingAim}:null);
 const key=pr.view&&p?pr.view.code+":"+p.id+":"+(knownSwing?.at??"take"):"";
 if(key!==predictionKey){predictionKey=key;prediction=pr.view&&p?evaluatePitch(pr.view,knownSwing,now):undefined;}
 const reaction=p?.reaction??prediction;
 const contactAt=knownSwing?.at??p?.reaction?.swingAt;
 const swingStart=side==="batter"?pr.swingTime:contactAt!=null?contactAt-SWING_CONTACT_MS:-Infinity;
 if(pr.swingTime!==lastSwingStart){lastSwingStart=pr.swingTime;frozenAim={...pr.aim.current}}
 const swingAge=now-swingStart,swingAim=side==="batter"?localSwing?.aim??frozenAim:knownSwing?.aim??p?.target??{x:0,y:0};
 const pose=swingPose(swingAge,swingAim,battingSign,now);
 poseBatter(batter,batterMirror,bat,pose);
 for(const [i,ghost] of batTrail.entries()){ghost.visible=swingAge>40&&swingAge<240;if(!ghost.visible)continue;const prev=swingPose(swingAge-(i+1)*9,swingAim,battingSign);const a=V(...prev.grip).addScaledVector(V(...prev.axis),.25),b=V(...prev.grip).addScaledVector(V(...prev.axis),.95);between(ghost,a,b)}
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
 return()=>{disposed=true;cancelAnimationFrame(frame);observer.disconnect();node.removeEventListener("pointermove",controls.move);node.removeEventListener("pointerdown",controls.down);node.removeEventListener("pointerup",controls.up);node.removeEventListener("pointercancel",controls.cancel);node.removeEventListener("lostpointercapture",controls.cancel);const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>(),textures=new Set<THREE.Texture>(),skeletons=new Set<THREE.Skeleton>();scene.traverse(o=>{const m=o as THREE.Mesh;if(m instanceof THREE.SkinnedMesh)skeletons.add(m.skeleton);if(m.geometry)geometries.add(m.geometry);if(m.material)for(const material of Array.isArray(m.material)?m.material:[m.material])materials.add(material)});for(const material of materials){for(const value of Object.values(material))if(value instanceof THREE.Texture)textures.add(value);material.dispose()}textures.forEach(texture=>texture.dispose());geometries.forEach(geometry=>geometry.dispose());skeletons.forEach(skeleton=>skeleton.dispose());lighting.dispose();renderer.dispose();renderer.domElement.remove()};
 },[]);
 return <><div className="three-surface" ref={mount}/><div ref={marker} className="last-pitch-marker" hidden><i/><span>직전 공</span></div><div ref={target} className={"aim-reticle "+(props.side==="pitcher"?"pitch-reticle":"")}><i/><b/><span/></div></>;
}
