"use client";
import {useEffect,useRef,MutableRefObject} from "react";
import * as THREE from "three";
import {ActionView,Vec,PitchResult,incomingBallPosition,clamp,playerProfile,batterStats,pitcherStats,throwsLeft,batsLeft,deliveryStyle,pitcherScale,evaluatePitch} from "../lib/action-engine";
import {swingPose,pitchingPose,pitcherBodyScale,profileThrowingHand,SWING_CONTACT_MS} from "../lib/player-motion";
import {FIELD_CAMERAS,fieldFov,ballTrackingCamera} from "../lib/field-camera";
import {localNow} from "../lib/game-clock";
import {lastPitchMarker} from "../lib/pitch-feedback";
import {battedBallPosition} from "../lib/batted-ball";
import {playerAppearance,PlayerCustomization} from "../lib/player-appearance";
import {scenePointerControls} from "../lib/scene-pointer";
import {createPlayer as player,dressPlayer,dressBat,setPlayerDetail,equipCatcher as catcherEquipment,createBat as batRig} from "../lib/player-model";
import {poseBatter,posePitcher,poseLeg} from "../lib/player-pose";
import {stadiumLighting} from "../lib/scene-lighting";
import {createStadiumWorld} from "../lib/stadium-world";
import type {SeasonGame} from "../lib/season-types";
import {BASES,DEFENSIVE_SPOTS,fieldSnapshot,fieldingTeam,defenseLineup,defensiveAssignments,runnerPlans,runnerPosition,nearestFielder,fielderPosition,FieldSnapshot,RunnerPlan} from "../lib/field-play";

export type LocalSwing={code:string;pitchId:number;at:number;aim:Vec};
type Props={view:ActionView|null;batterId:string;pitcherId:string;side:"batter"|"pitcher";aim:MutableRefObject<Vec>;clock:MutableRefObject<number>;swingTime:number;localSwing:LocalSwing|null;charging:boolean;chargeStarted:number;onAim?:(p:Vec)=>void;onSwing:()=>void;onContact:(key:string)=>void;onChargeStart:()=>void;onChargeEnd:()=>void;onChargeCancel:()=>void;onReady:(ok:boolean)=>void;seasonGame?:SeasonGame|null;appearances?:Record<string,PlayerCustomization>};
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
function mesh(geo:THREE.BufferGeometry,material:THREE.Material,parent:THREE.Object3D,x=0,y=0,z=0){const m=new THREE.Mesh(geo,material);m.position.set(x,y,z);parent.add(m);return m}
function between(m:THREE.Mesh,a:THREE.Vector3,b:THREE.Vector3){const d=b.clone().sub(a);m.position.copy(a).add(b).multiplyScalar(.5);m.quaternion.setFromUnitVectors(V(0,1,0),d.clone().normalize());m.scale.y=d.length();}
function baseball(parent:THREE.Object3D,r=.065){const group=new THREE.Group();mesh(new THREE.SphereGeometry(r,20,16),new THREE.MeshStandardMaterial({color:"#fffbe7",roughness:.55,emissive:"#fffbe0",emissiveIntensity:.22}),group);const red=new THREE.LineBasicMaterial({color:"#bc3324"});for(const sign of [-1,1]){const points=[];for(let i=0;i<=64;i++){const a=i/64*Math.PI*2;points.push(V(Math.cos(a)*r*.82,Math.sin(a)*r*.82,sign*r*.56))}group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points),red))}parent.add(group);return group}
function poseRunning(model:ReturnType<typeof player>,seconds:number,running:boolean){
 const step=Math.sin(seconds*12),bounce=running?Math.abs(step)*.035:0;
 model.root.position.y=bounce;model.hips.position.y=.89;model.torso.position.y=.91;model.torso.rotation.set(running?.18:.08,0,0);
 model.ll.position.set(.115,.87,0);model.rl.position.set(-.115,.87,0);
 for(const [leg,knee,foot,phase]of [[model.ll,model.lk,model.lf,step],[model.rl,model.rk,model.rf,-step]] as const){leg.rotation.set(running?phase*.68:-.12,0,0);knee.rotation.set(running?Math.max(0,-phase)*.9:.25,0,0);foot.rotation.set(running?-.12:-.13,0,0);}
 model.left.rotation.set(running?-step*.65:-.3,0,.1);model.right.rotation.set(running?step*.65:-.3,0,-.1);model.le.rotation.set(-.8,0,0);model.re.rotation.set(-.8,0,0);model.head.rotation.set(0,0,0);
}
export default function ActionScene(props:Props){
 const mount=useRef<HTMLDivElement>(null),target=useRef<HTMLDivElement>(null),marker=useRef<HTMLDivElement>(null),frameProps=useRef(props);frameProps.current=props;
 useEffect(()=>{if(!mount.current)return;const node=mount.current;let disposed=false,frame=0;let renderer:THREE.WebGLRenderer;
 try{renderer=new THREE.WebGLRenderer({antialias:true,alpha:true,powerPreference:"high-performance"});}catch{props.onReady(false);return}
 const compact=window.matchMedia("(any-pointer: coarse)").matches||node.clientWidth<700;
 renderer.setPixelRatio(Math.min(window.devicePixelRatio,compact?1.4:1.8));renderer.setClearColor(0,0);node.appendChild(renderer.domElement);
 const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(47,1,.03,450);scene.add(camera);const world=createStadiumWorld(scene,{compact}),lighting=stadiumLighting(renderer,scene,compact);
 renderer.shadowMap.type=THREE.PCFShadowMap;
 const pitcher=player("#e76c25"),batter=player("#2060b0",true),catcher=player("#263c54"),batterMirror=new THREE.Group();scene.add(pitcher.root,batterMirror,catcher.root);batterMirror.add(batter.root);pitcher.root.position.set(0,0,-18.44);batter.root.position.set(-.92,0,.06);catcher.root.position.set(0,-.66,1.1);catcher.root.scale.setScalar(.9);catcher.root.rotation.y=Math.PI;catcher.ll.rotation.x=-1.65;catcher.rl.rotation.x=-1.65;catcher.lk.rotation.x=2.1;catcher.rk.rotation.x=2.1;
 const heldBall=baseball(pitcher.re,.055);heldBall.position.set(0,-.34,0);
 catcherEquipment(catcher);
 const fielders=DEFENSIVE_SPOTS.map((spot,i)=>{const model=player("#e76c25");model.root.name="Defender "+(i+1);model.root.position.set(spot.x,0,spot.z);scene.add(model.root);return model;});
 const runners=Array.from({length:4},(_,i)=>{const model=player("#2060b0",true);model.root.name="Base runner "+i;model.root.visible=false;scene.add(model.root);return model;});
 const zone=new THREE.Group();scene.add(zone);const lineMat=new THREE.LineBasicMaterial({color:"#f0f7e5",transparent:true,opacity:.36,depthTest:false});const gridMat=new THREE.LineBasicMaterial({color:"#e5f5df",transparent:true,opacity:.14,depthTest:false});
 for(let i=0;i<=3;i++){const x=-.5+i/3,y=.5+i/3*1.1;zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(x,.5,0),V(x,1.6,0)]),i===0||i===3?lineMat:gridMat));zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(-.5,y,0),V(.5,y,0)]),i===0||i===3?lineMat:gridMat))}
 const ball=baseball(scene),trail=Array.from({length:9},(_,i)=>{const m=mesh(new THREE.SphereGeometry(.035-i*.002,8,6),new THREE.MeshBasicMaterial({color:"#f5f5b9",transparent:true,opacity:.33*(1-i/9),depthWrite:false}),scene);m.visible=false;return m});
 const bat=batRig(scene);
 const batTrail=Array.from({length:5},(_,i)=>{const m=mesh(new THREE.CylinderGeometry(.045,.023,1,10),new THREE.MeshBasicMaterial({color:"#f2d3a0",transparent:true,opacity:.12-i*.018,depthWrite:false}),scene);m.visible=false;return m});
 let width=1,height=1,lastSide="",lastSwingStart=-Infinity,frozenAim={x:0,y:0},predictionKey="",prediction:PitchResult|undefined,tracking=false;
 let snapshot:FieldSnapshot|null=null,previousSnapshot:FieldSnapshot|null=null,pitchSnapshot:FieldSnapshot|null=null,snapshotKey="",pitchSnapshotKey="",lastPlayKey="";
 let play:{key:string;pitchId:number;at:number;reaction:PitchResult;before:FieldSnapshot|null;batterId:string;plans:RunnerPlan[];fieldTarget:{x:number;y:number;z:number};fielder:number}|null=null;
 const configureCamera=()=>{const side=frameProps.current.side,config=FIELD_CAMERAS[side];camera.position.set(config.position[0],config.position[1],config.position[2]);camera.lookAt(config.target[0],config.target[1],config.target[2]);camera.aspect=width/height;camera.fov=fieldFov(side,camera.aspect);camera.updateProjectionMatrix();camera.updateMatrixWorld(true)};
 const resize=()=>{width=node.clientWidth;height=node.clientHeight;renderer.setSize(width,height,false);configureCamera()};const observer=new ResizeObserver(resize);observer.observe(node);resize();
 const ray=new THREE.Raycaster(),plane=new THREE.Plane(V(0,0,1),0),point=V();
 const aimEvent=(event:PointerEvent)=>{if(tracking)return;const rect=node.getBoundingClientRect();ray.setFromCamera(new THREE.Vector2((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1),camera);if(ray.ray.intersectPlane(plane,point)){const a={x:clamp(point.x/.5,-2,2),y:clamp((point.y-1.05)/.55,-2,2)};frameProps.current.aim.current=a;frameProps.current.onAim?.(a)}};
 const controls=scenePointerControls({side:()=>frameProps.current.side,aim:event=>aimEvent(event as PointerEvent),swing:()=>frameProps.current.onSwing(),chargeStart:()=>frameProps.current.onChargeStart(),chargeEnd:()=>frameProps.current.onChargeEnd(),chargeCancel:()=>frameProps.current.onChargeCancel(),focus:()=>node.focus({preventScroll:true}),capture:id=>node.setPointerCapture(id),release:id=>{if(node.hasPointerCapture(id))node.releasePointerCapture(id)}});
 node.addEventListener("pointermove",controls.move,{passive:false});node.addEventListener("pointerdown",controls.down,{passive:false});node.addEventListener("pointerup",controls.up);node.addEventListener("pointercancel",controls.cancel);node.addEventListener("lostpointercapture",controls.cancel);node.tabIndex=0;node.setAttribute("aria-label","야구 플레이 화면. 모바일은 짧게 탭해서 스윙 또는 조준하고 위아래로 밀어 스크롤하세요. 투수는 조준 후 투구 버튼 또는 스페이스를 사용하세요.");
 let appearanceKey="",lastAppearances:Props['appearances'];const runnerIds:string[]=[];
 const selectedAppearance=(pr:Props,id:string,side:"batter"|"pitcher",fallbackTeam="")=>{const profile=playerProfile(id,side) as {jerseyNumber?:unknown;bodyType?:PlayerCustomization['bodyType']}|undefined,custom={...pr.appearances?.[id],...(profile?.bodyType?{bodyType:profile.bodyType}:{})};try{const record=side==="batter"?batterStats(id):pitcherStats(id);return playerAppearance(record.team,record.name,profile?.jerseyNumber,custom);}catch{const stats=pr.seasonGame?.playerStats[id];return playerAppearance(stats?.team??fallbackTeam,stats?.name??"",undefined,custom);}};
 const refreshAppearance=(pr:Props,battingSign:number,throwSign:number)=>{
  const lineup=pr.seasonGame?defenseLineup(pr.seasonGame):[],team=pr.seasonGame?fieldingTeam(pr.seasonGame):"",defense=defensiveAssignments(lineup,pr.pitcherId,id=>playerProfile(id,"batter"));
  const key=pr.batterId+":"+pr.pitcherId+":"+(pr.view?.roster?.revision??"")+":"+battingSign+":"+throwSign+":"+lineup.join(",")+":"+[defense.catcher,...defense.fielders].join(",");
  if(key===appearanceKey&&lastAppearances===pr.appearances)return;appearanceKey=key;lastAppearances=pr.appearances;runnerIds.length=0;
  const batterLook=selectedAppearance(pr,pr.batterId,"batter"),pitcherLook=selectedAppearance(pr,pr.pitcherId,"pitcher");
  dressPlayer(batter,batterLook,battingSign);dressBat(bat,batterLook);dressPlayer(pitcher,pitcherLook,throwSign);
  const dressDefender=(model:ReturnType<typeof player>,id:string|undefined,isCatcher=false)=>{const profile=id?playerProfile(id,"batter"):undefined,look=id?selectedAppearance(pr,id,"batter",team):playerAppearance(team||pitcherLook.team,"",undefined),sign=profileThrowingHand(profile?.throws,profile?.batsThrows)==="L"?-1:1,scale=pitcherBodyScale(profile?.heightCm??look.heightCm)*(isCatcher?.9:1);dressPlayer(model,look,sign);model.root.scale.set(sign*scale,scale,scale);model.root.userData.playerId=id??null;if(isCatcher)model.root.position.y=-.66*scale/.9;};
  dressDefender(catcher,defense.catcher,true);fielders.forEach((model,i)=>dressDefender(model,defense.fielders[i]));
 };
 const render=()=>{if(disposed)return;const pr=frameProps.current,now=localNow()+pr.clock.current,p=pr.view?.pitch,side=pr.side;
 const battingSign=batsLeft(pr.batterId,pr.pitcherId)?-1:1,throwSign=throwsLeft(pr.pitcherId)?-1:1,style=deliveryStyle(pr.pitcherId);
 const game=pr.seasonGame,nextSnapshotKey=game?game.id+":"+game.inning+":"+game.half+":"+game.plateAppearances:"";
 if(nextSnapshotKey!==snapshotKey){previousSnapshot=snapshot;snapshot=game?fieldSnapshot(game):null;snapshotKey=nextSnapshotKey;if(previousSnapshot?.id!==snapshot?.id){play=null;lastPlayKey="";}}
 const currentPitchKey=p?pr.view!.code+":"+p.id:"";if(currentPitchKey!==pitchSnapshotKey){pitchSnapshotKey=currentPitchKey;pitchSnapshot=snapshot;}
 world.update(now/1000);world.setScoreboard(game?{home:game.homeTeam,away:game.awayTeam,homeScore:game.homeRuns,awayScore:game.awayRuns,inning:game.inning,half:game.half}:{home:"PITCHER",away:"BATTER",homeScore:0,awayScore:pr.view?.score??0,inning:Math.min(6,(pr.view?.round??0)+1),half:"top"});
 refreshAppearance(pr,battingSign,throwSign);
 if(side!==lastSide){lastSide=side;configureCamera();lighting.focus(side);pitcher.root.visible=true;batter.root.visible=true;catcher.root.visible=side==="pitcher"}
 const bodyScale=pitcherScale(pr.pitcherId),batterScale=pitcherBodyScale(playerProfile(pr.batterId,"batter")?.heightCm);pitcher.root.scale.set(throwSign*bodyScale,bodyScale,bodyScale);batterMirror.scale.set(battingSign*batterScale,batterScale,batterScale);
 const idle=Math.sin(now/750)*.008,relative=p?now-p.releaseAt:-2000,delivery=pitchingPose(relative,style);
 posePitcher(pitcher,delivery,throwSign,style==="underhand",relative < -900||relative>=640?idle:0);heldBall.visible=!p||relative<0;
 const localSwing=p&&pr.localSwing&&pr.localSwing.code===pr.view?.code&&pr.localSwing.pitchId===p.id?pr.localSwing:null;
 const knownSwing=localSwing??p?.aiBatterSwing??(p?.reaction?.swingAt!=null&&p.reaction.swingAim?{at:p.reaction.swingAt,aim:p.reaction.swingAim}:null);
 const key=pr.view&&p?pr.view.code+":"+p.id+":"+(knownSwing?.at??"take"):"";
 if(key!==predictionKey){predictionKey=key;prediction=pr.view&&p?evaluatePitch(pr.view,knownSwing,now):undefined;}
 const reaction=p?.reaction??prediction;
 const playAt=reaction?.contact?.at??reaction?.bodyHit?.at??(p?p.releaseAt+p.flightMs:Infinity);
 const playKey=p&&reaction?pr.view!.code+":"+p.id+":"+playAt:"";
 if(reaction&&(reaction.contact||reaction.plateEnded)&&now>=playAt&&playKey!==lastPlayKey){
  lastPlayKey=playKey;const before=pitchSnapshot??snapshot;
  const fieldTarget=reaction.contact?battedBallPosition(reaction,playAt+(reaction.trajectory==="fly"?4000:1900))!:{x:0,y:0,z:0};fieldTarget.x=clamp(fieldTarget.x,-80,80);fieldTarget.z=clamp(fieldTarget.z,-122,-3);
  play={key:playKey,pitchId:p!.id,at:playAt,reaction,before,batterId:pr.batterId,plans:before?runnerPlans(before,pr.batterId,reaction,snapshot??undefined):[],fieldTarget,fielder:nearestFielder(fieldTarget)};
 }
 if(play&&p?.id!==play.pitchId&&relative>=-900)play=null;
 if(play&&snapshot&&play.before&&snapshot.plateAppearances>play.before.plateAppearances)play.plans=runnerPlans(play.before,play.batterId,play.reaction,snapshot);
 const contactAt=knownSwing?.at??p?.reaction?.swingAt;
 const swingStart=side==="batter"?pr.swingTime:contactAt!=null?contactAt-SWING_CONTACT_MS:-Infinity;
 if(pr.swingTime!==lastSwingStart){lastSwingStart=pr.swingTime;frozenAim={...pr.aim.current}}
 const swingAge=now-swingStart,swingAim=side==="batter"?localSwing?.aim??frozenAim:knownSwing?.aim??p?.target??{x:0,y:0};
 const localAim={x:swingAim.x/batterScale,y:((1.05+swingAim.y*.55)/batterScale-1.05)/.55};
 const pose=swingPose(swingAge,localAim,battingSign,now);
 poseBatter(batter,batterMirror,bat,pose);
 for(const [i,ghost] of batTrail.entries()){ghost.visible=swingAge>40&&swingAge<240;if(!ghost.visible)continue;const prev=swingPose(swingAge-(i+1)*9,localAim,battingSign);const a=V(...prev.grip).addScaledVector(V(...prev.axis),.25).multiplyScalar(batterScale),b=V(...prev.grip).addScaledVector(V(...prev.axis),.95).multiplyScalar(batterScale);between(ghost,a,b)}
 ball.visible=false;for(const t of trail)t.visible=false;
 if(p&&now>=p.releaseAt){const u=(now-p.releaseAt)/p.flightMs;
 if(reaction?.contact&&now>=reaction.contact.at){const pos=battedBallPosition(reaction,now)!;ball.position.set(pos.x,pos.y,pos.z);ball.visible=now-reaction.contact.at<7000;ball.scale.setScalar(1.2);if(now-reaction.contact.at<1200)pr.onContact(pr.view!.code+":"+p.id+":bat");}
 else if(reaction?.bodyHit&&now>=reaction.bodyHit.at){const hit=reaction.bodyHit,elapsed=(now-hit.at)/1000;ball.position.set(hit.position.x,Math.max(.065,hit.position.y-4.9*elapsed*elapsed),hit.position.z-.25*Math.min(elapsed,1));ball.visible=elapsed<1.8;ball.scale.setScalar(1);}
 else if(u<1.24){
  const incoming=(time:number)=>incomingBallPosition(p,reaction,time,side);
  const pos=incoming(now);ball.position.set(pos.x,pos.y,pos.z);ball.visible=true;ball.scale.setScalar(side==="batter"?1.3:1.05);for(let i=0;i<trail.length;i++){const old=incoming(now-(i+1)*9);trail[i].position.set(old.x,old.y,old.z);trail[i].visible=now-(i+1)*9>=p.releaseAt}}
 ball.rotation.x=now/50;ball.rotation.z=now/75;}
 const playAge=play?now-play.at:Infinity,fieldAction=!!play&&!!play.reaction.contact&&playAge<8000;
 if(fieldAction&&play){
  const pos=battedBallPosition(play.reaction,now)??play.fieldTarget,shot=ballTrackingCamera(pos),base=FIELD_CAMERAS[side],t=clamp(playAge/650,0,1),blend=t*t*(3-2*t);
  ball.position.set(pos.x,pos.y,pos.z);ball.visible=true;ball.scale.setScalar(1.2);
  camera.position.copy(V(...base.position)).lerp(V(...shot.position),blend);camera.lookAt(V(...base.target).lerp(V(...shot.target),blend));camera.fov=fieldFov(side,width/height)+(shot.fov-fieldFov(side,width/height))*blend;camera.updateProjectionMatrix();camera.updateMatrixWorld(true);
 }else if(tracking)configureCamera();
 tracking=fieldAction;zone.visible=!tracking;catcher.root.visible=side==="pitcher"||tracking;
 fielders.forEach((model,i)=>{const start=DEFENSIVE_SPOTS[i],active=fieldAction&&play;let goal=start;
  if(active){if(i===active.fielder)goal=active.fieldTarget;else if(i<4)goal=i===0?BASES[1]:i===3?BASES[3]:BASES[2];else goal={x:start.x+clamp(active.fieldTarget.x-start.x,-12,12),y:0,z:start.z+clamp(active.fieldTarget.z-start.z,-8,8)};}
  const pos=active?fielderPosition(start,goal,playAge):start,run=!!active&&Math.hypot(pos.x-goal.x,pos.z-goal.z)>.2;model.root.position.set(pos.x,0,pos.z);model.root.rotation.y=Math.atan2((active?goal.x:0)-pos.x,(active?goal.z:0)-pos.z);poseRunning(model,now/1000+i,run);setPlayerDetail(model,model.root.position.distanceTo(camera.position),compact);
 });
 const runnerActive=!!play&&playAge<17000&&(p?.id===play.pitchId||relative<-900),plans=runnerActive?play!.plans:game?game.bases.flatMap((runner,i)=>runner?[{playerId:runner.playerId,from:i+1,to:i+1,out:false}]:[]):[];
 runners.forEach((model,i)=>{const plan=plans[i];if(!plan){model.root.visible=false;return;}const pos=runnerPosition(plan,runnerActive?Math.max(0,playAge-200):0);model.root.visible=!(pos.finished&&(plan.to===4||plan.out));if(!model.root.visible)return;
  if(runnerIds[i]!==plan.playerId){runnerIds[i]=plan.playerId;dressPlayer(model,selectedAppearance(pr,plan.playerId,"batter",game?game.half==="top"?game.awayTeam:game.homeTeam:""));model.root.scale.setScalar(pitcherBodyScale(playerProfile(plan.playerId,"batter")?.heightCm??pr.appearances?.[plan.playerId]?.heightCm));}
  model.root.position.set(pos.x,0,pos.z);model.root.rotation.y=pos.finished?Math.atan2(-pos.x,-pos.z):pos.heading;poseRunning(model,now/1000+i,runnerActive&&!pos.finished);setPlayerDetail(model,model.root.position.distanceTo(camera.position),compact);
 });
 const batterRunning=runnerActive&&play!.plans.some(plan=>plan.from===0)&&playAge>500;batter.root.visible=!batterRunning;bat.visible=!batterRunning;setPlayerDetail(batter,batter.root.getWorldPosition(V()).distanceTo(camera.position),compact);setPlayerDetail(pitcher,pitcher.root.position.distanceTo(camera.position),compact);setPlayerDetail(catcher,catcher.root.position.distanceTo(camera.position),compact);
 const aim=pr.aim.current,screen=V(aim.x*.5,1.05+aim.y*.55,0).project(camera);if(target.current){target.current.style.left=(screen.x*.5+.5)*width+"px";target.current.style.top=(-screen.y*.5+.5)*height+"px";target.current.style.setProperty("--reticle-size",side==="batter"?"54px":"32px")}
 if(target.current)target.current.hidden=tracking;
 const mark=lastPitchMarker(pr.view,now,reaction?.bodyHit??null);if(marker.current){marker.current.hidden=!mark||tracking;if(mark){const point=V(mark.position.x,mark.position.y,mark.position.z).project(camera);marker.current.style.left=(point.x*.5+.5)*width+"px";marker.current.style.top=(-point.y*.5+.5)*height+"px";marker.current.className="last-pitch-marker"+(mark.body?" body-hit-marker":"");marker.current.setAttribute("aria-label",mark.body?"직전 공 몸에 맞은 위치":"직전 공 통과 위치");marker.current.querySelector("span")!.textContent=mark.body?"몸에 맞음":"직전 공";}}
 renderer.render(scene,camera);frame=requestAnimationFrame(render);
 };frame=requestAnimationFrame(render);props.onReady(true);
 return()=>{disposed=true;cancelAnimationFrame(frame);observer.disconnect();node.removeEventListener("pointermove",controls.move);node.removeEventListener("pointerdown",controls.down);node.removeEventListener("pointerup",controls.up);node.removeEventListener("pointercancel",controls.cancel);node.removeEventListener("lostpointercapture",controls.cancel);world.dispose();const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>(),textures=new Set<THREE.Texture>(),skeletons=new Set<THREE.Skeleton>();scene.traverse(o=>{const m=o as THREE.Mesh;if(m instanceof THREE.SkinnedMesh)skeletons.add(m.skeleton);if(m.geometry)geometries.add(m.geometry);if(m.material)for(const material of Array.isArray(m.material)?m.material:[m.material])materials.add(material)});for(const material of materials){for(const value of Object.values(material))if(value instanceof THREE.Texture)textures.add(value);material.dispose()}textures.forEach(texture=>texture.dispose());geometries.forEach(geometry=>geometry.dispose());skeletons.forEach(skeleton=>skeleton.dispose());lighting.dispose();renderer.dispose();renderer.domElement.remove()};
 },[]);
 return <><div className="three-surface" ref={mount}/><div ref={marker} className="last-pitch-marker" hidden><i/><span>직전 공</span></div><div ref={target} className={"aim-reticle "+(props.side==="pitcher"?"pitch-reticle":"")}><i/><b/><span/></div></>;
}
