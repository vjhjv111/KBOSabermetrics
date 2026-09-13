import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import os from 'node:os';

// Run against an isolated development StateDirectory. Does not edit the source DB.
const origin=process.env.TEST_URL??'http://127.0.0.1:5188';
const sessionFile=process.env.TEST_SESSION_FILE??path.join(os.tmpdir(),'diamond-season-http-session.json');
const cookies=new Map();let csrf='';let checks=0;
const check=(condition,message)=>{assert.ok(condition,message);checks++;};
async function request(resource,body,expectedStatus=200){
 const response=await fetch(origin+resource,{method:body?'POST':'GET',headers:{Cookie:[...cookies].map(([k,v])=>`${k}=${v}`).join('; '),...(body?{'Content-Type':'application/json','X-CSRF-TOKEN':csrf,Origin:origin}:{})},body:body?JSON.stringify(body):undefined});
 for(const cookie of response.headers.getSetCookie()){const first=cookie.split(';')[0],at=first.indexOf('=');cookies.set(first.slice(0,at),first.slice(at+1));}
 const value=await response.json();assert.equal(response.status,expectedStatus,JSON.stringify(value));checks++;
 return value;
}
if(process.argv.includes('--resume')){
 const saved=JSON.parse(await fs.readFile(sessionFile,'utf8'));saved.cookies.forEach(([k,v])=>cookies.set(k,v));
 const career=await request('/api/diamond/career'),season=await request('/api/diamond/season');
 check(career.player.id===saved.playerId,'Career identity survives server restart');
 check(career.player.level===saved.level&&career.player.games===saved.games,'XP rewards survive exactly once');
 check(career.player.ratings.power===saved.power,'Training survives restart');
 check(season.save.id===saved.saveId&&season.save.game.id===saved.gameId,'Season/game identity survives restart');
 check(season.save.day===saved.day&&season.save.game.complete,'Completed game and league day survive restart');
 console.log(`PASS ${checks} real HTTP restart checks`);
}else{
csrf=(await request('/api/session')).csrfToken;
await request('/api/diamond/career');
const roster=await request('/api/diamond/roster');
check(roster.teams.length===10&&roster.batters.length>=90&&roster.pitchers.length>=10,'Real source roster contains all ten teams');
const appearance={skinTone:'#bc8865',bodyType:'athletic',heightCm:182,hairStyle:'short',hairColor:'#222222',gloveColor:'#80552e',cleatColor:'#182227',batColor:'#be915e',equipmentColor:'#29353d',jerseyNumber:'44'};
const created=await request('/api/diamond/career',{op:'create',requestId:crypto.randomUUID(),name:'회귀 타자',team:'HH',position:'CF',bats:'L',throws:'R',delivery:'overhand',archetype:'slugger',appearance});
let player=created.player;
const training={op:'train',version:player.version,requestId:crypto.randomUUID(),skill:'power'};
const trained=await request('/api/diamond/career',training);player=trained.player;
check(player.ratings.power===created.player.ratings.power+2,'Training changes power');
check((await request('/api/diamond/career',training)).player.trainingPoints===player.trainingPoints,'Duplicate train spends points once');
await request('/api/diamond/career',{...training,requestId:crypto.randomUUID()},409);
let state=await request('/api/diamond/season',{op:'create',requestId:crypto.randomUUID(),season:roster.season,team:'HH',pace:'full',seriesPerPair:8});
check(state.save.schedule.length===720&&state.save.totalDays===144,'Full 144-game league persisted');
const customId=`${roster.season}:${player.id}`;
check(state.save.teams.find(t=>t.code==='HH').lineup.includes(customId),'Custom hitter joins real team lineup');
const command=async(op,extra={})=>{state=await request('/api/diamond/season',{op,version:state.save.version,requestId:crypto.randomUUID(),...extra});return state;};
await command('start-game');
check(state.action.role==='pitcher','Home team controls defense in top of first');
const firstPitcher=state.action.roster.pitcher;
await command('pitch',{previousPitch:state.action.pitchCount,type:firstPitcher.arsenal[0].type,aim:{x:0,y:0},quality:.9});
const pitch=state.action.pitch;
check(Math.abs(pitch.flightMs-18440/(pitch.velocity/3.6))<2,'1.00x flight time matches displayed velocity');
await new Promise(resolve=>setTimeout(resolve,Math.max(0,pitch.releaseAt+pitch.flightMs+1100-Date.now())));
state=await request('/api/diamond/season');
check(state.action.pitch.resolved,'AI hitter completes live pitch through GET tick');
await command('sim-half');
check(state.save.game.half==='bottom'&&state.action.role==='batter','Three outs change user role to batting');
await command('ready',{previousPitch:state.action.pitchCount});
const ball=state.action.pitch,inputAt=ball.releaseAt+ball.flightMs-95;
await new Promise(resolve=>setTimeout(resolve,Math.max(0,inputAt-Date.now())));
await command('swing',{pitchId:ball.id,inputAt,aim:ball.target});
check(state.action.pitch.resolved&&state.action.pitch.reaction.contact,'Timed aimed input hits the live pitch');
const sameId=state.action.pitch.id;
await command('swing',{pitchId:sameId,inputAt,aim:ball.target});
check(state.action.pitch.id===sameId,'Repeat pitch input does not advance another pitch');
await command('sim-game');
check(state.save.game.complete&&state.save.game.inning>=9,'Playable game finishes at regulation/extra innings');
check(state.save.day===2&&state.save.schedule.filter(f=>f.complete).length===5,'Other clubs resolve and league advances one day');
player=(await request('/api/diamond/career')).player;
check(player.games===1&&player.rewards.length===1,'Actual completed custom-player game awards XP');
check(player.stats.pa>0,'Career batting totals recorded');
const after=await request('/api/diamond/season');
check((await request('/api/diamond/career')).player.games===1,'Repeated GET does not duplicate XP');
const anonymous=new Map(cookies);cookies.delete('diamond_owner');
check((await request('/api/diamond/season')).save===null,'Different owner cannot read saved league');
cookies.clear();anonymous.forEach((v,k)=>cookies.set(k,v));
await fs.writeFile(sessionFile,JSON.stringify({cookies:[...cookies],playerId:player.id,level:player.level,games:player.games,power:player.ratings.power,saveId:after.save.id,gameId:after.save.game.id,day:after.save.day}));
console.log(`PASS ${checks} real DB HTTP checks: ${roster.season}, ${roster.batters.length} batters / ${roster.pitchers.length} pitchers; manual pitching, timed batting, full game, custom training/XP, owner isolation. Restart fixture saved.`);
}
