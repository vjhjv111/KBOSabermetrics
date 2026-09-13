"use client";
import {useEffect,useRef,MutableRefObject} from "react";
import * as THREE from "three";
import {ActionView,Vec,PitchResult,incomingBallPosition,clamp,playerProfile,batterStats,pitcherStats,throwsLeft,batsLeft,deliveryStyle,pitcherScale,evaluatePitch} from "../lib/action-engine";
import {swingPose,pitchingPose,pitcherBodyScale,profileThrowingHand,SWING_CONTACT_MS,SWING_DURATION_MS,PITCH_WINDUP_MS,PITCH_RECOVERY_MS} from "../lib/player-motion";
import {battingLoadForSwing} from "../lib/batting-load";
import {FIELD_CAMERAS,fieldFov,ballTrackingCamera} from "../lib/field-camera";
import {localNow} from "../lib/game-clock";
import {lastPitchMarker} from "../lib/pitch-feedback";
import {fieldPlayTimeline,fieldBallPosition,type FieldPlayTimeline} from "../lib/field-play-timeline";
import {RESULT_DISPLAY_MS} from "../lib/pitch-cycle";
import {playerAppearance,PlayerCustomization} from "../lib/player-appearance";
import {scenePointerControls} from "../lib/scene-pointer";
import {createPlayer as player,dressPlayer,dressBat,setPlayerDetail,equipCatcher as catcherEquipment,equipRunnerHands,equipPitchGrip,createBat as batRig} from "../lib/player-model";
import {PITCH_GRIP_BALL_RADIUS,attachPitchGripBall} from '../lib/player-pitch-grip';
import {poseBatter,posePitcher} from "../lib/player-pose";
import {poseRunning,poseFielding} from "../lib/fielder-pose";
import {fielderMotion,runnerMotion} from "../lib/field-motion-path";
import {poseCatcher} from "../lib/catcher-pose";
import {setPlayerBlink} from '../lib/player-blink';
import {samplePlayerBlink,type BlinkWindow} from '../lib/blink-timing';
import {catcherPitchPlan,receivedBallPosition} from "../lib/catcher-presentation";
import {stadiumLighting} from "../lib/scene-lighting";
import {createStadiumWorld} from "../lib/stadium-world";
import {koreaTimeOfDay,watchKoreaTimeOfDay} from "../lib/korea-daylight";
import type {SeasonGame} from "../lib/season-types";
import {BASES,DEFENSIVE_SPOTS,fieldSnapshot,fieldingTeam,defenseLineup,defensiveAssignments,runnerPlans,FieldSnapshot,RunnerPlan} from "../lib/field-play";

export type LocalSwing={code:string;pitchId:number;at:number;aim:Vec};
type Props={view:ActionView|null;batterId:string;pitcherId:string;side:"batter"|"pitcher";aim:MutableRefObject<Vec>;clock:MutableRefObject<number>;swingTime:number;localSwing:LocalSwing|null;charging:boolean;chargeStarted:number;onAim?:(p:Vec)=>void;onSwing:()=>void;onContact:(key:string)=>void;onChargeStart:()=>void;onChargeEnd:()=>void;onChargeCancel:()=>void;onReady:(ok:boolean)=>void;seasonGame?:SeasonGame|null;seasonPresentationGame?:SeasonGame|null;hideScore?:boolean;appearances?:Record<string,PlayerCustomization>};
const V=(x=0,y=0,z=0)=>new THREE.Vector3(x,y,z);
function mesh(geo:THREE.BufferGeometry,material:THREE.Material,parent:THREE.Object3D,x=0,y=0,z=0){const m=new THREE.Mesh(geo,material);m.position.set(x,y,z);parent.add(m);return m}
function between(m:THREE.Mesh,a:THREE.Vector3,b:THREE.Vector3){const d=b.clone().sub(a);m.position.copy(a).add(b).multiplyScalar(.5);m.quaternion.setFromUnitVectors(V(0,1,0),d.clone().normalize());m.scale.y=d.length();}
function blinkPlayer(model:ReturnType<typeof player>,now:number,seed:string,windows:readonly BlinkWindow[],interruptAt?:number){if(model.root.visible)setPlayerBlink(model.head,samplePlayerBlink(now,seed,windows,interruptAt));}
function baseball(parent:THREE.Object3D,r=.065){const group=new THREE.Group();mesh(new THREE.SphereGeometry(r,20,16),new THREE.MeshStandardMaterial({color:"#fffbe7",roughness:.55,emissive:"#fffbe0",emissiveIntensity:.22}),group);const red=new THREE.LineBasicMaterial({color:"#bc3324"});for(const sign of [-1,1]){const points=[];for(let i=0;i<=64;i++){const a=i/64*Math.PI*2;points.push(V(Math.cos(a)*r*.82,Math.sin(a)*r*.82,sign*r*.56))}group.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints(points),red))}parent.add(group);return group}
export default function ActionScene(props:Props){
 const mount=useRef<HTMLDivElement>(null),target=useRef<HTMLDivElement>(null),marker=useRef<HTMLDivElement>(null),frameProps=useRef(props);frameProps.current=props;
 useEffect(()=>{if(!mount.current)return;const node=mount.current;let disposed=false,frame=0;let renderer:THREE.WebGLRenderer;
 try{renderer=new THREE.WebGLRenderer({antialias:true,alpha:true,powerPreference:"high-performance"});}catch{props.onReady(false);return}
 const compact=window.matchMedia("(any-pointer: coarse)").matches||node.clientWidth<700;
 renderer.setPixelRatio(Math.min(window.devicePixelRatio,compact?1.4:1.8));renderer.setClearColor(0,0);node.appendChild(renderer.domElement);
 const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(47,1,.03,450);scene.add(camera);const timeOfDay=koreaTimeOfDay(),world=createStadiumWorld(scene,{compact,timeOfDay}),lighting=stadiumLighting(renderer,scene,compact,timeOfDay);
 const stopDaylight=watchKoreaTimeOfDay(phase=>{world.setTimeOfDay(phase);lighting.setTimeOfDay(phase);node.dataset.timeOfDay=phase;});
 renderer.shadowMap.type=THREE.PCFShadowMap;
 const pitcher=player("#e76c25"),batter=player("#2060b0",true),catcher=player("#263c54"),batterMirror=new THREE.Group();scene.add(pitcher.root,batterMirror,catcher.root);batterMirror.add(batter.root);pitcher.root.position.set(0,0,-18.44);batter.root.position.set(-.92,0,.06);catcher.root.position.set(0,0,1.1);catcher.root.rotation.y=Math.PI;
 const pitcherGrip=equipPitchGrip(pitcher),heldBall=baseball(pitcherGrip.root,PITCH_GRIP_BALL_RADIUS);attachPitchGripBall(pitcherGrip,heldBall);
 catcherEquipment(catcher);
 const fielders=DEFENSIVE_SPOTS.map((spot,i)=>{const model=player("#e76c25");model.root.name="Defender "+(i+1);model.root.position.set(spot.x,0,spot.z);scene.add(model.root);return model;});
 const runners=Array.from({length:4},(_,i)=>{const model=player("#2060b0",true);equipRunnerHands(model);model.root.name="Base runner "+i;model.root.visible=false;scene.add(model.root);return model;});
 const zone=new THREE.Group();scene.add(zone);const lineMat=new THREE.LineBasicMaterial({color:"#f0f7e5",transparent:true,opacity:.36,depthTest:false});const gridMat=new THREE.LineBasicMaterial({color:"#e5f5df",transparent:true,opacity:.14,depthTest:false});
 for(let i=0;i<=3;i++){const x=-.5+i/3,y=.5+i/3*1.1;zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(x,.5,0),V(x,1.6,0)]),i===0||i===3?lineMat:gridMat));zone.add(new THREE.Line(new THREE.BufferGeometry().setFromPoints([V(-.5,y,0),V(.5,y,0)]),i===0||i===3?lineMat:gridMat))}
 const ball=baseball(scene),trail=Array.from({length:9},(_,i)=>{const m=mesh(new THREE.SphereGeometry(.035-i*.002,8,6),new THREE.MeshBasicMaterial({color:"#f5f5b9",transparent:true,opacity:.33*(1-i/9),depthWrite:false}),scene);m.visible=false;return m});
 const bat=batRig(scene);
 const batTrail=Array.from({length:5},(_,i)=>{const m=mesh(new THREE.CylinderGeometry(.045,.023,1,10),new THREE.MeshBasicMaterial({color:"#f2d3a0",transparent:true,opacity:.12-i*.018,depthWrite:false}),scene);m.visible=false;return m});
 let width=1,height=1,lastSide="",lastSwingStart=-Infinity,frozenAim={x:0,y:0},predictionKey="",prediction:PitchResult|undefined,tracking=false;
 let snapshot:FieldSnapshot|null=null,previousSnapshot:FieldSnapshot|null=null,pitchSnapshot:FieldSnapshot|null=null,snapshotKey="",pitchSnapshotKey="",lastPlayKey="";
 let play:{key:string;pitchId:number;at:number;reaction:PitchResult;before:FieldSnapshot|null;batterId:string;plans:RunnerPlan[];fieldTarget:{x:number;y:number;z:number};fielder:number;timeline:FieldPlayTimeline|null}|null=null;
 const configureCamera=()=>{const side=frameProps.current.side,config=FIELD_CAMERAS[side];camera.position.set(config.position[0],config.position[1],config.position[2]);camera.lookAt(config.target[0],config.target[1],config.target[2]);camera.aspect=width/height;camera.fov=fieldFov(side,camera.aspect);camera.updateProjectionMatrix();camera.updateMatrixWorld(true)};
 const resize=()=>{width=node.clientWidth;height=node.clientHeight;renderer.setSize(width,height,false);configureCamera()};const observer=new ResizeObserver(resize);observer.observe(node);resize();
 const ray=new THREE.Raycaster(),plane=new THREE.Plane(V(0,0,1),0),point=V();
 const aimEvent=(event:PointerEvent)=>{if(tracking)return;const rect=node.getBoundingClientRect();ray.setFromCamera(new THREE.Vector2((event.clientX-rect.left)/rect.width*2-1,-(event.clientY-rect.top)/rect.height*2+1),camera);if(ray.ray.intersectPlane(plane,point)){const a={x:clamp(point.x/.5,-2,2),y:clamp((point.y-1.05)/.55,-2,2)};frameProps.current.aim.current=a;frameProps.current.onAim?.(a)}};
 const controls=scenePointerControls({side:()=>frameProps.current.side,aim:event=>aimEvent(event as PointerEvent),swing:()=>frameProps.current.onSwing(),chargeStart:()=>frameProps.current.onChargeStart(),chargeEnd:()=>frameProps.current.onChargeEnd(),chargeCancel:()=>frameProps.current.onChargeCancel(),focus:()=>node.focus({preventScroll:true}),capture:id=>node.setPointerCapture(id),release:id=>{if(node.hasPointerCapture(id))node.releasePointerCapture(id)}});
 node.addEventListener("pointermove",controls.move,{passive:false});node.addEventListener("pointerdown",controls.down,{passive:false});node.addEventListener("pointerup",controls.up);node.addEventListener("pointercancel",controls.cancel);node.addEventListener("lostpointercapture",controls.cancel);node.tabIndex=0;node.setAttribute("aria-label","야구 플레이 화면. 모바일은 짧게 탭해서 스윙 또는 조준하고 위아래로 밀어 스크롤하세요. 투수는 조준 후 투구 버튼 또는 스페이스를 사용하세요.");
 let appearanceKey="",lastAppearances:Props['appearances'];const runnerIds:string[]=[];
 const pitchBlinkWindow={start:Infinity,end:-Infinity},fieldBlinkWindow={start:Infinity,end:-Infinity},swingBlinkWindow={start:Infinity,end:-Infinity};
 const fieldBlinkWindows=[pitchBlinkWindow,fieldBlinkWindow],batterBlinkWindows=[...fieldBlinkWindows,swingBlinkWindow];
 let previousBlinkSwing=-Infinity,swingInterruptAt=-Infinity;
 const selectedAppearance=(pr:Props,id:string,side:"batter"|"pitcher",fallbackTeam="")=>{const profile=playerProfile(id,side) as {jerseyNumber?:unknown;bodyType?:PlayerCustomization['bodyType'];heightCm?:number}|undefined,custom={...pr.appearances?.[id],...(profile?.bodyType?{bodyType:profile.bodyType}:{}),...(profile?.heightCm?{heightCm:profile.heightCm}:{})};try{const record=side==="batter"?batterStats(id):pitcherStats(id);return playerAppearance(record.team,record.name,profile?.jerseyNumber,custom);}catch{const stats=pr.seasonGame?.playerStats[id];return playerAppearance(stats?.team??fallbackTeam,stats?.name??"",undefined,custom);}};
 const refreshAppearance=(pr:Props,battingSign:number,throwSign:number)=>{
  const shownGame=pr.seasonPresentationGame===null?null:pr.seasonPresentationGame??pr.seasonGame;
  const lineup=shownGame?defenseLineup(shownGame):[],team=shownGame?fieldingTeam(shownGame):"",defense=defensiveAssignments(lineup,pr.pitcherId,id=>playerProfile(id,"batter"));
  const key=pr.batterId+":"+pr.pitcherId+":"+(pr.view?.roster?.revision??"")+":"+battingSign+":"+throwSign+":"+lineup.join(",")+":"+[defense.catcher,...defense.fielders].join(",");
  if(key===appearanceKey&&lastAppearances===pr.appearances)return;appearanceKey=key;lastAppearances=pr.appearances;runnerIds.length=0;
  const batterLook=selectedAppearance(pr,pr.batterId,"batter"),pitcherLook=selectedAppearance(pr,pr.pitcherId,"pitcher");
  dressPlayer(batter,batterLook,battingSign);dressBat(bat,batterLook);dressPlayer(pitcher,pitcherLook,throwSign);
  const dressDefender=(model:ReturnType<typeof player>,id:string|undefined,isCatcher=false)=>{const profile=id?playerProfile(id,"batter"):undefined,look=id?selectedAppearance(pr,id,"batter",team):playerAppearance(team||pitcherLook.team,"",undefined),sign=profileThrowingHand(profile?.throws,profile?.batsThrows)==="L"?-1:1,scale=pitcherBodyScale(profile?.heightCm??look.heightCm);dressPlayer(model,look,sign);model.root.scale.set(sign*scale,scale,scale);model.root.userData.playerId=id??null;if(isCatcher)model.root.position.y=0;};
  dressDefender(catcher,defense.catcher,true);fielders.forEach((model,i)=>dressDefender(model,defense.fielders[i]));
 };
 const render=()=>{if(disposed)return;const pr=frameProps.current,now=localNow()+pr.clock.current,p=pr.view?.pitch,side=pr.side;
 const battingSign=batsLeft(pr.batterId,pr.pitcherId)?-1:1,throwSign=throwsLeft(pr.pitcherId)?-1:1,style=deliveryStyle(pr.pitcherId);
 const game=pr.seasonGame,nextSnapshotKey=game?game.id+":"+game.inning+":"+game.half+":"+game.plateAppearances:"";
 if(nextSnapshotKey!==snapshotKey){previousSnapshot=snapshot;snapshot=game?fieldSnapshot(game):null;snapshotKey=nextSnapshotKey;if(previousSnapshot?.id!==snapshot?.id){play=null;lastPlayKey="";}}
 const currentPitchKey=p?pr.view!.code+":"+p.id:"";if(currentPitchKey!==pitchSnapshotKey){pitchSnapshotKey=currentPitchKey;pitchSnapshot=snapshot;}
 const shownGame=pr.seasonPresentationGame===null?null:pr.seasonPresentationGame??game;
 world.update(now/1000);world.setScoreboard(shownGame?{home:shownGame.homeTeam,away:shownGame.awayTeam,homeScore:shownGame.homeRuns,awayScore:shownGame.awayRuns,inning:shownGame.inning,half:shownGame.half,hidden:pr.hideScore}:{home:"PITCHER",away:"BATTER",homeScore:0,awayScore:pr.view?.score??0,inning:Math.min(6,(pr.view?.round??0)+1),half:"top",hidden:pr.hideScore});
 refreshAppearance(pr,battingSign,throwSign);
 if(side!==lastSide){lastSide=side;configureCamera();lighting.focus(side);pitcher.root.visible=true;batter.root.visible=true;catcher.root.visible=side==="pitcher"}
 const bodyScale=pitcherScale(pr.pitcherId),batterScale=pitcherBodyScale(playerProfile(pr.batterId,"batter")?.heightCm);pitcher.root.scale.set(throwSign*bodyScale,bodyScale,bodyScale);batterMirror.scale.set(battingSign*batterScale,batterScale,batterScale);
 const idle=Math.sin(now/750)*.008,relative=p?now-p.releaseAt:-2000,delivery=pitchingPose(relative,style);
 const restingBreath=clamp(Math.max(-relative-PITCH_WINDUP_MS,relative-PITCH_RECOVERY_MS)/200,0,1);
 posePitcher(pitcher,delivery,throwSign,style==="underhand",idle*restingBreath);heldBall.visible=!p||relative<0;
 const localSwing=p&&pr.localSwing&&pr.localSwing.code===pr.view?.code&&pr.localSwing.pitchId===p.id?pr.localSwing:null;
 const knownSwing=localSwing??p?.aiBatterSwing??(p?.reaction?.swingAt!=null&&p.reaction.swingAim?{at:p.reaction.swingAt,aim:p.reaction.swingAim}:null);
 const key=pr.view&&p?pr.view.code+":"+p.id+":"+(knownSwing?.at??"take"):"";
 if(key!==predictionKey){predictionKey=key;prediction=pr.view&&p?evaluatePitch(pr.view,knownSwing,now):undefined;}
 const reaction=p?.reaction??prediction;
 const reception=catcherPitchPlan(p,reaction),catcherPose=poseCatcher(catcher,{now,...(reception??{})});
 const playAt=reaction?.contact?.at??reaction?.bodyHit?.at??(p?p.releaseAt+p.flightMs:Infinity);
 const playKey=p&&reaction?pr.view!.code+":"+p.id+":"+playAt:"";
 if(reaction&&(reaction.contact||reaction.plateEnded)&&now>=playAt&&playKey!==lastPlayKey){
  lastPlayKey=playKey;const before=pr.seasonPresentationGame===null?null:pitchSnapshot??snapshot;
  const timeline=fieldPlayTimeline(reaction),fieldTarget=timeline?.fieldTarget??{x:0,y:0,z:0};
  play={key:playKey,pitchId:p!.id,at:playAt,reaction,before,batterId:pr.batterId,plans:before?runnerPlans(before,pr.batterId,reaction,snapshot??undefined):[],fieldTarget,fielder:timeline?.fielder??-1,timeline};
 }
 if(play&&reaction&&p?.id===play.pitchId&&play.reaction!==reaction){play.reaction=reaction;play.timeline=fieldPlayTimeline(reaction);if(play.timeline){play.fieldTarget=play.timeline.fieldTarget;play.fielder=play.timeline.fielder;}}
 if(play&&p?.id!==play.pitchId&&relative>=-PITCH_WINDUP_MS)play=null;
 if(play&&snapshot&&play.before&&snapshot.plateAppearances>play.before.plateAppearances)play.plans=runnerPlans(play.before,play.batterId,play.reaction,snapshot);
 const contactAt=knownSwing?.at??p?.reaction?.swingAt;
 const swingStart=side==="batter"?pr.swingTime:contactAt!=null?contactAt-SWING_CONTACT_MS:-Infinity;
 if(pr.swingTime!==lastSwingStart){lastSwingStart=pr.swingTime;frozenAim={...pr.aim.current}}
 const swingAge=now-swingStart,swingAim=side==="batter"?localSwing?.aim??frozenAim:knownSwing?.aim??p?.target??{x:0,y:0};
 const localAim={x:swingAim.x/batterScale,y:((1.05+swingAim.y*.55)/batterScale-1.05)/.55};
 // Sample the load at input time so a swing continues from the pose the player saw.
 const preparation=battingLoadForSwing(now,p,knownSwing?swingStart:null);
 const pose=swingPose(swingAge>=0&&swingAge<SWING_DURATION_MS?swingAge:-1,localAim,battingSign,now,preparation);
 poseBatter(batter,batterMirror,bat,pose);
 for(const [i,ghost] of batTrail.entries()){ghost.visible=swingAge>40&&swingAge<240;if(!ghost.visible)continue;const prev=swingPose(swingAge-(i+1)*9,localAim,battingSign,now,preparation);const a=V(...prev.grip).addScaledVector(V(...prev.axis),.25).multiplyScalar(batterScale),b=V(...prev.grip).addScaledVector(V(...prev.axis),.95).multiplyScalar(batterScale);between(ghost,a,b)}
 ball.visible=false;for(const t of trail)t.visible=false;
 if(p&&now>=p.releaseAt){const u=(now-p.releaseAt)/p.flightMs;
 if(reaction?.contact&&now>=reaction.contact.at){const pos=fieldBallPosition(reaction,now,play?.timeline??undefined);if(pos)ball.position.set(pos.x,pos.y,pos.z);ball.visible=!!pos;ball.scale.setScalar(1.2);if(now-reaction.contact.at<1200)pr.onContact(pr.view!.code+":"+p.id+":bat");}
 else if(reaction?.bodyHit&&now>=reaction.bodyHit.at){const hit=reaction.bodyHit,elapsed=(now-hit.at)/1000;ball.position.set(hit.position.x,Math.max(.065,hit.position.y-4.9*elapsed*elapsed),hit.position.z-.25*Math.min(elapsed,1));ball.visible=elapsed<1.8;ball.scale.setScalar(1);}
 else if(reception?.catchBall&&catcherPose.catchable&&now>=reception.receiveAt){const pos=receivedBallPosition(reception,now,catcherPose);if(pos)ball.position.set(pos.x,pos.y,pos.z);ball.visible=!!pos;ball.scale.setScalar(1);}
 else if(u<1.24){
  const incoming=(time:number)=>incomingBallPosition(p,reaction,time,side);
  const pos=incoming(now);ball.position.set(pos.x,pos.y,pos.z);ball.visible=true;ball.scale.setScalar(side==="batter"?1.3:1.05);for(let i=0;i<trail.length;i++){const old=incoming(now-(i+1)*9);trail[i].position.set(old.x,old.y,old.z);trail[i].visible=now-(i+1)*9>=p.releaseAt}}
 ball.rotation.x=now/50;ball.rotation.z=now/75;}
 const playAge=play?now-play.at:Infinity,fieldAction=!!play?.timeline&&now<play.timeline.completeAt+RESULT_DISPLAY_MS;
 if(fieldAction&&play){
  const visibleBall=fieldBallPosition(play.reaction,now,play.timeline),pos=visibleBall??(play.timeline?.throwTarget&&now>=play.timeline.throwArrivesAt!?play.timeline.throwTarget:play.fieldTarget),shot=ballTrackingCamera(pos),base=FIELD_CAMERAS[side],t=clamp((playAge-320)/850,0,1),blend=t*t*(3-2*t);
  if(visibleBall)ball.position.set(visibleBall.x,visibleBall.y,visibleBall.z);ball.visible=!!visibleBall;ball.scale.setScalar(1.2);
  camera.position.copy(V(...base.position)).lerp(V(...shot.position),blend);camera.lookAt(V(...base.target).lerp(V(...shot.target),blend));camera.fov=fieldFov(side,width/height)+(shot.fov-fieldFov(side,width/height))*blend;camera.updateProjectionMatrix();camera.updateMatrixWorld(true);
 }else if(tracking)configureCamera();
 tracking=fieldAction;zone.visible=!tracking;catcher.root.visible=side==="pitcher"||tracking;
 // Protect the actual play, including receiving and the final planted step.
 // These use the same absolute clock as the poses; result-reading time is idle.
 pitchBlinkWindow.start=p?p.releaseAt-PITCH_WINDUP_MS:Infinity;
 pitchBlinkWindow.end=p?Math.max(p.releaseAt+PITCH_RECOVERY_MS,p.releaseAt+p.flightMs*1.24,reception?(reception.cancelAt!=null?reception.cancelAt+300:reception.receiveAt+950):-Infinity):-Infinity;
 fieldBlinkWindow.start=play?.at??Infinity;fieldBlinkWindow.end=play?(play.timeline?.completeAt??play.at+5000)+350:-Infinity;
 if(swingStart!==previousBlinkSwing){if(!Number.isFinite(swingInterruptAt)||swingStart<swingInterruptAt||swingStart>=swingInterruptAt+35)swingInterruptAt=swingStart;previousBlinkSwing=swingStart;}
 swingBlinkWindow.start=side==='batter'?swingInterruptAt:swingStart;swingBlinkWindow.end=swingStart+SWING_DURATION_MS;
 fielders.forEach((model,i)=>{const start=DEFENSIVE_SPOTS[i],active=fieldAction?play:null;let goal=start;
  const receiverBase=active?.timeline?.receiveBase??1,preferredReceiver=receiverBase===3?3:receiverBase===2?1:0,receiver=active?.fielder===preferredReceiver?(preferredReceiver===1?2:1):preferredReceiver;
  if(active){if(i===active.fielder)goal={...active.fieldTarget,z:active.fieldTarget.z-.3};else if(active.timeline?.throwAt!=null&&i===receiver)goal={...BASES[receiverBase],z:BASES[receiverBase].z-.3};else if(i<4)goal=i===0?BASES[1]:i===3?BASES[3]:{...BASES[2],x:i===1?2:-2,z:BASES[2].z-1};else goal={x:start.x+clamp(active.fieldTarget.x-start.x,-12,12),y:0,z:start.z+clamp(active.fieldTarget.z-start.z,-8,8)};}
  const motion=fielderMotion(start,goal,active?Math.min(playAge,active.timeline!.completeAt-active.at):0),pos=motion.position,run=motion.speed>0,movement={now,travel:motion.travel,speed:motion.speed,nominalSpeed:motion.nominalSpeed,stoppedFor:motion.stoppedFor,samplePath:motion.samplePath};model.root.position.set(pos.x,0,pos.z);model.root.rotation.y=active?motion.heading:Math.atan2(-pos.x,-pos.z);poseRunning(model,movement);
  if(active?.timeline&&!run&&Math.hypot(pos.x-goal.x,pos.z-goal.z)<1e-5){if(i===active.fielder)poseFielding(model,active.timeline,now,false,{movement});else if(i===receiver&&active.timeline.throwAt!=null)poseFielding(model,active.timeline,now,true,{movement});}
  setPlayerDetail(model,model.root.position.distanceTo(camera.position),compact);
  blinkPlayer(model,now,'fielder:'+(model.root.userData.playerId??i),fieldBlinkWindows);
 });
 const runnerActive=!!play?.before&&now<(play.timeline?.completeAt??play.at+5000)+RESULT_DISPLAY_MS&&(p?.id===play.pitchId||relative<-PITCH_WINDUP_MS),plans=runnerActive?play!.plans:shownGame&&!pr.hideScore?shownGame.bases.flatMap((runner,i)=>runner?[{playerId:runner.playerId,from:i+1,to:i+1,out:false}]:[]):[];
 const batterRunDelay=SWING_DURATION_MS-SWING_CONTACT_MS;
 runners.forEach((model,i)=>{const plan=plans[i];if(!plan){model.root.visible=false;return;}const delay=plan.from===0?batterRunDelay:200,completeAt=play?.timeline?.completeAt??(play?play.at+5000:now),available=Math.max(1,completeAt-(play?.at??now)-delay-250),runScale=Math.max(1,(plan.to-plan.from)*27.432/7.4*1000/available),motion=runnerMotion(plan,runnerActive?Math.max(0,playAge-delay):0,runScale),pos=motion.position;model.root.visible=!(runnerActive&&plan.from===0&&playAge<=delay)&&!(pos.finished&&plan.to===4)&&!(plan.out&&now>=completeAt);if(!model.root.visible)return;
  if(runnerIds[i]!==plan.playerId){runnerIds[i]=plan.playerId;dressPlayer(model,selectedAppearance(pr,plan.playerId,"batter",game?game.half==="top"?game.awayTeam:game.homeTeam:""));model.root.scale.setScalar(pitcherBodyScale(playerProfile(plan.playerId,"batter")?.heightCm??pr.appearances?.[plan.playerId]?.heightCm));}
  model.root.position.set(pos.x,0,pos.z);model.root.rotation.y=motion.heading;poseRunning(model,{now,travel:motion.travel,speed:motion.speed,nominalSpeed:motion.nominalSpeed,stoppedFor:motion.stoppedFor,samplePath:motion.samplePath});setPlayerDetail(model,model.root.position.distanceTo(camera.position),compact);
  blinkPlayer(model,now,'runner:'+plan.playerId,fieldBlinkWindows);
 });
 const batterRunning=runnerActive&&play!.plans.some(plan=>plan.from===0)&&playAge>batterRunDelay;batter.root.visible=!batterRunning;bat.visible=!batterRunning;setPlayerDetail(batter,batter.root.getWorldPosition(V()).distanceTo(camera.position),compact);setPlayerDetail(pitcher,pitcher.root.position.distanceTo(camera.position),compact);setPlayerDetail(catcher,catcher.root.position.distanceTo(camera.position),compact);
 blinkPlayer(batter,now,'batter:'+pr.batterId,batterBlinkWindows,side==='batter'?swingInterruptAt:undefined);
 blinkPlayer(pitcher,now,'pitcher:'+pr.pitcherId,fieldBlinkWindows);
 blinkPlayer(catcher,now,'catcher:'+(catcher.root.userData.playerId??pr.pitcherId),fieldBlinkWindows);
 const aim=pr.aim.current,screen=V(aim.x*.5,1.05+aim.y*.55,0).project(camera);if(target.current){target.current.style.left=(screen.x*.5+.5)*width+"px";target.current.style.top=(-screen.y*.5+.5)*height+"px";target.current.style.setProperty("--reticle-size",side==="batter"?"54px":"32px")}
 if(target.current)target.current.hidden=tracking;
 const mark=lastPitchMarker(pr.view,now,reaction?.bodyHit??null);if(marker.current){marker.current.hidden=!mark||tracking;if(mark){const point=V(mark.position.x,mark.position.y,mark.position.z).project(camera);marker.current.style.left=(point.x*.5+.5)*width+"px";marker.current.style.top=(-point.y*.5+.5)*height+"px";marker.current.className="last-pitch-marker"+(mark.body?" body-hit-marker":"");marker.current.setAttribute("aria-label",mark.body?"직전 공 몸에 맞은 위치":"직전 공 통과 위치");marker.current.querySelector("span")!.textContent=mark.body?"몸에 맞음":"직전 공";}}
 renderer.render(scene,camera);frame=requestAnimationFrame(render);
 };frame=requestAnimationFrame(render);props.onReady(true);
 return()=>{disposed=true;stopDaylight();cancelAnimationFrame(frame);observer.disconnect();node.removeEventListener("pointermove",controls.move);node.removeEventListener("pointerdown",controls.down);node.removeEventListener("pointerup",controls.up);node.removeEventListener("pointercancel",controls.cancel);node.removeEventListener("lostpointercapture",controls.cancel);world.dispose();const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>(),textures=new Set<THREE.Texture>(),skeletons=new Set<THREE.Skeleton>();scene.traverse(o=>{const m=o as THREE.Mesh;if(m instanceof THREE.SkinnedMesh)skeletons.add(m.skeleton);if(m.geometry)geometries.add(m.geometry);if(m.material)for(const material of Array.isArray(m.material)?m.material:[m.material])materials.add(material)});for(const material of materials){for(const value of Object.values(material))if(value instanceof THREE.Texture)textures.add(value);material.dispose()}textures.forEach(texture=>texture.dispose());geometries.forEach(geometry=>geometry.dispose());skeletons.forEach(skeleton=>skeleton.dispose());lighting.dispose();renderer.dispose();renderer.domElement.remove()};
 },[]);
 return <><div className="three-surface" ref={mount}/><div ref={marker} className="last-pitch-marker" hidden><i/><span>직전 공</span></div><div ref={target} className={"aim-reticle "+(props.side==="pitcher"?"pitch-reticle":"")}><i/><b/><span/></div></>;
}
