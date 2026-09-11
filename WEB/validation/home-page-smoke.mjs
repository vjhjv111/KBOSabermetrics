import assert from 'node:assert/strict';
const base=new URL(process.argv[2]??'http://127.0.0.1:5193');
assert(['127.0.0.1','localhost'].includes(base.hostname));
const response=await fetch(new URL('/api/session',base)),session=await response.json();
const cookie=(response.headers.getSetCookie?.()??[]).map(c=>c.split(';')[0]).join('; ');
const catalog=await (await fetch(new URL('/api/catalog',base),{headers:{cookie}})).json();
async function query(section){const r=await fetch(new URL('/api/home',base),{method:'POST',headers:{cookie,'Content-Type':'application/json','X-CSRF-TOKEN':session.csrfToken},body:JSON.stringify({year:Math.max(...catalog.years),section})});assert.equal(r.status,200);return r.json();}
const standings=await query('standings'),leaders=await query('leaders');
assert(standings.rows.length>0);
const near=(a,b)=>assert(Math.abs(a-b)<1e-9,`${a} != ${b}`);
for(const r of standings.rows){
 assert(!['EA','WE'].includes(r.team));assert.equal(r.g,r.w+r.d+r.l);
 if(r.w+r.l)near(r.pct,r.w/(r.w+r.l));else assert.equal(r.pct,null);
 if(r.rf+r.ra){near(r.pyth,r.rf**1.83/(r.rf**1.83+r.ra**1.83));near(r.winDifference,r.w-r.pyth*(r.w+r.l));}
 if(standings.forecastAvailable)assert(r.playoff>=0&&r.playoff<=1);else assert.equal(r.playoff,null);
}
near(standings.rows.reduce((s,r)=>s+r.rf-r.ra,0),0);
near(standings.rows.reduce((s,r)=>s+r.w-r.l,0),0);
if(standings.forecastAvailable)near(standings.rows.reduce((s,r)=>s+r.playoff,0),5);
else assert(standings.reason);
assert(leaders.war.length>0&&leaders.war.length<=10);
for(let i=1;i<leaders.war.length;i++)assert(leaders.war[i-1].value>=leaders.war[i].value);
for(const row of leaders.leaders){assert(row.players.length<=3);for(const p of row.players){assert(p.code&&p.name);assert.equal(p.role,row.role);assert(Number.isFinite(p.value));}const low=['ERA','WHIP','FIP','BB/9'].includes(row.metric);for(let i=1;i<row.players.length;i++)assert(low?row.players[i-1].value<=row.players[i].value:row.players[i-1].value>=row.players[i].value);}
console.log('PASS home: standings consistency, Pythagorean formula, forecast bounds, WAR and metric ordering');
