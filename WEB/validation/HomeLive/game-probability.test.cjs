const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
function node(tag,value='',attrs={}){return {tag,value,attrs,children:[],style:{},events:{},offsetWidth:250,dataset:{},append(...x){this.children.push(...x)},before(){},setAttribute(k,v){this.attrs[k]=v},addEventListener(k,v){this.events[k]=v},getBoundingClientRect(){return {left:0,top:0,width:900,height:310}}};}
const ctx={text:node,$:()=>node('div'),document:{querySelector:()=>node('nav')},window:{addEventListener(){}},svgNode:(tag,attrs)=>node(tag,'',attrs),teamNames:{LG:'LG',KIA:'KIA'},markTeamName(){}};
vm.createContext(ctx);vm.runInContext(fs.readFileSync('WEB/frontend/games.js','utf8'),ctx);
const d={game:{Home:'LG',Away:'KIA',Status:'PLAY'},probability:[{Seq:3,Inning:2,Team:'KIA',Title:'홈런',Home:2,Away:98,WPA:20},{Seq:4,Inning:2,Team:'KIA',Home:1,Away:99},{Seq:5,Inning:4,Team:'LG',Home:3,Away:97}]};
const result=ctx.gameProbabilityPoints(d);assert.equal(result.innings,9);assert.ok(result.points[0].x>120+760/9);assert.match(result.points[0].label,/2회 초/);assert.match(result.points[2].label,/4회 말/);
const wrap=ctx.gameProbability(d),chart=wrap.children[0];assert.equal(chart.children.filter(n=>n.tag==='polyline').length,1,'do not bridge missing innings');
assert.ok(chart.children.some(n=>n.tag==='text'&&n.textContent==='1회'));assert.ok(chart.children.some(n=>n.tag==='text'&&n.textContent==='9회'));
chart.events.pointermove({clientX:result.points[0].x});const tip=wrap.children[1];assert.equal(tip.hidden,false);assert.match(tip.textContent,/LG 2.0% \/ KIA 98.0%/);assert.match(tip.textContent,/2회 초.*홈런/);chart.events.pointerleave();assert.equal(tip.hidden,true);
const dots=chart.children.filter(n=>n.events.focus);dots[0].events.focus();assert.equal(tip.hidden,false);dots[0].events.blur();assert.equal(tip.hidden,true);
const ext=ctx.gameProbabilityPoints({game:d.game,probability:[{Inning:12,Home:55,Away:45,Team:'LG'},{Inning:0,Home:50,Away:50},{Inning:1,Home:null,Away:100}]});assert.equal(ext.innings,12);assert.equal(ext.points.length,1);
const fallback=ctx.gameProbabilityPoints({game:d.game,probability:[{Seq:1,Inning:3,Home:40,Away:60}],plays:[{Seq:1,Team:'LG'}]});assert.match(fallback.points[0].label,/3회 말/);
console.log('PASS inning axis, missing intervals, pointer tooltip, keyboard focus, extra innings and team fallback');
