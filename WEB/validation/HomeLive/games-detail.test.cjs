const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
function node(tag,value=''){return {tag,value,children:[],append(...x){this.children.push(...x)},replaceChildren(...x){this.children=x},before(){},setAttribute(){},dataset:{},querySelector(){return node('h2')}}}
const roots={},ctx={text:node,$:id=>roots[id]??=node('div'),document:{createElement:node,createTextNode:t=>node('text',t),querySelector:()=>node('nav')},window:{addEventListener(){}},teamNameNode:x=>node('span',x),teamNames:{LG:'LG',KIA:'KIA'},homeTable:(heads,rows)=>node('table',JSON.stringify(rows)),playerCard:t=>node('section',t),URLSearchParams,AbortController,location:{hash:'#games'},state:{catalog:{}},abortQuery(){},clearTimeout(){},setTimeout(fn,ms){ctx.delay=ms;return 1;}};
vm.createContext(ctx);vm.runInContext(fs.readFileSync('WEB/frontend/games.js','utf8'),ctx);
vm.runInContext("gameState.month='2026-10'",ctx);
ctx.renderCalendar({rows:['BEFORE','PLAY','RESULT'].map((Status,i)=>({Id:'game'+i,Date:'2026-10-20',Status,Home:'LG',Away:'KIA'}))},2026,10);
const flatten=n=>[n,...(n.children??[]).flatMap(flatten)];
const all=flatten(vm.runInContext('gameRoot',ctx));
assert.equal(all.filter(n=>n.tag==='a'&&n.href?.startsWith('#game=')).length,3);
ctx.renderGame({game:{Id:'scheduled',Status:'BEFORE',Date:'2026-10-20',Home:'LG',Away:'KIA'},preview:true,lineups:{home:[{order:1,name:'선발타자',position:'포수'}],homePitcher:'선발투수'}});
assert.ok(flatten(vm.runInContext('gameRoot',ctx)).some(n=>n.tag==='table'&&n.value.includes('선발타자')));
(async()=>{ctx.location.hash='#game=live';ctx.api=async()=>({game:{Status:'PLAY'}});ctx.renderGame=()=>{};await ctx.gameRoute();assert.equal(ctx.delay,60000);ctx.delay=0;ctx.api=async()=>({game:{Status:'RESULT'}});await ctx.gameRoute();assert.equal(ctx.delay,0);console.log('PASS calendar all-status links, pregame lineup rendering, live refresh and final stop');})().catch(e=>{console.error(e);process.exitCode=1});
