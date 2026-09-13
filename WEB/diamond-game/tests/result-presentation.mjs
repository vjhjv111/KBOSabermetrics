import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
import {hitPresentationCases} from './hit-result-fixtures.mjs';

const cycle=load('lib/pitch-cycle.ts'),field=load('lib/field-play.ts');
const {fieldPlayTimeline,fieldBallPosition}=load('lib/field-play-timeline.ts');
const {selectSeasonPresentation,seasonPitchKey}=load('lib/season-presentation.ts');
const {SWING_DURATION_MS,SWING_CONTACT_MS}=load('lib/player-motion.ts');
const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);
const at=100000;
function result(trajectory='fly',outcome='OUT',overrides={}){
 return {id:1,label:'결과 전용 문구',kind:outcome==='OUT'?'out':outcome==='FOUL'?'foul':'hit',outcome,trajectory,
  at:at+60,timing:0,aimError:0,quality:.8,distance:85,exitSpeed:145,launchAngle:trajectory==='ground'?5:trajectory==='line'?18:35,direction:.3,
  points:0,plateEnded:outcome!=='FOUL',swingAt:at,swingAim:{x:0,y:0},contact:{at,position:{x:0,y:1.05,z:0}},...overrides};
}
const pitch=reaction=>({id:1,type:'fastball',velocity:145,releaseAt:at-600,flightMs:600,target:{x:0,y:0},resolved:true,reaction});
const hitCases=hitPresentationCases(at);
function hitStages(reaction){
 const timeline=fieldPlayTimeline(reaction),contact= reaction.contact.at;
 const bases={ '1B':1,'2B':2,'3B':3,HR:4 }[reaction.outcome];
 const runnerAt=contact+SWING_DURATION_MS-SWING_CONTACT_MS+bases*27.432/7.4*1000;
 const times=[contact-1,contact,contact+100,timeline.fieldedAt-1,timeline.fieldedAt,runnerAt-1,timeline.completeAt-1];
 if(timeline.throwAt!==undefined)times.push(timeline.throwAt-1,timeline.throwAt,timeline.throwArrivesAt-1,timeline.throwArrivesAt);
 return {timeline,runnerAt,times:[...new Set(times)].filter(time=>time<timeline.completeAt)};
}
let checks=0;
for(const trajectory of ['fly','line','ground'])for(const outcome of ['OUT','1B','2B','3B'])for(const direction of [-.75,0,.75]){
 const r=result(trajectory,outcome,{direction}),p=pitch(r),timeline=fieldPlayTimeline(r);
 assert(timeline&&Number.isFinite(timeline.completeAt));
 assert(timeline.fieldedAt>=at+timeline.physicalFieldMs,'Fielding cannot precede the presented ball reaching its target');
 const arrived=field.fielderPosition(field.DEFENSIVE_SPOTS[timeline.fielder],timeline.fieldTarget,timeline.fieldedAt-at);
 assert(Math.hypot(arrived.x-timeline.fieldTarget.x,arrived.z-timeline.fieldTarget.z)<1e-8,'A result must not outrun the selected defender');
 assert.equal(cycle.pitchResultRevealAt(p),timeline.completeAt,'Cards and defense use one absolute completion instant');
 assert(cycle.pitchPresentationEnd(p)>=timeline.completeAt+1800,'The next pitch leaves time to read the result');
 assert.equal(fieldBallPosition(r,at-1,timeline),null);
 const beforeCatch=fieldBallPosition(r,timeline.fieldedAt-.001,timeline);
 assert(beforeCatch&&distance(beforeCatch,timeline.fieldTarget)<.001,'The visible ball reaches the glove/pickup point continuously');
 if(timeline.kind==='catch'){
  assert(timeline.fieldTarget.y>.065,'An aerial out must be caught above the ground');
  assert.equal(fieldBallPosition(r,timeline.fieldedAt,timeline),null,'A caught ball cannot continue falling through the glove');
  assert(timeline.completeAt>=timeline.fieldedAt,'An out card waits for the catch');
 }
 if(timeline.kind==='throw'){
  assert(timeline.throwAt>timeline.fieldedAt&&timeline.throwArrivesAt>timeline.throwAt);
  assert.equal(fieldBallPosition(r,timeline.throwAt-1,timeline),null,'The ball stays in the glove during the transfer');
  const transfer=fieldBallPosition(r,timeline.throwAt,timeline),first=fieldBallPosition(r,timeline.throwArrivesAt-.001,timeline);
  assert(transfer&&Math.hypot(transfer.x-timeline.fieldTarget.x,transfer.z-timeline.fieldTarget.z)<1e-8);
  assert(first&&distance(first,{...field.BASES[1],y:1.2})<.001,'The putout throw reaches first base before revealing OUT');
  assert(timeline.completeAt>=timeline.throwArrivesAt);
 }
 if(timeline.kind==='retrieve'){
  const bases=outcome==='3B'?3:outcome==='2B'?2:1;
  const runnerEnd=at+SWING_DURATION_MS-SWING_CONTACT_MS+bases*27.432/7.4*1000;
  assert(timeline.completeAt>=runnerEnd,'An awarded base is reached before the result is revealed');
 }
 assert.equal(fieldBallPosition(r,timeline.completeAt,timeline),null);
 assert.equal(cycle.pitchResultRevealAt(pitch({...r,at:at+60000})),timeline.completeAt,'A delayed server response cannot restart presentation time');
 checks++;
}
for(const [trajectory,outcome,kind] of [['fly','HR','home-run'],['foul','FOUL','foul']]){
 const r=result(trajectory,outcome),timeline=fieldPlayTimeline(r);
 assert.equal(timeline.kind,kind);assert.equal(timeline.fielder,-1,'HR/foul do not create a fair-ball putout');
 assert.equal(timeline.throwAt,undefined);assert.equal(fieldBallPosition(r,timeline.fieldedAt,timeline),null);
 if(outcome==='HR')assert(timeline.completeAt>=at+SWING_DURATION_MS-SWING_CONTACT_MS+4*27.432/7.4*1000);
}
for(const outcome of ['BALL','STRIKE','K','BB']){
 const r={...result(),kind:outcome==='BB'?'walk':outcome==='K'?'out':outcome==='BALL'?'ball':'strike',outcome,contact:undefined,trajectory:undefined,swingAt:at-400};
 assert.equal(cycle.pitchResultRevealAt(pitch(r)),at,`${outcome}: an early swing cannot reveal a taken pitch before the plate`);
}
const hbp={...result(),kind:'hbp',outcome:'HBP',contact:undefined,bodyHit:{at:at-25,position:{x:.8,y:1,z:-.1}}};
assert.equal(cycle.pitchResultRevealAt(pitch(hbp)),at-25,'HBP follows the actual body collision');
assert.equal(cycle.pitchResultRevealAt(null),0);

for(const {origin,reaction} of hitCases){
 const {timeline,runnerAt}=hitStages(reaction),rPitch=pitch(reaction);
 assert(reaction.contact,`${origin}: each actual hit carries contact even with zero distance/points`);
 assert.equal(cycle.pitchResultRevealAt(rPitch),timeline.completeAt);
 assert(timeline.completeAt>=runnerAt,'A hit caption waits for the awarded bases, including the home-run lap');
 if(reaction.outcome!=='HR'){
  assert.equal(timeline.kind,'retrieve');
  assert.equal(timeline.receiveBase,reaction.outcome==='3B'?3:2);
  assert(timeline.throwAt>timeline.fieldedAt&&timeline.throwArrivesAt>timeline.throwAt,'Hits include a visible pickup and infield return');
  assert(timeline.completeAt>timeline.throwArrivesAt,'The result waits until the return throw is received');
  assert.equal(fieldBallPosition(reaction,timeline.throwAt-1,timeline),null,'The return starts after the fielder transfers the ball');
  const middle=fieldBallPosition(reaction,(timeline.throwAt+timeline.throwArrivesAt)/2,timeline);
  assert(middle&&Number.isFinite(middle.y),'The hit return remains visible during its travel');
  const received=fieldBallPosition(reaction,timeline.throwArrivesAt-.001,timeline);
  assert(received&&distance(received,timeline.throwTarget)<.001,'The return ball reaches its designated base');
  assert.equal(fieldBallPosition(reaction,timeline.throwArrivesAt,timeline),null,'The receiver holds the returned ball');
 }
 assert.equal(fieldBallPosition(reaction,timeline.completeAt,timeline),null);
 assert.equal(cycle.pitchResultRevealAt(pitch({...reaction,at:at-86400000})),timeline.completeAt,'Result creation time cannot bypass live hit presentation');
 checks++;
}

const batter={id:'2025:present-b',name:'화면 타자',team:'OB',profile:{bats:'R',throws:'R'}};
const pitcher={id:'2025:present-p',name:'화면 투수',team:'LG',profile:{bats:'R',throws:'R'},arsenal:[{type:'fastball',velocity:145,usage:100}]};
function state(mode='ai'){
 const game={id:'game-a',inning:8,half:'top',homeTeam:'LG',awayTeam:'OB',homeRuns:5,awayRuns:4,homeHits:7,awayHits:6,outs:2,bases:[null,{playerId:batter.id},null],
  homeLine:[0,1,0,1,0,2,1,0],awayLine:[0,1,0,1,0,2,0,0],homeOrder:0,awayOrder:0,homePitcher:pitcher.id,awayPitcher:pitcher.id,
  homeLineup:Array(9).fill(batter.id),awayLineup:Array(9).fill(batter.id),playerStats:{},plateAppearances:62,events:['이미 공개된 기록'],complete:false,endReason:''};
 return {save:{id:'save-a',version:1,season:2025,asOf:'fixture',revision:'fixture',team:'LG',complete:false,game,
  teams:[{code:'LG',name:'LG',batters:[batter],pitchers:[pitcher]},{code:'OB',name:'두산',batters:[batter],pitchers:[pitcher]}]},
  action:{format:'action-v2',code:'match-a',mode,role:'pitcher',pace:'full',waiting:false,done:false,round:4,score:2,winner:null,balls:1,strikes:2,pitchCount:1,history:[],batter:batter.id,pitcher:pitcher.id,
   roster:{season:2025,asOf:'fixture',revision:'fixture',batter,pitcher},pitch:{...pitch(undefined),resolved:false}}};
}
function resolved(before,finish=false){
 const after=structuredClone(before);after.save.version++;
 Object.assign(after.save.game,{inning:finish?9:8,half:'bottom',outs:finish?3:0,homeRuns:finish?6:5,awayRuns:4,bases:[null,null,null],plateAppearances:63,events:['이미 공개된 기록','새 결과 기록'],complete:finish,endReason:finish?'walkoff':''});
 Object.assign(after.action,{role:'batter',done:finish,balls:0,strikes:0,history:[result()],pitch:pitch(result())});return after;
}
function hitResolved(before,reaction,finish=false){
 const after=structuredClone(before),bases={ '1B':1,'2B':2,'3B':3,HR:4 }[reaction.outcome];
 after.save.version++;
 Object.assign(after.save.game,{outs:2,awayRuns:before.save.game.awayRuns+(bases>=2?1:0),awayHits:before.save.game.awayHits+1,
  bases:bases===4?[null,null,null]:Array.from({length:3},(_,i)=>i===bases-1?{playerId:batter.id}:null),
  plateAppearances:63,events:['이미 공개된 기록',reaction.label],complete:finish,endReason:finish?'walkoff':''});
 Object.assign(after.action,{balls:0,strikes:0,done:finish,history:[reaction],pitch:pitch(reaction)});
 return after;
}
for(const mode of ['ai','pvp'])for(const final of [false,true]){
 const before=state(mode),after=resolved(before,final),copy=structuredClone(after),reveal=cycle.pitchResultRevealAt(after.action.pitch);
 for(const now of [at+1,reveal-1]){
  const shown=selectSeasonPresentation(after,now,before);
  assert.equal(shown.pending,true);assert.equal(shown.masked,false);assert.equal(shown.state,before,'Keep one complete pre-result view instead of mixing old scores with new outs');
  assert.equal(shown.state.save.game.half,'top');assert.equal(shown.state.save.game.outs,2);assert.equal(shown.state.action.balls,1);
 }
 assert.equal(selectSeasonPresentation(after,reveal,before).state,after);assert.equal(selectSeasonPresentation(after,reveal,before).pending,false);
 assert.equal(selectSeasonPresentation(after,reveal+60000,null).pending,false,'An old restored pitch does not replay its result delay');
 const fresh=selectSeasonPresentation(after,at+1,null);assert(fresh.pending&&fresh.masked&&fresh.state===after,'Cold restore masks unknown pre-play scores instead of inventing them');
 for(const change of ['save','game','code','pitch']){
  const other=structuredClone(before);if(change==='save')other.save.id='other';if(change==='game')other.save.game.id='other';if(change==='code')other.action.code='other';if(change==='pitch')other.action.pitchCount--;
  assert.equal(selectSeasonPresentation(after,at+1,other).masked,true,`Never reuse an unrelated ${change} snapshot`);
 }
 const immediate=seasonPitchKey(after);assert.equal(selectSeasonPresentation(after,at+1,before,immediate).pending,false,'Explicit batch simulation is shown immediately');
 const next=structuredClone(after);next.action.pitchCount++;next.action.pitch.id++;
 assert.equal(selectSeasonPresentation(next,at+1,null,immediate).pending,true,'Simulation bypass never leaks into the next live pitch');
 assert.deepEqual(after,copy,'Presentation never mutates authoritative scores or input roles');checks++;
}

let now=at+1,presentationOverride;
const previousDocument=globalThis.document;globalThis.document={hidden:false};
function moduleLoader(){
 const cache=new Map();
 const read=file=>{file=path.resolve(file);if(cache.has(file))return cache.get(file).exports;const mod={exports:{}};cache.set(file,mod);const native=createRequire(file);
  const require=id=>{
   if(id.endsWith('.css'))return {};
   if(id==='./action-scene')return {__esModule:true,default:props=>React.createElement('span',{'data-scene-role':props.side,'data-hidden-score':String(!!props.hideScore)})};
   if(id==='../lib/game-clock')return {localNow:()=>now,createClockSync:()=>({})};
   if(id==='../lib/season-presentation')return {seasonPitchKey,useSeasonPresentation:()=>presentationOverride};
   if(id.startsWith('.')){const base=path.resolve(path.dirname(file),id);for(const ext of ['.ts','.tsx','.json'])if(fs.existsSync(base+ext))return ext==='.json'?JSON.parse(fs.readFileSync(base+ext,'utf8')):read(base+ext);}
   return native(id);
  };
  const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
  new Function('require','module','exports',js)(require,mod,mod.exports);return mod.exports;
 };return read;
}
try{
 const modules=moduleLoader(),SeasonMatch=modules('app/season-match.tsx').default,practice=modules('app/page.tsx');
 const render=(state)=>renderToStaticMarkup(React.createElement(SeasonMatch,{state,busy:false,clock:{current:0},command:async()=>state,appearances:{}}));
 for(const final of [false,true]){
  const before=state(),after=resolved(before,final),reveal=cycle.pitchResultRevealAt(after.action.pitch);
  now=at+1;presentationOverride=selectSeasonPresentation(after,now,before);let markup=render(after);
  assert(!markup.includes('결과 전용 문구')&&!markup.includes('season-final'),'Fielding hides both result text and final-game actions');
  assert(markup.includes('8회 초')&&markup.includes('B 1 · S 2 · O 2'),'The HUD keeps the original half and count until the reveal');
  assert(markup.includes('data-scene-role="pitcher"'),'The third-out play retains its originating camera');
  presentationOverride=selectSeasonPresentation(after,now,null);markup=render(after);
  const scores=markup.match(/<div class="season-score-teams">([\s\S]*?)<\/div>/)?.[1]??'';
  assert(!scores.includes('<b>5</b>')&&!scores.includes('<b>6</b>')&&!scores.includes('<b>4</b>'),'Cold restore masks the live scorebug as well as the inning table');
  assert(markup.includes('data-hidden-score="true"'),'Cold restore also masks the 3D stadium scoreboard');
  now=reveal;presentationOverride=selectSeasonPresentation(after,now,before);markup=render(after);
  assert(markup.includes('결과 전용 문구'),'The result appears exactly when defense has completed');
  assert.equal(markup.includes('season-final'),final,'Final actions appear only after the final play resolves visually');
 }
 for(const {origin,reaction} of hitCases)for(const mode of ['ai','pvp'])for(const final of [false,true]){
  const before=state(mode),after=hitResolved(before,reaction,final),copy=structuredClone(after),{timeline,times}=hitStages(reaction);
  for(const previous of [before,null])for(const phase of times){
   now=phase;presentationOverride=selectSeasonPresentation(after,now,previous);
   assert(presentationOverride.pending,`${origin}: the pending gate must survive every live-hit stage`);
   assert.equal(presentationOverride.masked,previous===null);
   if(previous)assert.equal(presentationOverride.state,before,'Hit/HR scores, bases, hits and logs remain one pre-play snapshot');
   const markup=render(after);
   assert(!markup.includes(reaction.label),`${origin}: ${mode} ${phase-reaction.contact.at}ms cannot expose the hit label`);
   assert(!markup.includes('season-final'),'A winning hit cannot reveal final actions before defense/running finishes');
   assert(!markup.includes('km/h ·'),'Exit speed and distance cannot announce an unrevealed hit');
   if(previous)assert(markup.includes('B 1 · S 2 · O 2'),'A live hit cannot advance the visible count');
   else assert(markup.includes('data-hidden-score="true"'),'Restored hits mask the stadium score too');
  }
  now=timeline.completeAt;presentationOverride=selectSeasonPresentation(after,now,before);
  const markup=render(after);
  assert(!presentationOverride.pending&&markup.includes(reaction.label),'The hit label appears at shared completion, never a separate timer');
  assert.equal(markup.includes('season-final'),final);
  assert.equal(selectSeasonPresentation(after,now+60000,null).pending,false,'An old restored hit is already public');
  assert.equal(selectSeasonPresentation(after,reaction.contact.at,before,seasonPitchKey(after)).pending,false,'An explicit hit simulation may bypass animation');
  const next=structuredClone(after);next.action.pitchCount++;
  assert(selectSeasonPresentation(next,reaction.contact.at,null,seasonPitchKey(after)).pending,'A previous simulated hit cannot bypass the next real hit');
  assert.deepEqual(after,copy);checks++;
 }
 for(const mode of ['ai','pvp'])for(const final of [false,true]){
  const before={code:'practice-a',round:4,balls:1,strikes:2,score:2,done:false,winner:null};
  const live={...before,round:final?6:5,balls:0,strikes:0,score:final?4:3,done:final,winner:final?'batter':null,mode,pitch:pitch(result())};
  const reveal=cycle.pitchResultRevealAt(live.pitch),pending=practice.practiceResultPresentation(live,reveal-1,before);
  assert(pending.pending&&!pending.last&&!pending.showResult);assert.equal(pending.score,before);
  const panel=renderToStaticMarkup(React.createElement(React.Fragment,null,
   React.createElement(practice.PracticeResultOverlay,{presentation:pending,side:'batter',onReset(){}}),React.createElement(practice.PracticeLastPitch,{presentation:pending})));
  assert(!panel.includes('결과 전용 문구')&&!panel.includes('FINAL RESULT')&&!panel.includes('다시 그라운드로'));
  const cold=practice.practiceResultPresentation(live,reveal-1,null);assert.equal(cold.score,null);
  const shown=practice.practiceResultPresentation(live,reveal,before);assert(!shown.pending&&shown.showResult&&shown.last);
  const overlay=renderToStaticMarkup(React.createElement(practice.PracticeResultOverlay,{presentation:shown,side:'batter',onReset(){}}));
  assert(overlay.includes(final?'FINAL RESULT':'결과 전용 문구'));checks++;
 }
}finally{globalThis.document=previousDocument;}
console.log(`PASS ${checks} result-presentation scenarios: actual C# ground single, evaluator double/HR and explicit triple contract; pickup/return/base-running completion; warm/cold AI/PvP hit and final cards; original third-out HUD/camera; simulation bypass; takes/HBP/foul`);
