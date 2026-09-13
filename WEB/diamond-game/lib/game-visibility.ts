import {useEffect,useState} from 'react';

/** The record-room shell hides its iframe without changing document.hidden. */
export function observeGameVisibility(update:(active:boolean)=>void){
 let parentActive=true;
 const refresh=()=>update(parentActive&&!document.hidden);
 const receive=(event:MessageEvent)=>{
  if(event.origin!==location.origin||event.source!==window.parent||event.data?.type!=='saber:visibility')return;
  parentActive=event.data.active===true;refresh();
 };
 window.addEventListener('message',receive);document.addEventListener('visibilitychange',refresh);refresh();
 if(window.parent!==window)window.parent.postMessage({type:'diamond:ready'},location.origin);
 return()=>{window.removeEventListener('message',receive);document.removeEventListener('visibilitychange',refresh);};
}

export function useGameVisibility(){
 const [active,setActive]=useState(!document.hidden);
 useEffect(()=>observeGameVisibility(setActive),[]);
 return active;
}
