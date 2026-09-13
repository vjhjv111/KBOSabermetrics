import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import React from 'react';
import {createRequire} from 'node:module';

const original=Object.fromEntries(['document','window','setTimeout','clearTimeout','setInterval','clearInterval'].map(key=>[key,globalThis[key]]));
let time=100000,serial=0,modal=false;
const timers=new Map(),intervals=new Map(),listeners=new Map();
globalThis.setTimeout=(fn,delay=0)=>{const id=++serial;timers.set(id,{fn,at:time+delay});return id;};
globalThis.clearTimeout=id=>timers.delete(id);
globalThis.setInterval=fn=>{const id=++serial;intervals.set(id,fn);return id;};
globalThis.clearInterval=id=>intervals.delete(id);
globalThis.document={hidden:false,querySelector:()=>modal?{}:null,addEventListener:(name,fn)=>{if(!listeners.has(name))listeners.set(name,new Set());listeners.get(name).add(fn);},removeEventListener:(name,fn)=>listeners.get(name)?.delete(fn)};
globalThis.window={addEventListener(){},removeEventListener(){}};
globalThis.window.parent=globalThis.window;
const settle=async()=>{for(let i=0;i<8;i++)await Promise.resolve();};
async function advance(ms){
 const until=time+ms;
 for(;;){const next=[...timers].filter(([,timer])=>timer.at<=until).sort((a,b)=>a[1].at-b[1].at)[0];if(!next)break;time=next[1].at;timers.delete(next[0]);next[1].fn();await settle();}
 time=until;for(const fn of [...intervals.values()])fn();await settle();
}
function deferred(){let resolve,reject;const promise=new Promise((a,b)=>{resolve=a;reject=b;});return {promise,resolve,reject};}
function hooks(){
 const slots=[];let index=0,effects=[],render;
 const same=(a,b)=>a&&b&&a.length===b.length&&a.every((v,i)=>Object.is(v,b[i]));
 const api={...React,useState(initial){const i=index++;slots[i]??={value:typeof initial==='function'?initial():initial};return [slots[i].value,value=>{slots[i].value=typeof value==='function'?value(slots[i].value):value;}];},useRef(initial){const i=index++;return slots[i]??(slots[i]={current:initial});},useCallback(fn,deps){const i=index++;if(!slots[i]||!same(slots[i].deps,deps))slots[i]={value:fn,deps};return slots[i].value;},useMemo(fn,deps){return api.useCallback(fn,deps)();},useEffect(fn,deps){const i=index++;if(!slots[i]||!same(slots[i].deps,deps)){const old=slots[i];slots[i]={deps,cleanup:old?.cleanup};effects.push(()=>{old?.cleanup?.();slots[i].cleanup=fn();});}}};
 return {api,run(fn){render=fn;return this.flush();},flush(){index=0;effects=[];const tree=render();for(const effect of effects)effect();return tree;},dispose(){slots.forEach(s=>s.cleanup?.());}};
}
const ActionScene=()=>null;
function loader(hooks){
 const cache=new Map();
 const load=file=>{file=path.resolve(file);if(cache.has(file))return cache.get(file).exports;const mod={exports:{}};cache.set(file,mod);const native=createRequire(file);
  const require=id=>{if(id==='react')return hooks;if(id==='./action-scene')return {__esModule:true,default:ActionScene};if(id==='../lib/game-clock')return {localNow:()=>time};if(id.endsWith('.css'))return {};
   if(id.startsWith('.')){const base=path.resolve(path.dirname(file),id);for(const ext of ['.ts','.tsx','.json'])if(fs.existsSync(base+ext))return ext==='.json'?JSON.parse(fs.readFileSync(base+ext,'utf8')):load(base+ext);}
   return native(id);
  };
  const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
  new Function('require','module','exports',js)(require,mod,mod.exports);return mod.exports;
 };return load;
}
function scheduler(initial={}){
 const h=hooks(),calls=[],options={matchKey:'match-a',pitchCount:0,eligible:true,request:()=>{const d=deferred();calls.push(d);return d.promise;},...initial};
 const {useAutoPitch}=loader(h.api)('lib/use-auto-pitch.ts');let value=h.run(()=>useAutoPitch(options));
 return {options,calls,get value(){return value;},render(){value=h.flush();return value;},dispose:()=>h.dispose()};
}
function entries(tree){if(Array.isArray(tree))return tree.flatMap(entries);if(!tree?.props)return [];return [tree,...entries(tree.props.children)];}
const element=(tree,predicate)=>entries(tree).find(predicate);
function seasonState(){
 const batter={id:'2025:auto-batter',name:'타자',team:'HH',pa:500,ab:450,h:130,hr:20,so:90,avg:.289,slg:.5,profile:{bats:'R',throws:'R'}};
 const pitcher={id:'2025:auto-pitcher',name:'투수',team:'LG',tbf:500,bb:35,so:100,outs:350,era:4,arsenal:[{type:'fastball',velocity:146,usage:100}],profile:{bats:'R',throws:'R'}};
 const game={id:'auto-game',homeTeam:'HH',awayTeam:'LG',inning:1,half:'bottom',homeLine:[0],awayLine:[0],homeLineup:Array(9).fill(batter.id),awayLineup:Array(9).fill(batter.id),homeOrder:0,awayOrder:0,homePitcher:pitcher.id,awayPitcher:pitcher.id,homeRuns:0,awayRuns:0,homeHits:0,awayHits:0,outs:0,bases:[null,null,null],playerStats:{},complete:false};
 return {save:{id:'auto-season',team:'HH',season:2025,asOf:'fixture',revision:'fixture',game,teams:[{code:'HH',name:'한화',batters:[batter],pitchers:[pitcher]}]},action:{code:'auto-match',role:'batter',mode:'ai',pace:'full',waiting:false,done:false,pitchCount:0,balls:0,strikes:0,history:[],batter:batter.id,pitcher:pitcher.id,roster:{season:2025,asOf:'fixture',revision:'fixture',batter,pitcher},pitch:null}};
}
function season(initial={}){
 const h=hooks(),calls=[],props={state:seasonState(),busy:false,suspended:false,clock:{current:0},appearances:{},command:body=>{calls.push(body);return Promise.resolve(props.state);},...initial};
 const SeasonMatch=loader(h.api)('app/season-match.tsx').default;let tree=h.run(()=>SeasonMatch(props));
 const render=()=>{tree=h.flush();return tree;};
 return {props,calls,get tree(){return tree;},render,ready(){element(tree,n=>n.type===ActionScene).props.onReady(true);render();},dispose:()=>h.dispose()};
}

try{
 {
  const h=scheduler();try{
   assert.equal(h.value.enabled,true,'The first AI pitch is automatic by default');await advance(349);assert.equal(h.calls.length,0);await advance(1);assert.equal(h.calls.length,1);
   h.render();h.options.eligible=false;h.render();h.options.eligible=true;h.render();await advance(5000);assert.equal(h.calls.length,1,'Busy/visibility changes cannot duplicate a pending ready');
   h.value.toggle();h.render();h.value.toggle();h.render();await advance(1000);assert.equal(h.calls.length,1,'Pause/resume cannot duplicate an already submitted pitch');
   h.options.pitchCount=1;h.render();await advance(1000);assert.equal(h.calls.length,1,'Polling a newer pitch cannot overtake a slow ready response');
   h.calls[0].resolve();await settle();h.render();await advance(350);assert.equal(h.calls.length,2,'A newer eligible pitch starts after the pending request settles');
   h.calls[1].resolve();await settle();h.render();h.options.eligible=false;h.render();h.options.eligible=true;h.render();await advance(2000);assert.equal(h.calls.length,2,'Repeated identical resolved snapshots are claimed only once');
  }finally{h.dispose();}
 }
 {
  const h=scheduler();try{
   h.value.toggle();h.render();await advance(1000);assert.equal(h.calls.length,0,'User pause cancels a scheduled first pitch');
   h.value.toggle();h.render();await advance(350);h.calls[0].reject(new Error('offline'));await settle();h.render();
   assert.equal(h.value.enabled,false);assert.equal(h.value.failed,true);await advance(10000);h.render();assert.equal(h.calls.length,1,'Failures pause instead of looping network requests');
   h.value.toggle();h.render();await advance(350);assert.equal(h.calls.length,2,'Explicit resume retries the same pitch once');
   h.options.matchKey='match-b';h.render();h.calls[1].reject(new Error('old match failure'));await settle();h.render();assert.equal(h.value.enabled,true,'A late failure cannot pause a new match');
   await advance(350);assert.equal(h.calls.length,3);h.calls[2].resolve();await settle();
  }finally{h.dispose();}
 }
 for(const block of ['hidden','modal','inactive']){
  const h=scheduler();try{
   if(block==='hidden')document.hidden=true;if(block==='modal')modal=true;if(block==='inactive'){h.options.eligible=false;h.render();}
   await advance(1000);assert.equal(h.calls.length,0,`${block} blocks dispatch even if it happened just before the timer`);
   document.hidden=false;modal=false;h.options.eligible=false;h.render();h.options.eligible=true;h.render();await advance(350);assert.equal(h.calls.length,1,`${block}: returning to the active game resumes automatic preparation`);h.calls[0].resolve();await settle();
  }finally{document.hidden=false;modal=false;h.dispose();}
 }
 {
  const h=scheduler();h.dispose();await advance(1000);assert.equal(h.calls.length,0,'Navigating away clears a scheduled request');
  const pending=scheduler();await advance(350);pending.dispose();pending.calls[0].reject(new Error('late'));await settle();await advance(1000);assert.equal(pending.calls.length,1,'A response after leaving cannot schedule new pitches');
 }
 const {pitchPresentationDuration,pitchPresentationEnd,pitchResultRevealAt,pitchCommandReadyAt,RESULT_DISPLAY_MS}=loader(React)('lib/pitch-cycle.ts');
 const result=(trajectory,outcome='OUT')=>({at:time,contact:{at:time,position:{x:0,y:1.05,z:0}},trajectory,outcome,exitSpeed:140,launchAngle:25});
 assert.equal(pitchPresentationDuration(),1200);
 for(const contact of [result('ground'),result('foul','FOUL'),result('fly'),result('fly','HR'),{...result('fly'),exitSpeed:180,launchAngle:65}]){
  const pitch={releaseAt:time-500,flightMs:500,reaction:contact},reveal=pitchResultRevealAt(pitch),end=pitchPresentationEnd(pitch);
  assert(reveal>contact.contact.at,'A contact result is held until its field play completes');assert.equal(end-reveal,RESULT_DISPLAY_MS,'The result remains readable after fielding and before the next pitch');
  assert.equal(pitchPresentationDuration(contact),end-contact.contact.at);assert.equal(pitchResultRevealAt({...pitch,reaction:{...contact,at:time+30000}}),reveal,'A late HTTP response cannot change the visual fielding timeline');
 }
 assert.equal(pitchPresentationEnd({releaseAt:time,flightMs:600,reaction:{at:time-100}}),time+1800,'A result received before arrival still holds until the ball reaches the plate');

 for(const mode of ['season','friendly']){
  const h=season({friendly:mode==='friendly'});try{
   await advance(1000);assert.equal(h.calls.length,0,`${mode}: WebGL readiness gates the first pitch`);h.ready();await advance(350);assert.equal(h.calls.length,1);assert.deepEqual(h.calls[0],{op:'ready',previousPitch:0});
   h.props.state={...h.props.state,action:{...h.props.state.action,pitchCount:1,pitch:{id:1,type:'fastball',velocity:146,resolved:true,releaseAt:time-1000,flightMs:600,reaction:result('fly','HR')}}};h.render();
   const reveal=pitchResultRevealAt(h.props.state.action.pitch),end=pitchPresentationEnd(h.props.state.action.pitch);
   await advance(reveal-time-1);h.render();assert.equal(h.calls.length,1,`${mode}: a homer finishes before the result is public`);
   await advance(2);h.render();assert.equal(h.calls.length,1,`${mode}: revealing the result does not skip the reading interval`);
   await advance(end-time+1);h.render();await advance(349);assert.equal(h.calls.length,1,`${mode}: only one scheduled next-pitch grace period follows the full presentation`);await advance(1);assert.equal(h.calls.length,2);
   h.props.state.action.role='pitcher';h.props.state.action.pitchCount=2;h.props.state.action.pitch=null;h.render();await advance(1000);assert.equal(h.calls.length,2,'Changing to defense keeps pitching manual');
   h.props.state.action.role='batter';h.render();await advance(350);assert.equal(h.calls.length,3,'Returning to offense resumes automatic pitching');
   const button=element(h.tree,n=>n.type==='button'&&n.props['aria-label']==='자동 투구 일시정지');assert(button,'Pause is inside the game field');button.props.onClick();h.render();h.props.state.action.pitchCount=3;h.render();await advance(1000);assert.equal(h.calls.length,3,'User pause remains across the next pitch count');
   element(h.tree,n=>n.type==='button'&&n.props['aria-label']==='자동 투구 재개').props.onClick();h.render();await advance(350);assert.equal(h.calls.length,4);
  }finally{h.dispose();}
 }
 for(const blocker of ['pvp','pitcher','waiting','complete','busy','suspended','hidden']){
  const h=season();try{
   if(blocker==='pvp')h.props.state.action.mode='pvp';if(blocker==='pitcher')h.props.state.action.role='pitcher';if(blocker==='waiting')h.props.state.action.waiting=true;if(blocker==='complete')h.props.state.save.game.complete=true;if(blocker==='busy')h.props.busy=true;if(blocker==='suspended')h.props.suspended=true;
   h.ready();if(blocker==='hidden'){document.hidden=true;for(const fn of listeners.get('visibilitychange')??[])fn();h.render();}
   await advance(1000);assert.equal(h.calls.length,0,`${blocker}: the full-game component never requests an AI pitch`);
  }finally{document.hidden=false;h.dispose();}
 }
 {
  const h=season({suspended:true});try{h.ready();await advance(500);h.props.suspended=false;h.render();await advance(350);assert.equal(h.calls.length,1,'Closing the save-code modal resumes preparation');}finally{h.dispose();}
 }

 // Evaluate the practice component's actual eligibility expression rather than
 // maintaining a second set of practice rules in the test.
 const source=fs.readFileSync('app/page.tsx','utf8'),ast=ts.createSourceFile('app/page.tsx',source,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX),home=ast.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text==='Home');
 const declarations=home.body.statements.filter(ts.isVariableStatement),statement=name=>declarations.find(node=>node.declarationList.declarations.some(d=>d.name.getText(ast)===name)).getText(ast);
 const code=ts.transpileModule(statement('canPitch')+'\n'+statement('automatic')+'\nreturn automatic;', {compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.None}}).outputText;
 const getPractice=new Function('context',`with(context){${code}}`);
 const base={game:{code:'practice',mode:'ai',role:'batter',pitchCount:0,done:false,waiting:false},presentation:{score:null},active:null,last:undefined,busy:false,tabActive:true,suspended:false,guideOpen:false,restoring:false,now:time,ready3d:true,pitchPresentationEnd,pitchCommandReadyAt,useAutoPitch:options=>options,askPitch:async()=>true};
 assert.equal(getPractice(base).eligible,true);assert.equal(getPractice({...base,game:null}).eligible,false);
 for(const name of ['busy','suspended','guideOpen','restoring'])assert.equal(getPractice({...base,[name]:true}).eligible,false,`Practice ${name} blocks auto-ready`);
 for(const name of ['tabActive','ready3d'])assert.equal(getPractice({...base,[name]:false}).eligible,false,`Practice needs ${name}`);
 for(const overrides of [{role:'pitcher'},{mode:'pvp'},{done:true},{waiting:true}])assert.equal(getPractice({...base,game:{...base.game,...overrides}}).eligible,false);
 const flying={releaseAt:time,flightMs:700,resolved:false};assert.equal(getPractice({...base,active:flying}).eligible,false);assert.equal(getPractice({...base,active:{...flying,resolved:true,reaction:result('ground')}}).eligible,false);
 const settledGround={...flying,resolved:true,reaction:result('ground')};assert.equal(getPractice({...base,active:settledGround,now:pitchPresentationEnd(settledGround)+1}).eligible,true);
 const reconnected={...flying,resolved:true,releaseAt:time-10000,flightMs:500,reaction:{...result(),contact:undefined,at:time}};
 assert.equal(getPractice({...base,active:reconnected,now:time+350}).eligible,false,'An old pitch resolved on reconnect must wait for the server cooldown');
 assert.equal(getPractice({...base,active:reconnected,now:time+901}).eligible,true,'Reconnect resumes automatically after the cooldown without delaying the result card');
 await assert.rejects(getPractice({...base,askPitch:async()=>false}).request(),/자동 투구 요청 실패/,'Practice reports request failures to the pause controller');
 assert(!source.includes('>다음 투구</button>'),'Practice no longer requires a manual next-pitch button');
 assert(source.includes('onClose={()=>setGuideOpen(false)}')&&source.includes('setGuideOpen(true);dialog.current?.showModal()'),'Opening and closing the native guide suspends and resumes automatic preparation');
 console.log('PASS automatic AI pitching: default first/next pitches, one claim per pitch, deferred transport, failure pause/retry, unmount/stale response, hidden/modal/busy gates, complete ball/fielding holds, role changes, season/friendly integration and practice eligibility');
}finally{Object.assign(globalThis,original);}
