const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const script=fs.readFileSync(path.join(__dirname,'../../integrations/kakao-games.js'),'utf8');
let response={code:200,body:JSON.stringify({text:'한화 3 : 2 삼성 | 8회 초'})},url='',calls=0;
const context={KBO_SITE_BASE_URL:'https://example.invalid',kboHttpGet:u=>{calls++;url=u;return response;}};
vm.createContext(context);vm.runInContext(script,context);
assert.equal(context.GetKboGames('/경기'),'한화 3 : 2 삼성 | 8회 초');
assert.equal(url,'https://example.invalid/api/bot/games');
context.GetKboGames('/경기 2026-09-29');assert.equal(url,'https://example.invalid/api/bot/games?date=2026-09-29');
let old=calls;assert.match(context.GetKboGames('/경기 잘못된날짜'),/사용법/);assert.equal(calls,old);
for(const [code,word] of [[404,'배포'],[400,'날짜'],[429,'지연'],[503,'지연'],[500,'서버 응답']]){response={code,body:'error'};assert.match(context.GetKboGames('/경기'),new RegExp(word));}
response={code:200,body:'invalid JSON'};assert.match(context.GetKboGames('/경기'),/연결하지 못/);
response={code:200,body:'{}'};assert.match(context.GetKboGames('/경기'),/응답을 확인/);
if(process.argv[2]){
  const replies=[];
  const full={java:{io:{},lang:{Thread:function(task){this.start=()=>task.run();}}}};
  vm.createContext(full);vm.runInContext(fs.readFileSync(process.argv[2],'utf8'),full);
  full.kboHttpGet=u=>{assert.match(u,/\/api\/bot\/games$/);return {code:200,body:'{"text":"경기 API 응답"}'};};
  full.requestToPC=()=>{throw Error('Unexpected PC forwarding');};
  full.responseFix('검증방','/경기','검증자',true,{reply:text=>replies.push(text)},null,'test');
  assert.deepEqual(replies,['경기 API 응답']);
  full.responseFix('검증방','/경기장','검증자',true,{reply:text=>replies.push(text)},null,'test');
  assert.equal(replies.length,1);
}
console.log('PASS /경기 formatter integration, date routing, errors, single reply; no network/messages sent');
