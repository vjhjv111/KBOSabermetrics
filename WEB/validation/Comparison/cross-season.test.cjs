const assert=require('node:assert/strict');
const fs=require('node:fs');
const vm=require('node:vm');
const source=fs.readFileSync('WEB/frontend/comparison.js','utf8');
function extract(name){
 const start=source.indexOf('function '+name+'(');
 const next=source.indexOf('\nfunction ',start+1),asyncNext=source.indexOf('\nasync function ',start+1);
 const ends=[next,asyncNext].filter(n=>n>=0);
 return source.slice(start,Math.min(...ends));
}
const nodes=new Map();
const node=()=>({disabled:false,hidden:false,value:'2026',textContent:'',setAttribute(){},replaceChildren(){},reportValidity(){return true;}});
const state={seq:0,selected:[],target:'',seasons:{},data:null};
const context={comparisonState:state,AbortController,comparisonActive:()=>true,$:id=>{if(!nodes.has(id))nodes.set(id,node());return nodes.get(id);},text:()=>({}),comparisonMessage:()=>{},comparisonRender:()=>{},comparisonRenderSelected:()=>{},comparisonButton:()=>({})};
nodes.set('cmp-min-pa',{...node(),value:'100'});
const requests=[];
context.api=async(_,r)=>{
 requests.push(r);
 return {year:r.year,minPa:r.minPa,players:[{code:'12345',name:'선수',values:{PA:r.year===2024?500:300},percentiles:{AVG:r.year===2024?90:60}}],targetCode:'12345'};
};
vm.createContext(context);
for(const name of ['comparisonKey','comparisonPlayer','comparisonLoad','comparisonChangeSeason']){
 let code=extract(name);
 if(['comparisonLoad','comparisonChangeSeason'].includes(name))code='async '+code;
 vm.runInContext(code,context);
}
(async()=>{
 await context.comparisonLoad();
 assert.deepEqual([...state.selected],['2026:12345']);
 nodes.get('cmp-year').value='2024';
 await context.comparisonLoad();
 assert.deepEqual([...state.selected],['2026:12345']);
 state.selected.push('2024:12345');
 await context.comparisonLoad();
 assert.equal(context.comparisonPlayer('2026:12345').values.PA,300);
 assert.equal(context.comparisonPlayer('2024:12345').values.PA,500);
 assert.equal(context.comparisonPlayer('2024:12345').percentiles.AVG,90);
 await context.comparisonChangeSeason(context.comparisonPlayer('2026:12345'),2024);
 assert.equal(state.selected.length,2,'duplicate season must not replace a card');
 await context.comparisonChangeSeason(context.comparisonPlayer('2026:12345'),2023);
 assert.deepEqual([...state.selected],['2023:12345','2024:12345']);
 nodes.get('cmp-min-pa').value='200';
 await context.comparisonLoad();
 assert.ok(requests.slice(-2).every(r=>r.minPa===200));
 console.log('Cross-season selection, duplicate protection, season change and cohort refresh passed.');
})().catch(e=>{console.error(e);process.exitCode=1;});

