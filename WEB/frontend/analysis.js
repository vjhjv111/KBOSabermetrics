'use strict';

// Analysis owns its requests and animation lifecycle; every value comes from the API.
const analysisTabs=[
  ['zones','공략 지도','구종과 카운트에 따라 달라지는 타자·투수의 코스별 결과를 확인하세요.'],
  ['sequences','볼배합','연속으로 던진 두 구종의 빈도와 결과를 살펴보세요.'],
  ['trend','최근 변화','경기별 성적과 이동 평균으로 시즌 안의 변화를 확인하세요.'],
  ['times','투구수·재대면','투구 수가 늘거나 같은 타자를 다시 만날 때의 성적을 비교하세요.'],
  ['workload','불펜 사용량','최근 등판과 휴식일을 함께 보고 팀 투수의 사용량을 확인하세요.'],
  ['expectancy','기대득점·주루','주자·아웃 상황별 기대득점과 실제 작전·주루 결과를 살펴보세요.'],
  ['replay','투구 재현','실제 타석을 고르고 기록된 투구 궤적을 천천히 재생해 보세요.'],
  ['provenance','기록 대조','서로 다른 수집·집계 자료의 기록과 누락 여부를 비교하세요.']
];
const analysisState={section:'zones',page:1,seq:0,controller:null,catalogKey:'',catalog:null,raf:0,initialized:false,gameId:'',paId:'',result:null};
const analysisRoot=text('main','','analysis-page');analysisRoot.id='analysis-page';analysisRoot.hidden=true;
$('workspace').before(analysisRoot);
const analysisNav=text('button','분석실','room');analysisNav.type='button';analysisNav.dataset.route='analysis';
analysisNav.onclick=()=>{if(location.hash==='#analysis')analysisRoute();else location.hash='analysis';};
document.querySelector('.room-nav').append(analysisNav);
window.addEventListener('hashchange',analysisRoute);
document.addEventListener('visibilitychange',()=>{if(document.hidden)analysisStopPlayback();});

function analysisActive(){const p=new URLSearchParams(location.hash.slice(1));return location.hash==='#analysis'||p.has('analysis');}
function analysisButton(label,handler,className='button outline'){const b=text('button',label,className);b.type='button';if(handler)b.onclick=handler;return b;}
function analysisField(key,label,entries,type='select'){
  const wrap=text('label');wrap.id=`an-field-${key}`;wrap.append(text('span',label));
  const input=document.createElement(type==='select'?'select':'input');input.id=`an-${key}`;
  if(type==='select')for(const [value,title] of entries??[])input.add(new Option(title,String(value)));else input.type=type;
  wrap.append(input);if(key==='team')colorTeamSelect(input);return wrap;
}
function analysisInitialize(){
  if(analysisState.initialized)return;analysisState.initialized=true;
  const hero=text('header','','analysis-hero'),copy=text('div');copy.append(text('p','KBO DATA LAB','analysis-eyebrow'),text('h1','분석실'));
  const sub=text('p','','analysis-subtitle');sub.id='analysis-subtitle';copy.append(sub);
  const badge=text('span',`${state.catalog.games.toLocaleString('ko-KR')}경기 · 수집된 DB 기준`,'analysis-data-badge');hero.append(copy,badge);analysisRoot.append(hero);
  const tabs=text('nav','','analysis-tabs');tabs.setAttribute('aria-label','분석 종류');tabs.id='analysis-tabs';
  for(const [key,title] of analysisTabs){const link=text('a',title);link.href=`#analysis=${key}`;link.dataset.section=key;tabs.append(link);}analysisRoot.append(tabs);
  const form=text('form','','analysis-filter');form.id='analysis-form';form.autocomplete='off';
  const years=[...state.catalog.years].sort((a,b)=>b-a).map(y=>[y,String(y)]);
  form.append(analysisField('year','시즌',years),analysisField('team','팀',[['','전체 팀'],...state.catalog.teams.map(t=>[t,teamNames[t]??t])]),analysisField('role','선수 구분',[['pitcher','투수'],['batter','타자']]));
  const player=analysisField('code','선수',[['','선수를 불러오는 중…']]);player.classList.add('analysis-player');form.append(player);
  form.append(analysisField('pitchType','구종',[['','전체 구종']]),analysisField('stance','타자 타석',[['','전체'],['L','좌타'],['R','우타']]),analysisField('count','투구 전 카운트',[['','전체'],...[0,1,2,3].flatMap(b=>[0,1,2].map(s=>[`${b}-${s}`,`${b}-${s}`]))]));
  form.append(analysisField('start','시작일',null,'date'),analysisField('end','종료일',null,'date'),analysisField('window','이동 평균',[['5','최근 5경기'],['10','최근 10경기'],['15','최근 15경기'],['20','최근 20경기']]));
  const game=analysisField('gameId','경기 ID (선택)',null,'text');game.querySelector('input').maxLength=40;game.querySelector('input').placeholder='비우면 선수의 최근 타석';form.append(game);
  const actions=text('div','','analysis-filter-actions'),submit=text('button','분석 조회','button primary');submit.type='submit';submit.id='an-submit';
  const reset=analysisButton('조건 초기화',()=>{for(const id of ['pitchType','stance','count','start','end','gameId'])$(`an-${id}`).value='';$('an-window').value='5';analysisState.page=1;analysisState.gameId=analysisState.paId='';analysisLoad();});
  const hint=text('p','조건을 고른 뒤 분석 조회를 누르세요. 기록이 없는 값은 —로 표시합니다.');actions.append(submit,reset,hint);form.append(actions);analysisRoot.append(form);
  const status=text('div','','analysis-status');status.id='analysis-status';status.setAttribute('role','status');status.setAttribute('aria-live','polite');analysisRoot.append(status);
  const content=text('div');content.id='analysis-content';analysisRoot.append(content);
  const pages=text('div','','analysis-pagination');pages.id='analysis-pagination';pages.hidden=true;
  const prev=analysisButton('← 이전',()=>{analysisState.page=Math.max(1,analysisState.page-1);analysisLoad(false);});prev.id='an-prev';
  const current=text('span');current.id='an-page';const next=analysisButton('다음 →',()=>{analysisState.page++;analysisLoad(false);});next.id='an-next';pages.append(prev,current,next);analysisRoot.append(pages);
  form.onsubmit=e=>{e.preventDefault();if(!form.reportValidity())return;analysisState.page=1;analysisState.gameId=$('an-gameId').value.trim();analysisState.paId='';analysisLoad();};
  for(const id of ['year','team','role'])$(`an-${id}`).onchange=()=>{analysisState.catalogKey='';analysisState.page=1;analysisState.gameId=analysisState.paId='';$('an-gameId').value='';analysisLoad();};
  $('an-code').onchange=()=>{analysisState.gameId=analysisState.paId='';$('an-gameId').value='';};
}
function analysisConfigure(){
  const section=analysisState.section;const tab=analysisTabs.find(t=>t[0]===section);$('analysis-subtitle').textContent=tab[2];
  for(const link of $('analysis-tabs').querySelectorAll('a'))link.setAttribute('aria-current',link.dataset.section===section?'page':'false');
  const pitcherOnly=['times','workload'].includes(section);if(pitcherOnly&&$('an-role').value!=='pitcher'){$('an-role').value='pitcher';analysisState.catalogKey='';}$('an-role').disabled=pitcherOnly;
  const pitchFilters=['zones','sequences','trend','times'].includes(section);
  for(const id of ['pitchType','stance','count'])$('an-field-'+id).hidden=!pitchFilters;
  $('an-field-window').hidden=section!=='trend';$('an-field-gameId').hidden=!['replay','provenance'].includes(section);
  $('an-field-start').hidden=section==='workload';
  $('an-field-code').hidden=section==='expectancy';$('an-field-role').hidden=section==='expectancy';
  document.title=`${tab[1]} · 분석실 | KBO Sabermetrics`;
}
async function analysisRoute(){
  analysisState.controller?.abort();++analysisState.seq;analysisStopPlayback();
  const active=analysisActive();analysisRoot.hidden=!active;syncRoomNavigation();if(!active||!state.catalog)return;
  for(const id of ['workspace','home-page','player-page','team-page','game-page','diamond-page']){const el=$(id);if(el)el.hidden=true;}
  abortQuery();if(typeof diamondVisibility==='function')diamondVisibility();analysisInitialize();
  const requested=new URLSearchParams(location.hash.slice(1)).get('analysis');analysisState.section=analysisTabs.some(t=>t[0]===requested)?requested:'zones';
  analysisState.page=1;analysisState.paId='';analysisConfigure();await analysisLoad();
}
function analysisRequest(section=analysisState.section){
  const r={section,year:Number($('an-year').value),team:$('an-team').value,role:$('an-role').value,code:$('an-code').value||'',page:analysisState.page,window:Number($('an-window').value)||5};
  for(const key of ['start','end','pitchType','stance','count'])if(!$(`an-field-${key}`).hidden&&$(`an-${key}`).value)r[key]=$(`an-${key}`).value;
  if(['replay','provenance'].includes(section)){r.gameId=analysisState.gameId||$('an-gameId').value.trim();r.paId=analysisState.paId;}
  if(section==='expectancy')r.code='';return r;
}
async function analysisCatalog(signal,seq){
  const key=[$('an-year').value,$('an-team').value,$('an-role').value,analysisState.section==='workload'].join('|');if(key===analysisState.catalogKey)return;
  const requested=analysisRequest('catalog');requested.code='';requested.page=1;
  // Selector membership is season-wide; narrowing dates must not strand a saved player.
  requested.start=requested.end=requested.pitchType=requested.stance=requested.count='';
  let catalog=await api('/api/analysis',requested,signal);if(seq!==analysisState.seq)return;
  // The catalog has its own small pages; collect them before displaying a complete selector.
  let attempts=0;while(catalog.hasMore&&attempts++<3){requested.page++;const more=await api('/api/analysis',requested,signal);if(seq!==analysisState.seq)return;for(const table of catalog.tables??[]){const addition=more.tables?.find(t=>t.key===table.key);if(addition)table.rows.push(...addition.rows);}catalog.hasMore=more.hasMore;}
  analysisState.catalog=catalog;analysisState.catalogKey=key;
  const players=analysisRows(catalog,'players'),oldCode=$('an-code').value,allAllowed=analysisState.section==='workload';
  $('an-code').replaceChildren();if(allAllowed)$('an-code').add(new Option('전체 투수',''));
  const seen=new Set();for(const player of players){if(seen.has(player.Code))continue;seen.add(player.Code);$('an-code').add(new Option(`${player.Name} · ${teamNames[player.Team]??player.Team??''} (${player.Code})`,player.Code));}
  if(players.some(p=>String(p.Code)===oldCode))$('an-code').value=oldCode;else if(allAllowed)$('an-code').value='';else if(!players.length)$('an-code').add(new Option('조건에 맞는 선수 없음',''));
  const oldType=$('an-pitchType').value;const types=analysisRows(catalog,'pitchTypes');$('an-pitchType').replaceChildren(new Option('전체 구종',''));for(const row of types)if(row.Type)$('an-pitchType').add(new Option(row.Type,row.Type));if(types.some(r=>r.Type===oldType))$('an-pitchType').value=oldType;
}
async function analysisLoad(clear=true){
  if(!analysisActive())return;analysisState.controller?.abort();analysisStopPlayback();const controller=new AbortController();analysisState.controller=controller;const seq=++analysisState.seq;
  $('analysis-status').className='analysis-status';$('analysis-status').textContent='선택한 기록을 분석하고 있습니다…';$('analysis-form').setAttribute('aria-busy','true');$('an-submit').disabled=true;$('analysis-pagination').hidden=true;
  if(clear)$('analysis-content').replaceChildren();
  try{
    await analysisCatalog(controller.signal,seq);if(seq!==analysisState.seq||!analysisActive())return;
    const r=analysisRequest();if(r.start&&r.end&&r.start>r.end)throw new Error('종료일은 시작일 이후로 선택해 주세요.');
    if(['zones','sequences','trend','times'].includes(r.section)&&!r.code)throw new Error('이 조건에 수집된 선수가 없습니다. 팀이나 시즌을 바꿔 주세요.');
    const data=await api('/api/analysis',r,controller.signal);if(seq!==analysisState.seq||!analysisActive())return;
    analysisState.result=data;analysisRender(data);
    const selected=$('an-code').selectedOptions[0]?.textContent??'';
    const status=$('analysis-status'),selectedPlayer=analysisRows(analysisState.catalog??{},'players').find(p=>String(p.Code)===r.code);
    status.replaceChildren(document.createTextNode(`${r.year} 정규시즌 · `));
    if(r.section==='expectancy'){if(r.team)status.append(teamNameNode(r.team));else status.append(document.createTextNode('리그 전체'));}
    else if(selectedPlayer)status.append(document.createTextNode(`${selectedPlayer.Name} · `),teamNamesNode(selectedPlayer.Team));else status.append(document.createTextNode(selected));
    status.append(document.createTextNode(' · 조회 완료'));
    $('an-page').textContent=`${data.page??analysisState.page} 페이지`;$('an-prev').disabled=(data.page??analysisState.page)<=1;$('an-next').disabled=!data.hasMore;$('analysis-pagination').hidden=!data.hasMore&&analysisState.page<=1;
  }catch(e){if(e.name==='AbortError'||seq!==analysisState.seq)return;const status=$('analysis-status');status.className='analysis-status analysis-error';status.replaceChildren(text('span',e.message),analysisButton('다시 시도',()=>analysisLoad()));}
  finally{if(seq===analysisState.seq){$('an-submit').disabled=false;$('analysis-form').setAttribute('aria-busy','false');}}
}
function analysisRows(data,key){return data.tables?.find(t=>t.key===key)?.rows??[];}
function analysisFormat(value,format='text'){
  if(value===null||value===undefined||value==='')return '—';
  if(format==='text')return String(value);
  const n=Number(value);if(!Number.isFinite(n))return String(value);
  if(format==='integer')return n.toLocaleString('ko-KR',{maximumFractionDigits:0});
  if(format==='percent')return n.toFixed(1)+'%';if(format==='rate')return n.toFixed(3);
  return n.toLocaleString('ko-KR',{minimumFractionDigits:1,maximumFractionDigits:2});
}
function analysisCard(title){const card=text('section','','analysis-card');card.append(text('h2',title));return card;}
function analysisRender(data){
  analysisStopPlayback();const root=$('analysis-content');root.replaceChildren();
  if(data.summary?.length){const summary=text('div','','analysis-summary');for(const metric of data.summary){const item=text('div','','analysis-metric');item.append(text('span',metric.label),text('strong',metric.value??'—'));summary.append(item);}root.append(summary);}
  const renderers={zones:analysisZones,sequences:analysisSequences,trend:analysisTrend,workload:analysisWorkload,times:analysisTimes,expectancy:analysisExpectancy,replay:analysisReplay};
  if(renderers[data.section])renderers[data.section](data,root);
  if(data.notes?.length){const notes=text('aside','','analysis-notes');notes.setAttribute('aria-label','해석과 데이터 안내');for(const note of data.notes)notes.append(text('p',note,'analysis-note'));root.append(notes);}
  for(const table of data.tables??[])root.append(analysisTable(table));
  if(!(data.tables??[]).some(t=>t.rows?.length)&&!data.chart?.pitches?.length)root.append(text('p','조건에 맞는 기록이 없습니다. 선수·기간을 바꿔 다시 조회해 보세요.','analysis-empty'));
}
function analysisTable(table){
  const card=analysisCard(table.title||table.key),wrap=text('div','','analysis-table-wrap');wrap.tabIndex=0;wrap.setAttribute('role','region');wrap.setAttribute('aria-label',`${table.title||table.key} · 좌우로 스크롤`);
  const grid=document.createElement('table'),head=document.createElement('thead'),header=document.createElement('tr');
  const replay=analysisState.section==='replay'&&table.key==='appearances';
  const provenance=analysisState.section==='provenance'&&table.key==='games';
  if(replay||provenance){const th=text('th',replay?'재현':'기록 대조');th.scope='col';header.append(th);}
  for(const c of table.columns??[]){const th=text('th',c.label||c.key);th.scope='col';header.append(th);}head.append(header);grid.append(head);
  const body=document.createElement('tbody');for(const row of table.rows??[]){const tr=document.createElement('tr');
    if(replay){const td=document.createElement('td');const b=analysisButton('타석 보기',()=>{analysisState.gameId=String(row.GameId??'');analysisState.paId=String(row.PaId??'');$('an-gameId').value=analysisState.gameId;analysisState.page=1;analysisLoad();},'analysis-row-action');b.setAttribute('aria-label',`${row.Date??''} ${row.Batter??''} 타석 재현`);td.append(b);tr.append(td);}
    if(provenance){const td=document.createElement('td');const b=analysisButton('이 경기 대조',()=>{analysisState.gameId=String(row.GameId??'');analysisState.paId='';$('an-gameId').value=analysisState.gameId;analysisState.page=1;analysisLoad();},'analysis-row-action');b.setAttribute('aria-label',`${row.Date??''} ${row.Away??''} 대 ${row.Home??''} 기록 대조`);td.append(b);tr.append(td);}
    for(const c of table.columns??[]){const td=text('td',analysisFormat(row[c.key],c.format));if(['Team','TeamCode','Opponent','Home','Away'].includes(c.key)&&row[c.key])td.replaceChildren(teamNamesNode(row[c.key]));if(c.key==='GameId'&&row[c.key]){const a=text('a',String(row[c.key]));a.href=`#game=${encodeURIComponent(row[c.key])}`;td.replaceChildren(a);}if(c.key==='Url'&&row[c.key]){try{const url=new URL(String(row[c.key]));if(url.protocol==='https:'&&url.hostname==='www.koreabaseball.com'){const a=text('a','KBO 원문 보기');a.href=url.href;a.target='_blank';a.rel='noopener noreferrer';td.replaceChildren(a);}}catch{ /* Preserve the plain source text if a URL is unavailable. */ }}tr.append(td);}body.append(tr);
  }grid.append(body);wrap.append(grid);card.append(wrap);
  if(!table.rows?.length)card.append(text('p','이 조건에서 표시할 기록이 없습니다.','analysis-note'));
  else card.append(text('p',`${table.rows.length.toLocaleString('ko-KR')}행 · 표를 좌우로 밀어 전체 항목을 확인하세요.`,'analysis-note'));return card;
}
function analysisMetricSelect(label,items,change){const wrap=text('label','','analysis-chart-control');wrap.append(text('span',label));const select=document.createElement('select');for(const [key,title] of items)select.add(new Option(title,key));select.onchange=()=>change(select.value);wrap.append(select);return {wrap,select};}
function analysisZones(data,root){
  const rows=analysisRows(data,'zones');if(!rows.length)return;const card=analysisCard('어느 코스에서 결과가 달라질까?');
  card.append(text('p','각 칸을 누르면 해당 코스의 표본과 결과가 표시됩니다. 진한 칸일수록 선택한 값이 큽니다.','analysis-note'));
  const layout=text('div','','analysis-zone-layout'),grid=text('div','','analysis-zone-grid'),detail=text('div','','analysis-zone-detail');detail.setAttribute('aria-live','polite');
  layout.append(grid,detail);const metrics=[['WhiffPct','헛스윙률 · 스윙 대비'],['SwingPct','스윙률 · 투구 대비'],['Pitches','투구 수'],['SLG','장타율 · 최종구 타석']];let selected=null;
  function draw(key){grid.replaceChildren();const format=key==='Pitches'?'integer':key==='SLG'?'rate':'percent';const values=rows.map(r=>r[key]).filter(v=>v!=null&&Number.isFinite(Number(v))).map(Number);const max=Math.max(...values,0);
    for(let z=4;z>=0;z--)for(let x=0;x<5;x++){const row=rows.find(r=>Number(r.XBin)===x&&Number(r.ZBin)===z),v=row?.[key],empty=v==null;const bucket=empty?'empty':max>0?Math.max(1,Math.min(5,Math.ceil(Number(v)/max*5))):1;
      const b=analysisButton('',null,`analysis-zone-cell heat-${bucket}${x>0&&x<4&&z>0&&z<4?' zone-inside':''}`);b.append(text('strong',analysisFormat(v,format)),text('span',`${analysisFormat(row?.Pitches??0,'integer')}구`));
      const where=`${['왼쪽 바깥','왼쪽','가운데','오른쪽','오른쪽 바깥'][x]} · ${['아래 바깥','낮은','중간','높은','위 바깥'][z]} 코스`;
      b.setAttribute('aria-label',`${where}: ${analysisFormat(v,format)}, ${row?.Pitches??0}구`);b.setAttribute('aria-pressed','false');
      b.onclick=()=>{selected=[x,z];for(const other of grid.children)other.setAttribute('aria-pressed','false');b.setAttribute('aria-pressed','true');detail.replaceChildren(text('h3',where));const dl=document.createElement('dl');for(const [k,title,f] of [['Pitches','투구 수','integer'],['Swings','스윙 수','integer'],['Whiffs','헛스윙 수','integer'],['SwingPct','스윙률','percent'],['WhiffPct','헛스윙률','percent'],['PA','최종구 타석','integer'],['SLG','최종구 장타율','rate']])dl.append(text('dt',title),text('dd',analysisFormat(row?.[k],f)));detail.append(dl);};grid.append(b);if(selected?.[0]===x&&selected?.[1]===z)b.click();
    }if(!selected)grid.children[12]?.click();
  }
  const select=analysisMetricSelect('지도에 표시할 기록',metrics,draw);card.append(select.wrap,layout,text('p','회색 테두리 안쪽 3×3칸이 타자별 존입니다. 좌우는 원본 좌표 기준이며 정확한 조준점이나 공식 스트라이크 판정을 의미하지 않습니다.','analysis-note'));root.append(card);draw(select.select.value);
}
function analysisSequences(data,root){
  const rows=analysisRows(data,'sequences');if(!rows.length)return;const card=analysisCard('자주 연결하는 두 구종'),list=text('div','','analysis-sequence-list');
  for(const row of [...rows].sort((a,b)=>Number(b.Pairs)-Number(a.Pairs)).slice(0,8)){const item=text('div','','analysis-sequence-item'),path=text('div','','analysis-sequence-path');path.append(text('span',row.Previous),text('span','→'),text('span',row.Current));item.append(path,text('p',`${analysisFormat(row.Pairs,'integer')}회 · 헛스윙 ${analysisFormat(row.WhiffPct,'percent')} · 뒤 공 구속 차이 ${analysisFormat(row.SpeedGap,'decimal')} km/h`,'analysis-sequence-meta'));list.append(item);}card.append(text('p','같은 타석 안에서 연속 투구한 구종입니다. 결과는 뒤에 던진 공 기준이며 빈도순으로 표시합니다.','analysis-note'),list);root.append(card);
}
function analysisSvg(name,attrs={}){const e=document.createElementNS('http://www.w3.org/2000/svg',name);for(const [k,v] of Object.entries(attrs))e.setAttribute(k,String(v));return e;}
function analysisLineChart(rows,key,label,format='percent'){
  const width=Math.min(840,Math.max(320,analysisRoot.clientWidth-80));
  const svg=analysisSvg('svg',{viewBox:`0 0 ${width} 300`,class:'analysis-chart',role:'img','aria-label':`${label} 경기별 변화`}),valid=rows.map((r,i)=>({row:r,i,v:r[key]==null?null:Number(r[key])})).filter(r=>r.v!==null&&Number.isFinite(r.v));
  if(!valid.length){const t=analysisSvg('text',{x:width/2,y:140,'text-anchor':'middle'});t.textContent='이 기록을 계산할 표본이 없습니다.';svg.append(t);return svg;}
  let min=Math.min(...valid.map(r=>r.v)),max=Math.max(...valid.map(r=>r.v));const pad=(max-min)*.15||Math.max(Math.abs(max)*.03,1);min=Math.max(0,min-pad);max+=pad;
  const x=i=>66+i/Math.max(1,rows.length-1)*(width-88),y=v=>252-(v-min)/(max-min)*224;
  for(let i=0;i<=4;i++){const value=min+(max-min)*i/4,yy=y(value);svg.append(analysisSvg('line',{x1:66,y1:yy,x2:width-22,y2:yy,class:'chart-grid'}));const t=analysisSvg('text',{x:56,y:yy+4,'text-anchor':'end'});t.textContent=analysisFormat(value,format);svg.append(t);}
  // Missing observations split the line; gaps are never shown as measured values.
  let path='',connected=false;for(let i=0;i<rows.length;i++){const v=rows[i][key]==null?null:Number(rows[i][key]);if(v===null||!Number.isFinite(v)){connected=false;continue;}path+=`${connected?'L':'M'}${x(i)},${y(v)} `;connected=true;}svg.append(analysisSvg('path',{d:path,class:'chart-series'}));
  for(const {row,i,v} of valid){const dot=analysisSvg('circle',{cx:x(i),cy:y(v),r:3.5,class:'chart-dot'}),title=analysisSvg('title');title.textContent=`${row.Date?.slice(0,10)??i+1}: ${analysisFormat(v,format)}`;dot.append(title);svg.append(dot);}
  for(const i of [...new Set(width<500?[0,rows.length-1]:[0,Math.floor((rows.length-1)/2),rows.length-1])]){const t=analysisSvg('text',{x:x(i),y:281,'text-anchor':i===0?'start':i===rows.length-1?'end':'middle'});t.textContent=rows[i]?.Date?.slice(0,10)??'';svg.append(t);}return svg;
}
function analysisTrend(data,root){
  const rows=analysisRows(data,'trend');if(rows.length){const card=analysisCard('최근 경기의 흐름'),plot=text('div'),metrics=[['RollingWhiffPct','이동 헛스윙률'],['RollingKPct','이동 삼진율'],['RollingBBPct','이동 볼넷율'],['WhiffPct','경기별 헛스윙률'],['KPct','경기별 삼진율'],['BBPct','경기별 볼넷율']];const draw=key=>{plot.replaceChildren(analysisLineChart(rows,key,metrics.find(m=>m[0]===key)[1]));};const choice=analysisMetricSelect('추이 지표',metrics,draw);card.append(choice.wrap,plot,text('p','이동 값은 선택한 경기 수의 분자·분모를 합산한 비율입니다. 그래프 점에 마우스를 올리면 날짜와 값이 표시됩니다.','analysis-note'));root.append(card);draw(choice.select.value);}
  const velocity=analysisRows(data,'velocity');if(velocity.length){const card=analysisCard('구종별 구속 변화'),plot=text('div'),types=[...new Set(velocity.map(r=>r.Type))];const draw=type=>{const values=velocity.filter(r=>r.Type===type);plot.replaceChildren(analysisLineChart(values,'RollingVelocity',`${type} 이동 평균 구속`,'decimal'));};const choice=analysisMetricSelect('구속을 볼 구종',types.map(t=>[t,t]),draw);card.append(choice.wrap,plot,text('p','같은 구종의 이동 평균 구속(km/h)입니다. 구속이 없는 공은 평균에서 제외됩니다.','analysis-note'));root.append(card);draw(choice.select.value);}
}
function analysisTimes(data,root){
  for(const [key,title] of [['pitchBands','투구 수 구간별 결과'],['encounters','같은 타자와 재대면할 때']]){const rows=analysisRows(data,key);if(!rows.length)continue;const card=analysisCard(title),metrics=[['WhiffPct','헛스윙률'],['KPct','삼진율'],['BBPct','볼넷율'],['SLG','피장타율']],plot=text('div');
    const draw=metric=>{plot.replaceChildren();const list=text('div','','analysis-sequence-list');for(const row of rows){const item=text('div','','analysis-sequence-item');item.append(text('strong',key==='encounters'?`${row.Group}번째 대면`:`${row.Group}`),text('p',`${metrics.find(m=>m[0]===metric)[1]} ${analysisFormat(row[metric],metric==='SLG'?'rate':'percent')} · ${analysisFormat(row.Pitches,'integer')}구 / ${analysisFormat(row.PA,'integer')}타석`,'analysis-sequence-meta'));list.append(item);}plot.append(list);};const choice=analysisMetricSelect('비교할 결과',metrics,draw);card.append(choice.wrap,plot);root.append(card);draw(choice.select.value);}
}
function analysisWorkload(data,root){
  const daily=analysisRows(data,'daily');if(!daily.length)return;const card=analysisCard('최근 등판 달력'),grid=text('div','','analysis-workload-grid');
  const dates=[...new Set(daily.map(r=>r.Date?.slice(0,10)))].filter(Boolean).sort().slice(-14);
  for(const date of dates){const dayRows=daily.filter(r=>r.Date?.slice(0,10)===date),pitches=dayRows.reduce((sum,r)=>sum+(Number(r.Pitches)||0),0),missing=dayRows.some(r=>r.Pitches==null);const cell=text('div',``,`analysis-workload-day${pitches>=60?' workload-high':''}`),time=text('time',date.slice(5));time.dateTime=date;cell.append(time,text('strong',missing?'—':`${pitches}구`));for(const row of dayRows)cell.append(text('p',`${row.Name} ${analysisFormat(row.Pitches,'integer')}구`));grid.append(cell);}card.append(text('p','현재 응답에서 확인된 최근 7일의 등판 기록입니다. 공백 날짜를 휴식일로 추정하지 않습니다. 아래 표에서 선수별 휴식일과 최근 3·7일 사용량을 확인하세요.','analysis-note'),grid);root.append(card);
}
function analysisExpectancy(data,root){
  const states=data.chart?.states??analysisRows(data,'states').map(r=>({outs:r.Outs,baseMask:r.BaseMask,n:r.N,re:r.RE}));if(!states.length)return;const card=analysisCard('이 상황에서 이닝이 끝날 때까지 몇 점을 냈을까?'),grid=text('div','','analysis-re24-grid');
  grid.append(text('span','주자'),text('span','0아웃'),text('span','1아웃'),text('span','2아웃'));const labels=['주자 없음','1루','2루','1·2루','3루','1·3루','2·3루','만루'];
  for(let base=0;base<8;base++){grid.append(text('span',labels[base]));for(let outs=0;outs<3;outs++){const row=states.find(s=>Number(s.outs)===outs&&Number(s.baseMask)===base),cell=text('div','','analysis-re24-cell');cell.append(text('strong',row?.re==null?'—':Number(row.re).toFixed(2)),text('small',`${analysisFormat(row?.n??0,'integer')}회`));cell.setAttribute('aria-label',`${labels[base]} ${outs}아웃, 기대득점 ${row?.re==null?'자료 없음':Number(row.re).toFixed(2)}점, ${row?.n??0}회`);grid.append(cell);}}
  card.append(text('p','수집된 정규시즌의 실제 결과를 평균한 기대득점(점)입니다. 각 칸 아래는 표본 수입니다.','analysis-note'),grid);root.append(card);
}
function analysisStopPlayback(){if(analysisState.raf)cancelAnimationFrame(analysisState.raf);analysisState.raf=0;analysisState.practice=false;const swing=$('an-replay-swing');if(swing)swing.disabled=true;const play=$('an-replay-play');if(play){play.setAttribute('aria-pressed','false');play.textContent='▶ 재생';}}
function analysisReplay(data,root){
  const chart=data.chart,pitches=chart?.pitches??[];if(!pitches.length)return;const card=analysisCard('실제 타석 투구 궤적');
  card.append(text('p',chart.description||'수집된 궤적 구간만 재현합니다. 기록이 없는 투구는 궤적을 표시하지 않습니다.','analysis-note'));
  const compact=analysisRoot.clientWidth<620;const canvas=document.createElement('canvas');canvas.width=compact?380:960;canvas.height=compact?620:430;canvas.className='analysis-replay-canvas';canvas.setAttribute('role','img');canvas.setAttribute('aria-label','실제 투구의 측면 궤적과 홈플레이트 통과 위치');card.append(canvas);
  const controls=text('div','','analysis-replay-controls'),pitchLabel=text('label','투구 선택','analysis-replay-select'),pitchSelect=document.createElement('select');pitchSelect.setAttribute('aria-label','재현할 투구');
  for(const [i,p] of pitches.entries())pitchSelect.add(new Option(`${p.index??i+1}구 · ${p.type??'구종 미상'} · ${analysisFormat(p.speed,'decimal')} km/h · ${p.result??''}`,String(i)));pitchLabel.append(pitchSelect);
  const prev=analysisButton('이전 공'),play=analysisButton('▶ 재생'),next=analysisButton('다음 공');play.id='an-replay-play';play.setAttribute('aria-pressed','false');
  const speedLabel=text('label','재생 속도'),speed=document.createElement('select');speed.setAttribute('aria-label','재생 속도');for(const value of [.25,.5,1])speed.add(new Option(`${value}×`,String(value)));speedLabel.append(speed);controls.append(pitchLabel,prev,play,next,speedLabel);card.append(controls);
  const slider=document.createElement('input');slider.type='range';slider.min='0';slider.max='1000';slider.value='0';slider.step='1';slider.className='analysis-replay-slider';slider.setAttribute('aria-label','투구 진행 위치');card.append(slider);
  const readout=text('p','','analysis-replay-readout');readout.setAttribute('aria-live','off');card.append(readout);
  card.append(text('p',`${compact?'위는 거리–높이 측면도, 아래는':'왼쪽은 거리–높이 측면도, 오른쪽은'} 홈플레이트 통과 위치입니다. 실제 릴리스 영상이 아니며 원본 PTS의 측정 기준면에서 시작합니다.`,'analysis-note'));
  const practiceControls=text('div','','analysis-replay-controls'),practice=analysisButton('이 공 타이밍 연습'),swing=analysisButton('스윙');swing.id='an-replay-swing';swing.disabled=true;const practiceStatus=text('p','연습을 시작하면 1초 준비 후 현재 공이 실제 속도(1×)로 출발합니다. 도착에 맞춰 스윙을 눌러보세요.','analysis-note');practiceStatus.setAttribute('aria-live','polite');practiceControls.append(practice,swing);card.append(practiceControls,practiceStatus,text('p','연습 결과는 홈 기준면 도착 시간과 입력 시간의 차이(ms)입니다. 실제 타구·안타 판정이나 선수 능력 평가는 아닙니다.','analysis-note'));root.append(card);
  const ctx=canvas.getContext('2d');if(!ctx){readout.textContent='이 브라우저에서 궤적 그림을 표시할 수 없습니다. 아래 투구 기록을 확인하세요.';return;}
  let chosen=0,progress=0;const validPoints=p=>(p.points??[]).filter(v=>[v.t,v.x,v.y,v.z].every(Number.isFinite));
  function draw(){
    const p=pitches[chosen],points=validPoints(p);ctx.clearRect(0,0,canvas.width,canvas.height);ctx.fillStyle='#f4f7fb';ctx.fillRect(0,0,canvas.width,canvas.height);ctx.font='16px sans-serif';ctx.fillStyle='#40536c';ctx.fillText('거리–높이 · 측면',34,34);ctx.fillText('홈플레이트 통과 위치',compact?34:690,compact?340:34);
    ctx.strokeStyle='#d7e0eb';ctx.lineWidth=1;for(let i=0;i<5;i++){ctx.beginPath();ctx.moveTo(34,(compact?70:92)+i*(compact?45:60));ctx.lineTo(compact?346:626,(compact?70:92)+i*(compact?45:60));ctx.stroke();}
    if(!points.length){ctx.fillStyle='#53647a';ctx.font='16px sans-serif';ctx.fillText('이 공은 궤적 데이터가 없습니다.',compact?44:100,compact?166:218);play.disabled=true;slider.disabled=true;practice.disabled=true;}
    else{play.disabled=false;slider.disabled=false;practice.disabled=false;const first=points[0],last=points.at(-1),minY=Math.min(...points.map(q=>q.y)),maxY=Math.max(...points.map(q=>q.y)),maxZ=Math.max(7,...points.map(q=>q.z));const x=q=>(compact?38:50)+(maxY-q.y)/Math.max(1,maxY-minY)*(compact?305:550),y=q=>(compact?268:370)-q.z/maxZ*(compact?208:290);
      ctx.strokeStyle='#c0cbd9';ctx.lineWidth=2;ctx.beginPath();points.forEach((q,i)=>i?ctx.lineTo(x(q),y(q)):ctx.moveTo(x(q),y(q)));ctx.stroke();
      const t=first.t+(last.t-first.t)*progress;let index=points.findIndex(q=>q.t>=t);if(index<0)index=points.length-1;const a=points[Math.max(0,index-1)],b=points[index],fraction=b.t>a.t?(t-a.t)/(b.t-a.t):0;const q={x:a.x+(b.x-a.x)*fraction,y:a.y+(b.y-a.y)*fraction,z:a.z+(b.z-a.z)*fraction};
      ctx.strokeStyle='#a52861';ctx.lineWidth=3;ctx.beginPath();points.slice(0,index).forEach((v,i)=>i?ctx.lineTo(x(v),y(v)):ctx.moveTo(x(v),y(v)));ctx.lineTo(x(q),y(q));ctx.stroke();ctx.beginPath();ctx.arc(x(q),y(q),8,0,Math.PI*2);ctx.fillStyle='#fff';ctx.fill();ctx.strokeStyle='#a52861';ctx.lineWidth=3;ctx.stroke();
      ctx.fillStyle='#53647a';ctx.font='13px sans-serif';ctx.fillText(`측정 시작 ${maxY.toFixed(1)} ft`,34,compact?298:399);ctx.fillText(`홈 ${minY.toFixed(2)} ft`,compact?258:472,compact?298:399);readout.textContent=`${p.index??chosen+1}구 · ${p.type??'구종 미상'} · ${analysisFormat(p.speed,'decimal')} km/h · ${(t-first.t).toFixed(3)}초 / ${(last.t-first.t).toFixed(3)}초`;
    }
    const bottom=Number(p.bottom),top=Number(p.top),hasZone=p.bottom!=null&&p.top!=null&&top>bottom;const scale=55,center=compact?190:790,zY=z=>(compact?605:365)-z*scale;
    if(hasZone){ctx.fillStyle='#e9edf3';ctx.fillRect(center-.7083*scale,zY(top),1.4166*scale,(top-bottom)*scale);ctx.strokeStyle='#64758b';ctx.lineWidth=2;ctx.strokeRect(center-.7083*scale,zY(top),1.4166*scale,(top-bottom)*scale);}
    if(p.plateX!=null&&p.plateZ!=null&&[Number(p.plateX),Number(p.plateZ)].every(Number.isFinite)){const px=center+Number(p.plateX)*scale,py=zY(Number(p.plateZ));ctx.beginPath();ctx.arc(px,py,7,0,Math.PI*2);ctx.fillStyle=progress>=1?'#a52861':'#bcc6d3';ctx.fill();}
    if(!points.length)readout.textContent=`${p.index??chosen+1}구 · ${p.type??'구종 미상'} · 궤적 자료 없음 · ${p.result??''}`;
    prev.disabled=chosen<=0;next.disabled=chosen>=pitches.length-1;slider.value=String(Math.round(progress*1000));slider.setAttribute('aria-valuetext',`진행 ${(progress*100).toFixed(0)}%`);
  }
  function selectPitch(index){analysisStopPlayback();chosen=Math.max(0,Math.min(pitches.length-1,index));pitchSelect.value=String(chosen);progress=0;draw();}
  pitchSelect.onchange=()=>selectPitch(Number(pitchSelect.value));prev.onclick=()=>selectPitch(chosen-1);next.onclick=()=>selectPitch(chosen+1);
  slider.oninput=()=>{analysisStopPlayback();progress=Number(slider.value)/1000;draw();};speed.onchange=()=>analysisStopPlayback();
  play.onclick=()=>{if(analysisState.raf){analysisStopPlayback();return;}const points=validPoints(pitches[chosen]);if(points.length<2)return;if(progress>=1)progress=0;
    const duration=Math.max(.05,points.at(-1).t-points[0].t),rate=Number(speed.value),start=performance.now()-progress*duration*1000/rate;play.textContent='Ⅱ 일시정지';play.setAttribute('aria-pressed','true');
    const frame=now=>{if(!analysisActive()||document.hidden){analysisStopPlayback();return;}progress=Math.min(1,(now-start)*rate/(duration*1000));draw();if(progress<1)analysisState.raf=requestAnimationFrame(frame);else analysisStopPlayback();};analysisState.raf=requestAnimationFrame(frame);
  };
  let practiceArrival=0;
  practice.onclick=()=>{analysisStopPlayback();const points=validPoints(pitches[chosen]);if(points.length<2)return;const duration=(points.at(-1).t-points[0].t)*1000,start=performance.now()+1000;practiceArrival=start+duration;analysisState.practice=true;progress=0;swing.disabled=false;swing.focus({preventScroll:true});practiceStatus.textContent='준비… 1초 뒤 출발합니다.';
    const frame=now=>{if(!analysisActive()||document.hidden||!analysisState.practice){analysisStopPlayback();return;}progress=Math.max(0,Math.min(1,(now-start)/duration));draw();if(now>=start&&now<practiceArrival)practiceStatus.textContent='투구 중 · 도착 순간에 스윙하세요.';if(now>practiceArrival+750){analysisStopPlayback();practiceStatus.textContent='스윙 입력이 없었습니다. 같은 공으로 다시 연습할 수 있습니다.';return;}analysisState.raf=requestAnimationFrame(frame);};analysisState.raf=requestAnimationFrame(frame);
  };
  swing.onclick=()=>{if(!analysisState.practice)return;const difference=performance.now()-practiceArrival;analysisStopPlayback();practiceStatus.textContent=`홈 기준면 도착보다 ${Math.abs(difference).toFixed(0)} ms ${difference<0?'빨랐습니다':'늦었습니다'}. 같은 공으로 다시 연습할 수 있습니다.`;};
  draw();
}
