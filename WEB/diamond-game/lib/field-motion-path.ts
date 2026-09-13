import {BASES,fielderPosition,runnerPosition,type RunnerPlan} from './field-play';
import type {Position3} from './pitch-feedback';

export type RunningPath=(travel:number)=>Position3;
const BASE_DISTANCE=27.432;
const finite=(value:number,fallback=0)=>Number.isFinite(value)?value:fallback;
const unit=(value:number)=>Math.max(0,Math.min(1,value));
const turn=(from:number,to:number,amount:number)=>{
 const t=unit(amount),blend=t*t*(3-2*t);
 return from+Math.atan2(Math.sin(to-from),Math.cos(to-from))*blend;
};

/** Pair the unchanged field path with the distance actually covered by the rig.
 * Grounded feet use the same path, including the 180 ms first-step delay. */
export function fielderMotion(start:Position3,target:Position3,elapsedMs:number){
 const elapsed=Math.max(0,finite(elapsedMs)),position=fielderPosition(start,target,elapsed);
 const dx=target.x-start.x,dz=target.z-start.z,total=Math.hypot(dx,dz),travel=Math.hypot(position.x-start.x,position.z-start.z);
 const arrival=180+total/7*1000,speed=elapsed>180&&elapsed<arrival?7:0,stoppedFor=total>0?Math.max(0,elapsed-arrival):0;
 const samplePath:RunningPath=distance=>total>1e-8?
  {x:start.x+dx/total*distance,y:0,z:start.z+dz/total*distance}:{x:start.x,y:0,z:start.z};
 // Turn the body during the final step instead of rotating it in one frame
 // when the unchanged path reaches its receiving spot.
 const heading=total>0?turn(Math.atan2(dx,dz),0,1-(total-travel)/1.1):0;
 return {position,travel,speed,nominalSpeed:7,stoppedFor,heading,samplePath};
}

/** Preserve runner timing and corner locations; only expose its motion to IK.
 * Extending the first/last straight lets a support foot straddle a path endpoint. */
export function runnerMotion(plan:RunnerPlan,elapsedMs:number,timeScale=1){
 const elapsed=Math.max(0,finite(elapsedMs)),scale=Math.max(0,finite(timeScale,1));
 const position=runnerPosition(plan,elapsed*scale),total=Math.max(0,plan.to-plan.from)*BASE_DISTANCE;
 const travel=Math.min(total,elapsed*scale/1000*7.4),speed=elapsed>0&&!position.finished?7.4*scale:0;
 const first=Math.max(0,Math.min(3,plan.from)),last=Math.max(first,Math.min(3,plan.to-1));
 const samplePath:RunningPath=distance=>{
  const progress=plan.from+distance/BASE_DISTANCE,index=Math.min(last,Math.max(first,Math.floor(progress))),fraction=progress-index;
  const a=BASES[index],b=BASES[index+1];
  return {x:a.x+(b.x-a.x)*fraction,y:0,z:a.z+(b.z-a.z)*fraction};
 };
 const before=samplePath(travel-.7),after=samplePath(travel+.7);
 let heading=Math.atan2(after.x-before.x,after.z-before.z);
 const arrival=scale>0?total/7.4/scale*1000:Infinity,stoppedFor=total>0?Math.max(0,elapsed-arrival):0;
 if(total===0)heading=Math.atan2(-position.x,-position.z);
 else if(position.finished)heading=turn(heading,Math.atan2(-position.x,-position.z),(stoppedFor-160)/240);
 return {position,travel,speed,nominalSpeed:7.4*scale,stoppedFor,heading,samplePath};
}
