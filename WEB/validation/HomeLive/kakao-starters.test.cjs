const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
let url='',calls=0,response={code:200,body:JSON.stringify({text:'NC 미발표 / 두산 곽빈'})};
const context={KBO_SITE_BASE_URL:'https://example.invalid',kboHttpGet:u=>{url=u;calls++;return response;}};
vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(__dirname,'../../integrations/kakao-starters.js'),'utf8'),context);
assert.equal(context.GetKboStarters('/선발'),'NC 미발표 / 두산 곽빈');
assert.equal(url,'https://example.invalid/api/bot/starters?dayOffset=0');
context.GetKboStarters('/내일선발');assert.ok(url.endsWith('dayOffset=1'));
const before=calls;assert.match(context.GetKboStarters('/선발 다른것'),/사용법/);assert.equal(calls,before);
for(const [code,pattern] of [[404,/배포/],[429,/지연/],[503,/지연/],[500,/서버 응답/]]){
 response={code,body:'error'};assert.match(context.GetKboStarters('/선발'),pattern);
}
response={code:200,body:'{}'};assert.match(context.GetKboStarters('/선발'),/응답을 확인/);
response={code:200,body:'malformed'};assert.match(context.GetKboStarters('/선발'),/연결하지 못/);
if(process.argv[2]){
 const full={java:{io:{},lang:{Thread:function(task){this.start=()=>task.run();}}}};
 vm.createContext(full);vm.runInContext(fs.readFileSync(process.argv[2],'utf8'),full);
 const replies=[];
 full.kboHttpGet=u=>({code:200,body:JSON.stringify({text:u})});
 full.requestToPC=()=>{throw Error('Unexpected PC forwarding');};
 for(const msg of ['/선발','/내일선발','/경기'])full.responseFix('테스트',msg,'테스트',true,{reply:t=>replies.push(t)},null,'test');
 assert.equal(replies.length,3);
 assert.ok(replies[0].endsWith('/api/bot/starters?dayOffset=0'));
 assert.ok(replies[1].endsWith('/api/bot/starters?dayOffset=1'));
 assert.ok(replies[2].endsWith('/api/bot/games'));
 full.responseFix('테스트','/선발대','테스트',true,{reply:t=>replies.push(t)},null,'test');
 assert.equal(replies.length,3);
}
console.log('PASS starters command routing, failures, full bot single reply, games retained (no messages sent)');
