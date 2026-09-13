'use strict';

// Batters are compared within one season and one league-wide PA cohort.
const comparisonState={initialized:false,seq:0,controller:null,data:null,selected:[],target:'',axis:'production',team:'',query:'',listLimit:30,point:'',messageTimer:0};
const comparisonAxes={production:{label:'출루와 장타',x:'OBP',y:'SLG'},power:{label:'타율과 순수 장타력',x:'AVG',y:'ISO'},discipline:{label:'볼넷과 삼진',x:'BBPct',y:'KPct'}};
const comparisonLabels={PA:'타석',AB:'타수',H:'안타',TB:'루타',HR:'홈런',BB:'볼넷',HBP:'사구',SO:'삼진',SH:'희생번트',SF:'희생플라이',SB:'도루',AVG:'AVG',OBP:'OBP',SLG:'SLG',OPS:'OPS',ISO:'ISO',BBPct:'BB%',KPct:'K%'};
const comparisonRoot=text('main','','comparison-page');comparisonRoot.id='comparison-page';comparisonRoot.hidden=true;$('workspace').before(comparisonRoot);
const comparisonNav=text('button','비교실','room');comparisonNav.type='button';comparisonNav.dataset.route='comparison';
comparisonNav.onclick=()=>{if(comparisonActive())comparisonRoute();else location.hash='comparison';};document.querySelector('.room-nav').append(comparisonNav);
window.addEventListener('hashchange',comparisonRoute);
function comparisonActive(){return location.hash==='#comparison'||new URLSearchParams(location.hash.slice(1)).has('comparison');}
function comparisonButton(label,handler,className='button outline'){const button=text('button',label,className);button.type='button';button.onclick=handler;return button;}
function comparisonNumber(value){return value===null||value===undefined||value===''||!Number.isFinite(Number(value))?null:Number(value);}
function comparisonFormat(value,key){const n=comparisonNumber(value);if(n===null)return '—';if(['AVG','OBP','SLG','OPS','ISO'].includes(key))return n.toFixed(3);if(['BBPct','KPct'].includes(key))return `${n.toFixed(1)}%`;return n.toLocaleString('ko-KR',{maximumFractionDigits:1});}
function comparisonPlayer(code){return comparisonState.data?.players.find(p=>String(p.code)===String(code));}
function comparisonTeams(player){const wrap=text('span','','comparison-team-names');for(const [index,team] of (player.teams??[]).entries()){if(index)wrap.append(document.createTextNode(' · '));wrap.append(teamNameNode(team));}if(!wrap.childNodes.length)wrap.textContent='팀 정보 없음';return wrap;}
function comparisonPhoto(player){
  const frame=text('div','','comparison-photo'),fallback=text('span',player.name?.slice(0,1)||'KBO');fallback.setAttribute('aria-hidden','true');frame.append(fallback);
  if(player.photoUrl)try{const url=new URL(player.photoUrl,location.origin);if(url.origin===location.origin){const img=document.createElement('img');img.alt='';img.width=72;img.height=88;img.loading='lazy';img.decoding='async';img.className='is-loading';img.onload=()=>{if(!img.isConnected)return;fallback.remove();img.classList.remove('is-loading');};img.onerror=()=>img.remove();frame.append(img);img.src=url.href;}}catch{}
  return frame;
}
function comparisonProfileLink(player){const link=text('a',player.name,'player-link');link.href=`#player=${encodeURIComponent(player.code)}`;return link;}
function comparisonAddButton(player,short=false){const button=comparisonButton('',()=>comparisonToggle(player.code),'comparison-add');button.dataset.compareCode=player.code;button.dataset.short=String(short);return button;}
function comparisonSyncSelection(){
  for(const button of comparisonRoot.querySelectorAll('[data-compare-code]')){const player=comparisonPlayer(button.dataset.compareCode),selected=comparisonState.selected.includes(button.dataset.compareCode);button.classList.toggle('is-selected',selected);button.setAttribute('aria-pressed',String(selected));button.textContent=selected?'선택 해제':button.dataset.short==='true'?'+ 비교':'+ 비교에 추가';button.setAttribute('aria-label',`${player?.name??'선수'} ${selected?'비교에서 제외':'비교에 추가'}`);}
  for(const point of comparisonRoot.querySelectorAll('[data-point-code]')){const selected=comparisonState.selected.includes(point.dataset.pointCode);point.classList.toggle('is-selected',selected);point.setAttribute('aria-pressed',String(selected));}
  $('cmp-selected-count').textContent=`${comparisonState.selected.length} / 4명 선택`;
}
function comparisonMessage(message=''){
  window.clearTimeout(comparisonState.messageTimer);comparisonState.messageTimer=0;
  const node=$('cmp-message');if(!node)return;node.textContent=message;
  if(message)comparisonState.messageTimer=window.setTimeout(()=>{node.textContent='';comparisonState.messageTimer=0;},5000);
}
function comparisonToggle(code){
  code=String(code);if(!comparisonPlayer(code))return;
  const selected=comparisonState.selected.includes(code);
  if(!selected&&comparisonState.selected.length>=4){comparisonMessage('최대 4명까지 비교할 수 있습니다. 선택한 선수 한 명을 먼저 해제해 주세요.');return;}
  comparisonState.selected=selected?comparisonState.selected.filter(c=>c!==code):[...comparisonState.selected,code];
  comparisonMessage(selected?`${comparisonPlayer(code).name} 선수를 비교에서 제외했습니다.`:`${comparisonPlayer(code).name} 선수를 비교에 추가했습니다.`);
  comparisonRenderSelected();comparisonSyncSelection();
}
function comparisonInitialize(){
  if(comparisonState.initialized)return;comparisonState.initialized=true;
  comparisonRoot.innerHTML=`<header class="comparison-hero"><div><p class="player-eyebrow">KBO PLAYER EXPLORER</p><h1>기록으로 찾는 타자의 차이</h1><p>리그에서의 위치를 보고, 선수들을 나란히 비교하고, 비슷한 타격 유형을 찾아보세요.</p></div><span class="comparison-version">타자 비교 · 정규시즌</span></header>
    <form id="cmp-form" class="comparison-filters"><label>시즌<select id="cmp-year"></select></label><label>최소 타석<input id="cmp-min-pa" type="number" min="1" max="1000" step="1" value="100" required inputmode="numeric"></label><button class="button primary" id="cmp-submit" type="submit">비교군 조회</button><p>최소 타석 이상인 리그 전체 타자가 비교 기준입니다.</p></form>
    <div id="cmp-status" class="comparison-status" role="status" aria-live="polite"></div><div id="cmp-content" hidden>
      <div class="comparison-summary"><strong id="cmp-cohort"></strong><span id="cmp-date"></span><span id="cmp-selected-count">0 / 4명 선택</span></div>
      <section class="comparison-card"><div class="comparison-section-heading"><div><p class="comparison-step">01 / 리그 지도</p><h2>어디에 있는 타자일까?</h2></div><label class="comparison-axis-label">지표<select id="cmp-axis"><option value="production">출루와 장타 · OBP × SLG</option><option value="power">타율과 순수 장타력 · AVG × ISO</option><option value="discipline">볼넷과 삼진 · BB% × K%</option></select></label></div>
      <div class="comparison-local-filters"><label>화면에 표시할 팀<select id="cmp-team"></select></label><label>선수 이름 검색<input id="cmp-search" type="search" placeholder="선수명" maxlength="60"></label><p>팀·이름 검색은 지도와 목록만 좁힙니다. 선택한 비교 카드와 리그 전체 백분위·유사도 기준은 유지됩니다.</p></div>
      <div class="comparison-map-layout"><div class="comparison-chart-area"><p id="cmp-chart-caption" class="comparison-caption"></p><div id="cmp-chart"></div><div id="cmp-point" class="comparison-point-detail"></div></div><div class="comparison-picker"><h3>선수 선택 <span id="cmp-visible-count"></span></h3><p>이름 옆 버튼으로 최대 4명을 담으세요.</p><div id="cmp-player-list"></div><button type="button" id="cmp-more" class="comparison-more">선수 더 보기</button></div></div></section>
      <section class="comparison-card"><div class="comparison-section-heading"><div><p class="comparison-step">02 / 나란히 비교</p><h2>같은 기준으로 함께 보기</h2></div></div><p class="comparison-caption">막대는 선택 시즌·최소 타석을 충족한 전체 타자 안의 백분위입니다. 높을수록 좋으며, K%는 낮을수록 높은 백분위가 됩니다.</p><div id="cmp-selected" class="comparison-selected-grid"></div><div id="cmp-comparison-table"></div></section>
      <section class="comparison-card"><div class="comparison-section-heading"><div><p class="comparison-step">03 / 비슷한 타격 유형</p><h2>어떤 선수가 닮았을까?</h2></div><label class="comparison-target-label">기준 선수<select id="cmp-target"></select></label></div><p class="comparison-caption">AVG·ISO·BB%·K% 백분위 차이를 같은 비중으로 비교합니다. 차이가 작을수록 기록의 유형이 비슷하며, 미래 성적이나 동일한 실력을 의미하지 않습니다.</p><div id="cmp-similar" class="comparison-similar-grid"></div></section>
      <aside id="cmp-notes" class="comparison-notes" aria-label="계산과 데이터 안내"></aside></div><p id="cmp-message" class="comparison-message" role="status" aria-live="polite"></p>`;
  $('cmp-year').replaceChildren(...[...state.catalog.years].sort((a,b)=>b-a).map(year=>new Option(String(year),String(year))));
  $('cmp-team').replaceChildren(new Option('전체 팀',''),...state.catalog.teams.map(team=>new Option(teamNames[team]??team,team)));colorTeamSelect($('cmp-team'));
  $('cmp-form').onsubmit=event=>{event.preventDefault();if($('cmp-form').reportValidity())comparisonLoad();};
  $('cmp-year').onchange=()=>comparisonLoad();
  $('cmp-axis').onchange=()=>{comparisonState.axis=$('cmp-axis').value;comparisonRenderScatter();};
  $('cmp-team').onchange=()=>{comparisonState.team=$('cmp-team').value;comparisonState.listLimit=30;comparisonRenderMap();};
  $('cmp-search').oninput=()=>{comparisonState.query=$('cmp-search').value.trim();comparisonState.listLimit=30;comparisonRenderMap();};
  $('cmp-more').onclick=()=>{comparisonState.listLimit+=30;comparisonRenderList();};
  $('cmp-target').onchange=()=>{comparisonState.target=$('cmp-target').value;comparisonLoad();};
}
async function comparisonRoute(){
  comparisonState.controller?.abort();++comparisonState.seq;comparisonMessage();const active=comparisonActive();comparisonRoot.hidden=!active;syncRoomNavigation();if(!active||!state.catalog)return;
  for(const id of ['workspace','home-page','player-page','team-page','game-page','diamond-page','analysis-page']){const element=$(id);if(element)element.hidden=true;}
  abortQuery();if(typeof diamondVisibility==='function')diamondVisibility();comparisonInitialize();document.title='타자 비교실 | KBO Sabermetrics';
  const params=new URLSearchParams(location.hash.slice(1)),code=params.get('comparison'),codes=(params.get('codes')??'').split(',').filter(c=>/^\d{4,10}$/.test(c));
  if(code&&/^\d{4,10}$/.test(code)){comparisonState.target=code;if(!codes.length)codes.push(code);}if(codes.length)comparisonState.selected=[...new Set(codes)].slice(0,4);
  const year=params.get('year');if(year&&[...$('cmp-year').options].some(option=>option.value===year))$('cmp-year').value=year;
  const minPa=params.get('minPa');if(minPa&&/^\d{1,4}$/.test(minPa)&&Number(minPa)>=1&&Number(minPa)<=1000)$('cmp-min-pa').value=String(Number(minPa));
  await comparisonLoad();
}
async function comparisonLoad(){
  if(!comparisonActive()||!$('cmp-form').reportValidity())return;comparisonState.controller?.abort();const controller=new AbortController();comparisonState.controller=controller;const seq=++comparisonState.seq;
  $('cmp-submit').disabled=true;$('cmp-target').disabled=true;$('cmp-form').setAttribute('aria-busy','true');$('cmp-content').hidden=true;comparisonMessage();$('cmp-status').replaceChildren(text('span','선택한 시즌의 비교군을 불러오는 중…'));
  const request={year:Number($('cmp-year').value),minPa:Number($('cmp-min-pa').value),codes:comparisonState.selected,targetCode:comparisonState.target||''};
  try{
    const data=await api('/api/comparison',request,controller.signal);if(seq!==comparisonState.seq||!comparisonActive())return;
    comparisonState.data=data;comparisonState.selected=(data.selectedCodes??comparisonState.selected).map(String).filter(code=>comparisonPlayer(code)).slice(0,4);comparisonState.target=String(data.targetCode??'');
    if(!comparisonState.selected.length&&comparisonPlayer(comparisonState.target))comparisonState.selected=[comparisonState.target];
    comparisonState.point=comparisonState.selected[0]??comparisonState.target;comparisonState.listLimit=30;comparisonRender();$('cmp-status').textContent='';$('cmp-content').hidden=false;
  }catch(error){if(error.name==='AbortError'||seq!==comparisonState.seq)return;$('cmp-status').replaceChildren(text('span',error.message),comparisonButton('다시 시도',()=>comparisonLoad()));}
  finally{if(seq===comparisonState.seq){$('cmp-submit').disabled=false;$('cmp-target').disabled=false;$('cmp-form').setAttribute('aria-busy','false');}}
}
function comparisonVisiblePlayers(){return (comparisonState.data?.players??[]).filter(player=>(!comparisonState.team||(player.teams??[]).includes(comparisonState.team))&&(!comparisonState.query||player.name.toLocaleLowerCase().includes(comparisonState.query.toLocaleLowerCase()))).sort((a,b)=>(b.values.PA??0)-(a.values.PA??0)||a.name.localeCompare(b.name,'ko'));}
function comparisonRender(){
  const data=comparisonState.data;$('cmp-cohort').textContent=`${data.year} 정규시즌 · ${data.minPa}타석 이상 ${data.cohortSize}명`;$('cmp-date').textContent=data.lastGameDate?`${String(data.lastGameDate).slice(0,10)} 경기까지`:'경기 기준일 없음';
  $('cmp-target').replaceChildren(...data.players.map(player=>new Option(`${player.name} · ${(player.teams??[]).map(team=>teamNames[team]??team).join('/')} · ${comparisonFormat(player.values.PA,'PA')} PA`,String(player.code))));if(!data.players.length)$('cmp-target').add(new Option('비교군 없음',''));$('cmp-target').value=comparisonState.target;
  comparisonRenderMap();comparisonRenderSelected();comparisonRenderSimilar();comparisonSyncSelection();
  $('cmp-notes').replaceChildren(text('h2','비교 기준'));for(const note of data.notes??[])$('cmp-notes').append(text('p',note));
  $('cmp-notes').append(text('p','선수 사진은 수집 시점의 공식 프로필이며 선택 시즌의 당시 모습과 다를 수 있습니다.'));
}
function comparisonRenderMap(){comparisonRenderScatter();comparisonRenderList();}
function comparisonSvg(tag,attrs={},content){const node=document.createElementNS('http://www.w3.org/2000/svg',tag);for(const [key,value] of Object.entries(attrs))node.setAttribute(key,String(value));if(content!==undefined)node.textContent=content;return node;}
function comparisonRenderScatter(){
  const root=$('cmp-chart');root.replaceChildren();const axis=comparisonAxes[comparisonState.axis],visible=comparisonVisiblePlayers(),players=visible.filter(player=>comparisonNumber(player.values[axis.x])!==null&&comparisonNumber(player.values[axis.y])!==null);
  $('cmp-chart-caption').textContent=`${axis.label} · 점은 선수 1명, 주황 테두리는 비교 중인 선수입니다. ${axis.y==='KPct'?'K%는 아래에 있을수록 삼진 비율이 낮습니다. ':''}점을 선택하면 기록을 볼 수 있습니다.`;
  if(!players.length){root.append(text('p','표시 조건에 맞는 타자가 없습니다. 팀·검색어·최소 타석을 조정해 보세요.','comparison-empty'));$('cmp-point').replaceChildren();return;}
  const xValues=players.map(p=>Number(p.values[axis.x])),yValues=players.map(p=>Number(p.values[axis.y]));
  const extent=values=>{const min=Math.min(...values),max=Math.max(...values),padding=Math.max((max-min)*.12,max*.04,.01);return [Math.max(0,min-padding),max+padding];};
  // Keep axes stable when the local team/name display filters change.
  const cohort=comparisonState.data.players.filter(p=>comparisonNumber(p.values[axis.x])!==null&&comparisonNumber(p.values[axis.y])!==null);
  const [xMin,xMax]=extent(cohort.length?cohort.map(p=>Number(p.values[axis.x])):xValues),[yMin,yMax]=extent(cohort.length?cohort.map(p=>Number(p.values[axis.y])):yValues);
  const compact=window.matchMedia('(max-width:550px)').matches,width=compact?380:680,height=compact?320:410,left=compact?54:66,right=compact?18:25,top=24,bottom=57,plotWidth=width-left-right,plotHeight=height-top-bottom;
  const sx=value=>left+(value-xMin)/(xMax-xMin)*plotWidth,sy=value=>top+plotHeight-(value-yMin)/(yMax-yMin)*plotHeight;
  const svg=comparisonSvg('svg',{viewBox:`0 0 ${width} ${height}`,class:'comparison-scatter',role:'group','aria-label':`${comparisonLabels[axis.x]}와 ${comparisonLabels[axis.y]} 리그 산점도. 방향키로 선수 이동, Enter로 비교 선택. 아래 선수 목록에서도 선택할 수 있습니다.`});
  svg.append(comparisonSvg('rect',{x:left,y:top,width:plotWidth,height:plotHeight,fill:'#f9fbfd',rx:4}));
  const ticks=compact?3:4;
  for(let i=0;i<=ticks;i++){const x=left+plotWidth*i/ticks,y=top+plotHeight*i/ticks;svg.append(comparisonSvg('line',{x1:x,x2:x,y1:top,y2:top+plotHeight,class:'comparison-grid-line'}),comparisonSvg('line',{x1:left,x2:left+plotWidth,y1:y,y2:y,class:'comparison-grid-line'}),comparisonSvg('text',{x,y:height-bottom+24,'text-anchor':'middle',class:'comparison-axis-tick'},comparisonFormat(xMin+(xMax-xMin)*i/ticks,axis.x)),comparisonSvg('text',{x:left-10,y:y+4,'text-anchor':'end',class:'comparison-axis-tick'},comparisonFormat(yMax-(yMax-yMin)*i/ticks,axis.y)));}
  svg.append(comparisonSvg('text',{x:left+plotWidth/2,y:height-7,'text-anchor':'middle',class:'comparison-axis-title'},comparisonLabels[axis.x]),comparisonSvg('text',{x:18,y:top+plotHeight/2,transform:`rotate(-90 18 ${top+plotHeight/2})`,'text-anchor':'middle',class:'comparison-axis-title'},comparisonLabels[axis.y]));
  const groups=[];
  players.forEach((player,index)=>{
    const x=sx(Number(player.values[axis.x])),y=sy(Number(player.values[axis.y])),selected=comparisonState.selected.includes(String(player.code)),g=comparisonSvg('g',{class:`comparison-dot${selected?' is-selected':''}`,role:'button',tabindex:index===0?'0':'-1','data-point-code':player.code,'aria-pressed':String(selected),'aria-label':`${player.name}, ${comparisonLabels[axis.x]} ${comparisonFormat(player.values[axis.x],axis.x)}, ${comparisonLabels[axis.y]} ${comparisonFormat(player.values[axis.y],axis.y)}, ${player.values.PA}타석. Enter로 비교 선택.`});
    markTeamName(g,player.teams?.[0]);g.append(comparisonSvg('circle',{cx:x,cy:y,r:12,class:'comparison-dot-hit'}),comparisonSvg('circle',{cx:x,cy:y,r:5,class:'comparison-dot-visible'}),comparisonSvg('title',{},`${player.name} · ${comparisonLabels[axis.x]} ${comparisonFormat(player.values[axis.x],axis.x)} · ${comparisonLabels[axis.y]} ${comparisonFormat(player.values[axis.y],axis.y)}`));
    const show=()=>{comparisonState.point=String(player.code);comparisonRenderPoint(player);};g.onpointerenter=show;g.onfocus=show;g.onclick=show;
    g.onkeydown=event=>{if(['Enter',' '].includes(event.key)){event.preventDefault();comparisonToggle(player.code);}else if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(event.key)){event.preventDefault();const direction=['ArrowLeft','ArrowUp'].includes(event.key)?-1:1,next=groups[(index+direction+groups.length)%groups.length];for(const group of groups)group.setAttribute('tabindex','-1');next.setAttribute('tabindex','0');next.focus();}};groups.push(g);svg.append(g);
  });root.append(svg);
  const player=players.find(p=>String(p.code)===comparisonState.point)??players[0];comparisonState.point=String(player.code);comparisonRenderPoint(player);
}
function comparisonRenderPoint(player){
  const detail=$('cmp-point'),axis=comparisonAxes[comparisonState.axis],identity=text('div','','comparison-point-identity');identity.append(comparisonProfileLink(player),comparisonTeams(player),text('span',`${comparisonFormat(player.values.PA,'PA')} PA`));
  const values=text('p',`${comparisonLabels[axis.x]} ${comparisonFormat(player.values[axis.x],axis.x)} · ${comparisonLabels[axis.y]} ${comparisonFormat(player.values[axis.y],axis.y)}`);
  detail.replaceChildren(identity,values,comparisonAddButton(player));comparisonSyncSelection();
}
function comparisonRenderList(){
  const players=comparisonVisiblePlayers();$('cmp-visible-count').textContent=`${players.length}명`;$('cmp-player-list').replaceChildren();
  for(const player of players.slice(0,comparisonState.listLimit)){const row=text('div','','comparison-player-row'),identity=text('div');identity.append(comparisonProfileLink(player),comparisonTeams(player),text('small',`${comparisonFormat(player.values.PA,'PA')} PA · OPS ${comparisonFormat(player.values.OPS,'OPS')}`));row.append(identity,comparisonAddButton(player,true));$('cmp-player-list').append(row);}
  if(!players.length)$('cmp-player-list').append(text('p','표시할 선수가 없습니다.','comparison-empty'));$('cmp-more').hidden=players.length<=comparisonState.listLimit;comparisonSyncSelection();
}
function comparisonRenderSelected(){
  const root=$('cmp-selected'),players=comparisonState.selected.map(comparisonPlayer).filter(Boolean);root.replaceChildren();$('cmp-comparison-table').replaceChildren();
  if(!players.length){root.append(text('p','리그 지도 또는 선수 목록에서 비교할 타자를 선택하세요. 최대 4명을 함께 볼 수 있습니다.','comparison-empty'));return;}
  const metricKeys=['AVG','OBP','SLG','ISO','BBPct','KPct'];
  for(const player of players){const card=text('article','','comparison-player-card'),header=text('header'),identity=text('div');identity.append(comparisonProfileLink(player),comparisonTeams(player),text('p',`${comparisonFormat(player.values.PA,'PA')} PA · ${comparisonState.data.year}`));header.append(comparisonPhoto(player),identity);card.append(header);
    for(const key of metricKeys){const row=text('div','','comparison-percentile-row'),label=text('div','','comparison-percentile-label'),percentile=comparisonNumber(player.percentiles?.[key]);label.append(text('span',comparisonLabels[key]),text('strong',comparisonFormat(player.values[key],key)),text('small',percentile===null?'비교 불가':`백분위 ${Math.round(percentile)}`));const track=comparisonSvg('svg',{viewBox:'0 0 100 6',preserveAspectRatio:'none',class:'comparison-percentile-track','aria-hidden':'true'});track.append(comparisonSvg('rect',{x:0,y:0,width:100,height:6,rx:3,fill:'#e9eef5'}),comparisonSvg('rect',{x:0,y:0,width:Math.max(0,Math.min(100,percentile??0)),height:6,rx:3,fill:'#446f97'}));row.append(label,track);card.append(row);}card.append(comparisonAddButton(player));root.append(card);
  }
  if(players.length===1)root.append(text('p','한 명을 더 선택하면 같은 지표를 나란히 비교할 수 있습니다.','comparison-select-hint'));
  const wrap=text('div','','comparison-table-wrap');wrap.tabIndex=0;wrap.setAttribute('role','region');wrap.setAttribute('aria-label','선수 비교 상세 기록, 좌우로 스크롤');const table=document.createElement('table'),head=document.createElement('thead'),header=document.createElement('tr');const metricHeader=text('th','지표');metricHeader.scope='col';header.append(metricHeader);for(const player of players){const th=text('th',player.name);th.scope='col';header.append(th);}head.append(header);table.append(head);const body=document.createElement('tbody');
  for(const key of ['PA','AB','H','HR','BB','HBP','SO','SH','SF','SB','TB','AVG','OBP','SLG','OPS','ISO','BBPct','KPct']){const row=document.createElement('tr'),label=text('th',comparisonLabels[key]??key);label.scope='row';row.append(label);for(const player of players)row.append(text('td',comparisonFormat(player.values[key],key)));body.append(row);}table.append(body);wrap.append(table);$('cmp-comparison-table').append(wrap);comparisonSyncSelection();
}
function comparisonRenderSimilar(){
  const root=$('cmp-similar');root.replaceChildren();const target=comparisonPlayer(comparisonState.target);
  if(!target){root.append(text('p','조건을 충족하는 기준 선수가 없습니다. 최소 타석을 낮춰 다시 조회해 보세요.','comparison-empty'));return;}
  for(const [index,item] of (comparisonState.data.similar??[]).entries()){const player=comparisonPlayer(item.code);if(!player)continue;const card=text('article','','comparison-similar-card'),header=text('header'),identity=text('div');identity.append(text('small',`${index+1}번째로 가까운 유형`),comparisonProfileLink(player),comparisonTeams(player));header.append(comparisonPhoto(player),identity);card.append(header);
    const difference=text('div','','comparison-distance');difference.append(text('span',`${target.name}과 백분위 차이`),text('strong',comparisonFormat(item.distance,'distance')));card.append(difference,text('p',`${comparisonFormat(player.values.PA,'PA')} PA · OPS ${comparisonFormat(player.values.OPS,'OPS')}`,'comparison-similar-stat'));
    const components=text('details','','comparison-components'),summary=text('summary','항목별 백분위 차이');components.append(summary);for(const key of ['AVG','ISO','BBPct','KPct'])components.append(text('p',`${comparisonLabels[key]} ${comparisonFormat(item.components?.[key],'distance')}점`));card.append(components,comparisonAddButton(player,true));root.append(card);
  }
  if(!root.childNodes.length)root.append(text('p','유효 지표가 있는 다른 타자가 없어 유사 유형을 계산할 수 없습니다.','comparison-empty'));comparisonSyncSelection();
}
