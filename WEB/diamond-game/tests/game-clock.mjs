import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
const mod={exports:{}};
const code=ts.transpileModule(fs.readFileSync('lib/game-clock.ts','utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}}).outputText;
new Function('module','exports',code)(mod,mod.exports);
const {clockSample,createClockSync,localNow}=mod.exports;
const sample=(start,offset,processing=0,up=20,down=20)=>({clientStart:start,serverReceived:start+up+offset,serverSent:start+up+offset+processing,clientEnd:start+up+processing+down});
for(const offset of [-300000,0,500000])for(const processing of [0,400,1000]){
 const result=clockSample(sample(1000000,offset,processing));
 assert.equal(result.offset,offset);assert.equal(result.rtt,40);
}
const sync=createClockSync();assert.equal(sync.sample(sample(1000000,500000)),500000);
assert.equal(sync.sample(sample(1001000,500000,400,20,500)),500000,'Ignore an asymmetric slow request when a lower RTT sample exists');
assert.equal(sync.sample(sample(1022000,500030)),500030,'Refresh expired samples');
assert.equal(clockSample({clientStart:0,clientEnd:1,serverReceived:0,serverSent:100}),null);
assert.equal(clockSample(sample(0,NaN)),null);
const before=localNow(),original=Date.now;
try{Date.now=()=>1;assert(localNow()>=before,'Wall-clock adjustments must not move the local game clock backwards');}finally{Date.now=original;}
console.log('PASS clock offsets, processing-delay isolation, low RTT selection, sample expiry and monotonic timing');
