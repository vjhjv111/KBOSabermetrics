import assert from 'node:assert/strict';
const base=process.env.TEST_URL??'http://127.0.0.1:5188';
const fullSeason=process.argv.includes('--full-season'),days=fullSeason?144:18;
let csrf='';const cookies=new Map();
async function request(resource,body){
 const r=await fetch(base+resource,{method:body?'POST':'GET',headers:{Cookie:[...cookies].map(([k,v])=>`${k}=${v}`).join('; '),Origin:base,...(body?{'Content-Type':'application/json','X-CSRF-TOKEN':csrf}:{})},body:body?JSON.stringify(body):undefined});
 for(const c of r.headers.getSetCookie()){const first=c.split(';')[0],i=first.indexOf('=');cookies.set(first.slice(0,i),first.slice(i+1));}
 const x=await r.json();if(!r.ok)throw new Error(JSON.stringify(x));return x;
}
csrf=(await request('/api/session')).csrfToken;await request('/api/diamond/career');
const roster=await request('/api/diamond/roster');
let state=await request('/api/diamond/season',{op:'create',requestId:crypto.randomUUID(),season:roster.season,team:'KT',pace:'full',seriesPerPair:fullSeason?8:1});
const started=Date.now();
for(let day=0;day<days;day++)state=await request('/api/diamond/season',{op:'sim-day',requestId:crypto.randomUUID(),version:state.save.version});
const fixtures=state.save.schedule.filter(f=>f.complete),players=Object.values(state.save.playerStats),sum=key=>players.reduce((total,p)=>total+p[key],0);
const starters=state.save.teams.flatMap(team=>team.lineup.map(id=>team.batters.find(b=>b.id===id)));
const expectedAverage=starters.reduce((sum,b)=>sum+b.avg,0)/starters.length;
const measured={season:roster.season,games:fixtures.length,runsPerTeamGame:fixtures.reduce((total,f)=>total+f.homeRuns+f.awayRuns,0)/fixtures.length/2,average:sum('h')/sum('ab'),homeRunsPerTeamGame:sum('hr')/fixtures.length/2,walksPerTeamGame:sum('bb')/fixtures.length/2,strikeoutsPerTeamGame:sum('k')/fixtures.length/2,starterRecordAverage:expectedAverage,seconds:(Date.now()-started)/1000};
console.log(JSON.stringify(measured,null,2));
if(!process.argv.includes('--report-only')){
 assert.equal(fixtures.length,days*5);assert.equal(state.save.complete,true);
 for(const team of state.save.teams)assert.equal(fixtures.filter(f=>f.homeTeam===team.code||f.awayTeam===team.code).length,days);
 assert.ok(measured.runsPerTeamGame>=2&&measured.runsPerTeamGame<=10,'AI scoring must stay in a playable baseball range');
 assert.ok(measured.average>=.19&&measured.average<=.37,'Balls in play must include realistic outs');
 assert.ok(Math.abs(measured.average-expectedAverage)<.1,'AI hitting must broadly track selected actual player records');
 assert.ok(measured.homeRunsPerTeamGame<3,'Home runs cannot be the default contact outcome');
 console.log(`PASS real-DB ${days}-game season for every team and AI balance checks`);
}
