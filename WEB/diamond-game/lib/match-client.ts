import {useCallback,useEffect,useRef,useState} from 'react';
import {createSeasonRequestQueue,acceptSeasonSnapshot,sameSeasonInputTarget} from './season-client';
import {registerRoster,pinMatchRoster} from './roster';
import {createClockSync,localNow} from './game-clock';
import type {FullMatchResponse} from './season-types';

const remember=(code:string)=>{try{if(code)sessionStorage.setItem('diamond.fullMatch',code);else sessionStorage.removeItem('diamond.fullMatch');}catch{}};
export function rememberedMatch(){try{return new URLSearchParams(location.search).get('room')??sessionStorage.getItem('diamond.fullMatch')??'';}catch{return '';}}
export function useMatchClient(active=true){
 const [state,setState]=useState<FullMatchResponse|null>(null),[busy,setBusy]=useState(false),[error,setError]=useState(''),[restoring,setRestoring]=useState(!!rememberedMatch());
 const current=useRef(state),queue=useRef(createSeasonRequestQueue()),pending=useRef(0),clock=useRef(0),sync=useRef(createClockSync()),csrf=useRef(''),alive=useRef(true),generation=useRef(0),registered=useRef(''),etag=useRef<{code:string;value:string}|null>(null);
 const apply=useCallback((next:FullMatchResponse,gen:number)=>{
  if(!alive.current||gen!==generation.current||!acceptSeasonSnapshot(current.current,next))return;
  const s=next.save!;
  // Joining can add the guest's custom player without changing the save id.
  const rosterKey=JSON.stringify([s.id,s.revision,s.teams.map(t=>[t.code,t.batters.map(p=>p.id),t.pitchers.map(p=>p.id)])]);
  if(registered.current!==rosterKey){registerRoster({season:s.season,seasons:[s.season],asOf:s.asOf,revision:s.revision,teams:s.teams.map(t=>({code:t.code,name:t.name})),batters:s.teams.flatMap(t=>t.batters),pitchers:s.teams.flatMap(t=>t.pitchers)});registered.current=rosterKey;}
  pinMatchRoster(next.action?.roster??null);current.current=next;setState(next);remember(next.match.code);
  history.replaceState(null,'','?room='+next.match.code);
 },[]);
 const request=useCallback(async(body?:Record<string,unknown>,code?:string):Promise<FullMatchResponse>=>{
  if(body&&!csrf.current){const response=await fetch('/api/session',{credentials:'same-origin',cache:'no-store'});if(!response.ok)throw new Error('경기 연결을 준비하지 못했습니다.');csrf.current=(await response.json()).csrfToken;}
  const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),12000),started=localNow();
  try{
   const response=await fetch('/api/diamond/match'+(body?'':'?code='+encodeURIComponent(code??'')),{method:body?'POST':'GET',credentials:'same-origin',cache:'no-store',signal:controller.signal,headers:body?{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.current}:etag.current&&etag.current.code===code?{'If-None-Match':etag.current.value}:undefined,body:body?JSON.stringify(body):undefined});
   const cached=current.current;if(response.status===304&&cached&&cached.match.code===code)return cached;
   const data=await response.json();if(!response.ok){if(data.code==='CSRF')csrf.current='';throw Object.assign(new Error(data.error??data.message??'친선 경기 연결을 확인해 주세요.'),{status:response.status,code:data.code});}
   if(Number.isFinite(data.serverReceivedAt)&&Number.isFinite(data.serverSentAt)){const offset=sync.current.sample({clientStart:started,clientEnd:localNow(),serverReceived:data.serverReceivedAt,serverSent:data.serverSentAt});if(offset!==null&&(!current.current?.action?.pitch||current.current.action.pitch.resolved))clock.current=offset;}
   const tag=response.headers.get('ETag');if(tag)etag.current={code:data.match.code,value:tag};else if(body)etag.current=null;
   return data as FullMatchResponse;
  }finally{clearTimeout(timer);}
 },[]);
 const post=useCallback(async(body:Record<string,unknown>)=>{try{return await request(body);}catch(e){const p=e as {status?:number;code?:string};if(p.status&&p.code!=='CSRF')throw e;return request(body);}},[request]);
 const command=useCallback((body:Record<string,unknown>)=>{
  const captured:Record<string,unknown>={...body,...(body.aim&&typeof body.aim==='object'?{aim:{...body.aim}}:{})};const gen=generation.current;pending.current++;setBusy(true);setError('');
  return queue.current.run(async()=>{
   try{
    const before=current.current;const base:Record<string,unknown>={...captured,...(captured.op==='create'||captured.op==='join'?{}:{code:before?.match.code,version:before?.match.version}),requestId:crypto.randomUUID()};
    let next:FullMatchResponse;
    try{next=await post(base);}catch(e){if((e as {status?:number}).status!==409||!before)throw e;const updated=await request(undefined,before.match.code);apply(updated,gen);if(!sameSeasonInputTarget(captured,before,updated))throw e;next=await post({...base,version:updated.match.version,requestId:crypto.randomUUID()});}
    apply(next,gen);return next;
   }catch(e){if(alive.current&&gen===generation.current)setError(e instanceof Error?e.message:'친선 경기 입력을 처리하지 못했습니다.');throw e;}
   finally{pending.current--;if(alive.current)setBusy(pending.current>0);}
  });
 },[post,request,apply]);
 useEffect(()=>{
  alive.current=true;const code=rememberedMatch(),gen=generation.current;
  if(code)void queue.current.run(async()=>{try{apply(await request(undefined,code),gen);}catch(e){if((e as {status?:number}).status!==403&&alive.current)setError(e instanceof Error?e.message:'경기를 복구하지 못했습니다.');}finally{if(alive.current)setRestoring(false);}});
  else setRestoring(false);
  return()=>{alive.current=false;};
 },[apply,request]);
 useEffect(()=>{
  if(!active||!state||state.save?.game?.complete)return;let cancelled=false,reading=false;const gen=generation.current;
  const poll=async()=>{if(reading||pending.current||document.hidden)return;reading=true;try{await queue.current.run(async()=>{if(cancelled||pending.current)return;const next=await request(undefined,state.match.code);if(!cancelled)apply(next,gen);});}catch(e){if(!cancelled)setError(e instanceof Error?e.message:'친선 경기에 다시 연결하고 있습니다.');}finally{reading=false;}};
  const timer=setInterval(poll,state.match.waiting?1000:250);const wake=()=>{if(!document.hidden)void poll();};document.addEventListener('visibilitychange',wake);window.addEventListener('online',wake);
  return()=>{cancelled=true;clearInterval(timer);document.removeEventListener('visibilitychange',wake);window.removeEventListener('online',wake);};
 },[active,state?.match.code,state?.match.waiting,state?.save?.game?.complete,apply,request]);
 const reset=useCallback(()=>{if(pending.current)return;generation.current++;current.current=null;setState(null);setError('');registered.current='';etag.current=null;remember('');history.replaceState(null,'',location.pathname);pinMatchRoster(null);},[]);
 return {state,busy,error,setError,restoring,command,clock,reset};
}
