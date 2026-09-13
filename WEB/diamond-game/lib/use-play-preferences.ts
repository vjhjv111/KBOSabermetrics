import {useCallback,useEffect,useRef,useState} from 'react';
import type {SeasonResponse} from './season-types';

export const PLAY_PREFERENCES_KEY='diamond.playPreferences.v1';
export type PlayPreferences={batting:boolean;pitching:boolean};
const defaults=():PlayPreferences=>({batting:true,pitching:true});
export function parsePlayPreferences(value:string|null):PlayPreferences{
 try{const parsed=JSON.parse(value??'null');return {batting:typeof parsed?.batting==='boolean'?parsed.batting:true,pitching:typeof parsed?.pitching==='boolean'?parsed.pitching:true};}catch{return defaults();}
}
function readPreferences(){try{return parsePlayPreferences(localStorage.getItem(PLAY_PREFERENCES_KEY));}catch{return defaults();}}
export function usePlayPreferences(){
 const [preferences,setPreferences]=useState(readPreferences),current=useRef(preferences);current.current=preferences;
 const setPreference=useCallback((role:keyof PlayPreferences,enabled:boolean)=>{
  const next={...current.current,[role]:enabled};current.current=next;setPreferences(next);
  try{localStorage.setItem(PLAY_PREFERENCES_KEY,JSON.stringify(next));}catch{}
 },[]);
 useEffect(()=>{
  const changed=(event:StorageEvent)=>{if(event.key!==null&&event.key!==PLAY_PREFERENCES_KEY)return;const next=readPreferences();current.current=next;setPreferences(next);};
  window.addEventListener('storage',changed);return()=>window.removeEventListener('storage',changed);
 },[]);
 return {preferences,setPreference};
}

export type AutoHalfBoundary={saveId:string;gameId:string;code:string;inning:number;half:string;role:string;pitchCount:number};
export function autoHalfBoundary(state:SeasonResponse):AutoHalfBoundary|null{
 const save=state.save,game=save?.game,action=state.action;
 return save&&game&&action?{saveId:save.id,gameId:game.id,code:action.code,inning:game.inning,half:game.half,role:action.role,pitchCount:action.pitchCount}:null;
}
/** Called again inside the write queue, before it adopts a newer state version. */
export function sameAutoHalfBoundary(expected:unknown,state:SeasonResponse|null){
 if(!expected||typeof expected!=='object'||!state?.save?.game||!state.action)return false;
 const actual=autoHalfBoundary(state)!,target=expected as AutoHalfBoundary;
 return state.action.mode==='ai'&&!state.action.waiting&&!state.action.done&&!state.save.game.complete&&(!state.action.pitch||state.action.pitch.resolved)
  &&Object.keys(actual).every(key=>actual[key as keyof AutoHalfBoundary]===target[key as keyof AutoHalfBoundary]);
}
export function autoHalfKey(state:SeasonResponse){const boundary=autoHalfBoundary(state);return boundary?`${boundary.saveId}:${boundary.gameId}:${boundary.code}:${boundary.inning}:${boundary.half}:${boundary.role}`:null;}

type HalfOptions={gameKey:string;boundaryKey:string|null;eligible:boolean;request:()=>Promise<unknown>};
export const AUTO_HALF_DELAY_MS=1200;
export function useAutoHalf(options:HalfOptions){
 const [enabled,setEnabled]=useState(true),[pending,setPending]=useState(false),[failed,setFailed]=useState(false);
 const latest=useRef(options),enabledRef=useRef(true),failedRef=useRef(false),alive=useRef(true),claimed=useRef<string|null>(null),inFlight=useRef<object|null>(null);
 latest.current=options;
 useEffect(()=>{alive.current=true;return()=>{alive.current=false;};},[]);
 useEffect(()=>{claimed.current=null;enabledRef.current=true;failedRef.current=false;setEnabled(true);setFailed(false);},[options.gameKey]);
 const toggle=useCallback(()=>{
  const next=!enabledRef.current;enabledRef.current=next;setEnabled(next);
  if(next&&failedRef.current){claimed.current=null;failedRef.current=false;setFailed(false);}
 },[]);
 useEffect(()=>{
  if(!enabled||!options.eligible||!options.boundaryKey||inFlight.current||claimed.current===options.boundaryKey)return;
  const key=options.boundaryKey,gameKey=options.gameKey;
  const timer=setTimeout(()=>{
   const current=latest.current;
   if(!alive.current||!enabledRef.current||!current.eligible||current.gameKey!==gameKey||current.boundaryKey!==key||inFlight.current||claimed.current===key)return;
   if(document.hidden||document.querySelector?.('dialog[open]'))return;
   const flight={};inFlight.current=flight;claimed.current=key;setPending(true);
   void (async()=>{
    try{await current.request();}
    catch{if(alive.current&&latest.current.gameKey===gameKey){enabledRef.current=false;failedRef.current=true;setEnabled(false);setFailed(true);}}
    finally{if(inFlight.current===flight){inFlight.current=null;if(alive.current)setPending(false);}}
   })();
  },AUTO_HALF_DELAY_MS);
  return()=>clearTimeout(timer);
 },[enabled,pending,options.gameKey,options.boundaryKey,options.eligible]);
 return {enabled,pending,failed,toggle};
}
