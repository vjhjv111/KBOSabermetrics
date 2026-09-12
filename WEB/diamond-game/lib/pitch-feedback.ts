export type Position3 = {x:number;y:number;z:number};
export type BodyHit = {at:number;position:Position3};
export type BodyCapsule = {a:Position3;b:Position3;radius:number};
const dot=(a:Position3,b:Position3)=>a.x*b.x+a.y*b.y+a.z*b.z;
const sub=(a:Position3,b:Position3):Position3=>({x:a.x-b.x,y:a.y-b.y,z:a.z-b.z});
const mix=(a:Position3,b:Position3,t:number):Position3=>({x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t,z:a.z+(b.z-a.z)*t});

// Distance from a point to a capsule's centre line, with the ball radius included.
export function touchesBody(point:Position3,capsules:BodyCapsule[]){
 return capsules.some(({a,b,radius})=>{const ab=sub(b,a),ap=sub(point,a),t=Math.max(0,Math.min(1,dot(ap,ab)/(dot(ab,ab)||1))),d=sub(point,mix(a,b,t));return dot(d,d)<=(radius+.065)**2});
}
const unitClamp=(v:number)=>Math.max(0,Math.min(1,v));
export function segmentTouchesBody(from:Position3,to:Position3,capsules:BodyCapsule[]){
 return capsules.some(({a,b,radius})=>{
  const d1=sub(to,from),d2=sub(b,a),r=sub(from,a),aa=dot(d1,d1),ee=dot(d2,d2),f=dot(d2,r);let s=0,t=0;
  if(aa<=1e-14){t=ee>1e-14?unitClamp(f/ee):0;}
  else{const c=dot(d1,r);if(ee<=1e-14)s=unitClamp(-c/aa);else{const bb=dot(d1,d2),denom=aa*ee-bb*bb;s=denom>1e-14?unitClamp((bb*f-c*ee)/denom):0;t=(bb*s+f)/ee;if(t<0){t=0;s=unitClamp(-c/aa)}else if(t>1){t=1;s=unitClamp((bb-c)/aa)}}}
  const d=sub(mix(from,to,s),mix(a,b,t));return dot(d,d)<=(radius+.065)**2;
 });
}

/** Sweep the last metre in <=1cm segments, then refine the first contact time.
 * This is independent of rendering FPS and cannot tunnel through a limb at 1x. */
export function sweepBody(positionAt:(at:number)=>Position3,arrival:number,flightMs:number,capsules:BodyCapsule[]):BodyHit|null{
 const start=arrival-flightMs*.065,end=arrival+flightMs*.065,steps=260;
 let previous=start;
 for(let i=0;i<=steps;i++){
  const at=start+(end-start)*i/steps;
  if(segmentTouchesBody(positionAt(previous),positionAt(at),capsules)){
   let lo=previous,hi=at;
   for(let j=0;j<14;j++){const mid=(lo+hi)/2;if(segmentTouchesBody(positionAt(previous),positionAt(mid),capsules))hi=mid;else lo=mid;}
   return {at:hi,position:positionAt(hi)};
  }
  previous=at;
 }
 return null;
}

/** The last pitch marker survives the windup, and clears when the next ball is released. */
export function lastPitchMarker(view:{pitch:{id:number;releaseAt:number;flightMs:number;target:{x:number;y:number};reaction?:Feedback}|null;history:Feedback[]}|null,now:number,currentBodyHit:BodyHit|null=null){
 const p=view?.pitch;if(!p)return null;
 const arrival=p.releaseAt+p.flightMs;
 const hit=p.reaction?.bodyHit??currentBodyHit;
 if(hit&&now>=hit.at)return {id:p.id,position:hit.position,body:true};
 if(now>=arrival)return {id:p.id,position:{x:p.target.x*.5,y:1.05+p.target.y*.55,z:0},body:false};
 if(now>=p.releaseAt)return null;
 const previous=[...view.history].reverse().find(r=>r.id<p.id);if(!previous?.plateLocation)return null;
 return {id:previous.id,position:previous.bodyHit?.position??{x:previous.plateLocation.x*.5,y:1.05+previous.plateLocation.y*.55,z:0},body:!!previous.bodyHit};
}
type Feedback={id:number;plateLocation?:{x:number;y:number};bodyHit?:BodyHit};
