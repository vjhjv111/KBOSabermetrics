import {ballPosition,type Pitch,type PitchResult} from './action-engine';
import type {Position3} from './pitch-feedback';

/** The catcher reaches forward from his stance behind home plate. */
export const CATCHER_RECEIVE_Z=.65;
export type CatcherPitchPlan={target:Position3;receiveAt:number;prepareAt:number;catchBall:boolean;cancelAt?:number};

/** Visual receiving only: never changes a pitch, adjudication or result clock. */
export function catcherPitchPlan(pitch:Pitch|null|undefined,result:PitchResult|undefined=pitch?.reaction):CatcherPitchPlan|null{
 if(!pitch||!Number.isFinite(pitch.releaseAt)||!Number.isFinite(pitch.flightMs)||pitch.flightMs<=0)return null;
 const releaseZ=pitch.releaseZ??-18.44;
 if(!Number.isFinite(releaseZ)||releaseZ>=-.5)return null;
 const receiveAt=pitch.releaseAt+pitch.flightMs*(1-CATCHER_RECEIVE_Z/releaseZ);
 const target=ballPosition(pitch,receiveAt);
 if(!Object.values(target).every(Number.isFinite)||Math.abs(target.z-CATCHER_RECEIVE_Z)>1e-7)return null;
 const reaction=result?.id===pitch.id?result:undefined;
 // A predicted late swing can contact after the nominal receiving time. Never
 // catch that ball first: the existing contact path belongs to the batter.
 const diverted=!!(reaction?.contact||reaction?.bodyHit);
 const eventAt=reaction?.contact?.at??reaction?.bodyHit?.at;
 return {target,receiveAt,prepareAt:pitch.releaseAt+Math.min(100,pitch.flightMs*.12),catchBall:!diverted,
  ...(diverted&&Number.isFinite(eventAt)?{cancelAt:eventAt}:{}),};
}

/** Keep a received ball with the moving pocket during the short absorption.
 * Reachability comes from the same static target pose at every frame rate.
 */
export function receivedBallPosition(plan:CatcherPitchPlan|null,now:number,pose:{pocket:Position3;catchable:boolean}):Position3|null{
 if(!plan?.catchBall||!pose.catchable||!Number.isFinite(now)||now<plan.receiveAt||now>=plan.receiveAt+650||![pose.pocket.x,pose.pocket.y,pose.pocket.z].every(Number.isFinite))return null;
 return {...pose.pocket};
}
