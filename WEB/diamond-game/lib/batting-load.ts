import {SWING_DURATION_MS} from './player-motion';

/** Prepare while watching the pitcher; taking a pitch lets the hitter settle back into the stance. */
export function battingLoad(now:number,pitch:{releaseAt:number;flightMs:number}|null|undefined){
 if(!pitch||!Number.isFinite(now))return 0;
 const ease=(value:number)=>{const t=Math.max(0,Math.min(1,value));return t*t*(3-2*t);};
 const coil=ease((now-(pitch.releaseAt-1050))/850);
 // Plant and settle before a taken ball reaches the body. This also preserves
 // the server's ready-pose body-contact envelope instead of leaving a raised foot.
 const settleEnd=pitch.releaseAt+pitch.flightMs-Math.max(45,pitch.flightMs*.035);
 const settle=ease((now-(settleEnd-180))/180);
 return coil*(1-settle);
}

/** A completed early swing must not reload against the same incoming pitch. */
export function battingLoadForSwing(now:number,pitch:{releaseAt:number;flightMs:number}|null|undefined,swingStart:number|null){
 if(swingStart!==null&&now>=swingStart)return now-swingStart>=SWING_DURATION_MS?0:battingLoad(swingStart,pitch);
 return battingLoad(now,pitch);
}
