import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
const web=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..'),native=createRequire(path.join(web,'package.json')),ts=native('typescript');
const file=path.join(web,'lib/blink-timing.ts'),source=fs.readFileSync(file,'utf8'),mod={exports:{}};
const forbidden=()=>{throw Error('Time helper touched an external clock/random/timer');};
const safeMath=Object.create(Math);safeMath.random=forbidden;
const sandbox={module:mod,exports:mod.exports,Math:safeMath,Date:class{constructor(){forbidden();}static now=forbidden;},setTimeout:forbidden,setInterval:forbidden};
vm.runInNewContext(ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText,sandbox);
const {samplePlayerBlink:sample}=mod.exports;let checks=0;
const ok=(value,message)=>{checks++;assert(value,message);},near=(a,b,tol=1e-7,message='Values agree')=>ok(Math.abs(a-b)<=tol,`${message}: ${a} / ${b}`);
function schedule(bucket,seed){
 // Locate the visible interval through the public sampler, not a duplicated
 // seed hash or copy of its curve. Each blink is shorter than the 6s bucket.
 const base=bucket*6000;let found=null;
 for(let offset=0;offset<=2200;offset+=25)if(sample(base+offset,seed)>0){found=base+offset;break;}
 ok(found!==null,`A scheduled blink exists in bucket ${bucket}`);
 const quantum=Math.max(1e-8,Math.abs(base)*Number.EPSILON*2);let a=found-25,b=found;
 // Refine the first boundary to adjacent doubles: the last zero is the exact
 // scheduled start, allowing half-open-window tests without copying the hash.
 while(true){const m=(a+b)/2;if(m===a||m===b)break;if(sample(m,seed)>0)b=m;else a=m;}
 const start=a;a=found;b=base+2200;
 while(b-a>quantum){const m=(a+b)/2;if(m===a||m===b)break;if(sample(m,seed)>0)a=m;else b=m;}
 const end=(a+b)/2;return{start,end,tolerance:Math.max(1e-5,quantum*4)};
}
const schedules=[];
for(const seed of['player-17','player-29',17,'17']){
 let prior=null;
 for(let bucket=-12;bucket<=12;bucket++){
  const s=schedule(bucket,seed);schedules.push({seed,bucket,...s});near(s.end-s.start,190,s.tolerance,'Blink keeps its complete duration');
  ok(s.start>=bucket*6000-s.tolerance&&s.start<bucket*6000+2000,'Jitter stays within first two seconds');
  if(prior!==null)ok(s.start-prior>=4000-s.tolerance&&s.start-prior<=8000+s.tolerance,'Adjacent starts remain 4–8 seconds apart');prior=s.start;
  near(sample(s.start,seed),0,s.tolerance);near(sample(s.start+55,seed),1,s.tolerance);near(sample(s.start+70,seed),1,s.tolerance);near(sample(s.end,seed),0,s.tolerance);
  near(sample(s.start+27.5,seed),.5,s.tolerance,'Close midpoint');near(sample(s.start+130,seed),.5,s.tolerance,'Open midpoint');
  let previous=0;for(let age=0;age<=55;age+=5){const v=sample(s.start+age,seed);ok(v>=previous-1e-7&&v>=0&&v<=1,'Closing is bounded and monotonic');previous=v;}
  previous=1;for(let age=70;age<=195;age+=5){const v=sample(s.start+age,seed);ok(v<=previous+1e-7&&v>=0&&v<=1,'Opening is bounded and monotonic');previous=v;}
 }
}
const rhythm=seed=>schedules.filter(s=>s.seed===seed).map(s=>s.start-s.bucket*6000);
ok(JSON.stringify(rhythm('player-17'))!==JSON.stringify(rhythm('player-29')),'Player IDs produce different rhythms');
ok(JSON.stringify(rhythm(17))!==JSON.stringify(rhythm('17')),'Numeric and string IDs are stable but distinct');
const seed='test-boundaries',s=schedule(2,seed),start=s.start;
for(const windows of[[{start:start+25,end:start+26}],[{start:start-100,end:start+1}],[{start:start+189,end:start+500}],[{start:-Infinity,end:Infinity}]]){
 const immutable=Object.freeze(windows.map(w=>Object.freeze(w)));
 for(const age of[1,20,55,65,100,189])near(sample(start+age,seed,immutable),0,0,'Any planned overlap skips whole blink');
}
for(const windows of[[{start:start-100,end:start}],[{start:start+190,end:start+300}],[{start:1,end:1}],[{start:2,end:1}],[{start:NaN,end:Infinity}]])near(sample(start+55,seed,windows),1,1e-7,'Exactly adjacent/empty/invalid windows do not cancel');
near(sample(start+55,seed,[{start:start+189.999,end:start+300}]),0,0,'Even a 1-microsecond tail overlap cancels the whole blink');
near(sample(start+55,seed,[{start:start-100,end:start+.001}]),0,0,'Even a 1-microsecond leading overlap cancels the whole blink');
for(const age of[5,27,55,64,100,175,189]){
 const event=start+age,ownWindow=Object.freeze([{start:event,end:event+1000}]),value=sample(event,seed),duration=Math.min(35,190-age);
 near(sample(event,seed,ownWindow,event),value,1e-8,'Own protection cannot erase the interruption starting value');
 near(sample(event-.001,seed,ownWindow,event),sample(event-.001,seed),1e-8,'Backward seek restores pre-event history');
 near(sample(event+duration*.5,seed,ownWindow,event),value*.5,1e-6,'Interrupted opening has a smooth midpoint');
 near(sample(event+35.001,seed,ownWindow,event),0,0,'Interrupted eye is open after at most 35ms');
 let previous=value;for(let dt=0;dt<=36;dt+=.5){const v=sample(event+dt,seed,ownWindow,event);ok(v>=0&&v<=previous+1e-8,'Unexpected interruption never closes further');previous=v;}
 const planned=[...ownWindow,{start:start+10,end:start+11}];near(sample(event,seed,planned,event),0,0,'Independent planned protection still skips the entire blink');
 for(const hz of[30,60,144]){
  const forward=[];for(let t=start-100;t<start+300;t+=1000/hz)forward.push([t,sample(t,seed,ownWindow,event)]);
  for(const [t,value]of forward.reverse())near(sample(t,seed,ownWindow,event),value,0,`Cold/reverse seek is exact at ${hz}Hz`);
 }
}
const interruption=start+27,next=schedule(3,seed),longOwn=[{start:interruption,end:next.end+100}];near(sample(next.start+55,seed,longOwn,interruption),0,0,'The new activity protection applies normally to later blinks');
for(const time of[-1.7e12,1.7e12,-8.64e15,8.64e15,-Number.MAX_SAFE_INTEGER,Number.MAX_SAFE_INTEGER]){const v=sample(time,seed);ok(Number.isFinite(v)&&v>=0&&v<=1,'Large and negative clocks are bounded');near(sample(time,seed),v,0,'Large-clock cold seeks repeat exactly');}
for(const bucket of[-283333334,283333333]){const q=schedule(bucket,seed);near(sample(q.start+55,seed),1,q.tolerance,'Epoch-scale clock still reaches closed');near(q.end-q.start,190,q.tolerance,'Epoch-scale duration is maintained');}
for(const time of[NaN,Infinity,-Infinity,Number.MAX_SAFE_INTEGER*2])near(sample(time,seed),0,0,'Unsupported clocks return open');
for(const hz of[30,60,144]){const forward=[];for(let t=-6500;t<=12500;t+=1000/hz)forward.push([t,sample(t,seed)]);for(const [t,value]of forward.reverse())near(sample(t,seed),value,0,`Normal sampling is frame-rate independent at ${hz}Hz`);}
const result={checks,scheduleCount:schedules.length,frameRates:[30,60,144],durationMs:190,maximumReopenMs:35,externalStateAccess:false};
console.log('PASS blink timing '+JSON.stringify(result));
