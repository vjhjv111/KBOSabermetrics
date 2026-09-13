import type {PitchResult} from './action-engine';
import type {Position3} from './pitch-feedback';
import {battedBallPosition} from './batted-ball';
import {BASES,DEFENSIVE_SPOTS,nearestFielder} from './field-play';
import {SWING_DURATION_MS,SWING_CONTACT_MS} from './player-motion';

export type FieldPlayTimeline={kind:'catch'|'throw'|'retrieve'|'home-run'|'foul';fielder:number;fieldTarget:Position3;physicalFieldMs:number;fieldedAt:number;completeAt:number;throwAt?:number;throwArrivesAt?:number;throwTarget?:Position3;receiveBase?:number};
const finite=(n:number|undefined,fallback:number)=>Number.isFinite(n)?n!:fallback;
const distance=(a:Position3,b:Position3)=>Math.hypot(a.x-b.x,a.z-b.z);
const flightResult=(r:PitchResult)=>({...r,exitSpeed:Math.max(0,finite(r.exitSpeed,100)),launchAngle:finite(r.launchAngle,25),direction:finite(r.direction,0)});
const flightData=(r:PitchResult)=>{
 const speed=Math.max(0,finite(r.exitSpeed,100))/3.6*Math.sqrt(.63),angle=finite(r.launchAngle,25)*Math.PI/180,up=speed*Math.sin(angle),height=Math.max(.065,r.contact?.position.y??1.05);
 return {up,height,landing:(up+Math.sqrt(up*up+2*9.81*height))/9.81*1000};
};
/** One deterministic clock drives the ball, fielders, result cards and next pitch. */
export function fieldPlayTimeline(result:PitchResult):FieldPlayTimeline|null{
 if(!result.contact)return null;
 const at=result.contact.at,{up,height,landing}=flightData(result),out=result.kind==='out'||result.outcome==='OUT',airOut=out&&result.trajectory!=='ground';
 if(result.trajectory==='foul'||result.outcome==='FOUL')return {kind:'foul',fielder:-1,fieldTarget:result.contact.position,physicalFieldMs:2500,fieldedAt:at+2500,completeAt:at+2500};
 if(result.outcome==='HR'){
  const completeAt=at+Math.max(landing+500,SWING_DURATION_MS-SWING_CONTACT_MS+4*27.432/7.4*1000+250);
  return {kind:'home-run',fielder:-1,fieldTarget:result.contact.position,physicalFieldMs:landing,fieldedAt:at+landing,completeAt};
 }
 const catchHeight=Math.min(1.4,height+Math.max(0,up*up/(2*9.81))*.75),disc=up*up+2*9.81*(height-catchHeight);
 const physicalFieldMs=airOut?Math.max(120,(up+Math.sqrt(Math.max(0,disc)))/9.81*1000):Math.max(1900,landing+350);
 const fieldTarget=battedBallPosition(flightResult(result),at+physicalFieldMs)??{x:0,y:1.05,z:-30};
 if(airOut)fieldTarget.y=catchHeight;
 const fielder=nearestFielder(fieldTarget),runMs=180+distance(DEFENSIVE_SPOTS[fielder],fieldTarget)/7*1000;
 const fieldedAt=at+Math.max(physicalFieldMs,runMs+120);
 if(airOut)return {kind:'catch',fielder,fieldTarget,physicalFieldMs,fieldedAt,completeAt:fieldedAt+450};
 if(out){
  const throwAt=fieldedAt+350,throwArrivesAt=throwAt+Math.max(500,distance(fieldTarget,BASES[1])/26*1000);
  return {kind:'throw',fielder,fieldTarget,physicalFieldMs,fieldedAt,throwAt,throwArrivesAt,throwTarget:{...BASES[1],y:1.2},receiveBase:1,completeAt:throwArrivesAt+350};
 }
 const bases=result.outcome==='3B'?3:result.outcome==='2B'?2:1;
 // An uncaught hit is still live while the defense returns it to the infield.
 // Finish both the return throw and the hitter's run before exposing the result.
 const receiveBase=bases===3?3:2,throwTarget={...BASES[receiveBase],y:1.2},throwAt=fieldedAt+420;
 const throwArrivesAt=throwAt+Math.max(500,distance(fieldTarget,throwTarget)/26*1000);
 const runnerMs=SWING_DURATION_MS-SWING_CONTACT_MS+bases*27.432/7.4*1000+250;
 return {kind:'retrieve',fielder,fieldTarget,physicalFieldMs,fieldedAt,throwAt,throwArrivesAt,throwTarget,receiveBase,completeAt:Math.max(throwArrivesAt+350,at+runnerMs)};
}
/** Render the batted ball until its glove contact, then the putout throw if needed. */
export function fieldBallPosition(result:PitchResult,now:number,timeline=fieldPlayTimeline(result)):Position3|null{
 const contact=result.contact;if(!contact||now<contact.at||!timeline)return null;
 if(timeline.kind==='home-run'||timeline.kind==='foul')return now<timeline.fieldedAt?battedBallPosition(flightResult(result),now):null;
 if(now<timeline.fieldedAt){
  const progress=Math.max(0,Math.min(1,(now-contact.at)/(timeline.fieldedAt-contact.at)));
  return battedBallPosition(flightResult(result),contact.at+timeline.physicalFieldMs*progress);
 }
 if(timeline.throwAt!=null&&now>=timeline.throwAt&&now<timeline.throwArrivesAt!){
  const t=(now-timeline.throwAt)/(timeline.throwArrivesAt!-timeline.throwAt),a={...timeline.fieldTarget,y:1.25},b=timeline.throwTarget??{...BASES[1],y:1.2};
  return {x:a.x+(b.x-a.x)*t,y:a.y+(b.y-a.y)*t+Math.sin(Math.PI*t)*1.3,z:a.z+(b.z-a.z)*t};
 }
 return null;
}
