import {useEffect,useRef,useState,type MutableRefObject} from 'react';
import {localNow} from './game-clock';
import {pitchResultRevealAt} from './pitch-cycle';
import type {SeasonResponse} from './season-types';

export type SeasonPresentation={state:SeasonResponse|null;pending:boolean;masked:boolean};
export function seasonPitchKey(state:SeasonResponse|null){return state?.save?.game&&state.action?`${state.save.id}:${state.save.game.id}:${state.action.code}:${state.action.pitchCount}`:null;}
/** Display snapshots never replace the authoritative state used for mutations. */
export function selectSeasonPresentation(state:SeasonResponse|null,now:number,previous:SeasonResponse|null,immediatePitch?:string|null):SeasonPresentation{
 const pitch=state?.action?.pitch;
 const pending=!!pitch?.reaction&&now<pitchResultRevealAt(pitch)&&(!immediatePitch||immediatePitch!==seasonPitchKey(state));
 if(!pending)return {state,pending:false,masked:false};
 const sameGame=!!previous?.save?.game&&seasonPitchKey(previous)===seasonPitchKey(state);
 // A reconnect can first see a resolved ball still in the air. Do not invent
 // the score, runners or outs that existed before the authoritative result.
 return {state:sameGame?previous:state,pending:true,masked:!sameGame};
}
export function useSeasonPresentation(state:SeasonResponse|null,clock:MutableRefObject<number>,immediatePitch?:string|null,active=true):SeasonPresentation{
 const previous=useRef<SeasonResponse|null>(null),[,refresh]=useState(0);
 // Read the current synchronized clock; an initial unsynchronized timestamp
 // must not win over a later negative server offset and expose a result early.
 const presentation=selectSeasonPresentation(state,localNow()+clock.current,previous.current,immediatePitch);
 useEffect(()=>{if(!presentation.pending)previous.current=state;},[state,presentation.pending]);
 useEffect(()=>{
  if(!active||!presentation.pending)return;
  const tick=()=>{if(!document.hidden)refresh(value=>value+1);};
  const timer=setInterval(tick,40);document.addEventListener('visibilitychange',tick);
  return()=>{clearInterval(timer);document.removeEventListener('visibilitychange',tick);};
 },[presentation.pending,clock,active]);
 return presentation;
}
