import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import React from 'react';
import {createRequire} from 'node:module';

const originals=Object.fromEntries(['document','window','localStorage','sessionStorage','location','history','fetch','setTimeout','clearTimeout','setInterval','clearInterval'].map(key=>[key,globalThis[key]]));
let time=100000,serial=0,modal=false;
const timers=new Map(),intervals=new Map(),windowEvents=new Map(),documentEvents=new Map(),stored=new Map(),session=new Map();
const eventTarget=events=>({addEventListener(type,fn){if(!events.has(type))events.set(type,new Set());events.get(type).add(fn);},removeEventListener(type,fn){events.get(type)?.delete(fn);}});
const storage=map=>({getItem:key=>map.get(key)??null,setItem:(key,value)=>map.set(key,String(value)),removeItem:key=>map.delete(key),clear:()=>map.clear()});
globalThis.document={hidden:false,querySelector:()=>modal?{}:null,...eventTarget(documentEvents)};
globalThis.window={...eventTarget(windowEvents)};globalThis.localStorage=storage(stored);globalThis.sessionStorage=storage(session);
globalThis.window.parent=globalThis.window;
globalThis.location={search:'',pathname:'/diamond/',origin:'https://game.test'};globalThis.history={replaceState(){}};
globalThis.setTimeout=(fn,delay=0)=>{const id=++serial;timers.set(id,{fn,at:time+delay});return id;};globalThis.clearTimeout=id=>timers.delete(id);
globalThis.setInterval=fn=>{const id=++serial;intervals.set(id,fn);return id;};globalThis.clearInterval=id=>intervals.delete(id);
const settle=async()=>{for(let i=0;i<20;i++)await Promise.resolve();};
async function advance(ms){const until=time+ms;for(;;){const next=[...timers].filter(([,t])=>t.at<=until).sort((a,b)=>a[1].at-b[1].at)[0];if(!next)break;time=next[1].at;timers.delete(next[0]);next[1].fn();await settle();}time=until;for(const fn of [...intervals.values()])fn();await settle();}
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
function hooks(){
 const slots=[];let index=0,effects=[],render;
 const same=(a,b)=>a&&b&&a.length===b.length&&a.every((v,i)=>Object.is(v,b[i]));
 const api={...React,useState(initial){const i=index++;slots[i]??={value:typeof initial==='function'?initial():initial};return [slots[i].value,value=>{slots[i].value=typeof value==='function'?value(slots[i].value):value;}];},useRef(initial){const i=index++;return slots[i]??(slots[i]={current:initial});},useCallback(fn,deps){const i=index++;if(!slots[i]||!same(slots[i].deps,deps))slots[i]={value:fn,deps};return slots[i].value;},useMemo(fn,deps){return api.useCallback(fn,deps)();},useEffect(fn,deps){const i=index++;if(!slots[i]||!same(slots[i].deps,deps)){const old=slots[i];slots[i]={deps,cleanup:old?.cleanup};effects.push(()=>{old?.cleanup?.();slots[i].cleanup=fn();});}}};api.useLayoutEffect=api.useEffect;
 return {api,run(fn){render=fn;return this.flush();},flush(){index=0;effects=[];const result=render();for(const effect of effects)effect();return result;},dispose(){slots.forEach(s=>s.cleanup?.());}};
}
const ActionScene=()=>null;
function loader(hooks=React,overrides={}){
 const cache=new Map();
 const load=file=>{file=path.resolve(file);if(cache.has(file))return cache.get(file).exports;const mod={exports:{}};cache.set(file,mod);const native=createRequire(file);
  const require=id=>{if(id in overrides)return overrides[id];if(id==='react')return hooks;if(id==='./action-scene')return {__esModule:true,default:ActionScene};if(id==='../lib/game-clock'||id==='./game-clock')return {localNow:()=>time,createClockSync:()=>({sample:()=>null})};if(id.endsWith('.css'))return {};
   if(id.startsWith('.')){const base=path.resolve(path.dirname(file),id);for(const ext of ['.ts','.tsx','.json'])if(fs.existsSync(base+ext))return ext==='.json'?JSON.parse(fs.readFileSync(base+ext,'utf8')):load(base+ext);}
   return native(id);
  };
  const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
  new Function('require','module','exports',js)(require,mod,mod.exports);return mod.exports;
 };return load;
}
const plain=loader(),{PLAY_PREFERENCES_KEY,parsePlayPreferences,autoHalfBoundary,sameAutoHalfBoundary,AUTO_HALF_DELAY_MS}=plain('lib/use-play-preferences.ts'),{pitchPresentationEnd}=plain('lib/pitch-cycle.ts');
function state(role='batter',mode='ai'){
 const batter={id:'2025:pref-batter',name:'타자',team:'HH',pa:500,ab:450,h:130,hr:20,so:90,avg:.289,slg:.5,profile:{bats:'R',throws:'R'}};
 const pitcher={id:'2025:pref-pitcher',name:'투수',team:'LG',tbf:500,bb:35,so:100,outs:350,era:4,arsenal:[{type:'fastball',velocity:146,usage:100}],profile:{bats:'R',throws:'R'}};
 const game={id:'pref-game',homeTeam:'HH',awayTeam:'LG',inning:1,half:role==='batter'?'bottom':'top',homeLine:[0],awayLine:[0],homeLineup:Array(9).fill(batter.id),awayLineup:Array(9).fill(batter.id),homeOrder:0,awayOrder:0,homePitcher:pitcher.id,awayPitcher:pitcher.id,homeRuns:0,awayRuns:0,homeHits:0,awayHits:0,outs:0,bases:[null,null,null],playerStats:{},complete:false,events:[]};
 return {save:{id:'pref-season',version:1,team:'HH',season:2025,asOf:'fixture',revision:'fixture',game,teams:[{code:'HH',name:'한화',batters:[batter],pitchers:[pitcher]}]},action:{code:'pref-match',role,mode,pace:'full',waiting:false,done:false,pitchCount:0,balls:0,strikes:0,history:[],batter:batter.id,pitcher:pitcher.id,roster:{season:2025,asOf:'fixture',revision:'fixture',batter,pitcher},pitch:null}};
}
function nextHalf(value){const next=structuredClone(value),g=next.save.game;g.inning+=g.half==='bottom'?1:0;g.half=g.half==='top'?'bottom':'top';next.action.role=next.action.role==='batter'?'pitcher':'batter';next.action.pitch=null;next.action.pitchCount+=15;next.save.version++;if(next.match)next.match.version++;return next;}
function entries(tree){if(Array.isArray(tree))return tree.flatMap(entries);return tree?.props?[tree,...entries(tree.props.children)]:[];}
const find=(tree,predicate)=>entries(tree).find(predicate);
function sceneHarness({preferences,initial=state(),...overrides}={}){
 if(preferences===undefined)stored.delete(PLAY_PREFERENCES_KEY);else stored.set(PLAY_PREFERENCES_KEY,JSON.stringify(preferences));
 const h=hooks(),requests=[],props={state:initial,busy:false,suspended:false,clock:{current:0},appearances:{},command:body=>{const d=deferred();requests.push({...d,body});return d.promise;},...overrides};
 const SeasonMatch=loader(h.api)('app/season-match.tsx').default;let tree=h.run(()=>SeasonMatch(props));
 const render=()=>{tree=h.flush();return tree;},scene=()=>find(tree,n=>n.type===ActionScene);
 return {props,requests,scene,get tree(){return tree;},render,ready(){scene().props.onReady(true);render();},switch(role){const name=role==='batting'?'타격 직접 플레이':'투구 직접 플레이';find(tree,n=>n.props.role==='switch'&&n.props['aria-label']===name).props.onClick();render();},toggleAuto(){find(tree,n=>n.type==='button'&&n.props['aria-label']?.startsWith('자동 공수 진행')).props.onClick();render();},async resolve(index,next){props.state=next;requests[index].resolve(next);await settle();render();},dispose:()=>h.dispose()};
}
try{
 assert.deepEqual(parsePlayPreferences(null),{batting:true,pitching:true});assert.deepEqual(parsePlayPreferences('{bad'),{batting:true,pitching:true});assert.deepEqual(parsePlayPreferences('{"batting":false,"pitching":"false"}'),{batting:false,pitching:true});
 {
  stored.set(PLAY_PREFERENCES_KEY,'{"batting":false,"pitching":true}');const h=hooks(),usePreferences=loader(h.api)('lib/use-play-preferences.ts').usePlayPreferences;let value=h.run(()=>usePreferences());
  assert.deepEqual(value.preferences,{batting:false,pitching:true});value.setPreference('pitching',false);value=h.flush();assert.equal(stored.get(PLAY_PREFERENCES_KEY),'{"batting":false,"pitching":false}');
  stored.set(PLAY_PREFERENCES_KEY,'{"batting":true,"pitching":false}');for(const fn of windowEvents.get('storage')??[])fn({key:PLAY_PREFERENCES_KEY});value=h.flush();assert.deepEqual(value.preferences,{batting:true,pitching:false},'Other tabs can update the browser preference');h.dispose();
  const unavailable=globalThis.localStorage;globalThis.localStorage={getItem(){throw new Error('denied');},setItem(){throw new Error('denied');}};const denied=hooks(),useDenied=loader(denied.api)('lib/use-play-preferences.ts').usePlayPreferences;let prefs=denied.run(()=>useDenied());assert.deepEqual(prefs.preferences,{batting:true,pitching:true});prefs.setPreference('batting',false);prefs=denied.flush();assert.equal(prefs.preferences.batting,false,'Unavailable browser storage still permits an in-memory choice');denied.dispose();globalThis.localStorage=unavailable;
 }
 for(const role of ['batter','pitcher']){
  const h=sceneHarness({initial:state(role)});try{
   assert.equal(entries(h.tree).filter(n=>n.props.role==='switch'&&n.props['aria-checked']===true).length,2,'Both roles default to direct play');h.ready();
   if(role==='batter'){await advance(350);assert.equal(h.requests[0].body.op,'ready','Direct batting keeps automatic first pitches');}
   else{await advance(3000);assert.equal(h.requests.length,0,'Direct pitching remains manual');h.scene().props.onChargeStart();h.render();await advance(700);h.scene().props.onChargeEnd();await settle();assert.equal(h.requests[0].body.op,'pitch');}
  }finally{h.dispose();}
 }
 for(const friendly of [false,true]){
  const h=sceneHarness({friendly,preferences:{batting:false,pitching:true}});try{
   h.ready();await advance(350);assert.equal(h.requests.length,0,'A disabled batting role never starts a stray ready');await advance(AUTO_HALF_DELAY_MS-350);assert.equal(h.requests.length,1);assert.equal(h.requests[0].body.op,'sim-half');assert.deepEqual(h.requests[0].body._autoHalfBoundary,autoHalfBoundary(h.props.state));
   h.render();await advance(10000);assert.equal(h.requests.length,1,'Repeated renders do not repeat a pending half');
   await h.resolve(0,nextHalf(h.props.state));await advance(3000);assert.equal(h.requests.length,1,'The next enabled defensive role waits for manual pitching');
  }finally{h.dispose();}
 }
 for(const role of ['batter','pitcher']){
  const initial=state(role);initial.action.pitchCount=1;initial.action.pitch={id:1,type:'fastball',velocity:146,resolved:true,releaseAt:time-10000,flightMs:600,reaction:{id:1,kind:'ball',outcome:'BALL',label:'볼',at:time}};
  const h=sceneHarness({initial});try{
   h.ready();assert(pitchPresentationEnd(initial.action.pitch)<time,'A resumed old pitch has already finished its visual presentation');
   await advance(899);h.render();h.scene().props.onChargeStart();h.render();assert.equal(h.requests.length,0,'A newly settled old pitch keeps the server command cooldown');assert.equal(h.scene().props.charging,false);
   await advance(2);h.render();
   if(role==='batter'){await advance(349);assert.equal(h.requests.length,0);await advance(1);assert.equal(h.requests[0].body.op,'ready','After reconnect, automatic ready starts only after cooldown plus debounce');}
   else{h.scene().props.onChargeStart();h.render();assert.equal(h.scene().props.charging,true);h.scene().props.onChargeEnd();assert.equal(h.requests[0].body.op,'pitch','Manual pitching also respects the resumed-result cooldown');}
  }finally{h.dispose();}
 }
 {
  const h=sceneHarness();try{
   h.ready();await advance(350);h.switch('batting');await advance(AUTO_HALF_DELAY_MS);assert.equal(h.requests.length,1,'Switching OFF during a ready request does not enqueue a simultaneous half');
   const next=structuredClone(h.props.state);next.action.pitchCount=1;next.action.pitch={id:1,type:'fastball',velocity:146,resolved:false,releaseAt:time+2200,flightMs:600};await h.resolve(0,next);await advance(5000);h.render();assert.equal(h.requests.length,1,'An already thrown pitch remains live until the server resolves it');
   const resolved=structuredClone(next);resolved.action.pitch.resolved=true;resolved.action.pitch.reaction={id:1,kind:'ball',outcome:'BALL',label:'볼',at:time};h.props.state=resolved;h.render();await advance(AUTO_HALF_DELAY_MS);assert.equal(h.requests[1].body.op,'sim-half','The OFF preference applies at the next finished-pitch boundary');
  }finally{h.dispose();}
 }
 {
  const h=sceneHarness();try{h.ready();await advance(300);h.switch('batting');await advance(AUTO_HALF_DELAY_MS);assert.equal(h.requests.length,1);assert.equal(h.requests[0].body.op,'sim-half','Switching off cancels a ready timer before it fires');}finally{h.dispose();}
 }
 {
  const h=sceneHarness({preferences:{batting:false,pitching:false}});try{
   h.ready();await advance(AUTO_HALF_DELAY_MS);const next=nextHalf(h.props.state);next.action.pitch={id:next.action.pitchCount,type:'fastball',velocity:146,resolved:true,releaseAt:time-1200,flightMs:600,reaction:{id:1,kind:'hit',outcome:'HR',at:time,contact:{at:time,position:{x:0,y:1.05,z:0}},exitSpeed:150,launchAngle:30,direction:0}};
   await h.resolve(0,next);
   await advance(AUTO_HALF_DELAY_MS-1);assert.equal(h.requests.length,1,'Both OFF progresses at a readable half-by-half interval');await advance(1);assert.equal(h.requests.length,2,'Synthetic last-pitch animation does not block the next automatic half');
   h.toggleAuto();await h.resolve(1,nextHalf(h.props.state));await advance(5000);assert.equal(h.requests.length,2,'Pause waits for the submitted half and then stops');
   h.toggleAuto();await advance(AUTO_HALF_DELAY_MS);assert.equal(h.requests.length,3,'Resume advances one next half');
   const end=nextHalf(h.props.state);end.save.game.complete=true;end.action.done=true;await h.resolve(2,end);await advance(5000);assert.equal(h.requests.length,3,'Automatic play stops at the completed game');
  }finally{h.dispose();}
 }
 {
  const h=sceneHarness({preferences:{batting:false,pitching:true}});try{h.ready();await advance(AUTO_HALF_DELAY_MS);h.requests[0].reject(new Error('network'));await settle();h.render();assert(find(h.tree,n=>n.props['aria-label']==='자동 공수 진행 재개'));await advance(10000);assert.equal(h.requests.length,1,'A failed half pauses instead of retrying in a loop');h.toggleAuto();await advance(AUTO_HALF_DELAY_MS);assert.equal(h.requests.length,2,'Explicit resume retries the same half once');}finally{h.dispose();}
 }
 {
  const initial=state(),reaction={id:1,kind:'out',outcome:'OUT',trajectory:'ground',label:'땅볼 아웃',at:time,timing:0,exitSpeed:110,launchAngle:5,direction:0,contact:{at:time,position:{x:0,y:1.05,z:0}}};initial.action.pitchCount=1;initial.action.pitch={id:1,type:'fastball',velocity:146,resolved:true,releaseAt:time-600,flightMs:600,reaction};
  const h=sceneHarness({initial,preferences:{batting:false,pitching:false}});try{h.ready();const end=pitchPresentationEnd(initial.action.pitch);await advance(end-time-1);h.render();assert.equal(h.requests.length,0,'A disabled role waits through fielding and result display');await advance(2);h.render();await advance(AUTO_HALF_DELAY_MS);assert.equal(h.requests.length,1);}finally{h.dispose();}
 }
 {
  const h=sceneHarness({preferences:{batting:false,pitching:false}});try{h.ready();h.props.state=nextHalf(h.props.state);h.render();await advance(AUTO_HALF_DELAY_MS);assert.equal(h.requests.length,1);assert.equal(h.requests[0].body._autoHalfBoundary.role,'pitcher','A role change replaces an old timer with the current side');}finally{h.dispose();}
 }
 for(const blocker of ['busy','suspended','hidden','modal','waiting','complete']){
  const h=sceneHarness({preferences:{batting:false,pitching:false}});try{
   h.ready();if(blocker==='busy')h.props.busy=true;if(blocker==='suspended')h.props.suspended=true;if(blocker==='hidden')document.hidden=true;if(blocker==='modal')modal=true;if(blocker==='waiting')h.props.state.action.waiting=true;if(blocker==='complete')h.props.state.save.game.complete=true;h.render();await advance(AUTO_HALF_DELAY_MS+100);assert.equal(h.requests.length,0,`${blocker} blocks automatic half dispatch`);
  }finally{document.hidden=false;modal=false;h.dispose();}
 }
 {
  const h=sceneHarness({initial:state('pitcher','pvp'),preferences:{batting:false,pitching:false}});try{h.ready();assert(!entries(h.tree).some(n=>n.props.role==='switch'),'PvP omits AI-only preferences');await advance(5000);assert.equal(h.requests.length,0);h.scene().props.onChargeStart();h.render();h.scene().props.onChargeEnd();assert.equal(h.requests[0].body.op,'pitch','PvP remains manually playable even with both stored AI preferences OFF');}finally{h.dispose();}
 }
 {
  const h=sceneHarness({preferences:{batting:false,pitching:false}});h.ready();h.dispose();await advance(5000);assert.equal(h.requests.length,0,'Leaving the game cancels a scheduled automatic half');
 }
 const base=state(),expected=autoHalfBoundary(base);assert(sameAutoHalfBoundary(expected,base));
 for(const change of [s=>s.save.id='other',s=>s.save.game.id='other',s=>s.save.game.inning++,s=>s.save.game.half='top',s=>s.action.role='pitcher',s=>s.action.pitchCount++,s=>s.action.code='other',s=>s.action.mode='pvp',s=>s.action.waiting=true,s=>s.save.game.complete=true,s=>s.action.pitch={resolved:false}]){const changed=structuredClone(base);change(changed);assert(!sameAutoHalfBoundary(expected,changed));}

 // Exercise both actual write queues: the next version must never turn a
 // scheduled old-side auto request into a new-side simulation.
 for(const kind of ['season','match']){
  const h=hooks(),rosterStub={parseRoster:v=>v,registerRoster(){},pinMatchRoster(){}};
  let server={...state(),match:{code:'FABCDEFG',mode:'ai',team:'HH',hostTeam:'HH',guestTeam:'LG',version:1,waiting:false},appearances:{}},posts=[],handler;
  const response=data=>({ok:true,status:200,headers:{get:()=>null},json:async()=>data});
  globalThis.location.search=kind==='match'?'?room=FABCDEFG':'';session.clear();
  globalThis.fetch=async(url,options={})=>{if(url==='/api/session')return response({csrfToken:'token'});if(url.endsWith('/career'))return response({player:null});if(url.includes('/roster'))return response({season:2025,seasons:[2025],asOf:'fixture',revision:'fixture',teams:[],batters:[],pitchers:[]});if(options.method==='POST'){const body=JSON.parse(options.body);posts.push(body);return handler(body);}return response(server);};
  const modules=loader(h.api,{'./roster':rosterStub}),useClient=kind==='season'?modules('lib/season-client.ts').useSeasonClient:modules('lib/match-client.ts').useMatchClient;
  let client=h.run(()=>useClient(false));for(let i=0;i<8;i++)await settle();client=h.flush();assert(client.state,`${kind} fixture must restore before writes`);
  const gate=deferred();handler=async()=>{await gate.promise;server=nextHalf(server);return response(server);};
  const first=client.command({op:'sim-half'});await settle();const staleBoundary=autoHalfBoundary(client.state),queued=client.command({op:'sim-half',_autoHalfBoundary:staleBoundary});const rejected=assert.rejects(queued,/공수가 바뀌었습니다/);
  Object.assign(staleBoundary,autoHalfBoundary(nextHalf(client.state)));gate.resolve();await first;await rejected;client=h.flush();assert.equal(posts.length,1,`${kind}: queued old-half request makes no HTTP mutation`);assert.equal(client.busy,false,`${kind}: rejected boundary releases queue busy state`);
  handler=async()=>{server=nextHalf(server);return response(server);};await client.command({op:'sim-half'});client=h.flush();assert.equal(posts.length,2,`${kind}: explicit manual simulation remains available`);
  const guard=autoHalfBoundary(client.state);await client.command({op:'sim-half',_autoHalfBoundary:guard});client=h.flush();assert.equal(posts.length,3);assert(!('_autoHalfBoundary' in posts.at(-1)),`${kind}: client-only boundary metadata is never sent to the API`);
  h.dispose();
 }
 console.log('PASS direct-play preferences: browser persistence/defaults, independent roles, manual PvP, half-by-half pause/resume/failure, fielding/visibility/modal gates, simulation handoff, no stray ready, and actual season/friendly queued-boundary isolation');
}finally{Object.assign(globalThis,originals);}
