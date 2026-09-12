import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {load} from './load-ts.mjs';

// Exercise the production callbacks with deferred transport and React-like state setters.
// AST extraction avoids a second implementation of the lock, time and response-order rules.
const source=fs.readFileSync('app/page.tsx','utf8');
const ast=ts.createSourceFile('app/page.tsx',source,ts.ScriptTarget.Latest,true,ts.ScriptKind.TSX);
const home=ast.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text==='Home');
assert(home?.body,'The game page must expose its Home component');
const declarations=home.body.statements.flatMap(node=>ts.isVariableStatement(node)?[...node.declarationList.declarations]:[]);
function callback(name){
 const declaration=declarations.find(node=>ts.isIdentifier(node.name)&&node.name.text===name);
 assert(declaration&&ts.isCallExpression(declaration.initializer),`Missing production callback ${name}`);
 const value=declaration.initializer.arguments[0];
 assert(value&&(ts.isArrowFunction(value)||ts.isFunctionExpression(value)),`${name} must contain a callback`);
 return `const ${name}=${value.getText(ast)};`;
}
function functionDeclaration(name){
 const declaration=home.body.statements.find(node=>ts.isFunctionDeclaration(node)&&node.name?.text===name);
 assert(declaration,`Missing production function ${name}`);return declaration.getText(ast);
}
const code=ts.transpileModule([
 callback('apply'),functionDeclaration('run'),functionDeclaration('reset'),functionDeclaration('askPitch'),callback('swing'),
].join('\n'),{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.None}}).outputText;
const createCallbacks=new Function('context',`with(context){${code};return {apply,run,reset,askPitch,swing};}`);
const {SWING_CONTACT_MS}=load('lib/player-motion.ts');
const tick=()=>new Promise(resolve=>setImmediate(resolve));
const activeGame=(overrides={})=>({code:'DABCDEFG',batter:'batter',pitcher:'pitcher',role:'batter',mode:'ai',pace:'practice',
 done:false,waiting:false,pitchCount:1,pitch:{id:1,releaseAt:10000,flightMs:650,resolved:false},history:[],...overrides});
const completedGame=game=>({...game,pitch:{...game.pitch,resolved:true},history:[...game.history,{id:game.pitch.id,outcome:'STRIKE'}]});

function harness(game=activeGame()){
 let time=9900;
 const state={game,busy:false,error:'',connected:false,localSwing:null,swingTime:-100000,tones:[],busyChanges:[],pinned:[],urls:[]};
 const requests=[];
 const setter=key=>value=>{state[key]=typeof value==='function'?value(state[key]):value;};
 const context={
  liveGame:{current:game},generation:{current:0},lock:{current:false},clock:{current:0},aim:{current:{x:.3,y:-.2}},
  swungPitch:{current:null},chargeRef:{current:null},playedContacts:{current:new Set()},
  // This deliberately stays stale. Input acceptance must use localNow, never this UI clock.
  now:9800,current:{current:{canSwing:false,canPitch:false,side:'batter',run:null}},localNow:()=>time,SWING_CONTACT_MS,
  setGame(value){setter('game')(value);context.liveGame.current=state.game;},
  setBusy(value){state.busy=value;state.busyChanges.push(value);},setError:setter('error'),setConnected:setter('connected'),
  setLocalSwing:setter('localSwing'),setSwingTime:setter('swingTime'),setCharging:setter('charging'),
  setBatter:setter('batter'),setPitcher:setter('pitcher'),setSide:setter('side'),setMode:setter('mode'),setPace:setter('pace'),
  setSeasonRequest:setter('season'),pinMatchRoster:value=>state.pinned.push(value),batterStats:()=>({}),pitcherStats:()=>({}),
  tone:kind=>state.tones.push(kind),history:{replaceState:(_state,_title,url)=>state.urls.push(url)},location:{pathname:'/diamond/'},
  request(body){return new Promise((resolve,reject)=>requests.push({body,resolve,reject}));},
 };
 const callbacks=createCallbacks(context);context.current.current.run=callbacks.run;
 return {...callbacks,context,state,requests,setTime:value=>{time=value;},
  async resolve(index,view){requests[index].resolve(view);await tick();},
  async reject(index,message){requests[index].reject(new Error(message));await tick();},
 };
}

{
 const beforePitch=activeGame({pitch:null,pitchCount:0}),h=harness(beforePitch),ready=h.askPitch();
 assert.equal(h.requests.length,1);assert.equal(h.requests[0].body.op,'ready');assert.equal(h.context.lock.current,true);assert.equal(h.state.busy,true);
 await assert.rejects(h.run({op:'ready'}),/처리 중/,'Unrelated mutations still obey the shared lock');
 const flying=activeGame();h.apply(flying,0); // A GET poll reveals the pitch before its ready POST returns.
 h.context.clock.current=100;h.setTime(9800);h.swing();
 assert.equal(h.requests.length,2,'The first tap must submit during a slow ready request');
 const input=h.requests[1].body;
 assert.equal(input.op,'swing');assert.equal(input.inputAt,9900);assert.equal(input.at,9900+SWING_CONTACT_MS);
 assert.equal(h.state.swingTime,9900);assert.equal(h.state.localSwing.at,input.at);
 assert.deepEqual(h.state.tones,['swing'],'The first accepted tap animates and sounds immediately');
 h.context.aim.current.x=-1;h.context.aim.current.y=1;
 assert.deepEqual(input.aim,{x:.3,y:-.2},'The input freezes its aim before transport completes');
 h.swing();assert.equal(h.requests.length,2,'Repeated taps on the same pitch must not send a second swing');
 const completed=completedGame(flying);await h.resolve(1,completed);
 assert.equal(h.context.lock.current,true,'An independent swing must not unlock the pending ready request');
 assert.equal(h.state.busy,true);assert.deepEqual(h.state.busyChanges,[true]);
 await h.resolve(0,flying);await ready;
 assert.equal(h.context.lock.current,false);assert.equal(h.state.busy,false);assert.deepEqual(h.state.busyChanges,[true,false]);
 assert.equal(h.state.game,completed,'A late ready response must not restore a resolved pitch');
 assert.equal(h.state.game.history.length,1);h.swing();assert.equal(h.requests.length,2);
 const later=activeGame({pitchCount:2,pitch:{...flying.pitch,id:2},history:completed.history});
 h.apply(later,0);h.apply(completed,0);
 assert.equal(h.state.game,later,'A response for an older pitch must not replace a newer pitch');
}

{
 const h=harness(),ready=h.run({op:'ready',code:'DABCDEFG',previousPitch:0});
 h.swing();await h.reject(1,'temporary network failure');
 assert.equal(h.context.lock.current,true,'A rejected independent swing must also preserve the ready lock');
 assert.equal(h.state.busy,true);assert.equal(h.state.error,'temporary network failure');
 assert.equal(h.context.swungPitch.current,null);assert.equal(h.state.localSwing,null);
 h.setTime(9910);h.swing();
 assert.equal(h.requests.length,3,'A failed swing can be retried once on the same active pitch');
 assert.equal(h.requests[2].body.inputAt,9910);assert.equal(h.requests[2].body.at,9910+SWING_CONTACT_MS);
 const complete=completedGame(activeGame());await h.resolve(2,complete);await h.resolve(0,activeGame());await ready;
 assert.equal(h.state.game,complete);assert.equal(h.state.error,'');assert.deepEqual(h.state.tones,['swing','swing']);
}

for(const outcome of ['success','failure']){
 const h=harness();h.swing();
 assert.equal(h.context.lock.current,false,'A swing must not take the shared mutation lock');
 assert.equal(h.state.busy,false);assert.deepEqual(h.state.busyChanges,[]);
 h.reset();assert.equal(h.context.generation.current,1);assert.equal(h.state.game,null);
 const next=activeGame({code:'DHJKLMNP'});h.apply(next,1);h.setTime(9920);h.swing();
 const newerSwing=h.state.localSwing,pinnedCount=h.state.pinned.length,urlCount=h.state.urls.length;
 h.state.error='new match notice';h.state.connected=false;
 if(outcome==='success')await h.resolve(0,completedGame(activeGame()));else await h.reject(0,'old match failure');
 assert.equal(h.state.game,next,`An old generation's ${outcome} cannot replace the new match`);
 assert.equal(h.state.error,'new match notice');assert.equal(h.state.connected,false);
 assert.equal(h.state.localSwing,newerSwing,'An old callback cannot clear the new match swing');
 assert.equal(h.context.swungPitch.current,'DHJKLMNP:1');
 assert.equal(h.state.pinned.length,pinnedCount);assert.equal(h.state.urls.length,urlCount);
 await h.resolve(1,completedGame(next));
}

for(const game of [null,activeGame({pitch:null}),activeGame({role:'pitcher'}),activeGame({done:true}),
 activeGame({waiting:true}),activeGame({pitch:{...activeGame().pitch,resolved:true}})]){
 const h=harness(game);h.swing();assert.equal(h.requests.length,0);assert.equal(h.state.localSwing,null);
 assert.deepEqual(h.state.tones,[],'Unavailable game states must not animate or send a swing');
}
{
 const h=harness();h.setTime(9879);h.swing();assert.equal(h.requests.length,0,'A genuinely early input remains outside the acceptance window');
 h.setTime(9880);h.swing();assert.equal(h.requests.length,1,'The actual input clock accepts the exact release-minus-120ms boundary');
 assert.equal(h.requests[0].body.inputAt,9880);await h.resolve(0,completedGame(activeGame()));
}

console.log('PASS actual page callbacks: first-tap swing during slow ready, current input clock, frozen aim/contact time, one swing per pitch, independent lock lifetime, retry, generation guards and monotonic responses');
