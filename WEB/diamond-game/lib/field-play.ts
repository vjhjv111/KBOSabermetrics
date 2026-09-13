import type {SeasonGame} from "./season-types";
import type {PitchResult} from "./action-engine";
import type {Position3} from "./pitch-feedback";
export const BASE_CORNER=27.432/Math.SQRT2;
export const BASES:Position3[]=[{x:0,y:0,z:0},{x:BASE_CORNER,y:0,z:-BASE_CORNER},{x:0,y:0,z:-BASE_CORNER*2},{x:-BASE_CORNER,y:0,z:-BASE_CORNER},{x:0,y:0,z:0}];
export const DEFENSIVE_SPOTS:Position3[]=[{x:17.5,y:0,z:-23},{x:11,y:0,z:-32},{x:-10,y:0,z:-32},{x:-18,y:0,z:-22},{x:-43,y:0,z:-76},{x:0,y:0,z:-91},{x:43,y:0,z:-76}];
export const DEFENSIVE_SLOTS=["1B","2B","SS","3B","LF","CF","RF"] as const;
export type DefensivePosition="C"|typeof DEFENSIVE_SLOTS[number];
/** Explicit positions are reserved before fallback allocation. DH and inactive pitchers never take a field slot. */
export function defensiveAssignments(lineup:string[],pitcherId:string,profile:(id:string)=>{position?:string}|undefined){
 const slots=["C",...DEFENSIVE_SLOTS] as DefensivePosition[],assignments:Partial<Record<DefensivePosition,string>>={},used=new Set<string>();
 const candidates=[...new Set(lineup)].filter(id=>id!==pitcherId&&!["DH","P"].includes(profile(id)?.position?.trim().toUpperCase()??""));
 for(const id of candidates){const position=profile(id)?.position?.trim().toUpperCase() as DefensivePosition;if(slots.includes(position)&&!assignments[position]){assignments[position]=id;used.add(id);}}
 const available=candidates.filter(id=>!used.has(id));for(const slot of slots)if(!assignments[slot]){const id=available.shift();if(id)assignments[slot]=id;}
 return {pitcher:pitcherId,catcher:assignments.C,fielders:DEFENSIVE_SLOTS.map(slot=>assignments[slot])};
}
export type RunnerPlan={playerId:string;from:number;to:number;out:boolean};
export type FieldSnapshot=Pick<SeasonGame,"id"|"inning"|"half"|"plateAppearances"|"bases"|"homeRuns"|"awayRuns">;
export function fieldSnapshot(game:SeasonGame):FieldSnapshot{return {...game,bases:game.bases.map(r=>r?{...r}:null)}}
export function battingTeam(game:SeasonGame){return game.half==="top"?game.awayTeam:game.homeTeam}
export function fieldingTeam(game:SeasonGame){return game.half==="top"?game.homeTeam:game.awayTeam}
export function defenseLineup(game:SeasonGame){return game.half==="top"?game.homeLineup:game.awayLineup}
/** Use the authoritative post-play bases whenever they have arrived; prediction only fills the brief gap. */
export function runnerPlans(before:FieldSnapshot,batterId:string,result:PitchResult,after?:FieldSnapshot):RunnerPlan[]{
 const advance=result.outcome==="HR"?4:result.outcome==="3B"?3:result.outcome==="2B"?2:1;
 const endedHalf=after&&(after.inning!==before.inning||after.half!==before.half);
 const authoritative=!!after&&(after.plateAppearances>before.plateAppearances||endedHalf);
 const scoring=authoritative?Math.max(0,(after!.homeRuns+after!.awayRuns)-(before.homeRuns+before.awayRuns)):0;
 let scored=0;
 const plans:RunnerPlan[]=[];
 for(let i=2;i>=0;i--){const runner=before.bases[i];if(!runner)continue;const final=authoritative&&!endedHalf?after!.bases.findIndex(r=>r?.playerId===runner.playerId):-1;
  const forced=result.kind==="walk"||result.kind==="hbp";
  const predicted=forced?(before.bases.slice(0,i+1).every(Boolean)?i+2:i+1):result.kind==="hit"?Math.min(4,i+1+advance):i+1;
  const to=authoritative?(final>=0?final+1:scored<scoring?(scored++,4):i+1):predicted;
  plans.push({playerId:runner.playerId,from:i+1,to,out:!!endedHalf&&to<4});
 }
 if(result.kind==="hit"||(result.kind==="out"&&!!result.contact)||result.kind==="walk"||result.kind==="hbp"){
  const final=authoritative&&!endedHalf?after!.bases.findIndex(r=>r?.playerId===batterId):-1;
  const to=final>=0?final+1:result.outcome==="HR"?4:result.kind==="out"?1:advance;
  plans.push({playerId:batterId,from:0,to,out:result.kind==="out"});
 }
 return plans;
}
export function runnerPosition(plan:RunnerPlan,elapsedMs:number):Position3&{finished:boolean;heading:number}{
 const progress=Math.max(0,elapsedMs)/1000*7.4/27.432,base=Math.min(plan.to,plan.from+progress),index=Math.min(3,Math.floor(base)),fraction=base-index;
 const a=BASES[index],b=BASES[index+1];return {x:a.x+(b.x-a.x)*fraction,y:0,z:a.z+(b.z-a.z)*fraction,finished:base>=plan.to,heading:Math.atan2(b.x-a.x,b.z-a.z)};
}
export function nearestFielder(target:Position3){let index=0,best=Infinity;DEFENSIVE_SPOTS.forEach((p,i)=>{const distance=Math.hypot(p.x-target.x,p.z-target.z);if(distance<best){best=distance;index=i;}});return index;}
export function fielderPosition(start:Position3,target:Position3,elapsedMs:number){const distance=Math.hypot(target.x-start.x,target.z-start.z),amount=Math.min(1,Math.max(0,elapsedMs-180)/1000*7/(distance||1));return {x:start.x+(target.x-start.x)*amount,y:0,z:start.z+(target.z-start.z)*amount};}
