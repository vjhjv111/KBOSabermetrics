import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import React from 'react';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

// Render the real SeasonMatch callbacks and JSX without mounting WebGL. The
// shared pointer controller supplies the same callbacks used by ActionScene.
const {scenePointerControls}=load('lib/scene-pointer.ts');
const real={window:globalThis.window,document:globalThis.document,setInterval:globalThis.setInterval,clearInterval:globalThis.clearInterval};
const windowListeners=new Map(),documentListeners=new Map();
const events=(target)=>({addEventListener:(type,fn)=>{if(!target.has(type))target.set(type,new Set());target.get(type).add(fn);},removeEventListener:(type,fn)=>target.get(type)?.delete(fn)});
globalThis.window={...events(windowListeners)};
globalThis.document={hidden:false,...events(documentListeners)};
globalThis.setInterval=()=>1;globalThis.clearInterval=()=>{};
let time=100000;
const ActionScene=()=>null;
function moduleLoader(hooks){
 const cache=new Map();
 const source=file=>{file=path.resolve(file);if(cache.has(file))return cache.get(file).exports;
  const mod={exports:{}};cache.set(file,mod);const native=createRequire(file);
  const require=id=>{
   if(id==='react')return hooks;if(id==='./action-scene')return {__esModule:true,default:ActionScene};
   if(id==='../lib/game-clock')return {localNow:()=>time};if(id.endsWith('.css'))return {};
   if(id.startsWith('.')){const base=path.resolve(path.dirname(file),id);for(const ext of ['.ts','.tsx','.json'])if(fs.existsSync(base+ext))return ext==='.json'?JSON.parse(fs.readFileSync(base+ext,'utf8')):source(base+ext);}
   return native(id);
  };
  const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
  new Function('require','module','exports',js)(require,mod,mod.exports);return mod.exports;
 };return source;
}
function hooks(){
 const slots=[];let index=0,effects=[],render;
 const same=(a,b)=>a&&b&&a.length===b.length&&a.every((v,i)=>Object.is(v,b[i]));
 const api={...React,useState(initial){const i=index++;slots[i]??={value:typeof initial==='function'?initial():initial};return [slots[i].value,v=>{slots[i].value=typeof v==='function'?v(slots[i].value):v;}];},useRef(initial){const i=index++;return slots[i]??(slots[i]={current:initial});},useCallback(fn,deps){const i=index++;if(!slots[i]||!same(slots[i].deps,deps))slots[i]={value:fn,deps};return slots[i].value;},useMemo(fn,deps){return api.useCallback(fn,deps)();},useEffect(fn,deps){const i=index++;if(!slots[i]||!same(slots[i].deps,deps)){const old=slots[i];slots[i]={deps,cleanup:old?.cleanup};effects.push(()=>{old?.cleanup?.();slots[i].cleanup=fn();});}}};
 return {api,run(fn){render=fn;return this.flush();},flush(){index=0;effects=[];const tree=render();for(const effect of effects)effect();return tree;},dispose(){slots.forEach(s=>s.cleanup?.());}};
}
function entries(tree,parents=[]){
 if(Array.isArray(tree))return tree.flatMap(n=>entries(n,parents));
 if(!tree||typeof tree!=='object'||!tree.props)return [];
 const route=[...parents,tree];return [{node:tree,route},...entries(tree.props.children,route)];
}
const hasClass=(node,name)=>(node.props?.className??'').split(' ').includes(name);
const find=(tree,predicate)=>entries(tree).find(({node})=>predicate(node));
const all=(tree,predicate)=>entries(tree).filter(({node})=>predicate(node));
function pointer(values={}){return {pointerId:1,pointerType:'mouse',button:0,isPrimary:true,clientX:30,clientY:40,timeStamp:1000,preventDefault(){},stopPropagation(){this.stopped=true;},currentTarget:{setPointerCapture(){},hasPointerCapture(){return false;},releasePointerCapture(){}},...values};}
function bubble(entry,name,event=pointer()){
 for(const node of [...entry.route].reverse()){event.currentTarget={...event.currentTarget};node.props[name]?.(event);if(event.stopped)break;}
}
function key(type,target,code='Space'){
 const event={code,key:code==='Space'?' ':code,target,repeat:false,preventDefault(){this.prevented=true;}};
 for(const listener of windowListeners.get(type)??[])listener(event);return event;
}
function state(role='batter'){
 const batter={id:'2025:input-batter',name:'입력타자',team:'HH',pa:500,ab:450,h:130,hr:20,so:90,avg:.289,slg:.5,profile:{bats:'R',throws:'R'}};
 const pitcher={id:'2025:input-pitcher',name:'입력투수',team:'LG',tbf:500,bb:35,so:100,outs:350,era:4,arsenal:[{type:'fastball',velocity:146,usage:65},{type:'slider',velocity:134,usage:35}],profile:{bats:'R',throws:'R'}};
 const game={id:'input-game',homeTeam:'HH',awayTeam:'LG',inning:1,half:role==='batter'?'bottom':'top',homeLine:[0],awayLine:[0],homeLineup:Array(9).fill(batter.id),awayLineup:Array(9).fill(batter.id),homeOrder:0,awayOrder:0,homePitcher:pitcher.id,awayPitcher:pitcher.id,homeRuns:0,awayRuns:0,homeHits:0,awayHits:0,outs:0,bases:[null,null,null],playerStats:{},complete:false};
 return {save:{id:'input-season',team:'HH',season:2025,asOf:'fixture',revision:'fixture',game,teams:[{code:'HH',name:'한화',batters:[batter],pitchers:[pitcher]}]},action:{code:'input-match',role,mode:'ai',pace:'full',waiting:false,done:false,pitchCount:role==='batter'?1:0,balls:0,strikes:0,history:[],batter:batter.id,pitcher:pitcher.id,roster:{season:2025,asOf:'fixture',revision:'fixture',batter,pitcher},pitch:role==='batter'?{id:1,type:'fastball',velocity:146,releaseAt:time-300,flightMs:550,resolved:false}:null}};
}
function harness(role='batter'){
 const h=hooks(),commands=[],captures=[],calls=[],props={state:state(role),busy:false,clock:{current:0},appearances:{},command:body=>{commands.push(body);return Promise.resolve(props.state);}};
 const SeasonMatch=moduleLoader(h.api)('app/season-match.tsx').default;let tree=h.run(()=>SeasonMatch(props));
 const scene=()=>find(tree,n=>n.type===ActionScene).node;
 scene().props.onReady(true);tree=h.flush();
 const controls=scenePointerControls({side:()=>scene().props.side,aim:e=>{calls.push('aim');scene().props.aim.current={x:e.clientX/100,y:e.clientY/100};},swing:()=>{calls.push('swing');scene().props.onSwing();},chargeStart:()=>{calls.push('start');scene().props.onChargeStart();},chargeEnd:()=>{calls.push('end');scene().props.onChargeEnd();},chargeCancel:()=>{calls.push('cancel');scene().props.onChargeCancel();},focus(){},capture:id=>captures.push(id),release(){}});
 return {props,commands,calls,captures,controls,scene,get tree(){return tree;},render(){tree=h.flush();return tree;},dispose:()=>h.dispose()};
}

try{
 for(const pointerType of ['mouse','pen','touch']){
  const h=harness();try{
   h.controls.down(pointer({pointerType}));
   assert.equal(h.commands.length,pointerType==='touch'?0:1,'Mouse/pen swing on down; touch waits for tap acceptance');
   h.controls.up(pointer({pointerType,timeStamp:1120}));
   assert.equal(h.commands.length,1,`${pointerType}: one field input submits one swing`);
   assert.deepEqual(h.commands[0].aim,{x:.3,y:.4},'Field aim is updated before the actual swing command');
   assert.equal(h.commands[0].inputAt,time);assert.equal(h.commands[0].op,'swing');
   h.render();assert.equal(h.scene().props.localSwing.pitchId,1,'Field input starts the local swing animation');
   h.controls.down(pointer({pointerType,pointerId:2,timeStamp:1200}));h.controls.up(pointer({pointerType,pointerId:2,timeStamp:1300}));
   assert.equal(h.commands.length,1,'Repeated field taps cannot swing twice on one pitch');
  }finally{h.dispose();}
 }
 for(const gesture of ['scroll','cancel','second-finger','long-hold']){
  const h=harness();try{
   h.controls.down(pointer({pointerType:'touch'}));
   if(gesture==='scroll')h.controls.move(pointer({pointerType:'touch',clientY:100,timeStamp:1040}));
   if(gesture==='cancel')h.controls.cancel(pointer({pointerType:'touch',timeStamp:1040}));
   if(gesture==='second-finger')h.controls.down(pointer({pointerType:'touch',pointerId:2,isPrimary:false,timeStamp:1040}));
   h.controls.up(pointer({pointerType:'touch',timeStamp:gesture==='long-hold'?1800:1150}));
   assert.equal(h.commands.length,0,`${gesture}: no accidental swing reaches transport`);
   assert.deepEqual(h.captures,[],'Touch scrolling is never forcibly captured');
  }finally{h.dispose();}
 }
 for(const role of ['batter','pitcher']){
  const h=harness(role);try{
   const stage=find(h.tree,n=>hasClass(n,'season-stage'));
   assert(stage&&find(stage.node,n=>n.type===ActionScene),'Field scene remains inside its game stage');
   for(const tool of all(stage.node,n=>n.type==='button'&&(n.props['aria-label']?.includes('소리')||n.props['aria-label']==='전체화면'))){
    const entry=find(h.tree,n=>n===tool.node);
    bubble(entry,'onPointerDown');bubble(entry,'onPointerUp');bubble(entry,'onClick');
   }
   assert.equal(h.commands.length,0,'Sound/fullscreen overlay clicks never swing or pitch');
   assert.equal(h.scene().props.charging,false);
   if(role==='batter'){
    const pause=find(h.tree,n=>n.type==='button'&&n.props['aria-label']==='자동 투구 일시정지');assert(pause,'Automatic pitching has a field pause control');
    bubble(pause,'onPointerDown');bubble(pause,'onPointerUp');bubble(pause,'onClick');h.render();
    assert(find(h.tree,n=>n.type==='button'&&n.props['aria-label']==='자동 투구 재개'),'Pause click updates the automatic control');
    key('keydown',{tagName:'BUTTON'},'Space');key('keyup',{tagName:'BUTTON'},'Space');
    assert.equal(h.commands.length,0,'Pause/resume controls never leak a swing or charge into the field');
   }
   const group=find(stage.node,n=>n.props.role==='group'&&n.props['aria-label']==='구종 선택');
   if(role==='batter')assert(!group,'Batting screen does not expose opponent pitch-selection controls');
   else{
    assert(group,'Pitch selection is visible inside the field stage');
    const buttons=all(group.node,n=>n.type==='button');assert.equal(buttons.length,2,'Field menu uses the current pitcher arsenal');
    assert(buttons.every(b=>!b.node.props.disabled));
    const slider=find(h.tree,n=>n===buttons[1].node);
    bubble(slider,'onPointerDown');bubble(slider,'onPointerUp');bubble(slider,'onClick');
    assert.equal(h.commands.length,0,'Selecting a pitch changes selection without beginning a pitch');
    // Select and begin within the same event turn before React commits the render.
    h.scene().props.onChargeStart();time+=700;h.scene().props.onChargeEnd();
    assert.equal(h.commands.length,1);assert.equal(h.commands[0].op,'pitch');assert.equal(h.commands[0].type,'slider','The selected pitch ref is current even before a new render');
    h.render();const selected=all(h.tree,n=>n.type==='button'&&n.props['aria-pressed']===true);
    assert(selected.some(e=>entries(e.node).some(x=>x.node.type==='b'&&x.node.props.children==='슬라이더')),'The on-field menu marks the selected pitch');
   }
   for(const tagName of ['BUTTON','INPUT','SELECT','TEXTAREA']){const before=h.commands.length;key('keydown',{tagName});key('keyup',{tagName});assert.equal(h.commands.length,before,`${tagName}: Space cannot activate global swing or charge`);}
  }finally{h.dispose();}
 }
 for(const blocked of ['charging','busy','waiting','flying']){
  const h=harness('pitcher');try{
   if(blocked==='charging')h.scene().props.onChargeStart();
   if(blocked==='busy')h.props.busy=true;
   if(blocked==='waiting')h.props.state.action.waiting=true;
   if(blocked==='flying')h.props.state.action.pitch={id:1,type:'fastball',velocity:146,resolved:false,releaseAt:time,flightMs:500};
   h.render();const group=find(h.tree,n=>n.props.role==='group'&&n.props['aria-label']==='구종 선택');
   assert(group&&all(group.node,n=>n.type==='button').every(e=>e.node.props.disabled),`${blocked}: pitch changes are locked`);
  }finally{h.dispose();}
 }
 for(const cancellation of ['release','pointercancel','lostpointercapture']){
  const h=harness('pitcher');try{
   const entry=find(h.tree,n=>n.type==='button'&&n.props['aria-label']==='누르고 놓아 투구');
   assert(entry,'Pitch-release button stays inside the field');
   bubble(entry,'onPointerDown',pointer({pointerType:'touch'}));h.render();assert.equal(h.scene().props.charging,true);
   bubble(entry,'onPointerUp',pointer({pointerType:'touch',pointerId:2,isPrimary:false}));
   assert.equal(h.commands.length,0,'Another finger cannot release the active pitch');
   if(cancellation==='release'){time+=700;bubble(entry,'onPointerUp',pointer({pointerType:'touch'}));assert.equal(h.commands.length,1);assert.equal(h.commands[0].op,'pitch');}
   else{bubble(entry,cancellation==='pointercancel'?'onPointerCancel':'onLostPointerCapture',pointer({pointerType:'touch'}));bubble(entry,'onPointerUp',pointer({pointerType:'touch'}));assert.equal(h.commands.length,0,`${cancellation}: captured control cancellation does not pitch`);}
   h.render();assert.equal(h.scene().props.charging,false,'Release and cancellation both clear the visible meter');
  }finally{h.dispose();}
 }
 {
  const h=harness('pitcher');try{
   const entry=find(h.tree,n=>n.type==='button'&&n.props['aria-label']==='누르고 놓아 투구');
   bubble(entry,'onKeyDown',pointer({code:'Enter',repeat:false}));h.render();
   assert.equal(h.scene().props.charging,true,'Enter starts the keyboard pitch charge');
   entry.node.props.onBlur();h.render();
   assert.equal(h.scene().props.charging,false,'Leaving the pitch button with Tab cancels its keyboard charge');
   time+=700;bubble(entry,'onKeyUp',pointer({code:'Enter'}));
   assert.equal(h.commands.length,0,'Key release after focus loss cannot send an unintended pitch');
   bubble(entry,'onKeyDown',pointer({code:'Enter',repeat:false}));time+=700;
   bubble(entry,'onKeyUp',pointer({code:'Enter'}));
   assert.equal(h.commands.length,1,'A new deliberate Enter press still pitches after blur cancellation');
  }finally{h.dispose();}
 }
 for(const role of ['batter','pitcher']){
  const h=harness(role);try{
   const callbacks=h.scene().props;if(role==='pitcher')callbacks.onChargeStart();
   h.props.active=false;h.render();
   assert(!find(h.tree,n=>n.type===ActionScene),'Hiding the record-room iframe disposes the full-game scene');
   callbacks.onSwing();callbacks.onChargeEnd();key('keydown',{tagName:'BODY'});key('keyup',{tagName:'BODY'});
   assert.equal(h.commands.length,0,'A hidden iframe cannot swing or release a pending charge');
   h.props.active=true;h.render();assert.equal(h.scene().props.charging,false,'Returning starts without a stuck charge');
  }finally{h.dispose();}
 }
 {
  const {observeGameVisibility}=load('lib/game-visibility.ts'),previousLocation=globalThis.location,updates=[],messages=[];
  globalThis.location={origin:'https://game.test'};
  globalThis.window.parent={postMessage:(message,origin)=>messages.push({message,origin})};
  const stop=observeGameVisibility(value=>updates.push(value));
  const emit=data=>{for(const listener of windowListeners.get('message')??[])listener({origin:'https://game.test',source:window.parent,data});};
  try{
   assert.equal(updates.at(-1),true);assert.equal(messages[0].message.type,'diamond:ready','New scenes request shell visibility if the load event already fired');
   emit({type:'saber:visibility',active:false});assert.equal(updates.at(-1),false);
   document.hidden=true;for(const listener of documentListeners.get('visibilitychange')??[])listener();
   document.hidden=false;for(const listener of documentListeners.get('visibilitychange')??[])listener();
   assert.equal(updates.at(-1),false,'Changing browser tabs does not resume a game hidden by the record-room shell');
   const count=updates.length;for(const listener of windowListeners.get('message')??[])listener({origin:'https://other.test',source:window.parent,data:{type:'saber:visibility',active:true}});
   assert.equal(updates.length,count,'Messages from other origins are ignored');
   emit({type:'saber:visibility',active:true});assert.equal(updates.at(-1),true);
  }finally{stop();delete window.parent;globalThis.location=previousLocation;document.hidden=false;}
 }
 const css=fs.readFileSync('app/field-controls.css','utf8');
 const declarations=selector=>[...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].filter(m=>m[1].split(',').map(x=>x.trim()).includes(selector)).map(m=>m[2]).join(';');
 assert.match(declarations('.season-stage .season-field-inputs'),/pointer-events\s*:\s*none/,'The control wrapper keeps noninteractive space out of pointer routing');
 assert.match(declarations('.season-stage .season-field-inputs'),/z-index\s*:\s*(1[2-9]|[2-9]\d)/,'On-field controls paint above the WebGL surface and passive HUD');
 assert.match(css,/pointer-events\s*:\s*auto/,'Only interactive islands restore pointer hit testing');
 console.log('PASS field scene input: click/pen/tap swing, shared gesture cancellation, frozen aim, overlay isolation, in-field arsenal, immediate selection, pitch locks and keyboard focus/blur cancellation');
}finally{Object.assign(globalThis,real);}
