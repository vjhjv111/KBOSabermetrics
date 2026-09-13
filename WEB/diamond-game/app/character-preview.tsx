"use client";
import {useEffect,useRef,useState} from "react";
import * as THREE from "three";
import {PlayerAppearance,playerDimensions} from "../lib/player-appearance";
import {createPlayer,createBat,dressPlayer,dressBat} from "../lib/player-model";
import {DeliveryStyle,pitchingPose,swingPose,SWING_DURATION_MS,MOUND_HEIGHT} from "../lib/player-motion";
import {poseBatter,posePitcher} from "../lib/player-pose";
import {stadiumLighting} from "../lib/scene-lighting";
import "./character-preview.css";

export type CharacterPreviewProps={active?:boolean;appearance:PlayerAppearance;role:"batter"|"pitcher";bats:"L"|"R"|"S";throws:"L"|"R";delivery:DeliveryStyle};
type Form={hand:"L"|"R";style:DeliveryStyle};
const FORM_NAMES:Record<DeliveryStyle,string>={overhand:"오버핸드",sidearm:"사이드암",underhand:"언더핸드"};
const FORMS:Form[]=["R","L"].flatMap(hand=>(["overhand","sidearm","underhand"] as const).map(style=>({hand:hand as "L"|"R",style})));

/** A standalone avatar stage using exactly the same rig and poses as live play. */
export default function CharacterPreview(props:CharacterPreviewProps){
 const mount=useRef<HTMLDivElement>(null),live=useRef(props);live.current=props;
 const action=useRef({at:-Infinity,angle:.58,form:null as Form|null,switchHand:false});
 const [playing,setPlaying]=useState(false),[unavailable,setUnavailable]=useState(false),[sample,setSample]=useState<Form|null>(null);
 useEffect(()=>{action.current.form=null;action.current.at=-Infinity;action.current.switchHand=false;setSample(null);setPlaying(false);},[props.role,props.throws,props.delivery,props.bats]);
 const play=(form:Form|null=null)=>{action.current.form=form;action.current.at=performance.now();setSample(form);setPlaying(true);};
 useEffect(()=>{
  const node=mount.current;if(!node||props.active===false)return;let renderer:THREE.WebGLRenderer;
  try{renderer=new THREE.WebGLRenderer({antialias:true,alpha:false,powerPreference:"low-power"});}catch{setUnavailable(true);return;}
  setUnavailable(false);renderer.setPixelRatio(Math.min(window.devicePixelRatio,1.6));node.appendChild(renderer.domElement);
  const scene=new THREE.Scene();scene.background=new THREE.Color("#101d2b");
  const camera=new THREE.PerspectiveCamera(35,1,.03,200),lighting=stadiumLighting(renderer,scene,true);lighting.focus("batter");
  const actor=new THREE.Group(),mirror=new THREE.Group();scene.add(actor);actor.add(mirror);
  const model=createPlayer(props.appearance.jersey,props.role==="batter");mirror.add(model.root);const bat=createBat(actor);bat.visible=props.role==="batter";
  const floor=new THREE.Mesh(new THREE.CylinderGeometry(1.65,1.7,.08,64),new THREE.MeshStandardMaterial({color:"#334654",roughness:.92}));floor.position.y=-.075;floor.receiveShadow=true;scene.add(floor);
  const rim=new THREE.Mesh(new THREE.TorusGeometry(1.64,.009,5,72),new THREE.MeshBasicMaterial({color:"#6d9a9c"}));rim.rotation.x=-Math.PI/2;rim.position.y=-.03;scene.add(rim);
  const heldBall=new THREE.Mesh(new THREE.SphereGeometry(.0365,16,12),new THREE.MeshStandardMaterial({color:"#f1eee2",roughness:.65}));heldBall.position.set(0,-.34,0);model.re.add(heldBall);heldBall.visible=props.role==="pitcher";
  const resize=()=>{const w=Math.max(1,node.clientWidth),h=Math.max(1,node.clientHeight);renderer.setSize(w,h,false);camera.aspect=w/h;camera.fov=camera.aspect<.85?42:35;camera.updateProjectionMatrix();};
  const observer=new ResizeObserver(resize);observer.observe(node);resize();
  let disposed=false,frame=0,lastLook="",lastSign=0,drag:{id:number;x:number;y:number;last:number;active:boolean}|null=null;
  const down=(event:PointerEvent)=>{if(!event.isPrimary||event.button!==0)return;drag={id:event.pointerId,x:event.clientX,y:event.clientY,last:event.clientX,active:false};};
  const move=(event:PointerEvent)=>{if(!drag||drag.id!==event.pointerId)return;const dx=event.clientX-drag.x,dy=event.clientY-drag.y;
   if(!drag.active){if(Math.abs(dy)>10&&Math.abs(dy)>Math.abs(dx)){drag=null;return;}if(Math.abs(dx)<6)return;drag.active=true;node.setPointerCapture(event.pointerId);}
   event.preventDefault();action.current.angle-=(event.clientX-drag.last)*.009;drag.last=event.clientX;
  };
  const up=(event:PointerEvent)=>{if(drag?.id!==event.pointerId)return;drag=null;if(node.hasPointerCapture(event.pointerId))node.releasePointerCapture(event.pointerId);};
  node.addEventListener("pointerdown",down);node.addEventListener("pointermove",move,{passive:false});node.addEventListener("pointerup",up);node.addEventListener("pointercancel",up);node.addEventListener("lostpointercapture",up);
  const render=(now:number)=>{
   if(disposed)return;const p=live.current,settings=action.current,form=settings.form??{hand:p.throws,style:p.delivery},scale=playerDimensions(p.appearance).heightScale;
   const hand=(p.role==="pitcher"?form.hand:p.bats==="S"?(settings.switchHand?"L":"R"):p.bats)==="L"?-1:1;
   const look=JSON.stringify(p.appearance);if(look!==lastLook||hand!==lastSign){dressPlayer(model,p.appearance,hand);dressBat(bat,p.appearance);lastLook=look;lastSign=hand;}
   const age=now-settings.at,duration=p.role==="batter"?SWING_DURATION_MS+250:2250;
   if(p.role==="batter"){
    mirror.scale.set(hand*scale,scale,scale);model.root.scale.setScalar(1);actor.position.set(.92*hand*scale,0,0);
    const localAim={x:0,y:(1.05/scale-1.05)/.55};
    poseBatter(model,mirror,bat,swingPose(age<duration?age:-1,localAim,hand,now));
   }else{
    mirror.scale.set(1,1,1);actor.position.set(0,-MOUND_HEIGHT,0);model.root.scale.set(hand*scale,scale,scale);
    const relative=age<duration?age-1250:-1500;posePitcher(model,pitchingPose(relative,form.style),hand,false,age>=duration?Math.sin(now/850)*.006:0);heldBall.visible=relative<0;
   }
   if(settings.at!==-Infinity&&age>=duration){settings.at=-Infinity;setPlaying(false);}
   const angle=settings.angle;camera.position.set(Math.sin(angle)*5.3,1.72,Math.cos(angle)*5.3);camera.lookAt(0,1.12,0);
   renderer.render(scene,camera);frame=requestAnimationFrame(render);
  };frame=requestAnimationFrame(render);
  return()=>{
   disposed=true;cancelAnimationFrame(frame);observer.disconnect();node.removeEventListener("pointerdown",down);node.removeEventListener("pointermove",move);node.removeEventListener("pointerup",up);node.removeEventListener("pointercancel",up);node.removeEventListener("lostpointercapture",up);
   const geometries=new Set<THREE.BufferGeometry>(),materials=new Set<THREE.Material>(),textures=new Set<THREE.Texture>(),skeletons=new Set<THREE.Skeleton>();
   scene.traverse(o=>{if(!(o instanceof THREE.Mesh))return;geometries.add(o.geometry);if(o instanceof THREE.SkinnedMesh)skeletons.add(o.skeleton);for(const m of Array.isArray(o.material)?o.material:[o.material])materials.add(m);});
   materials.forEach(m=>{Object.values(m).forEach(v=>{if(v instanceof THREE.Texture)textures.add(v);});m.dispose();});textures.forEach(t=>t.dispose());geometries.forEach(g=>g.dispose());skeletons.forEach(s=>s.dispose());lighting.dispose();renderer.dispose();renderer.domElement.remove();
  };
 },[props.role,props.active]);
 const activeForm=sample??{hand:props.throws,style:props.delivery};
 return <section className="character-preview" aria-label="커스텀 선수 3D 미리보기">
  <div className="character-preview__heading"><span>PLAYER STUDIO</span><strong>{props.appearance.name||"나만의 선수"}</strong><small>{props.role==="pitcher"?`${activeForm.hand==="L"?"좌":"우"}투 · ${FORM_NAMES[activeForm.style]}`:`${props.bats==="S"?"스위치":props.bats==="L"?"좌":"우"}타`}</small></div>
  <div className="character-preview__canvas" ref={mount} aria-label="선수 모델. 좌우로 드래그해 회전합니다."/>
  {unavailable&&<p className="character-preview__fallback">이 브라우저에서는 3D 미리보기를 표시할 수 없습니다. 선수 설정은 그대로 저장됩니다.</p>}
  <div className="character-preview__tools"><button type="button" onClick={()=>{action.current.angle+=.45;}} aria-label="선수를 왼쪽으로 회전">↶</button><span>좌우로 드래그해 회전</span><button type="button" onClick={()=>{action.current.angle-=.45;}} aria-label="선수를 오른쪽으로 회전">↷</button></div>
  <button className="character-preview__play" type="button" disabled={unavailable} onClick={()=>play(sample)}>{playing?"처음부터 다시 재생":props.role==="batter"?"스윙 미리보기":"투구 미리보기"}<span aria-hidden="true">▶</span></button>
  {props.role==="pitcher"&&<div className="character-preview__forms"><span>다른 폼 둘러보기</span><div>{FORMS.map(form=><button type="button" key={form.hand+form.style} disabled={unavailable} aria-pressed={activeForm.hand===form.hand&&activeForm.style===form.style} onClick={()=>play(form)}>{form.hand==="L"?"좌":"우"} {FORM_NAMES[form.style]}</button>)}</div>{sample&&<button type="button" className="character-preview__reset" onClick={()=>play(null)}>설정한 투구 폼으로 돌아가기</button>}</div>}
  {props.role==="batter"&&props.bats==="S"&&<button className="character-preview__reset" type="button" onClick={()=>{action.current.switchHand=!action.current.switchHand;play(null);}}>반대 타석에서 스윙</button>}
 </section>;
}
