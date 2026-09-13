import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
import * as THREE from 'three';
import {load} from './load-ts.mjs';

const {koreaTimeOfDay,millisecondsUntilKoreaTimeChange,watchKoreaTimeOfDay}=load('lib/korea-daylight.ts');
const HOUR=3_600_000;
function withThree(file,three){
 const native=createRequire(path.resolve(file)),mod={exports:{}};
 const require=id=>id==='three'?three:id==='./korea-daylight'?{koreaTimeOfDay,millisecondsUntilKoreaTimeChange,watchKoreaTimeOfDay}:native(id);
 const code=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
 new Function('require','module','exports',code)(require,mod,mod.exports);return mod.exports;
}
function checkStadium(){
 const originalDocument=globalThis.document;
 const frames=[];let frame=[];
 const context={fillRect(){frame=[];frames.push(frame);},fillText(text){frame.push(text);}};
 globalThis.document={createElement(tag){assert.equal(tag,'canvas');return{width:0,height:0,getContext:()=>context};}};
 const {createStadiumWorld}=withThree('lib/stadium-world.ts',{...THREE,TextureLoader:class{load(url){const texture=new THREE.Texture();texture.name=url;return texture;}}});
 try{for(const compact of[true,false]){
  const scene=new THREE.Scene(),previousBackground=new THREE.Color('#135724'),previousFog=new THREE.Fog('#234567',100,700);scene.background=previousBackground;scene.fog=previousFog;
  const world=createStadiumWorld(scene,{compact,timeOfDay:'day'}),daySky=scene.background,fog=scene.fog;
  assert(daySky.isDataTexture,'Daylight has an actual sky image with clouds');assert.equal(daySky.mapping,THREE.EquirectangularReflectionMapping);
  const nightMeshes=['LED floodlight banks','Night sky stars','Distant city windows'].map(name=>{const node=world.root.getObjectByName(name);assert(node,name);return node;});
  assert(nightMeshes.every(node=>!node.visible),'Day sky hides stars and artificial window/lamp lights');
  const objects=[],resources=new Set([daySky]);
  world.root.traverse(node=>{objects.push(node);if(node.geometry)resources.add(node.geometry);for(const material of Array.isArray(node.material)?node.material:node.material?[node.material]:[]){resources.add(material);for(const value of Object.values(material))if(value?.isTexture)resources.add(value);}});
  const disposals=new Map([...resources].map(resource=>[resource,0]));for(const resource of resources)resource.addEventListener('dispose',()=>disposals.set(resource,disposals.get(resource)+1));
  const score={home:'LG',away:'두산',homeScore:7,awayScore:3,inning:8,half:'TOP'};world.setScoreboard(score);
  assert(frame.includes('DIAMOND  /  DAY GAME')&&frame.includes('7')&&frame.includes('3'));
  score.homeScore=99;world.setTimeOfDay('night');const nightSky=scene.background;
  assert(nightSky.isColor&&nightSky!==daySky);assert.equal(scene.fog,fog);assert(nightMeshes.every(node=>node.visible));
  assert(frame.includes('DIAMOND  /  NIGHT GAME')&&frame.includes('7')&&!frame.includes('99'),'A phase change redraws the existing score snapshot');
  const count=frames.length;world.setTimeOfDay('night');assert.equal(frames.length,count,'Repeated phase notifications do not redraw unchanged score textures');
  for(let i=0;i<8;i++){world.setTimeOfDay('day');assert.equal(scene.background,daySky);world.setTimeOfDay('night');assert.equal(scene.background,nightSky);}
  const after=[];world.root.traverse(node=>after.push(node));assert.deepEqual(after,objects,'Changing daylight never replaces geometry or player/field parents');
  assert([...disposals.values()].every(n=>n===0),'Live phase changes never dispose resources');
  world.setScoreboard({home:'LG',away:'두산',homeScore:7,awayScore:3,hidden:true});world.setTimeOfDay('day');
  assert(frame.includes('DIAMOND  /  DAY GAME')&&frame.includes('PLAY IN PROGRESS')&&frame.filter(text=>text==='—').length===2,'Day/night changes preserve unrevealed result masking');
  assert(!frame.includes('7')&&!frame.includes('3'));
  const externalBackground=new THREE.Color('#ffeedd'),externalFog=new THREE.Fog('#ccddff',40,600);
  if(!compact){scene.background=externalBackground;scene.fog=externalFog;}
  const finalFrame=frames.length;world.dispose();world.dispose();world.setTimeOfDay('night');world.setScoreboard({homeScore:10});
  assert.equal(frames.length,finalFrame,'Disposed worlds ignore later timer and scoreboard callbacks');
  assert.equal(scene.background,compact?previousBackground:externalBackground);assert.equal(scene.fog,compact?previousFog:externalFog);
  assert(!scene.children.includes(world.root));assert([...disposals.values()].every(n=>n===1),'Sky, geometry, materials and texture lifetime ends exactly once');
 }}finally{if(originalDocument===undefined)delete globalThis.document;else globalThis.document=originalDocument;}

 const generated=[];let generatorDisposed=0;
 const {stadiumLighting}=withThree('lib/scene-lighting.ts',{...THREE,PMREMGenerator:class{
  fromScene(scene){const target={texture:new THREE.Texture(),disposed:0,dispose(){this.disposed++;this.texture.dispose();}};generated.push(target);return target;}
  dispose(){generatorDisposed++;}
 }});
 for(const compact of[true,false]){
  const scene=new THREE.Scene(),renderer={shadowMap:{}},start=generated.length;
  const lighting=stadiumLighting(renderer,scene,compact,'day'),dayEnvironment=scene.environment;
  assert.equal(generated.length-start,2,'Both reflection environments are created exactly once');
  const children=[...scene.children],key=scene.getObjectByName('Stadium primary light'),sky=scene.getObjectByName('Stadium ambient sky');
  assert(key&&sky);assert.equal(key.shadow.mapSize.x,compact?1024:2048);assert(key.position.y>60&&sky.intensity>1,'Day light comes from a high sun and bright ambient sky');
  const dayIntensity=sky.intensity,dayExposure=renderer.toneMappingExposure;lighting.focus('pitcher');const focus=key.target.position.clone();
  lighting.setTimeOfDay('night');const nightEnvironment=scene.environment;
  assert.notEqual(dayEnvironment,nightEnvironment);assert(key.position.y<50&&sky.intensity<dayIntensity);
  assert.notEqual(renderer.toneMappingExposure,dayExposure);assert(key.target.position.equals(focus),'Dusk does not move the shadow focus away from the active pitcher');
  for(let i=0;i<8;i++){lighting.setTimeOfDay('day');assert.equal(scene.environment,dayEnvironment);lighting.setTimeOfDay('night');assert.equal(scene.environment,nightEnvironment);}
  assert.deepEqual(scene.children,children);assert.equal(generated.length-start,2,'Repeated dawn/dusk never recomputes PMREM or adds lights');
  assert(generated.slice(start).every(target=>target.disposed===0));lighting.focus('batter');assert.equal(key.target.position.z,0);
  const externalEnvironment=new THREE.Texture();if(!compact)scene.environment=externalEnvironment;
  lighting.dispose();lighting.dispose();lighting.setTimeOfDay('day');
  assert(generated.slice(start).every(target=>target.disposed===1),'Both PMREM render targets are released once');
  assert.equal(scene.children.length,0);assert.equal(scene.environment,compact?null:externalEnvironment,'Cleanup never erases an environment now owned by another scene component');
 }
 assert.equal(generatorDisposed,2);
}
function checkPure(){
 const boundaries=[
  ['2026-09-12T20:59:59.999Z','night',1],
  ['2026-09-12T21:00:00.000Z','day',12*HOUR],
  ['2026-09-13T08:59:59.999Z','day',1],
  ['2026-09-13T09:00:00.000Z','night',12*HOUR],
  ['2026-09-13T14:59:59.999Z','night',6*HOUR+1],
  ['2026-09-13T15:00:00.000Z','night',6*HOUR],
  ['2026-12-31T21:00:00.000Z','day',12*HOUR],
  ['2024-02-28T21:00:00.000Z','day',12*HOUR],
  ['2024-02-29T09:00:00.000Z','night',12*HOUR],
  ['1969-12-31T20:59:59.999Z','night',1],
  ['1969-12-31T21:00:00.000Z','day',12*HOUR],
 ];
 for(const [iso,phase,delay] of boundaries){
  const at=Date.parse(iso);assert.equal(koreaTimeOfDay(at),phase,iso);assert.equal(millisecondsUntilKoreaTimeChange(at),delay,iso);
  assert.equal(koreaTimeOfDay(at+delay-1),phase,'The scheduled phase remains valid through the final millisecond');
  assert.notEqual(koreaTimeOfDay(at+delay),phase,'The exact boundary changes phase');
 }
 // Intl's explicit Asia/Seoul formatter is an independent reference. Include
 // the US DST change weeks to detect accidental host-local clock arithmetic.
 const koreanHour=new Intl.DateTimeFormat('en-GB',{timeZone:'Asia/Seoul',hour:'2-digit',hourCycle:'h23'});
 for(const day of ['2026-03-08','2026-09-13','2026-11-01','2024-02-29','2027-01-01'])for(let minute=0;minute<1440;minute+=17){
  const at=Date.parse(day+'T00:00:00Z')+minute*60_000+999,hour=Number(koreanHour.format(at));
  const expected=hour>=6&&hour<18?'day':'night',delay=millisecondsUntilKoreaTimeChange(at);
  assert.equal(koreaTimeOfDay(at),expected);assert(delay>0&&delay<=12*HOUR);
  assert.equal(koreaTimeOfDay(at+delay-1),expected);assert.notEqual(koreaTimeOfDay(at+delay),expected);
 }
 const original=Date.now;
 try{for(const iso of ['2026-09-12T21:00:00Z','2026-09-13T09:00:00Z']){
  Date.now=()=>Date.parse(iso);assert.equal(koreaTimeOfDay(),koreaTimeOfDay(Date.now()));assert.equal(millisecondsUntilKoreaTimeChange(),12*HOUR);
 }}finally{Date.now=original;}
 return new Date('2026-09-12T00:00:00Z').getHours();
}
if(process.argv.includes('--timezone-only')){
 console.log(JSON.stringify({localHour:checkPure()}));
}else{
 checkPure();
 const hostHours=new Set();
 for(const TZ of ['UTC','Asia/Seoul','America/Los_Angeles','Pacific/Auckland']){
  const run=spawnSync(process.execPath,[fileURLToPath(import.meta.url),'--timezone-only'],{env:{...process.env,TZ},encoding:'utf8'});
  assert.equal(run.status,0,`${TZ}: ${run.stderr||run.stdout}`);hostHours.add(JSON.parse(run.stdout).localHour);
 }
 assert(hostHours.size>=3,'Tests must actually run under distinct device-local time zones');

 const original={now:Date.now,setTimeout:globalThis.setTimeout,clearTimeout:globalThis.clearTimeout,document:globalThis.document,window:globalThis.window};
 const timers=new Map();let wall=Date.parse('2026-09-12T20:59:59.990Z'),monotonic=0,nextId=1;
 class Surface{
  listeners=new Map();hidden=false;
  addEventListener(type,callback){if(!this.listeners.has(type))this.listeners.set(type,new Set());this.listeners.get(type).add(callback);}
  removeEventListener(type,callback){this.listeners.get(type)?.delete(callback);}
  emit(type){for(const callback of [...(this.listeners.get(type)??[])])callback({type});}
  count(){return [...this.listeners.values()].reduce((sum,set)=>sum+set.size,0);}
 }
 const doc=new Surface(),win=new Surface();
 globalThis.document=doc;globalThis.window=win;Date.now=()=>wall;
 globalThis.setTimeout=(callback,delay)=>{assert(Number.isFinite(delay)&&delay>0&&delay<=60_000,'Every timer targets the boundary or the clock-change recheck');const id=nextId++;timers.set(id,{callback,due:monotonic+delay,delay});return id;};
 globalThis.clearTimeout=id=>timers.delete(id);
 const advance=ms=>{const end=monotonic+ms;let count=0;while(true){const entry=[...timers].filter(([,t])=>t.due<=end).sort((a,b)=>a[1].due-b[1].due)[0];if(!entry)break;assert(++count<1000,'No timer may busy-loop');const[id,t]=entry;wall+=t.due-monotonic;monotonic=t.due;timers.delete(id);t.callback();}wall+=end-monotonic;monotonic=end;};
 try{
  const changes=[],stop=watchKoreaTimeOfDay(phase=>changes.push(phase));
  assert.deepEqual(changes,['night'],'The first value is emitted synchronously');assert.equal(timers.size,1);assert.equal([...timers.values()][0].delay,10);
  advance(9);assert.deepEqual(changes,['night']);advance(1);assert.deepEqual(changes,['night','day']);assert.equal(timers.size,1);
  advance(180_000);assert.deepEqual(changes,['night','day'],'Minute checks never emit duplicate phase changes');
  doc.emit('visibilitychange');win.emit('focus');assert.equal(timers.size,1);assert.equal(changes.length,2,'Focus does not create duplicate timers or notifications');
  wall=Date.parse('2026-09-13T09:10:00Z');win.emit('focus');assert.equal(changes.at(-1),'night','Focus corrects a changed system wall clock immediately');
  wall=Date.parse('2026-09-13T00:00:00Z');doc.hidden=false;doc.emit('visibilitychange');assert.equal(changes.at(-1),'day','Restored visibility reevaluates a clock moved backwards');
  wall=Date.parse('2026-09-13T09:00:00Z');advance(60_000);assert.equal(changes.at(-1),'night','An unfocused clock change is detected within a minute');
  wall=Date.parse('2026-09-13T20:59:59.990Z');doc.emit('visibilitychange');assert.equal([...timers.values()][0].delay,10);advance(10);assert.equal(changes.at(-1),'day','The next Korean date resumes daylight at exactly 06:00');
  const stale=[...timers.values()][0].callback,emitted=changes.length;stop();stop();assert.equal(timers.size,0);assert.equal(doc.count(),0);assert.equal(win.count(),0);
  wall=Date.parse('2026-09-14T09:00:00Z');stale();doc.emit('visibilitychange');win.emit('focus');assert.equal(changes.length,emitted);assert.equal(timers.size,0,'Unmounted callbacks cannot recreate a timer');

  // Exercise the hook's actual effect cleanup without requiring a browser or
  // a second React renderer. The scheduler above remains the real watcher.
  let value,effect,updates=0;
  const mod={exports:{}},code=ts.transpileModule(fs.readFileSync('lib/use-korea-daylight.ts','utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
  const require=id=>id==='react'?{useState(initial){value=typeof initial==='function'?initial():initial;return[value,next=>{value=typeof next==='function'?next(value):next;updates++;}];},useEffect(fn,deps){assert.deepEqual(deps,[]);effect=fn;}}:id==='./korea-daylight'?{koreaTimeOfDay,watchKoreaTimeOfDay}:(()=>{throw new Error('Unexpected hook dependency: '+id);})();
  new Function('require','module','exports',code)(require,mod,mod.exports);
  assert.equal(mod.exports.useKoreaTimeOfDay(),'night');assert.equal(timers.size,0,'Rendering alone never creates a watcher');
  const cleanup=effect();assert.equal(timers.size,1);wall=Date.parse('2026-09-14T21:00:00Z');win.emit('focus');assert.equal(value,'day');assert(updates>=2);cleanup();assert.equal(timers.size,0);assert.equal(doc.count()+win.count(),0);
 }finally{
  Date.now=original.now;globalThis.setTimeout=original.setTimeout;globalThis.clearTimeout=original.clearTimeout;
  for(const key of ['document','window'])if(original[key]===undefined)delete globalThis[key];else globalThis[key]=original[key];
 }
 checkStadium();
 console.log('PASS Korean daylight: exact KST boundaries/rollovers and four host time zones; wall-clock/focus/visibility corrections and hook cleanup; day/night sky, hidden score preservation, geometry/PMREM reuse, shadow focus and one-time GPU disposal');
}
