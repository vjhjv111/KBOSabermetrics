import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import {createRequire} from 'node:module';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';

function loader(overrides={}){
 overrides={'../lib/use-korea-daylight':{useKoreaTimeOfDay:()=> 'night'},...overrides};
 const cache=new Map();
 const load=file=>{file=path.resolve(file);if(cache.has(file))return cache.get(file).exports;const mod={exports:{}};cache.set(file,mod);const native=createRequire(file);
  const require=id=>{if(id in overrides)return overrides[id];if(id.endsWith('.css'))return {};if(id.startsWith('.')){const base=path.resolve(path.dirname(file),id);for(const ext of ['.ts','.tsx','.json'])if(fs.existsSync(base+ext))return ext==='.json'?JSON.parse(fs.readFileSync(base+ext,'utf8')):load(base+ext);}return native(id);};
  const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX,esModuleInterop:true}}).outputText;
  new Function('require','module','exports',js)(require,mod,mod.exports);return mod.exports;
 };return load;
}
const plain=loader(),{initialSeasonTab}=plain('lib/title-navigation.ts');
for(const search of ['', '?utm_source=bookmark','?year=2025'])assert.equal(initialSeasonTab(search),'title','An ordinary visit starts at the title');
assert.equal(initialSeasonTab('?room=FABCDEFG'),'friendly');assert.equal(initialSeasonTab('?match=ABCDEFGH'),'practice');assert.equal(initialSeasonTab('?room=FABCDEFG&match=ABCDEFGH'),'friendly','Existing invitation precedence is preserved');
const saved={serverNow:0,save:{id:'saved-season',team:'LG',season:2025,seasonNumber:1,day:1,totalDays:144,schedule:[],standings:[],teams:[],appearances:{},game:{id:'saved-game',complete:false,events:[]}},action:{code:'in-progress',pitch:{id:7,resolved:false}}};
const globals={location:globalThis.location,history:globalThis.history};
const replacements=[];globalThis.history={replaceState:(_state,_title,url)=>replacements.push(url)};
const find=(node,predicate)=>{if(!node||typeof node!=='object')return null;if(Array.isArray(node)){for(const child of node){const hit=find(child,predicate);if(hit)return hit;}return null;}if(predicate(node))return node;return find(node.props?.children,predicate);};
function page({search='',state=saved,loading=false,error='',visible=true}={}){
 globalThis.location={search,pathname:'/diamond/',origin:'https://game.test'};
 let cursor=0,tree,reloads=0,careerReads=0;const slots=[],active=[];
 const hooks={...React,useState(initial){const index=cursor++;if(!(index in slots))slots[index]=typeof initial==='function'?initial():initial;return[slots[index],value=>{slots[index]=typeof value==='function'?value(slots[index]):value;}];},useRef(initial){const index=cursor++;return slots[index]??(slots[index]={current:initial});},useCallback:fn=>fn,useEffect(){}};
 const marker=name=>()=>React.createElement('span',{'data-test-surface':name},name);
 const Panel=props=>React.createElement('div',{'data-save-dialog':true,'data-has-data':props.hasData},'SAVE_DIALOG');
 const client={state,career:{player:null},roster:null,busy:false,loading,error,clock:{current:0},command:async()=>state,reload:async()=>{reloads++;return state;},refreshCareer:async()=>{careerReads++;},getSaveCode:async()=>({code:null,hasData:!!state?.save,updatedAt:null}),saveCode:async()=>({code:'TEST',hasData:true,updatedAt:0}),loadSaveCode:async()=>{},setError(){}};
 const load=loader({react:hooks,'../lib/game-visibility':{useGameVisibility:()=>visible},'../lib/season-client':{useSeasonClient:value=>{active.push(value);return client;}},'../lib/season-presentation':{useSeasonPresentation:()=>({state,pending:false,masked:false}),seasonPitchKey:()=>null},'./season-match':{__esModule:true,default:marker('FIELD')},'./page':{__esModule:true,default:marker('PRACTICE')},'./career-page':{__esModule:true,default:marker('CAREER')},'./exhibition-page':{__esModule:true,default:marker('FRIENDLY')},'./team-management':{TeamManagement:marker('LINEUP')},'./save-code-panel':{__esModule:true,default:Panel,SaveCodeNotice:()=>null}});
 const Page=load('app/season-page.tsx').default,Title=load('app/title-screen.tsx').default;
 const render=()=>{cursor=0;tree=Page();return renderToStaticMarkup(tree);};
 return{render,active,setVisible:value=>{visible=value;},get reloads(){return reloads;},get careerReads(){return careerReads;},title:()=>find(tree,e=>e.type===Title),primary:()=>find(Title(find(tree,e=>e.type===Title).props),e=>e.type==='button'&&e.props.className==='title-primary'),brand:()=>find(tree,e=>e.props?.className==='season-brand'),saveButton:()=>find(tree,e=>e.props?.className==='save-code-header-button'),dialog:()=>find(tree,e=>e.type===Panel)};
}
try{
 const original=JSON.stringify(saved),p=page();let markup=p.render();
 assert(markup.includes('diamond-title-heading')&&markup.includes('이어하기'));assert(!markup.includes('data-test-surface'),'Title does not render any gameplay, practice, or career surface');assert.equal(p.active.at(-1),false,'Title never enables season polling');assert.equal(p.reloads,0);
 for(const label of ['친선 경기','마이 플레이어','타석 연습','저장·불러오기'])assert(markup.includes(label));
 p.saveButton().props.onClick();markup=p.render();assert(markup.includes('SAVE_DIALOG'));assert.equal(p.dialog().props.hasData,true);assert.equal(p.active.at(-1),false);p.dialog().props.onClose();p.render();
 p.primary().props.onClick();markup=p.render();assert(markup.includes('data-test-surface="FIELD"'));assert.equal(p.active.at(-1),true);assert.equal(p.reloads,1,'Continue refreshes the existing snapshot');
 p.setVisible(false);p.render();assert.equal(p.active.at(-1),false,'A hidden parent page disables season polling');p.setVisible(true);p.render();assert.equal(p.active.at(-1),true,'Returning to the parent game tab resumes polling');
 p.saveButton().props.onClick();p.render();assert.equal(p.active.at(-1),false,'Save dialog suspends polling while a match is open');p.dialog().props.onClose();p.render();assert.equal(p.active.at(-1),true);
 p.brand().props.onClick({preventDefault(){}});markup=p.render();assert(markup.includes('diamond-title-heading'));assert(!markup.includes('data-test-surface="FIELD"'));assert.equal(p.active.at(-1),false);assert.equal(replacements.at(-1),'/diamond/');assert.equal(JSON.stringify(saved),original,'Opening and leaving the title does not rewrite progress');
 for(const [tab,marker] of [['friendly','FRIENDLY'],['career','CAREER'],['practice','PRACTICE']]){const mode=page();mode.render();mode.title().props.onNavigate(tab);assert(mode.render().includes(`data-test-surface="${marker}"`));assert.equal(mode.active.at(-1),false);}
 for(const [search,marker] of [['?room=FABCDEFG','FRIENDLY'],['?match=ABCDEFGH','PRACTICE']]){const invited=page({search});const html=invited.render();assert(html.includes(`data-test-surface="${marker}"`));assert(!html.includes('diamond-title-heading'));assert.equal(invited.active.at(-1),false);invited.brand().props.onClick({preventDefault(){}});assert(invited.render().includes('diamond-title-heading'));assert.equal(replacements.at(-1),'/diamond/');}
 const fresh=page({state:null});assert(fresh.render().includes('리그 시작'));fresh.primary().props.onClick();assert(fresh.render().includes('구단 선택'),'New league enters the existing setup without a destructive reset');
 const cold=page({state:null,loading:true,error:'offline'});markup=cold.render();assert(markup.includes('diamond-title-heading')&&markup.includes('저장 기록 확인 중'));assert(!markup.includes('season-loading'),'A slow initial connection never replaces the title');assert.equal(cold.primary().props.disabled,true);cold.saveButton().props.onClick();markup=cold.render();assert(markup.includes('SAVE_DIALOG'));assert.equal(cold.dialog().props.hasData,false,'An empty/offline profile can still open restore');
 const Title=plain('app/title-screen.tsx').default,actions=[],props={onNavigate:tab=>actions.push(tab),onOpenSave:()=>actions.push('save'),onRetry:()=>actions.push('retry'),loading:false,error:'',savedTeam:null,careerName:null};
 const title=Title(props);find(title,e=>e.props?.className==='title-primary').props.onClick();for(const button of find(title,e=>e.props?.className==='title-modes').props.children)button.props.onClick();find(title,e=>e.type==='button'&&e.props['aria-haspopup']==='dialog').props.onClick();assert.deepEqual(actions,['game','friendly','career','practice','save'],'Every displayed title action reaches its intended screen');
 const titleMarkup=renderToStaticMarkup(React.createElement(Title,props));assert(!/<canvas|<img|<iframe/.test(titleMarkup),'The title uses a lightweight native vector stadium');assert(titleMarkup.includes('aria-hidden="true"')&&titleMarkup.includes('focusable="false"'),'Decorative field is hidden from keyboard and screen readers');
 let phase='day';
 const DayTitle=loader({'../lib/use-korea-daylight':{useKoreaTimeOfDay:()=>phase}})('app/title-screen.tsx').default;
 const dayMarkup=renderToStaticMarkup(React.createElement(DayTitle,props));
 assert(dayMarkup.includes('data-time-of-day="day"')&&dayMarkup.includes('title-day-sky')&&dayMarkup.includes('UNDER THE BLUE SKY'));
 assert(!dayMarkup.includes('title-night-stars')&&!dayMarkup.includes('title-floodlight-beams')&&!dayMarkup.includes('title-floodlight-glow'),'Day mode removes stars and illuminated floodlights');
 phase='night';const nightMarkup=renderToStaticMarkup(React.createElement(DayTitle,props));
 assert(nightMarkup.includes('data-time-of-day="night"')&&nightMarkup.includes('title-night-stars')&&nightMarkup.includes('UNDER THE LIGHTS'));
 assert(nightMarkup.includes('title-floodlight-beams')&&nightMarkup.includes('title-floodlight-glow')&&!nightMarkup.includes('title-day-sky'),'Night mode restores stadium lighting');
 for(const markup of[dayMarkup,nightMarkup])for(const text of['리그 시작','친선 경기','마이 플레이어','타석 연습','저장·불러오기'])assert(markup.includes(text),'Changing daylight never changes navigation or save actions');
 phase='day';assert.equal(renderToStaticMarkup(React.createElement(DayTitle,props)),dayMarkup,'A phase round trip restores the same title without touching saved progress');
}finally{Object.assign(globalThis,globals);}
console.log('PASS title navigation: no gameplay mount or polling, invitations, continue without reset, all menu actions, loading/offline restore, modal suspension, day/night vector sky and unchanged navigation across phase transitions');
