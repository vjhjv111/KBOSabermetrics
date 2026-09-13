import assert from 'node:assert/strict';
import {load} from './load-ts.mjs';

const engine=load('lib/action-engine.ts');

/** Captured from the C# season validation's persisted fielding25 result.
 * Keep its real zero distance/points and explicit null bodyHit: presentation
 * must depend on Contact, not on a positive score or an airborne distance.
 */
export function serverGroundSingle(at){
 return {id:1,label:'안타!',kind:'hit',outcome:'1B',timing:0,aimError:.45,
  quality:.5934625492772667,distance:0,exitSpeed:149.79082785808146,
  launchAngle:4.199999999999999,direction:0,points:0,plateEnded:true,
  at,swingAt:at,swingAim:{x:0,y:.45},plateLocation:{x:0,y:0},bodyHit:null,
  contact:{at,position:{x:0,y:1.05,z:0}},trajectory:'ground'};
}

// Evaluate actual practice inputs instead of constructing the result shape.
// The same evaluator supplies the scene's immediate local contact prediction.
export function practiceHitCases(at){
 const pitch={id:1,type:'fastball',velocity:145,releaseAt:at-600,flightMs:600,
  releaseX:-.33,releaseY:2.1,releaseZ:-18.44,target:{x:0,y:0},breakX:.02,breakY:.03,
  quality:1,resolved:false,bodyHit:null};
 const game={batter:'65357',pitcher:'55730',pitch,pace:'practice'};
 const found=new Map();
 for(let y=-60;y<=60;y+=5)for(let timing=-80;timing<=80;timing+=10){
  const swing={at:at+timing,aim:{x:0,y:y/100}},reaction=engine.evaluatePitch(game,swing,at-150);
  if(['1B','2B','HR'].includes(reaction.outcome)&&!found.has(reaction.outcome))found.set(reaction.outcome,{pitch:{...pitch},reaction,swing});
 }
 for(const outcome of ['1B','2B','HR'])assert(found.has(outcome),`Actual practice inputs must still exercise ${outcome}`);
 return [...found.values()];
}

export function hitPresentationCases(at){
 const generated=practiceHitCases(at),byOutcome=new Map(generated.map(c=>[c.reaction.outcome,c.reaction]));
 const single=serverGroundSingle(at);
 // The current C# fair-ball rule generates 1B/2B/HR. 3B is a supported DTO and
 // rendering boundary, deliberately identified as synthetic rather than a
 // server-produced result.
 const triple={...structuredClone(byOutcome.get('2B')),outcome:'3B',label:'3루타!',points:0};
 return [
  {origin:'captured C# season ground single',reaction:single},
  {origin:'current practice evaluator double',reaction:byOutcome.get('2B')},
  {origin:'synthetic supported triple contract',reaction:triple},
  {origin:'current practice evaluator home run',reaction:byOutcome.get('HR')},
 ];
}
