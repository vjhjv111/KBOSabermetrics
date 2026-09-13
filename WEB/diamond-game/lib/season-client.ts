import {useState,useRef,useCallback,useEffect,useLayoutEffect} from 'react';
import {parseRoster,registerRoster,pinMatchRoster,type RosterResponse} from './roster';
import {localNow,createClockSync} from './game-clock';
import type {SeasonResponse} from './season-types';
import type {CareerResponse} from './career-types';

export interface SaveCodeInfo {code:string|null;hasData:boolean;updatedAt:number|null}

/** GET /season can advance a pitch and its version, so reads and writes share a queue. */
export function createSeasonRequestQueue(){
 let tail:Promise<unknown>=Promise.resolve();
 return {run<T>(operation:()=>Promise<T>):Promise<T>{const result=tail.then(operation,operation);tail=result.catch(()=>{});return result;}};
}
export function acceptSeasonSnapshot(previous:SeasonResponse|null,next:SeasonResponse){
 if(previous?.save&&!next.save)return false;
 return !previous?.save||!next.save||previous.save.id!==next.save.id||next.save.version>=previous.save.version;
}
export function acceptCareerSnapshot(previous:CareerResponse,next:CareerResponse){
 if(previous.player&&!next.player)return false;
 return !previous.player||!next.player||previous.player.id!==next.player.id||next.player.version>=previous.player.version;
}
export function sameSeasonInputTarget(body:Record<string,unknown>,before:SeasonResponse|null,after:SeasonResponse|null){
 if(!before?.save||before.save.id!==after?.save?.id||before.save.game?.id!==after.save.game?.id||after.save.game?.complete)return false;
 const a=after.action;
 if(body.op==='swing')return a?.role==='batter'&&!a.done&&!!a.pitch&&a.pitch.id===body.pitchId&&!a.pitch.resolved;
 if(body.op==='ready'||body.op==='pitch')return !!a&&!a.done&&a.pitchCount===body.previousPitch&&(!a.pitch||a.pitch.resolved)&&a.role===(body.op==='ready'?'batter':'pitcher');
 return false;
}
export function completedGameKey(s:SeasonResponse|null){return s?.save?.game?.complete?s.save.id+':'+s.save.game.id:'';}

export function useSeasonClient(active:boolean){
 const [state,setState]=useState<SeasonResponse|null>(null),[career,setCareer]=useState<CareerResponse>({player:null});
 const [roster,setRoster]=useState<RosterResponse|null>(null),[busy,setBusy]=useState(false),[loading,setLoading]=useState(true),[error,setError]=useState('');
 const current=useRef(state),careerCurrent=useRef(career),csrf=useRef(''),pending=useRef(0),clock=useRef(0),sync=useRef(createClockSync()),alive=useRef(true);
 const queue=useRef(createSeasonRequestQueue()),rosterKey=useRef(''),rosterRequest=useRef(0),careerRefresh=useRef<Promise<CareerResponse>|null>(null);
 const seasonTag=useRef<{value:string;saveId:string|null}|null>(null);
 const activeRef=useRef(active);activeRef.current=active;
 const request=useCallback(async<T,>(path:string,body?:unknown):Promise<T>=>{
  if(body&&!csrf.current){const res=await fetch('/api/session',{credentials:'same-origin',cache:'no-store'});if(!res.ok)throw new Error('연결을 준비하지 못했습니다.');csrf.current=(await res.json()).csrfToken;}
  const start=localNow(),controller=new AbortController(),timeout=setTimeout(()=>controller.abort(),12000);
  try{
   const conditional=path==='season'&&!body&&current.current&&seasonTag.current?.saveId===(current.current.save?.id??null)?seasonTag.current.value:'';
   const res=await fetch('/api/diamond/'+path,{method:body?'POST':'GET',credentials:'same-origin',cache:'no-store',signal:controller.signal,headers:body?{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.current}:conditional?{'If-None-Match':conditional}:undefined,body:body?JSON.stringify(body):undefined});
   if(path==='season'&&!body&&res.status===304&&current.current)return current.current as T;
   const data=await res.json();if(!res.ok){if(data.code==='CSRF')csrf.current='';throw Object.assign(new Error(data.error??data.message??'연결을 확인해 주세요.'),{status:res.status,code:data.code});}
   if(path==='season'&&!body){const tag=res.headers.get('ETag');seasonTag.current=tag?{value:tag,saveId:data.save?.id??null}:null;}
   if(path==='season'&&Number.isFinite(data.serverReceivedAt)&&Number.isFinite(data.serverSentAt)){
    const offset=sync.current.sample({clientStart:start,clientEnd:localNow(),serverReceived:data.serverReceivedAt,serverSent:data.serverSentAt});
    if(offset!==null&&(!current.current?.action?.pitch||current.current.action.pitch.resolved))clock.current=offset;
   }return data;
  }finally{clearTimeout(timeout);}
 },[]);
 // A lost response is retried with the IDENTICAL requestId, version and inputAt.
 const post=useCallback(async<T,>(path:string,payload:Record<string,unknown>):Promise<T>=>{
  try{return await request<T>(path,payload);}catch(e){const problem=e as {status?:number;code?:string};if(problem.status&&problem.code!=='CSRF')throw e;return request<T>(path,payload);}
 },[request]);
 const applyCareer=useCallback((next:CareerResponse)=>{if(alive.current&&acceptCareerSnapshot(careerCurrent.current,next)){careerCurrent.current=next;setCareer(next);}},[]);
 const refreshCareer=useCallback(()=>{
  if(careerRefresh.current)return careerRefresh.current;
  const job=queue.current.run(async()=>{const c=await request<CareerResponse>('career');applyCareer(c);return c;});careerRefresh.current=job;
  void job.finally(()=>{if(careerRefresh.current===job)careerRefresh.current=null;}).catch(()=>{});return job;
 },[request,applyCareer]);
 const apply=useCallback((s:SeasonResponse)=>{
  if(!alive.current||!acceptSeasonSnapshot(current.current,s))return;
  if(seasonTag.current?.saveId!==(s.save?.id??null))seasonTag.current=null;
  if(activeRef.current&&s.save){const key=s.save.id+':'+s.save.game?.id+':'+s.save.revision;
   if(key!==rosterKey.current){registerRoster({season:s.save.season,seasons:[s.save.season],asOf:s.save.asOf,revision:s.save.revision,teams:s.save.teams.map(t=>({code:t.code,name:t.name})),batters:s.save.teams.flatMap(t=>t.batters),pitchers:s.save.teams.flatMap(t=>t.pitchers)});rosterKey.current=key;}
  }
  if(activeRef.current)pinMatchRoster(s.action?.roster??null);
  const completed=completedGameKey(s);const needsReward=!!completed&&completed!==completedGameKey(current.current);
  current.current=s;setState(s);if(needsReward)void refreshCareer().catch(()=>{});
 },[refreshCareer]);
 const reload=useCallback(()=>queue.current.run(async()=>{const next=await request<SeasonResponse>('season');apply(next);return next;}),[request,apply]);
 const loadRoster=useCallback(async(year?:number)=>{const requestId=++rosterRequest.current;const data=parseRoster(await request('roster'+(year?'?season='+year:'')));if(alive.current&&requestId===rosterRequest.current){setRoster(data);if(activeRef.current&&!current.current?.save)registerRoster(data);}return data;},[request]);
 // Exhibition and practice share the engine's roster registry. Restore this
 // save's profiles when returning to the season, even if its version is unchanged.
 useLayoutEffect(()=>{if(active&&current.current){rosterKey.current='';apply(current.current);}},[active,apply]);
 useEffect(()=>{alive.current=true;let cancelled=false;setLoading(true);
  void(async()=>{try{await refreshCareer();if(cancelled)return;await Promise.all([loadRoster(),reload()]);}
   catch(e){if(!cancelled)setError(e instanceof Error?e.message:'불러오지 못했습니다.');}finally{if(!cancelled)setLoading(false);}})();
  return()=>{cancelled=true;alive.current=false;};
 },[refreshCareer,loadRoster,reload]);
 const command=useCallback((body:Record<string,unknown>)=>{
  pending.current++;setBusy(true);setError('');
  // Copy nested aim at capture time; later pointer motion cannot alter queued input.
  const captured:Record<string,unknown>={...body,...(body.aim&&typeof body.aim==='object'?{aim:{...body.aim}}:{})};
  return queue.current.run(async()=>{
   const before=current.current;let payload={...captured,requestId:crypto.randomUUID(),...(captured.op==='create'?{}:{version:before?.save?.version})};
   try{
    let next:SeasonResponse;
    try{next=await post<SeasonResponse>('season',payload);}catch(e){
     if((e as {status?:number}).status!==409)throw e;
     const recovered=await request<SeasonResponse>('season');apply(recovered);
     if(!sameSeasonInputTarget(captured,before,current.current))throw e;
     payload={...payload,version:current.current?.save?.version,requestId:crypto.randomUUID()};next=await post<SeasonResponse>('season',payload);
    }apply(next);return next;
   }catch(e){if(alive.current)setError(e instanceof Error?e.message:'입력을 처리하지 못했습니다.');try{apply(await request<SeasonResponse>('season'));}catch{}throw e;}
   finally{pending.current--;if(alive.current)setBusy(pending.current>0);}
  });
 },[apply,post,request]);
 const editCareer=useCallback((body:Record<string,unknown>)=>{
  pending.current++;setBusy(true);setError('');const captured:Record<string,unknown>={...body,...(body.appearance&&typeof body.appearance==='object'?{appearance:{...body.appearance}}:{})};
  return queue.current.run(async()=>{
   try{const next=await post<CareerResponse>('career',{...captured,...(captured.op==='create'?{}:{version:careerCurrent.current.player?.version}),requestId:crypto.randomUUID()});applyCareer(next);return next;}
   catch(e){if(alive.current)setError(e instanceof Error?e.message:'선수를 저장하지 못했습니다.');try{applyCareer(await request<CareerResponse>('career'));}catch{}throw e;}
   finally{pending.current--;if(alive.current)setBusy(pending.current>0);}
  });
 },[post,applyCareer,request]);
 const getSaveCode=useCallback(()=>queue.current.run(()=>request<SaveCodeInfo>('save')),[request]);
 const saveCode=useCallback(()=>{
  pending.current++;setBusy(true);
  return queue.current.run(async()=>{
   try{return await post<SaveCodeInfo>('save',{op:'save'});}
   finally{pending.current--;if(alive.current)setBusy(pending.current>0);}
  });
 },[post]);
 const loadSaveCode=useCallback((code:string)=>{
  pending.current++;setBusy(true);
  return queue.current.run(async()=>{
   let loaded=false;
   try{
    await post<{loaded:true}>('save',{op:'load',code});loaded=true;
    // A restored owner may have lower versions or no player. A fresh page also
    // discards the old roster, ETags and in-flight gameplay views together.
    window.location.replace(window.location.pathname);
   }finally{
    pending.current--;
    if(alive.current&&!loaded)setBusy(pending.current>0);
   }
  });
 },[post]);
 useEffect(()=>{
  if(!active||!state?.save?.game||state.save.game.complete)return;let cancelled=false,inflight=false;
  const poll=async()=>{if(inflight||pending.current||document.hidden)return;inflight=true;try{await queue.current.run(async()=>{if(cancelled||pending.current||document.hidden)return;const data=await request<SeasonResponse>('season');if(!cancelled)apply(data);});}
   catch(e){if(!cancelled)setError(e instanceof Error?e.message:'경기 연결을 확인해 주세요.');}finally{inflight=false;}};
  const timer=setInterval(poll,300);const wake=()=>{if(!document.hidden)void poll();};document.addEventListener('visibilitychange',wake);window.addEventListener('online',wake);
  return()=>{cancelled=true;clearInterval(timer);document.removeEventListener('visibilitychange',wake);window.removeEventListener('online',wake);};
 },[active,state?.save?.game?.id,state?.save?.game?.complete,apply,request]);
 return {state,career,roster,busy,loading,error,setError,command,editCareer,loadRoster,reload,refreshCareer,getSaveCode,saveCode,loadSaveCode,clock};
}
