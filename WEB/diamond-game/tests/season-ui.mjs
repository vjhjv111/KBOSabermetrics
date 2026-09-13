import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import {createRequire} from 'node:module';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';

function moduleLoader(overrides={}){
 const cache=new Map();
 const load=file=>{file=path.resolve(file);if(cache.has(file))return cache.get(file).exports;const mod={exports:{}};cache.set(file,mod);const native=createRequire(file);
  const require=id=>{if(id in overrides)return overrides[id];if(id.endsWith('.css'))return {};
   if(id.includes('character-preview')||id==='./action-scene'||id==='./page')return {__esModule:true,default:()=>null};
   if(id.startsWith('.')){const base=path.resolve(path.dirname(file),id);for(const ext of ['.ts','.tsx','.json'])if(fs.existsSync(base+ext)){if(ext==='.json')return JSON.parse(fs.readFileSync(base+ext,'utf8'));return load(base+ext);}}
   return native(id);
  };const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
  new Function('require','module','exports',js)(require,mod,mod.exports);return mod.exports;
 };return load;
}
function hookHarness(){
 const slots=[];let index=0,effects=[],layouts=[],render;
 const equal=(a,b)=>!!a&&!!b&&a.length===b.length&&a.every((v,i)=>Object.is(v,b[i]));
 const hooks={...React,useState(initial){const at=index++;if(!slots[at])slots[at]={value:typeof initial==='function'?initial():initial};return [slots[at].value,next=>{slots[at].value=typeof next==='function'?next(slots[at].value):next;}];},useRef(initial){const at=index++;return slots[at]??(slots[at]={current:initial});},useCallback(fn,deps){const at=index++;if(!slots[at]||!equal(slots[at].deps,deps))slots[at]={value:fn,deps};return slots[at].value;},useMemo(fn,deps){return hooks.useCallback(fn,deps)();},useEffect(fn,deps){const at=index++;if(!slots[at]||!equal(slots[at].deps,deps)){const previous=slots[at];slots[at]={deps,cleanup:previous?.cleanup};effects.push(()=>{previous?.cleanup?.();slots[at].cleanup=fn();});}}};
 hooks.useLayoutEffect=(fn,deps)=>{const at=index++;if(!slots[at]||!equal(slots[at].deps,deps)){const previous=slots[at];slots[at]={deps,cleanup:previous?.cleanup};layouts.push(()=>{previous?.cleanup?.();slots[at].cleanup=fn();});}};
 return {hooks,run(fn){render=fn;return this.flush();},flush({beforeLayout,beforeEffects}={}){index=0;effects=[];layouts=[];const result=render();beforeLayout?.(result);for(const effect of layouts)effect();beforeEffects?.(result);for(const effect of effects)effect();return result;},dispose(){slots.forEach(s=>s.cleanup?.());}};
}
const flush=()=>new Promise(resolve=>setImmediate(resolve));
const deferred=()=>{let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};};
const plainLoad=moduleLoader(),clientFns=plainLoad('lib/season-client.ts');
const {visibleDialogViewport}=plainLoad('lib/dialog-viewport.ts');
assert.deepEqual(visibleDialogViewport(200,3000,3000,0,844),{top:12,height:620},'Save dialog fits the visible phone area below the outer header');
assert.deepEqual(visibleDialogViewport(-800,3000,3000,0,844),{top:812,height:820},'Outer scrolling moves the dialog within the tall game iframe');
assert.deepEqual(visibleDialogViewport(-800,3000,3000,150,320),{top:962,height:296},'The keyboard visual viewport keeps the close control and dialog content reachable');
assert.equal(visibleDialogViewport(900,3000,3000,0,844),null,'An iframe outside the visible viewport does not invent dialog bounds');
const game=(id='g1',complete=false)=>({id,complete,inning:1,half:'top',homeTeam:'LG',awayTeam:'OB',homeLine:[0],awayLine:[0],homeLineup:Array.from({length:9},(_,i)=>'b'+i),awayLineup:Array.from({length:9},(_,i)=>'a'+i),homeOrder:0,awayOrder:0,homePitcher:'p0',awayPitcher:'p1',homeUsedPitchers:['p0'],awayUsedPitchers:['p1'],homeUsedBatters:Array.from({length:9},(_,i)=>'b'+i),awayUsedBatters:Array.from({length:9},(_,i)=>'a'+i),outs:0,homeRuns:0,awayRuns:0,homeHits:0,awayHits:0,bases:[null,null,null],events:[],playerStats:{},endReason:'',plateAppearances:0,completedAt:null});
const snapshot=(version,id='g1',complete=false)=>({save:{id:'season',version,season:2026,seasonNumber:1,team:'LG',revision:'r1',asOf:'2026-09-13',teams:[],game:game(id,complete)},action:{code:'action',role:'batter',done:complete,pitchCount:1,pitch:{id:1,resolved:false,releaseAt:0,flightMs:1000},roster:null}});
assert(!clientFns.acceptSeasonSnapshot(snapshot(10),{save:null,action:null}),'An old empty GET cannot erase a newly created save');
assert(!clientFns.acceptSeasonSnapshot(snapshot(10),snapshot(9)),'Older polls cannot rewind the league');
assert(clientFns.acceptSeasonSnapshot(snapshot(10),{...snapshot(1),save:{...snapshot(1).save,id:'other'}}),'Version counters belong to a save ID');
assert(!clientFns.acceptCareerSnapshot({player:{id:'p',version:5}},{player:{id:'p',version:4}}));
assert(!clientFns.acceptCareerSnapshot({player:{id:'p',version:1}},{player:null}),'Late empty reads cannot undo creation');
assert.notEqual(clientFns.completedGameKey(snapshot(10,'g1',true)),clientFns.completedGameKey(snapshot(11,'g2',true)),'Consecutive simulated games both trigger reward refresh');

// Exercise the actual hook with delayed HTTP and a deterministic hook scheduler.
const original={fetch:globalThis.fetch,document:globalThis.document,window:globalThis.window,setInterval:globalThis.setInterval,clearInterval:globalThis.clearInterval};
globalThis.document={hidden:false,addEventListener(){},removeEventListener(){}};globalThis.window={addEventListener(){},removeEventListener(){}};globalThis.setInterval=()=>1;globalThis.clearInterval=()=>{};
const h=hookHarness(),rosterStub={parseRoster:v=>v,registerRoster(){},pinMatchRoster(){}};
const hookLoad=moduleLoader({react:h.hooks,'./roster':rosterStub}),{useSeasonClient}=hookLoad('lib/season-client.ts');
let serverState=snapshot(10),careerGets=0,posts=[],conditionalGets=0,postHandler=async body=>({...serverState,save:{...serverState.save,version:body.version+1}});
const response=(data,status=200,etag=null)=>({ok:status<400,status,headers:{get:name=>name==='ETag'?etag:null},json:async()=>{assert.notEqual(status,304,'A 304 has no JSON body');return data;}});
globalThis.fetch=async(url,options={})=>{
 if(url==='/api/session')return response({csrfToken:'token'});
 if(url.endsWith('/career')){careerGets++;return response({player:{id:'career',version:careerGets}});}
 if(url.includes('/roster'))return response({season:2026,seasons:[2026],revision:'r',asOf:'today',teams:[],batters:[],pitchers:[]});
 if(options.method==='POST'){const body=JSON.parse(options.body);posts.push(body);return postHandler(body);}
 const etag='"'+serverState.save.id+':'+serverState.save.version+'"';if(options.headers?.['If-None-Match']===etag){conditionalGets++;return response(null,304);}
 return response(serverState,200,etag);
};
// Handler responses are HTTP responses, whereas fixture state stays separate.
postHandler=async body=>response(serverState={...serverState,save:{...serverState.save,version:body.version+1}});
let hook=h.run(()=>useSeasonClient(false));for(let i=0;i<8;i++)await flush();hook=h.flush();assert.equal(hook.loading,false);assert.equal(hook.state.save.version,10);
const cached=hook.state;assert.equal(await hook.reload(),cached,'304 returns the current snapshot unchanged');assert.equal(conditionalGets,1);
const gate=deferred();let first=true,loss=true;
postHandler=async body=>{if(first){first=false;await gate.promise;serverState=snapshot(11);return response(serverState);}if(body.op==='swing'&&loss){loss=false;serverState=snapshot(12);throw new TypeError('response lost after commit');}return response(serverState);};
const ready=hook.command({op:'ready',previousPitch:0});await flush();
const aim={x:.2,y:.3},swing=hook.command({op:'swing',pitchId:1,inputAt:12345,aim});aim.x=99;
await flush();assert.equal(posts.length,1,'A queued swing waits for the prior write instead of being rejected');gate.resolve();await Promise.all([ready,swing]);hook=h.flush();
assert.equal(posts[1].version,11,'Queued input uses the last committed version');assert.equal(posts[1].inputAt,12345);assert.equal(posts[1].aim.x,.2,'Aim is captured at the first input');assert.deepEqual(posts[1],posts[2],'Uncertain delivery retries the identical idempotent request');assert.equal(hook.busy,false);
let conflict=true;
postHandler=async body=>{if(conflict){conflict=false;serverState=snapshot(13);return response({error:'state advanced'},409);}serverState=snapshot(14);return response(serverState);};
await hook.command({op:'swing',pitchId:1,inputAt:23456,aim:{x:0,y:0}});assert.equal(posts.at(-1).version,13);assert.equal(posts.at(-1).inputAt,23456);assert.notEqual(posts.at(-1).requestId,posts.at(-2).requestId,'Confirmed version conflict gets a new payload identity');
const beforeRewards=careerGets;let match=1;
postHandler=async body=>response(serverState=snapshot(body.version+1,'reward'+match++,true));
await hook.command({op:'sim-day'});for(let i=0;i<4;i++)await flush();await hook.command({op:'sim-day'});for(let i=0;i<4;i++)await flush();assert.equal(careerGets,beforeRewards+2,'Every newly completed fixture refreshes server rewards');
// Save-code operations share the gameplay queue; switching owners requires a
// fresh page because snapshot guards correctly reject older/empty versions.
const gameplayFetch=globalThis.fetch,saveCalls=[],navigation=[];
let saveHandler=async body=>response(body?.op==='load'?{loaded:true}:{code:'ABCD-EFGH-JKLM-NPQR-STUV',hasData:true,updatedAt:123});
globalThis.window.location={pathname:'/diamond/',replace:url=>navigation.push(url)};
globalThis.fetch=async(url,options={})=>{
 if(url==='/api/diamond/save'){const body=options.body?JSON.parse(options.body):null;saveCalls.push(body);return saveHandler(body);}
 return gameplayFetch(url,options);
};
const saveGate=deferred();postHandler=async body=>{await saveGate.promise;return response(serverState=snapshot(body.version+1));};
const finishing=hook.command({op:'sim-half'}),codeInfo=hook.getSaveCode();await flush();
assert.equal(saveCalls.length,0,'Code lookup waits for the gameplay save to commit');
saveGate.resolve();await finishing;assert.equal((await codeInfo).hasData,true);
assert.equal((await hook.saveCode()).code,'ABCD-EFGH-JKLM-NPQR-STUV');
assert.deepEqual(saveCalls.at(-1),{op:'save'},'Save sends no client-authored game state');
hook=h.flush();const beforeRestore=hook.state;
saveHandler=async()=>response({error:'코드를 확인해 주세요.'},404);
await assert.rejects(hook.loadSaveCode('INVALID'),/코드를 확인/);hook=h.flush();
assert.equal(hook.busy,false);assert.equal(hook.state,beforeRestore);assert.equal(navigation.length,0,'A failed restore leaves the active game intact');
let restoreLost=true;saveHandler=async()=>{if(restoreLost){restoreLost=false;throw new TypeError('restore response lost');}return response({loaded:true});};
await hook.loadSaveCode('ABCD-EFGH-JKLM-NPQR-STUV');hook=h.flush();
assert.deepEqual(saveCalls.at(-1),saveCalls.at(-2),'An uncertain restore retries the same code');
assert.deepEqual(navigation,['/diamond/'],'Successful restore drops old match parameters and reloads all owner state');
assert.equal(hook.busy,true,'Gameplay stays locked until the restored page opens');
h.dispose();Object.assign(globalThis,original);

// Clipboard requests can remain pending in embedded browsers; the UI must
// reach its manual-copy fallback rather than leave the button silent forever.
const copyCode=plainLoad('app/save-code-panel.tsx').copySaveCodeToClipboard;
const navigatorDescriptor=Object.getOwnPropertyDescriptor(globalThis,'navigator'),realTimeout=globalThis.setTimeout,realClearTimeout=globalThis.clearTimeout;
let timeoutCallback=null,clearedTimers=0,copiedCode='';
globalThis.setTimeout=(callback,ms)=>{assert.equal(ms,1500);timeoutCallback=callback;return 11;};
globalThis.clearTimeout=()=>{clearedTimers++;};
const setClipboard=clipboard=>Object.defineProperty(globalThis,'navigator',{configurable:true,value:{clipboard}});
try{
 setClipboard({writeText:async value=>{copiedCode=value;}});await copyCode('TEST-CODE');assert.equal(copiedCode,'TEST-CODE');assert.equal(clearedTimers,1);
 setClipboard({writeText:()=>new Promise(()=>{})});const stuck=copyCode('TEST-CODE');timeoutCallback();await assert.rejects(stuck,/timeout/);assert.equal(clearedTimers,2);
 setClipboard({writeText:async()=>{throw new Error('denied');}});await assert.rejects(copyCode('TEST-CODE'),/denied/);assert.equal(clearedTimers,3);
 setClipboard(undefined);await assert.rejects(copyCode('TEST-CODE'),/unavailable/);
}finally{if(navigatorDescriptor)Object.defineProperty(globalThis,'navigator',navigatorDescriptor);else delete globalThis.navigator;globalThis.setTimeout=realTimeout;globalThis.clearTimeout=realClearTimeout;}

const uiLoad=moduleLoader(),{default:CareerPage}=uiLoad('app/career-page.tsx'),{TeamManagement,replaceLineupSlot}=uiLoad('app/team-management.tsx'),{default:SeasonMatch,lineScoreValue,nextMatchup,seasonDisplayAction}=uiLoad('app/season-match.tsx');
const appearance=uiLoad('lib/player-appearance.ts').DEFAULT_PLAYER_CUSTOMIZATION;
const player={id:'career_new',version:1,name:'신인',team:'LG',position:'CF',bats:'R',throws:'R',delivery:'overhand',archetype:'balanced',appearance:{...appearance,jerseyNumber:'17'},ratings:{contact:50,power:50,discipline:50,speed:50,fielding:50,velocity:50,control:50,stamina:50},level:1,xp:0,nextLevelXp:100,trainingPoints:1,games:0,stats:{ab:0,h:0,hr:0,rbi:0},rewards:[]};
const careerProps={teams:[{code:'LG',name:'LG'}],busy:false,edit:async()=>{},onLeague(){}};
assert(renderToStaticMarkup(React.createElement(CareerPage,{...careerProps,player:null})).includes('<form'),'New career starts in editor');
const createdMarkup=renderToStaticMarkup(React.createElement(CareerPage,{...careerProps,player,key:player.id}));assert(!createdMarkup.includes('<form'),'New player ID remount opens growth, not the create form');assert(createdMarkup.includes('훈련'));
for(const position of ['P','C','1B','2B','3B','SS','LF','CF','RF','DH']){
 const trainingMarkup=renderToStaticMarkup(React.createElement(CareerPage,{...careerProps,player:{...player,position}}));
 const labels=[...trainingMarkup.matchAll(/aria-label="([^"]+) 훈련"/g)].map(match=>match[1]);
 assert.deepEqual(labels,position==='P'?['구속','제구','체력']:['컨택','파워','선구안','주력','수비'],`${position} can spend PT only on ratings used by its role`);
 assert(trainingMarkup.includes(position==='P'?'다음 경기부터 구속·제구·체력에 적용':'다음 경기부터 타격·주루·수비에 적용'),'Training explanation follows the saved player role');
}
const t={code:'LG',name:'LG',lineup:Array.from({length:9},(_,i)=>'b'+i),batters:Array.from({length:11},(_,i)=>({id:'b'+i,name:'타자'+i})),pitchers:[{id:'p0',name:'선발',era:3},{id:'p2',name:'불펜',era:4}]};
const save={...snapshot(1).save,game:null,teams:[t]};
const pregame=renderToStaticMarkup(React.createElement(TeamManagement,{save,busy:false,send(){}}));assert(pregame.includes('선발 라인업에 편입')&&pregame.includes('타자10'),'Bench hitters can enter pregame lineup');
assert.deepEqual(replaceLineupSlot(t.lineup,3,'b10'),['b0','b1','b2','b10','b4','b5','b6','b7','b8']);assert.equal(replaceLineupSlot(t.lineup,2,'b0'),null,'Duplicate starters are rejected');
const live=game();const liveMarkup=renderToStaticMarkup(React.createElement(TeamManagement,{save:{...save,game:live},busy:false,pitchInFlight:true,send(){}}));assert(liveMarkup.includes('이 공이 끝나면'));assert(/<button[^>]*disabled=""[^>]*>타자 교체/.test(liveMarkup),'No mid-pitch substitute request');assert(!liveMarkup.includes('value="p0"'),'Current pitcher is not offered as a reliever');
const end={...game(),inning:9,complete:true,endReason:'home-ahead',homeLine:Array(8).fill(0),awayLine:Array(9).fill(0)};assert.equal(lineScoreValue(end,'home',8),'X');assert.equal(lineScoreValue({...end,endReason:'walkoff',homeLine:Array(9).fill(0)},'home',8),0);assert.equal(lineScoreValue(end,'away',9),'–');
const changed={...snapshot(3),save:{...save,game:{...game(),half:'bottom',homeOrder:2,awayPitcher:'p2'},teams:[t]},action:{...snapshot(3).action,batter:'a0',pitcher:'p0',pitch:{id:1,resolved:true,releaseAt:0,flightMs:1000,reaction:{at:1000}}}};
assert.deepEqual(nextMatchup(changed.save.game),{batter:'b2',pitcher:'p2'});assert.equal(seasonDisplayAction(changed,1500).pitcher,'p0','Finished play retains original player during animation');assert.equal(seasonDisplayAction(changed,5000).pitcher,'p2','Next half binds the current pitcher after animation');assert.equal(seasonDisplayAction(changed,5000).pitch,null);
const thirdOut={...changed,action:{...changed.action,role:'batter',roster:{batter:{id:'a0',team:'OB'},pitcher:{id:'p0',team:'LG'}},pitch:{...changed.action.pitch,reaction:{at:1000,contact:{at:1000}}}}};
assert.equal(seasonDisplayAction(thirdOut,3500).role,'pitcher','Third-out ball flight keeps its originating camera and player roles');assert.equal(seasonDisplayAction(thirdOut,4200).role,'batter','The next half changes camera after the play animation');assert.equal(thirdOut.action.role,'batter','Visual hold never changes authoritative input state');

// The first tap during the windup must animate and submit once, even while another write is queued.
const source=fs.readFileSync('app/season-match.tsx','utf8'),part=source.slice(source.indexOf('const swing=useCallback'),source.indexOf('const begin=useCallback'));
let submitted=[],animated=[],local=[];const current={current:{action:{role:'batter',code:'game',done:false,pitch:{id:1,resolved:false,releaseAt:2200}},command:async body=>{submitted.push(body);}}};
const swingAction=new Function('useCallback','current','clock','localNow','swung','aim','setSwingTime','setLocalSwing','tone','SWING_CONTACT_MS',ts.transpileModule(part,{compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText+';return swing;')(f=>f,current,{current:0},()=>1000,{current:''},{current:{x:.2,y:.1}},v=>animated.push(v),v=>local.push(v),()=>{},95);
swingAction();swingAction();assert.equal(submitted.length,1);assert.equal(submitted[0].inputAt,1000);assert.equal(animated[0],1000);assert.equal(local[0].at,1095);
current.current.action.waiting=true;current.current.action.pitch.id=2;swingAction();assert.equal(submitted.length,1,'A room waiting for its opponent cannot swing');
globalThis.document={hidden:false};
const matchProps={state:{...changed,action:{...changed.action,mode:'pvp',waiting:true,pitch:null,balls:0,strikes:0,pace:'practice'}},busy:false,clock:{current:0},command:async()=>{},appearances:{},friendly:true};
const waitingMarkup=renderToStaticMarkup(React.createElement(SeasonMatch,matchProps));assert(!waitingMarkup.includes('자동 다음 공')&&!waitingMarkup.includes('>다음 투구<'));assert(waitingMarkup.includes('상대 참가를 기다리고'));assert(waitingMarkup.includes('게임 화면을 클릭 · 터치해 스윙'));assert(!/>스윙<\/button>/.test(waitingMarkup),'Swing uses the playing surface instead of a separate button');
const finalMarkup=renderToStaticMarkup(React.createElement(SeasonMatch,{...matchProps,state:{...matchProps.state,save:{...changed.save,complete:true,game:{...changed.save.game,complete:true}}}}));assert(finalMarkup.includes('새 친선 경기')&&!finalMarkup.includes('다음 경기 시작'));assert(!finalMarkup.includes('선수 경험치가 저장'));

// Reproduce the actual friendly → season failure: a two-team registry, then a
// season render involving other clubs before any layout/passive effects run.
const registry=uiLoad('lib/roster.ts'),engine=uiLoad('lib/action-engine.ts');
const rosterTeams=['HH','HT','KT','LG','LT','NC','OB','SK','SS','WO'].map(code=>{
 const record=(kind,index=0)=>({id:`2026:${code}-${kind}${index}`,playerId:`${code}-${kind}${index}`,name:`${code} ${kind}${index}`,team:code,profile:{batsThrows:'우투우타',bats:'R',throws:'R',heightCm:185},discipline:{}});
 const batters=Array.from({length:9},(_,i)=>({...record('b',i),pa:100,ab:90,h:27,hr:3,so:20,avg:.3,slg:.5,ops:.8}));
 return {code,name:code,lineup:batters.map(b=>b.id),batters,pitchers:[{...record('p'),tbf:100,bb:8,so:25,outs:75,er:8,era:2.88,whip:1.1,arsenal:[{type:'cutter',velocity:149.7,usage:100}],arsenalSource:'observed'}]};
});
const rosterData=teams=>({season:2026,seasons:[2026],asOf:'2026-09-13',revision:'frozen',teams:teams.map(({code,name})=>({code,name})),batters:teams.flatMap(t=>t.batters),pitchers:teams.flatMap(t=>t.pitchers)});
const friendlyTeams=rosterTeams.filter(t=>['LG','OB'].includes(t.code)),homeTeam=rosterTeams.find(t=>t.code==='HT'),awayTeam=rosterTeams.find(t=>t.code==='NC');
const returnState={...snapshot(20),save:{...snapshot(20).save,team:'HT',teams:rosterTeams,game:{...game(),homeTeam:'HT',awayTeam:'NC',homePitcher:homeTeam.pitchers[0].id,awayPitcher:awayTeam.pitchers[0].id,homeLineup:homeTeam.lineup,awayLineup:awayTeam.lineup}},action:{...snapshot(20).action,mode:'ai',role:'pitcher',waiting:false,pitch:null,batter:awayTeam.batters[0].id,pitcher:homeTeam.pitchers[0].id,pace:'practice',balls:0,strikes:0,roster:{season:2026,asOf:'2026-09-13',revision:'frozen',batter:awayTeam.batters[0],pitcher:homeTeam.pitchers[0]}}};
const useFriendlyRegistry=()=>{registry.registerRoster(rosterData(friendlyTeams));registry.pinMatchRoster({season:2026,asOf:'2026-09-13',revision:'friendly',batter:friendlyTeams[0].batters[0],pitcher:friendlyTeams[1].pitchers[0]});};
useFriendlyRegistry();assert.throws(()=>engine.arsenal(homeTeam.pitchers[0].id),'The reproduction must actually lack the season pitcher globally');
const renderReturningSeason=state=>renderToStaticMarkup(React.createElement(SeasonMatch,{...matchProps,state,friendly:false}));
assert(renderReturningSeason(returnState).includes('149.7 km/h'),'First render obtains the current pitcher arsenal directly from the season snapshot');
const transitionOriginal={fetch:globalThis.fetch,document:globalThis.document,window:globalThis.window,setInterval:globalThis.setInterval,clearInterval:globalThis.clearInterval};
globalThis.document={hidden:false,addEventListener(){},removeEventListener(){}};globalThis.window={addEventListener(){},removeEventListener(){}};globalThis.setInterval=()=>1;globalThis.clearInterval=()=>{};
let returningActive=false,transitionServer=returnState,readGate=null,writeGate=null;
globalThis.fetch=async(url,options={})=>{
 if(url==='/api/session')return response({csrfToken:'token'});
 if(url.endsWith('/career'))return response({player:null});
 if(url.includes('/roster'))return response(rosterData(rosterTeams));
 if(options.method==='POST'){if(writeGate)await writeGate.promise;return response(transitionServer);}
 if(readGate)await readGate.promise;return response(transitionServer);
};
const transition=hookHarness(),transitionLoad=moduleLoader({react:transition.hooks,'./roster':registry}),useReturningSeason=transitionLoad('lib/season-client.ts').useSeasonClient;
let returning=transition.run(()=>useReturningSeason(returningActive));for(let i=0;i<8;i++)await flush();returning=transition.flush();
assert.equal(returning.state.save.version,20);assert.throws(()=>engine.arsenal(homeTeam.pitchers[0].id),'Inactive bootstrap/roster GET must not overwrite the friendly registry');
returningActive=true;returning=transition.flush({beforeLayout:client=>{assert.throws(()=>engine.arsenal(homeTeam.pitchers[0].id));assert(renderReturningSeason(client.state).includes('149.7 km/h'));},beforeEffects:()=>assert.equal(engine.arsenal(homeTeam.pitchers[0].id)[0].velocity,149.7,'Layout restoration precedes the scene effect/RAF')});
readGate=deferred();const lateRead=returning.reload();await flush();returningActive=false;returning=transition.flush();useFriendlyRegistry();transitionServer={...returnState,save:{...returnState.save,version:21}};readGate.resolve();await lateRead;readGate=null;returning=transition.flush();
assert.equal(returning.state.save.version,21);assert.throws(()=>engine.arsenal(homeTeam.pitchers[0].id),'Late inactive GET updates saved state without changing the visible friendly registry');
returningActive=true;returning=transition.flush();writeGate=deferred();const lateWrite=returning.command({op:'sim-half'});await flush();returningActive=false;returning=transition.flush();useFriendlyRegistry();transitionServer={...returnState,save:{...returnState.save,version:22}};writeGate.resolve();await lateWrite;returning=transition.flush();
assert.equal(returning.state.save.version,22);assert.throws(()=>engine.arsenal(homeTeam.pitchers[0].id),'Late inactive command cannot replace the visible friendly roster or pinned players');
transition.dispose();Object.assign(globalThis,transitionOriginal);

const oldLocation=globalThis.location;globalThis.location={search:'',pathname:'/diamond/',origin:'https://game.test'};
const frozenAppearance={'2026:career_new':{...appearance,jerseyNumber:'17'}},liveAppearance={...appearance,jerseyNumber:'88'};
const pageState={...changed,save:{...changed.save,day:1,totalDays:144,schedule:[],standings:[],appearances:frozenAppearance}};
const pageLoad=moduleLoader({'../lib/season-client':{useSeasonClient:()=>({state:pageState,career:{player:{...player,appearance:liveAppearance}},roster:null,busy:false,loading:false,error:'',command:async()=>{},clock:{current:0}})},'./season-match':{__esModule:true,default:props=>React.createElement('span',{'data-saved-appearance':JSON.stringify(props.appearances)})},'./exhibition-page':{__esModule:true,default:()=>null}});
const seasonMarkup=renderToStaticMarkup(React.createElement(pageLoad('app/season-page.tsx').default));assert(seasonMarkup.includes('jerseyNumber&quot;:&quot;17')&&!seasonMarkup.includes('jerseyNumber&quot;:&quot;88'),'An in-progress season renders its frozen server appearance, not a live career edit');assert(seasonMarkup.includes('내 선수 차례까지'));
globalThis.location=oldLocation;
globalThis.document=original.document;
console.log('PASS season UI: queued first swing, exact retry, version recovery, conditional GET/304, stale-save guards, consecutive rewards, save-code queue and restore retry/failure/reload, career remount, pregame lineup, substitutions, line score, next-half snapshots and friendly waiting');
