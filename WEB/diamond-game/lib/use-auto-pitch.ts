import {useCallback,useEffect,useRef,useState} from 'react';

type Options={matchKey:string|null;pitchCount:number;eligible:boolean;request:()=>Promise<unknown>};
// A resolved pitch may be returned by many polls. Send ready once per pitch key,
// keep that claim across busy/visibility changes, and require a user retry on error.
export function useAutoPitch(options:Options){
 const [enabled,setEnabled]=useState(true),[pending,setPending]=useState(false),[failed,setFailed]=useState(false);
 const current=useRef(options),enabledRef=useRef(true),failedRef=useRef(false),alive=useRef(true);
 const claimed=useRef<string|null>(null),inFlight=useRef<object|null>(null);
 current.current=options;
 useEffect(()=>{alive.current=true;return()=>{alive.current=false;};},[]);
 useEffect(()=>{claimed.current=null;enabledRef.current=true;failedRef.current=false;setEnabled(true);setFailed(false);},[options.matchKey]);
 const toggle=useCallback(()=>{
  const next=!enabledRef.current;enabledRef.current=next;setEnabled(next);
  if(next&&failedRef.current){claimed.current=null;failedRef.current=false;setFailed(false);}
 },[]);
 useEffect(()=>{
  if(!enabled||!options.matchKey||!options.eligible||inFlight.current)return;
  const key=options.matchKey+':'+options.pitchCount;
  if(claimed.current===key)return;
  // The server supplies the full windup; this small grace period lets a user pause.
  const timer=setTimeout(()=>{
   const latest=current.current;
   if(!alive.current||!enabledRef.current||!latest.eligible||latest.matchKey+':'+latest.pitchCount!==key||inFlight.current||claimed.current===key)return;
   // Recheck the DOM at dispatch as a dialog or hidden tab can precede React's effect.
   if(typeof document!=='undefined'&&(document.hidden||document.querySelector?.('dialog[open]')))return;
   const flight={},matchKey=latest.matchKey;inFlight.current=flight;claimed.current=key;setPending(true);
   void (async()=>{
    try{await latest.request();}
    catch{
     if(alive.current&&current.current.matchKey===matchKey){enabledRef.current=false;failedRef.current=true;setEnabled(false);setFailed(true);}
    }finally{
     if(inFlight.current===flight){inFlight.current=null;if(alive.current)setPending(false);}
    }
   })();
  },350);
  return()=>clearTimeout(timer);
 },[enabled,pending,options.matchKey,options.pitchCount,options.eligible]);
 return {enabled,pending,failed,toggle};
}
