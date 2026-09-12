"use client";
import {useEffect,useRef,MutableRefObject} from "react";
import * as THREE from "three";
import {ActionView,Vec,PitchResult,incomingBallPosition,clamp,playerProfile,throwsLeft,batsLeft,isUnderhand,evaluatePitch} from "../lib/action-engine";
import {swingPose,pitchingPose,SWING_CONTACT_MS} from "../lib/player-motion";
import {FIELD_CAMERAS,fieldFov} from "../lib/field-camera";
import {localNow} from "../lib/game-clock";
import {lastPitchMarker} from "../lib/pitch-feedback";
import {battedBallPosition} from "../lib/batted-ball";

export type LocalSwing={code:string;pitchId:number;at:number;aim:Vec};
type Props={view:ActionView|null;batterId:string;pitcherId:string;side:"batter"|"pitcher";aim:MutableRefObject<Vec>;clock:MutableRefObject<number>;swingTime:number;localSwing:LocalSwing|null;charging:boolean;chargeStarted:number;onAim?:(p:Vec)=>void;onSwing:()=>void;onContact:(key:string)=>void;onChargeStart:()=>void;onChargeEnd:()=>void;onReady:(ok:boolean)=>void};
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
function mat(color:string,roughness=.7){return new THREE.MeshStandardMaterial({color,roughness})}
function mesh(geo:THREE.BufferGeometry,material:THREE.Material,parent:THREE.Object3D,x=0,y=0,z=0){const m=new THREE.Mesh(geo,material);m.position.set(x,y,z);parent.add(m);return m}
function capsule(parent:THREE.Object3D,r:number,length:number,material:THREE.Material,x=0,y=0,z=0){return mesh(new THREE.CapsuleGeometry(r,length,4,10),material,parent,x,y,z)}
function segment(parent:THREE.Object3D,length:number,r:number,material:THREE.Material,x:number,y:number,z:number){const group=new THREE.Group();group.position.set(x,y,z);parent.add(group);capsule(group,r,length-2*r,material,0,-length/2);return group}
function player(color:string,isBatter=false){
 const root=new THREE.Group(),jersey=mat(color),pants=mat("#e8eced"),skin=mat("#c99670"),dark=mat("#152135"),white=mat("#fafafa"),leather=mat("#8a5529");
 const hips=new THREE.Group();hips.position.y=.93;root.add(hips);capsule(hips,.2,.12,pants);
 const torso=new THREE.Group();torso.position.y=.95;root.add(torso);capsule(torso,.23,.35,jersey,0,.38);mesh(new THREE.BoxGeometry(.43,.07,.27),dark,torso,0,.02);
 const head=new THREE.Group();head.position.y=1.64;torso.add(head);head.position.y=.82;
 mesh(new THREE.SphereGeometry(.145,16,12),skin,head,0,.06);mesh(new THREE.SphereGeometry(.151,16,10,0,Math.PI*2,0,Math.PI*.52),dark,head,0,.1);mesh(new THREE.BoxGeometry(.28,.03,.16),dark,head,0,.12,.12);mesh(new THREE.SphereGeometry(.035,8,6),skin,head,0,.05,.135);
 for(const x of [-.057,.057])mesh(new THREE.SphereGeometry(.009,6,6),dark,head,x,.083,.133);
 mesh(new THREE.BoxGeometry(.06,.3,.013),white,torso,0,.37,.238);for(const y of [.22,.34,.47])mesh(new THREE.SphereGeometry(.009,5,5),dark,torso,0,y,.251);
 // The model faces +z: its anatomical right shoulder is on -x.
 const left=segment(torso,.37,.079,jersey,.27,.58,0),right=segment(torso,.37,.079,jersey,-.27,.58,0);
 const le=segment(left,.35,.067,skin,0,-.34,0),re=segment(right,.35,.067,skin,0,-.34,0);
 const glove=mesh(new THREE.SphereGeometry(.13,12,10),leather,le,0,-.34,.025);glove.scale.set(.9,1.2,.6);glove.visible=!isBatter;const hand=mesh(new THREE.SphereGeometry(.06,10,8),skin,re,0,-.34,0);hand.visible=!isBatter;
 const ll=segment(root,.46,.105,pants,.115,.91,0),rl=segment(root,.46,.105,pants,-.115,.91,0),lk=segment(ll,.43,.084,pants,0,-.43,0),rk=segment(rl,.43,.084,pants,0,-.43,0);
 for(const knee of [lk,rk]){mesh(new THREE.BoxGeometry(.17,.11,.28),dark,knee,0,-.39,.055);mesh(new THREE.BoxGeometry(.175,.025,.29),white,knee,0,-.448,.05)}
 return {root,hips,torso,head,left,right,le,re,ll,rl,lk,rk};
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
 const gear=mat("#1a2233");mesh(new THREE.BoxGeometry(.4,.46,.15),gear,catcher.torso,0,.4,.25);const mask=mesh(new THREE.SphereGeometry(.17,12,8),new THREE.MeshStandardMaterial({color:"#818894",wireframe:true}),catcher.torso,0,.88,.03);mask.scale.z=1.1;
 const zone=new THREE.Group();scene.add(zone);const lineMat=new THREE.LineBasicMaterial({color:"#f0f7e5",transparent:true,opacity:.36,depthTest:false});const gridMat=new THREE.LineBasicMaterial({color:"#e5f5df",transparent:true,opacity:.14,depthTest:false});
 for(let i=0;i<=3;i++){const x=-.5+i/3,y=.5+i/3*1.1;zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(x,.5,0),V(x,1.6,0)]),i===0||i===3?lineMat:gridMat));zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(-.5,y,0),V(.5,y,0)]),i===0||i===3?lineMat:gridMat))}
 const ball=baseball(scene),trail=Array.from({length:9},(_,i)=>{const m=mesh(new THREE.SphereGeometry(.035-i*.002,8,6),new THREE.MeshBasicMaterial({color:"#f5f5b9",transparent:true,opacity:.33*(1-i/9),depthWrite:false}),scene);m.visible=false;return m});
 const bat=batRig(scene);
 const batTrail=Array.from({length:5},(_,i)=>{const m=mesh(new THREE.CylinderGeometry(.045,.023,1,10),new THREE.MeshBasicMaterial({color:"#f2d3a0",transparent:true,opacity:.12-i*.018,depthWrite:false}),scene);m.visible=false;return m});
 const fpGlove=new THREE.Group();camera.add(fpGlove);const mitt=mesh(new THREE.SphereGeometry(.19,16,12),mat("#85532a"),fpGlove);mitt.scale.set(1,1.2,.46);for(let i=0;i<4;i++)capsule(fpGlove,.027,.15,mat("#b27e49"),-.105+i*.06,.075,.087);const fpBall=baseball(camera,.066);
 const fpThrowHand=mesh(new THREE.SphereGeometry(.063,14,10),mat("#c99670"),camera),fpThrowArm=mesh(new THREE.CylinderGeometry(.04,.065,1,10),mat("#c99670"),camera);
 let width=1,height=1,lastSide="",lastSwingStart=-Infinity,frozenAim={x:0,y:0},predictionKey="",prediction:PitchResult|undefined;
 const configureCamera=()=>{const side=frameProps.current.side,config=FIELD_CAMERAS[side];camera.position.set(config.position[0],config.position[1],config.position[2]);camera.lookAt(config.target[0],config.target[1],config.target[2]);camera.aspect=width/height;camera.fov=fieldFov(side,camera.aspect);camera.updateProjectionMatrix();camera.updateMatrixWorld(true)};
 const resize=()=>{width=node.clientWidth;height=node.clientHeight;renderer.setSize(width,height,false);configureCamera()};const observer=new ResizeObserver(resize);observer.observe(node);resize();
 const ray=new THREE.Raycaster(),plane=new THREE.Plane(V(0,0,1),0),point=V();
 const aimEvent=(event:PointerEvent)=>{const rect=node.getBoundingClientRect();ray.setFromCamera(new THREE.Vector2((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1),camera);if(ray.ray.intersectPlane(plane,point)){const a={x:clamp(point.x/.5,-2,2),y:clamp((point.y-1.05)/.55,-2,2)};frameProps.current.aim.current=a;frameProps.current.onAim?.(a)}};
 let down:{x:number;y:number;touch:boolean}|null=null;
 const pointerDown=(e:PointerEvent)=>{if(e.button!==0)return;node.focus();aimEvent(e);down={x:e.clientX,y:e.clientY,touch:e.pointerType==="touch"};if(e.pointerType!=="touch"){if(frameProps.current.side==="batter")frameProps.current.onSwing();else frameProps.current.onChargeStart()}node.setPointerCapture(e.pointerId)};
 const pointerUp=(e:PointerEvent)=>{if(down&&!down.touch&&frameProps.current.side==="pitcher")frameProps.current.onChargeEnd();down=null};
 node.addEventListener("pointermove",aimEvent);node.addEventListener("pointerdown",pointerDown);node.addEventListener("pointerup",pointerUp);node.addEventListener("pointercancel",pointerUp);node.tabIndex=0;node.setAttribute("aria-label","야구 플레이 화면. 마우스 또는 터치로 조준하고 스페이스로 스윙하거나 투구하세요.");
 const render=()=>{if(disposed)return;const pr=frameProps.current,now=localNow()+pr.clock.current,p=pr.view?.pitch,side=pr.side;
 const battingSign=batsLeft(pr.batterId,pr.pitcherId)?-1:1,throwSign=throwsLeft(pr.pitcherId)?-1:1,underhand=isUnderhand(pr.pitcherId);
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
 fpGlove.position.set(-.23*throwSign,-.19+(pr.charging?.04:0),-.85);fpGlove.scale.setScalar(.57);fpGlove.rotation.set(-.2,.4*throwSign,-.2*throwSign);
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
 return()=>{disposed=true;cancelAnimationFrame(frame);observer.disconnect();node.removeEventListener("pointermove",aimEvent);node.removeEventListener("pointerdown",pointerDown);node.removeEventListener("pointerup",pointerUp);node.removeEventListener("pointercancel",pointerUp);scene.traverse(o=>{const m=o as THREE.Mesh;if(m.geometry)m.geometry.dispose();if(m.material){for(const material of Array.isArray(m.material)?m.material:[m.material])material.dispose()}});renderer.dispose();renderer.domElement.remove()};
 },[]);
 return <><div className="three-surface" ref={mount}/><div ref={marker} className="last-pitch-marker" hidden><i/><span>직전 공</span></div><div ref={target} className={"aim-reticle "+(props.side==="pitcher"?"pitch-reticle":"")}><i/><b/><span/></div></>;
}
