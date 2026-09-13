import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
import {hitPresentationCases,practiceHitCases} from './hit-result-fixtures.mjs';

// Render the production practice result components with the production fielding clock.
const file=path.resolve('app/page.tsx'),source=fs.readFileSync(file,'utf8'),native=createRequire(file),mod={exports:{}};
const require=id=>{
 if(id==='./action-scene')return {__esModule:true,default:()=>null};
 if(id.endsWith('.css'))return {};
 if(id.startsWith('.')){const target=path.resolve(path.dirname(file),id+'.ts');if(fs.existsSync(target))return load(target);}
 return native(id);
};
const js=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
new Function('require','module','exports',js)(require,mod,mod.exports);
const {practiceResultPresentation:present,PracticeScoreboard,PracticeResultOverlay,PracticeLastPitch}=mod.exports;
const {pitchResultRevealAt,pitchPresentationEnd}=load('lib/pitch-cycle.ts');
const {fieldPlayTimeline}=load('lib/field-play-timeline.ts');
const {SWING_DURATION_MS,SWING_CONTACT_MS}=load('lib/player-motion.ts');
const at=10600,base={code:'DABCDEFG',round:2,balls:3,strikes:2,score:1,done:false,winner:null,mode:'ai',role:'batter',pitchCount:4,
 pitch:{id:4,releaseAt:10000,flightMs:650,resolved:false},history:[]};
const snapshot=present(base,10000,null).score;
const result=(overrides={})=>({id:4,at,kind:'out',outcome:'OUT',label:'뜬공 아웃',trajectory:'fly',exitSpeed:125,launchAngle:36,direction:.2,distance:78,
 timing:5,aimError:.04,swingAt:at,contact:{at,position:{x:0,y:1.05,z:0}},...overrides});
const resolved=(reaction,overrides={})=>({...base,round:3,balls:0,strikes:0,score:3,pitch:{...base.pitch,resolved:true,reaction},history:[reaction],...overrides});
const draw=presentation=>renderToStaticMarkup(React.createElement(React.Fragment,null,
 React.createElement(PracticeScoreboard,{inGame:true,mode:'ai',presentation}),
 React.createElement(PracticeResultOverlay,{presentation,side:'batter',onReset(){}}),
 React.createElement(PracticeLastPitch,{presentation})));
const unchanged=JSON.stringify(snapshot);

for(const reaction of[
 result(),result({trajectory:'ground',label:'땅볼 아웃',launchAngle:3}),
 result({kind:'hit',outcome:'2B',label:'2루타',trajectory:'fly'}),
 result({kind:'hit',outcome:'HR',label:'홈런',exitSpeed:175,launchAngle:33,distance:145}),
 result({kind:'foul',outcome:'FOUL',label:'파울',trajectory:'foul'}),
]){
 const view=resolved(reaction),reveal=pitchResultRevealAt(view.pitch),end=pitchPresentationEnd(view.pitch),timeline=fieldPlayTimeline(reaction);
 assert.equal(reveal,timeline.completeAt,'Practice results share the actual fielding timeline');
 assert(reveal>reaction.contact.at);
 for(const now of[9000,at-1,at,at+100,reveal-1]){
  const value=present(view,now,snapshot),html=draw(value);
  assert(value.pending);assert.equal(value.score,snapshot,'Counters retain the last publicly visible snapshot');assert.equal(value.last,undefined);
  assert(!html.includes(reaction.label),'A predicted result must not appear in either result UI');
  assert(!html.includes('비거리'));assert(!html.includes('타구 속도'));assert(!html.includes('FINAL RESULT'));
  assert(html.includes('3볼 2스트라이크'),'The count must not reset before the visible out/hit');
  assert(html.includes('<strong>1<small> / 4</small>'),'The score must not increment before fielding finishes');
  assert(html.includes('<strong>3<small> / 6 타석</small>'),'The plate appearance must not advance early');
  assert(html.includes(now<at?'투구 진행 중':'수비 플레이 중'));
  assert.equal(value.defending,now>=at,'Do not announce a defensive play before contact');
 }
 const shown=present(view,reveal,snapshot),html=draw(shown);
 assert(!shown.pending);assert.equal(shown.last,reaction);assert.equal(shown.score.score,3);assert.equal(shown.score.round,3);
 assert(html.includes(reaction.label));assert(html.includes('0볼 0스트라이크'));assert(html.includes('hit-result'));
 assert(present(view,end-1,snapshot).showResult,'The result retains its reading window');
 assert(!present(view,end,snapshot).showResult,'The overlay ends at the next-pitch boundary');
 assert.equal(present(view,end,snapshot).last,reaction,'The last-pitch row remains after the brief overlay');
}

// Final win/loss uses the same gate, including a restore with no pre-play snapshot.
const final=resolved(result(),{round:6,score:5,done:true,winner:'batter'}),reveal=pitchResultRevealAt(final.pitch);
for(const previous of[snapshot,null,{...snapshot,code:'OTHERROOM'}]){
 const hidden=present(final,reveal-1,previous),html=draw(hidden);
 assert(!hidden.score?.done);assert(!html.includes('YOU WIN'));assert(!html.includes('YOU LOSE'));assert(!html.includes('승리'));
 if(previous!==snapshot){assert.equal(hidden.score,null);assert(html.includes('볼카운트 확인 중'));assert(html.includes('<strong>—<small> / 4</small>'));}
}
const finish=draw(present(final,reveal,snapshot));assert(finish.includes('YOU WIN'));assert(finish.includes('6타석 · 5점'));

// Current evaluator predictions use at=the early evaluation instant, while
// Contact.At is the later physical swing. An early `result.at` must not make a
// predicted 1B/2B/HR public, either before the server reply or after it arrives.
for(const {pitch,reaction} of practiceHitCases(at)){
 assert(reaction.at<reaction.contact.at);
 const unresolved={...base,pitch:{...pitch,resolved:false}};
 const noServerResult=present(unresolved,reaction.contact.at,snapshot);
 assert(!noServerResult.last&&!noServerResult.showResult,'The scene-only local prediction cannot produce a result caption');
 const view=resolved(reaction,{pitch:{...pitch,resolved:true,reaction}}),complete=fieldPlayTimeline(reaction).completeAt;
 assert.equal(pitchResultRevealAt(view.pitch),complete);
 for(const phase of[reaction.at,reaction.contact.at-1,reaction.contact.at,complete-1]){
  const value=present(view,phase,snapshot);
  assert(value.pending&&!value.last&&!value.showResult,'The authoritative copy of a prediction still waits for its field play');
  assert(!draw(value).includes(reaction.label));
 }
}

let hitChecks=0;
for(const {origin,reaction} of hitPresentationCases(at))for(const mode of['ai','pvp'])for(const done of[false,true]){
 const view=resolved(reaction,{mode,done,round:done?6:3,score:5,winner:done?'batter':null});
 const timeline=fieldPlayTimeline(reaction),bases={'1B':1,'2B':2,'3B':3,HR:4}[reaction.outcome];
 const runnerAt=reaction.contact.at+SWING_DURATION_MS-SWING_CONTACT_MS+bases*27.432/7.4*1000;
 const stages=[reaction.contact.at-1,reaction.contact.at,timeline.fieldedAt-1,timeline.fieldedAt,runnerAt-1,timeline.completeAt-1];
 if(timeline.throwAt!==undefined)stages.push(timeline.throwAt,timeline.throwArrivesAt-1,timeline.throwArrivesAt);
 for(const previous of[snapshot,null])for(const time of stages.filter(t=>t<timeline.completeAt)){
  const value=present(view,time,previous),html=draw(value);
  assert(value.pending&&!value.last&&!value.showResult,`${origin}: ${mode} must hold every hit stage`);
  assert.equal(value.score,previous,'A hit preserves the entire old scoreboard, or masks unknown cold-restore counters');
  assert(!html.includes(reaction.label)&&!html.includes('FINAL RESULT')&&!html.includes('YOU WIN'));
  assert(!html.includes('비거리')&&!html.includes('타구 속도'),'Secondary result metrics must not spoil the hit either');
  assert.equal(value.defending,time>=reaction.contact.at);
 }
 const shown=present(view,timeline.completeAt,snapshot),html=draw(shown);
 assert(!shown.pending&&shown.last===reaction&&shown.showResult);
 assert(html.includes(reaction.label),'The permanent last-pitch row reveals all hit types at completion');
 assert.equal(html.includes('FINAL RESULT'),done,'The final practice card shares the hit gate');
 assert.equal(shown.score.score,5);assert.equal(shown.score.round,done?6:3);
 assert(!present(view,timeline.completeAt+60000,null).pending,'Reloading an old hit must not replay its delay');
 hitChecks++;
}

// Predicted takes and hit-by-pitches cannot disclose a result before the ball arrives.
for(const reaction of[
 result({contact:undefined,bodyHit:undefined,kind:'strike',outcome:'STRIKE',label:'스트라이크',trajectory:undefined}),
 result({contact:undefined,bodyHit:undefined,kind:'ball',outcome:'BALL',label:'볼',trajectory:undefined}),
 result({contact:undefined,bodyHit:{at:10510,position:{x:.5,y:1,z:0}},kind:'walk',outcome:'HBP',label:'사구',trajectory:undefined}),
]){
 const view=resolved(reaction),reveal=pitchResultRevealAt(view.pitch);
 assert.equal(present(view,reveal-1,snapshot).last,undefined);
 assert(!present(view,reveal-1,snapshot).defending);
 assert.equal(present(view,reveal,snapshot).last,reaction);
}
assert.equal(JSON.stringify(snapshot),unchanged,'Presentation must not mutate the held state');
assert.equal(present(null,20000,snapshot).score,null,'Reset clears the displayed scoreboard');
assert.match(source,/<ActionScene view=\{game\} hideScore=\{presentation\.pending\}/,'The scene keeps its raw reaction while its scoreboard hides early results');
assert.match(source,/canPitch=!!game&&!game\.done/,'Delayed winner display must never unlock a completed game');
console.log(`PASS practice result timing: ${hitChecks} AI/PvP actual-server/evaluator/synthetic-contract hit scenarios, early local prediction vs server result, pickup/return/base-running gates, final winner, cold restore, counters and 3D scoreboard`);
