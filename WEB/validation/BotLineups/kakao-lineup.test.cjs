const fs=require('node:fs'), vm=require('node:vm'), assert=require('node:assert/strict'), path=require('node:path');
let calls=0,url='',response={code:200,body:JSON.stringify({text:'한화 선발 라인업'})};
const ctx={KBO_SITE_BASE_URL:'https://example.invalid',kboHttpGet:u=>{calls++;url=u;return response;}};
vm.createContext(ctx);vm.runInContext(fs.readFileSync(path.join(__dirname,'../../integrations/kakao-lineup.js'),'utf8'),ctx);
assert.equal(ctx.GetKboLineup('/라인업 @한화'),'한화 선발 라인업');
assert.equal(url,'https://example.invalid/api/bot/lineup?team='+encodeURIComponent('한화'));
ctx.GetKboLineup('/라인업 한화 이글스');assert.ok(url.endsWith(encodeURIComponent('한화 이글스')));
const before=calls;assert.match(ctx.GetKboLineup('/라인업'),/사용법/);assert.equal(calls,before);
for(const [code,re] of [[400,/팀명/],[404,/배포/],[429,/지연/],[503,/지연/],[500,/실패/]]) {
 response={code,body:''};assert.match(ctx.GetKboLineup('/라인업 LG'),re);
}
response={code:200,body:'{}'};assert.match(ctx.GetKboLineup('/라인업 LG'),/확인/);
response={code:200,body:'bad'};assert.match(ctx.GetKboLineup('/라인업 LG'),/연결/);
if(process.argv[2]) {
 const full={java:{io:{},lang:{Thread:function(t){this.start=()=>t.run();}}}};
 vm.createContext(full);vm.runInContext(fs.readFileSync(process.argv[2],'utf8'),full);
 full.kboHttpGet=u=>({code:200,body:JSON.stringify({text:u})});
 full.requestToPC=()=>{throw Error('Unexpected PC forwarding');};
 const replies=[];
 for(const msg of ['/라인업 @한화','/라인업 LG','/라인업','/선발','/내일선발','/경기'])
   full.responseFix('테스트',msg,'테스트',true,{reply:t=>replies.push(t)},null,'test');
 assert.equal(replies.length,6);assert.match(replies[0],/api\/bot\/lineup/);assert.match(replies[2],/사용법/);
 assert.match(replies[3],/dayOffset=0/);assert.match(replies[4],/dayOffset=1/);assert.match(replies[5],/bot\/games/);
}
console.log('PASS lineup commands, errors, full bot routing and existing commands (no messages sent)');
