// Run against a local server backed by SampleData/2026.zip.
// node validation/player-page-smoke.mjs http://127.0.0.1:PORT
import assert from 'node:assert/strict';
const base=new URL(process.argv[2]??'http://127.0.0.1:5188');
assert(['127.0.0.1','localhost'].includes(base.hostname),'Use a local sample server');
const sessionResponse=await fetch(new URL('/api/session',base));
assert.equal(sessionResponse.status,200);
const session=await sessionResponse.json();
const cookie=(sessionResponse.headers.getSetCookie?.()??[]).map(c=>c.split(';')[0]).join('; ');
async function post(path,body,status=200){
  const res=await fetch(new URL(path,base),{method:'POST',headers:{'content-type':'application/json','X-CSRF-TOKEN':session.csrfToken,cookie},body:JSON.stringify(body)});
  const data=await res.json();assert.equal(res.status,status,JSON.stringify(data));return data;
}
const batters=await post('/api/players/search',{query:'노시환'});assert(batters.length);
const code=batters[0].pcode;
const profile=await post('/api/player',{code,section:'profile'});assert(profile.seasons.length);
const year=Number(profile.seasons[0].Year);
let checked=0;
for(const section of ['profile','summary','career','years','trend','games','opponents','situations','plays','pitches','arsenal','direction']){
  const result=await post('/api/player',{code,year,section,pageSize:2});checked++;
  if(section==='summary'){
    assert(result.metrics.some(m=>m.label==='HR'&&m.population>0));
    assert(result.metrics.some(m=>m.label==='BB%'&&m.population>0),'Advanced metrics appear with basic metrics');
    assert.equal(new Set(result.metrics.map(m=>m.label)).size,result.metrics.length,'No duplicate labels');
    const alternate=await post('/api/player',{code,year,section,view:'advanced'});
    assert.deepEqual(result.metrics,alternate.metrics,'Summary is independent of view selection');
  }
  if(result.rows&& !['years','trend'].includes(section))assert(result.rows.length<=2,'Bounded page');
  for(const m of result.metrics??[]){if(m.percentile!==null)assert(m.percentile>=0&&m.percentile<=100);if(m.population<2)assert.equal(m.percentile,null);}
  for(const r of result.rows??[]){
    if(typeof r.AB==='number'&&r.OBP!==null&&r.OBP!==undefined&&r.AB+r.BB+r.HBP+r.SF>0){
      const expected=(r.H+r.BB+r.HBP)/(r.AB+r.BB+r.HBP+r.SF);assert(Math.abs(r.OBP-expected)<=0.00051,'OBP includes SF');
    }
  }
}
const p1=await post('/api/player',{code,year,section:'plays',pageSize:1,page:1});
const p2=await post('/api/player',{code,year,section:'plays',pageSize:1,page:2});
assert(p1.hasMore);assert.notDeepEqual(p1.rows,p2.rows,'Stable paginated logs');
const pitchers=await post('/api/query',{room:'season',role:'pitcher',view:'basic',year,pageSize:1});
assert(pitchers.rows.length,'Sample pitcher must exist');
{
  const p=pitchers.rows[0].entityCode;
  for(const section of ['summary','years','games','plays','opponents','pitches','arsenal','trend']){
    const result=await post('/api/player',{code:p,year,role:'pitcher',section});checked++;
    assert(!JSON.stringify(result).includes('RA9-WAR'),'Do not expose hidden WAR variants');
  }
}
for(const bad of [{code:"x' OR 1=1--",year},{code,year,section:'bad'},{code,year,section:'plays',pageSize:101},{code,year,section:'plays',start:'2026-08-01',end:'2026-01-01'}])await post('/api/player',bad,400);
const empty=await post('/api/player',{code,year,section:'games',start:'2000-01-01',end:'2000-01-02'});assert.equal(empty.rows.length,0);
console.log(`PASS ${checked} player views, pagination, OBP denominator, hidden WAR, bad inputs and empty dates`);
