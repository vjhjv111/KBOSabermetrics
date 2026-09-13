import type {Pitch,PitchResult} from './action-engine';
import {fieldPlayTimeline} from './field-play-timeline';

export const RESULT_DISPLAY_MS=1800;
export function pitchResultRevealAt(pitch?:Pitch|null){
 if(!pitch)return 0;
 const result=pitch.reaction;
 if(result?.contact)return fieldPlayTimeline(result)!.completeAt;
 return result?.bodyHit?.at??pitch.releaseAt+pitch.flightMs;
}

// Keep the current matchup on screen until its ball and fielding animation ends.
export function pitchPresentationDuration(result?:PitchResult){
 if(!result?.contact)return 1200;
 return fieldPlayTimeline(result)!.completeAt-result.contact.at+RESULT_DISPLAY_MS;
}
export function pitchPresentationEnd(pitch?:Pitch|null){
 if(!pitch)return 0;
 return pitchResultRevealAt(pitch)+(pitch.reaction?.contact?RESULT_DISPLAY_MS:1200);
}

// A reconnect may settle an old pitch now. Its animation is over, but the
// server still needs its 900ms command cooldown after recording that result.
export function pitchCommandReadyAt(pitch?:Pitch|null){
 if(!pitch)return 0;
 return Math.max(pitchPresentationEnd(pitch),(pitch.reaction?.at??0)+900);
}
