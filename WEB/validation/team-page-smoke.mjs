// Run against a loopback server with the sample DB.
import assert from 'node:assert/strict';
const base=new URL(process.argv[2]??'http://127.0.0.1:5193');
assert(['127.0.0.1','localhost'].includes(base.hostname));
const response=await fetch(new URL('/api/session',base));const session=await response.json();
const cookie=(response.headers.getSetCookie?.()??[]).map(c=>c.split(';')[0]).join('; ');
const catalog=await (await fetch(new URL('/api/catalog',base),{headers:{cookie}})).json();
async function query(body){const r=await fetch(new URL('/api/team',base),{method:'POST',headers:{cookie,'Content-Type':'application/json','X-CSRF-TOKEN':session.csrfToken},body:JSON.stringify(body)});assert.equal(r.status,200);return r.json();}
for(const team of catalog.teams){
 const req={team,year:Math.max(...catalog.years)};const d=await query(req),s=await query({...req,section:'scores'});
 assert.equal(d.record.G,d.record.W+d.record.D+d.record.L);
 assert.equal(d.record.PCT,d.record.W+d.record.L?Math.round(d.record.W/(d.record.W+d.record.L)*1000)/1000:null);
 for(const key of ['scored','allowed']){assert.equal(s[key].reduce((n,x)=>n+x.record.G,0),d.record.G);assert.equal(s[key].reduce((n,x)=>n+x.score*x.record.G,0),d.record[key==='scored'?'RF':'RA']);}
 for(const x of s.inningDistribution){const t=s.innings.find(t=>t.inning===x.inning&&t.side===x.side);assert.equal(x.bins.reduce((a,b)=>a+b,0),t.games);}
 assert.equal(d.opponents.reduce((n,x)=>n+x.record.G,0),d.record.G);
 for(const role of ['batter','pitcher']){const r=await query({...req,section:'roster',role});assert(r.rows.length<=25);assert(r.rows.every(x=>x.Code&&x.Name));}
 const schedule=await query({...req,section:'schedule'});assert(schedule.rows.length<=25);
 console.log(`PASS ${team}: records, score totals, inning bins, opponents, rosters, schedule`);
}
