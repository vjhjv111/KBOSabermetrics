// The browser only handles input, requests, and rendering. Statistics, comparisons,
// sorting, paging and numeric formatting are implemented on the server.
const $ = id => document.getElementById(id);
const state = { room: 'season', role: 'batter', view: 'basic', session: null, catalog: null,
  schema: [], applied: null, result: null, playerCode: null, sequence: 0, controller: null, schemaSequence: 0 };
const roomNames = { season: '시즌', career: '통산', team: '팀', constants: '연도별 상수' };
const descriptions = { season: '선택 시즌의 선수별 기록', career: '적재된 전체 기간의 선수 기록', team: '팀별 누적 기록과 세부 성적', constants: '적재된 전체 정규시즌 기준 · 화면 연도와 독립' };
const teamNames = { HH: '한화', HT: 'KIA', LG: 'LG', LT: '롯데', SS: '삼성', SK: 'SSG/SK', WO: '히어로즈', KT: 'KT', NC: 'NC', OB: '두산' };
const text = (tag, value, className) => { const e=document.createElement(tag); e.textContent=value; if(className)e.className=className; return e; };
function showError(message){ $('error-box').textContent=message; $('error-box').hidden=false; }
function clearError(){ $('error-box').hidden=true; $('error-box').textContent=''; }
async function api(path, body, signal){
  const response = await fetch(path,{method:body===undefined?'GET':'POST',credentials:'same-origin',cache:'no-store',signal,
    headers:body===undefined?{}:{'Content-Type':'application/json','X-CSRF-TOKEN':state.session?.csrfToken??''},
    body:body===undefined?undefined:JSON.stringify(body)});
  const data = await response.json().catch(()=>({message:'서버 응답을 읽지 못했습니다.'}));
  if(!response.ok){
    const error = new Error(data.message??`조회에 실패했습니다. (${response.status})`);
    error.status=response.status; error.code=data.code; error.requestId=data.requestId;
    throw error;
  }
  return data;
}
async function bootstrap(){
  try {
    state.session=await api('/api/session');
    await loadCatalog();
    await playerRoute();
    await teamRoute();
    await homeRoute();
  }catch(e){showError(`${e.message} 서버가 실행 중인지 확인하세요.`);$('connection').textContent='연결 실패';}
}

function options(select, values, selected=''){
  select.replaceChildren(new Option('전체',''));
  for(const value of values){ const [v,label]=Array.isArray(value)?value:[String(value),String(value)];select.add(new Option(label,v)); }
  select.value=selected;
}
async function loadCatalog(){
  state.catalog=await api('/api/catalog');
  const c=state.catalog;
  c.teams=c.teams.filter(t=>!['EA','WE'].includes(t.toUpperCase()));
  options($('year'),c.years, String(Math.max(...c.years)));
  options($('team'),c.teams.map(t=>[t,teamNames[t]??t]));
  options($('opponent'),c.teams.map(t=>[t,teamNames[t]??t]));
  options($('stadium'),c.stadiums);
  $('start-date').value=c.minDate??''; $('end-date').value=c.maxDate??'';
  $('page-size').replaceChildren(...[25,50,100].filter(n=>n<=c.limits.maxPageSize).map(n=>new Option(String(n),String(n))));
  if(!$('page-size').options.length)$('page-size').add(new Option(String(c.limits.maxPageSize),String(c.limits.maxPageSize)));
  $('page-size').value=String(Math.min(c.limits.maxPageSize,50));
  if(!$('page-size').value)$('page-size').selectedIndex=0;
  $('dataset-caption').textContent=`${c.minDate??'—'} – ${c.maxDate??'—'} · ${c.games.toLocaleString('ko-KR')}경기`;
  $('quota-info').textContent=`서버 기본 제한: IP별 하루 ${c.limits.dailyQueries}회 · 응답 요청 ${c.limits.dailyRows.toLocaleString('ko-KR')}행. 한 페이지 ${c.limits.maxPageSize}행, 한 조회 결과는 최대 ${c.limits.maxAccessibleRows.toLocaleString('ko-KR')}행입니다. 관리자가 설정을 변경할 수 있습니다.`;
  $('connection').textContent='● 서버 연결됨 · 읽기 전용 SQLite';
  $('demo-badge').hidden=!c.demo;
  if(location.hash!=='#records')navigation();else await changeView();
}
function navigation(){
  document.querySelectorAll('.room').forEach(b=>{const yes=b.dataset.room===state.room;b.classList.toggle('active',yes);b.setAttribute('aria-current',yes?'page':'false');});
  document.querySelectorAll('.role').forEach(b=>{const yes=b.dataset.role===state.role;b.classList.toggle('active',yes);b.setAttribute('aria-pressed',String(yes));});
  const constants=state.room==='constants';
  document.querySelector('.role-switch').hidden=constants;
  $('page-title').textContent=constants?'연도별 상수':`${roomNames[state.room]} ${state.role==='batter'?'타자':'투수'}`;
  $('page-description').textContent=descriptions[state.room];
  $('year').disabled=state.room==='career'||constants;
  $('position').disabled=state.role==='pitcher'||state.room!=='season';
  $('qualification').disabled=state.room!=='season';
  if($('position').disabled)$('position').value='';
  if($('qualification').disabled)$('qualification').value='0';
  $('qual-label').textContent=state.role==='batter'?'규정타석':'규정이닝';
  // Constants describe the complete source environment; hide irrelevant filters.
  $('detail-filters').hidden=constants||$('detail-toggle').getAttribute('aria-expanded')==='false';
  $('detail-toggle').disabled=constants;
  for(const id of ['competition','team'])$(id).disabled=constants;
  const views=state.catalog?.views.filter(v=>v.role===(constants?'constants':state.role))??[];
  if(!views.some(v=>v.key===state.view))state.view=views[0]?.key??'basic';
  $('view-nav').replaceChildren(...views.map(v=>{
    const b=text('button',v.title,v.key===state.view?'active':''); b.type='button';b.dataset.view=v.key;
    b.setAttribute('aria-pressed',String(v.key===state.view));b.onclick=async()=>{if(v.key===state.view)return;state.view=v.key;await changeView();};return b;
  }));
}
async function changeView(){
  abortQuery();clearError();state.applied=null;state.result=null;
  navigation();
  $('records').querySelector('tbody').replaceChildren();$('empty').hidden=false;
  $('result-count').textContent='';$('page-caption').textContent='—';$('timing').textContent='';
  $('previous').disabled=$('next').disabled=true;
  const seq=++state.schemaSequence;
  if(!state.catalog){markDirty();return;}
  try{
    const role=state.room==='constants'?'constants':state.role;
    const schema=await api(`/api/schema/${role}/${state.view}`);
    if(seq!==state.schemaSequence)return;
    state.schema=schema.columns;
    for(const n of [1,2]){
      options($(`stat${n}`),schema.columns.filter(c=>c.numeric&&c.key!=='Rank').map(c=>[c.key,c.label]));
      $(`stat${n}`).options[0].textContent='없음';$(`value${n}`).value='';
    }
    renderHeader(schema.columns);
    $('result-title').textContent=schema.title;
    markDirty();

    // Initial/open-tab load: entering a record-room view should immediately show
    // the current/default result without requiring the user to press 조회.
    // Changing individual filters still only marks the form dirty; users can
    // finish several filter edits and then press 조회 once.
    try{
      await query(readRequest());
    }catch(e){
      showError(e.message);
    }
  }catch(e){showError(e.message);}
}
function val(id){return $(id).value.trim()||null;}
function integer(id){const v=val(id);return v===null?null:Number(v);}
function readRequest(){
  const conditions=[];
  for(const i of [1,2])if(val(`stat${i}`)){
    const v=val(`value${i}`);if(v===null||!Number.isFinite(Number(v)))throw new Error(`조건${i}의 기준값을 입력하세요.`);
    conditions.push({stat:val(`stat${i}`),operator:val(`op${i}`),value:Number(v)});
  }
  const period=val('period'), custom=period==='custom', count=val('count')?.split('-').map(Number);
  if(custom && (!val('start-date')||!val('end-date')))throw new Error('시작일과 종료일을 지정하세요.');
  if(custom && val('start-date')>val('end-date'))throw new Error('시작일은 종료일보다 늦을 수 없습니다.');
  const r={room:state.room,role:state.role,view:state.view,year:state.room==='career'?null:integer('year'),competition:val('competition'),team:val('team'),position:val('position'),qualificationPercent:Number(val('qualification')),
    startDate:custom?val('start-date'):null,endDate:custom?val('end-date'):null,recentDays:period?.startsWith('d')?Number(period.slice(1)):null,recentGames:period?.startsWith('g')?Number(period.slice(1)):null,
    weekday:val('weekday'),venue:val('venue'),opponent:val('opponent'),stadium:val('stadium'),playerName:val('player-name'),playerCode:state.playerCode,
    inning:val('inning'),outs:integer('outs'),runners:val('runners'),score:val('score'),balls:count?.[0]??null,strikes:count?.[1]??null,batOrder:integer('bat-order'),conditions,page:1,pageSize:Number(val('page-size')),sortBy:null,descending:true};
  if(state.room==='constants')return{room:'constants',role:state.role,view:state.view,conditions:[],page:1,pageSize:r.pageSize};
  return r;
}
function markDirty(){ $('draft-state').textContent='조건을 선택한 뒤 조회를 눌러 적용하세요.';$('draft-state').classList.add('dirty'); }
function setBusy(busy){ $('workspace').setAttribute('aria-busy',String(busy));$('workspace').classList.toggle('busy',busy);$('loading').hidden=!busy;$('cancel-query').hidden=!busy; }
function abortQuery(){state.sequence++;if(state.controller)state.controller.abort();state.controller=null;setBusy(false);}
async function query(r){
  abortQuery(); const seq=state.sequence, controller=new AbortController();state.controller=controller;setBusy(true);clearError();
  try{
    const result=await api('/api/query',r,controller.signal);
    if(seq!==state.sequence)return;
    state.applied=structuredClone(r);state.result=result;
    renderTable(result);
    $('draft-state').textContent='현재 조건 적용 완료';$('draft-state').classList.remove('dirty');
  }catch(e){if(e.name!=='AbortError'&&seq===state.sequence)showError(e.message+(e.requestId?` (요청 ID: ${e.requestId})`:''));}
  finally{if(seq===state.sequence){setBusy(false);state.controller=null;}}
}
const STAT_HEADER_TITLES={"Rank":"Rank · 순위","Name":"Name · 선수명","적용 조건":"Applied condition · 적용 조건","Team":"Team · 팀","G":"Games · 경기","GS":"Games Started · 선발 등판","GR":"Relief Games · 구원 등판","CG":"Complete Games · 완투","SHO":"Shutouts · 완봉","IP":"Innings Pitched · 투구 이닝","ER":"Earned Runs · 자책점","R":"Runs Allowed · 실점","TBF":"Total Batters Faced · 상대 타자 수","H":"Hits Allowed · 피안타","HR":"Home Runs Allowed · 피홈런","BB":"Walks · 볼넷","HBP":"Hit By Pitch · 사구","SO":"Strikeouts · 탈삼진","IFFB":"Infield Fly Balls · 내야 뜬공","WP":"Wild Pitches · 폭투","ERA":"Earned Run Average · 평균자책점","RA9":"Runs Allowed per 9 · 9이닝당 실점","FIP":"Fielding Independent Pitching · 수비무관 평균자책","WHIP":"Walks plus Hits per Inning Pitched · 이닝당 출루 허용","피OBP":"Opponent On-base Percentage · 피출루율","피OPS":"Opponent OPS · 피OPS","K/9":"Strikeouts per 9 innings · 9이닝당 탈삼진","BB/9":"Walks per 9 innings · 9이닝당 볼넷","K/BB":"Strikeout-to-Walk Ratio · 탈삼진/볼넷 비율","HR/9":"Home Runs per 9 innings · 9이닝당 피홈런","K%":"Strikeout Rate · 탈삼진율","BB%":"Walk Rate · 볼넷율","K-BB%":"Strikeout minus Walk Rate · 탈삼진율-볼넷율","BABIP":"Batting Average on Balls in Play · 인플레이 타구 피안타율","LOB%*":"Left On Base Percentage · 잔루율","xFIP":"Expected FIP · 기대 수비무관 평균자책","FIP-":"FIP Minus · 리그/구장 보정 FIP 지수","xFIP-":"xFIP Minus · 리그 보정 xFIP 지수","ERA-FIP":"ERA minus FIP · ERA-FIP 차이","피AVG":"Opponent Batting Average · 피안타율","NP":"Number of Pitches · 투구 수","P/G":"Pitches per Game · 경기당 투구 수","P/IP":"Pitches per Inning · 이닝당 투구 수","P/PA":"Pitches per Plate Appearance · 타석당 투구 수","KBO fWAR":"KBO Fielding Independent WAR · KBO 수비무관 투수 WAR","KBO fWAR v4":"KBO Fielding Independent WAR v4 · KBO 수비무관 투수 WAR v4","gmLI*":"Game-entering Leverage Index · 등판 시 레버리지 지수","QS":"Quality Starts · 퀄리티스타트","QS%":"Quality Start Rate · 퀄리티스타트 비율","QS+":"Quality Start Plus · 퀄리티스타트 플러스","QS+%":"Quality Start Plus Rate · QS+ 비율","RS*":"Run Support · 득점 지원","RS9*":"Run Support per 9 innings · 9이닝당 득점 지원","팀 W":"Team Wins · 팀 승","팀 L":"Team Losses · 팀 패","팀 W%":"Team Winning Percentage · 팀 승률","IP/GS":"Innings per Start · 선발 경기당 이닝","P/GS":"Pitches per Start · 선발 경기당 투구 수","2연투":"Back-to-back Appearances · 2연투","3연투":"Three-day Streaks · 3연투","4연투":"Four-day Streaks · 4연투","1+이닝":"One-plus Inning Relief Games · 1이닝 초과 구원 등판","IP/GR":"Innings per Relief Game · 구원 경기당 이닝","P/GR":"Pitches per Relief Game · 구원 경기당 투구 수","WAR":"Wins Above Replacement · 대체선수 대비 승리기여","wRC+":"Weighted Runs Created Plus · 조정 득점생산력","AVG":"Batting Average · 타율","OBP":"On-base Percentage · 출루율","SLG":"Slugging Percentage · 장타율","OPS":"On-base Plus Slugging · 출루율+장타율","PA":"Plate Appearances · 타석","AB":"At Bats · 타수","2B":"Doubles · 2루타","3B":"Triples · 3루타","RBI":"Runs Batted In · 타점","SB":"Stolen Bases · 도루","CS":"Caught Stealing · 도루 실패"};
function statHeaderTitle(c){return STAT_HEADER_TITLES[c.label]||STAT_HEADER_TITLES[c.key]||c.label;}
function makeSortHeader(c){
  const th=document.createElement('th');th.scope='col';
  th.className=c.key==='Applied'?'applied':c.key==='Name'?'name':c.key==='Rank'?'rank':c.kind==='text'?'text':'';
  if(c.sortable){
    const b=text('button',c.label);b.type='button';b.title=statHeaderTitle(c);
    if(state.applied?.sortBy===c.key){th.setAttribute('aria-sort',state.applied.descending?'descending':'ascending');b.append(text('span',state.applied.descending?'▼':'▲','sort-arrow'));}
    b.onclick=()=>{if(!state.applied)return;const desc=state.applied.sortBy===c.key?!state.applied.descending:true;query({...state.applied,sortBy:c.key,descending:desc,page:1});};
    th.append(b);
  }else{th.textContent=c.label;th.title=statHeaderTitle(c);}
  return th;
}
function renderHeader(columns){
  const thead=$('records').querySelector('thead');
  const pitchMatrix=state.role==='batter'&&state.view==='pitch-types'&&columns.some(c=>c.label==='직구 AVG');
  if(!pitchMatrix){
    const tr=document.createElement('tr');
    for(const c of columns)tr.append(makeSortHeader(c));
    thead.replaceChildren(tr);return;
  }
  const groupRow=document.createElement('tr');groupRow.className='pitch-group-row';
  const metricRow=document.createElement('tr');metricRow.className='pitch-metric-row';
  const fixed=new Set(['Rank','Name','Applied','TeamCode']);
  for(const c of columns.filter(c=>fixed.has(c.key))){
    const th=makeSortHeader(c);th.rowSpan=2;th.classList.add('pitch-fixed');groupRow.append(th);
  }
  const pitchNames=['직구','투심','커터','커브','슬라이더','체인지업','싱커','포크','너클','기타'];
  for(const name of pitchNames){
    const members=columns.filter(c=>c.label.startsWith(name+' '));
    if(!members.length)continue;
    const group=document.createElement('th');group.colSpan=members.length;group.scope='colgroup';group.textContent=name;group.className='pitch-group';groupRow.append(group);
    for(const c of members){const th=makeSortHeader(c);th.classList.add('pitch-metric');const label=c.label.slice(name.length+1);const b=th.querySelector('button');if(b){b.firstChild.textContent=label;}else th.textContent=label;metricRow.append(th);}
  }
  thead.replaceChildren(groupRow,metricRow);
}
function renderLeagueOverview(result){
  const box=$('league-overview'), metrics=$('league-overview-metrics');
  const summary=result.leagueOverview;
  if(state.room!=='team'||!summary){box.hidden=true;metrics.replaceChildren();return;}
  $('league-overview-title').textContent=summary.title;
  const frag=document.createDocumentFragment();
  for(const m of summary.metrics){
    const item=document.createElement('div');item.className='league-metric'+(m.emphasis?' emphasis':'');
    item.append(text('span',m.label,'league-metric-label'),text('strong',m.value,'league-metric-value'));frag.append(item);
  }
  metrics.replaceChildren(frag);box.hidden=false;
}
function renderTable(result){
  renderLeagueOverview(result);
  renderHeader(result.columns);
  const body=document.createDocumentFragment();
  for(const row of result.rows){
    const tr=document.createElement('tr');
    for(const col of result.columns){
      const value=row.cells[col.key]??'-',td=document.createElement('td');td.textContent=value;
      td.className=col.key==='Applied'?'applied':col.key==='Name'?'name':col.key==='Rank'?'rank':col.key==='TeamCode'?'team':col.kind==='text'?'text':'';
      if(col.key==='TeamCode')td.textContent=teamNames[value]??value;
      if(['WrcPlus','OPS','ERA'].includes(col.key))td.classList.add('stat-emphasis');
      if(state.room==='team'&&['Name','TeamCode'].includes(col.key)&&row.entityCode){
        const a=text('a',teamNames[row.entityCode]??value,'player-link');a.href=`#team=${encodeURIComponent(row.entityCode)}&year=${$('year').value}`;td.replaceChildren(a);
      }else if(col.key==='Name'&&row.entityCode&&state.room!=='team'){
        const b=text('a',value,'player-link');b.title='선수 개인 페이지';b.href=`#player=${encodeURIComponent(row.entityCode)}&role=${state.role}`;
        td.replaceChildren(b);
      }
      tr.append(td);
    }
    body.append(tr);
  }
  $('records').querySelector('tbody').replaceChildren(body);
  $('empty').hidden=result.rows.length>0;
  if(!result.rows.length){$('empty').querySelector('h2').textContent='조건에 맞는 기록이 없습니다';$('empty').querySelector('p').textContent='필터를 완화하거나 조회 기간을 확인해 주세요.';}
  $('result-count').textContent=`${result.total.toLocaleString('ko-KR')}건 중 ${result.rows.length}건 표시`;
  $('timing').textContent=`서버 ${result.elapsedMs.toLocaleString('ko-KR')} ms${result.cached?' · 계산 캐시 사용':''}`;
  $('page-caption').textContent=`열람 가능 ${result.accessibleTotal.toLocaleString('ko-KR')}건 · 페이지당 ${result.pageSize}건`;
  $('page-number').textContent=`${result.page} / ${Math.max(1,Math.ceil(result.accessibleTotal/result.pageSize))}`;
  $('previous').disabled=result.page<=1;$('next').disabled=result.page*result.pageSize>=result.accessibleTotal;
  $('warnings').replaceChildren(...result.warnings.map(w=>text('p',w)));
}
function selectPlayer(code,name){
  $('search-dialog').close();
  location.hash=`player=${encodeURIComponent(code)}&role=${state.role}`;
}
$('filters').onsubmit=e=>{e.preventDefault();try{query(readRequest());}catch(err){showError(err.message);}};
$('filters').addEventListener('input',markDirty);$('filters').addEventListener('change',markDirty);
$('period').onchange=()=>{const custom=val('period')==='custom';$('start-date').disabled=$('end-date').disabled=!custom;};
$('player-name').oninput=()=>{state.playerCode=null;$('player-chip').hidden=true;};
$('player-chip').querySelector('button').onclick=()=>{state.playerCode=null;$('player-name').value='';$('player-chip').hidden=true;markDirty();};
$('detail-toggle').onclick=()=>{const expanded=$('detail-toggle').getAttribute('aria-expanded')==='true';$('detail-toggle').setAttribute('aria-expanded',String(!expanded));$('detail-filters').hidden=expanded;$('detail-toggle').textContent=expanded?'상세 열기 +':'상세 접기 −';};
$('reset').onclick=()=>{
  const year=$('year').value;$('filters').reset();$('year').value=year;
  $('start-date').disabled=$('end-date').disabled=true;state.playerCode=null;$('player-chip').hidden=true;navigation();markDirty();
};
$('cancel-query').onclick=()=>{abortQuery();$('draft-state').textContent='조회 요청을 취소했습니다.';};
$('previous').onclick=()=>state.applied&&query({...state.applied,page:state.applied.page-1});
$('next').onclick=()=>state.applied&&query({...state.applied,page:state.applied.page+1});
for(const b of document.querySelectorAll('.room'))b.onclick=async()=>{if(state.room===b.dataset.room)return;state.room=b.dataset.room;state.view=state.room==='constants'?'league':'basic';if(['team','constants'].includes(state.room)){state.playerCode=null;$('player-name').value='';$('player-chip').hidden=true;}await changeView();};
for(const b of document.querySelectorAll('.role'))b.onclick=async()=>{if(state.role===b.dataset.role)return;state.role=b.dataset.role;state.view='basic';await changeView();};
$('policy').onclick=()=>$('policy-dialog').showModal();
for(const b of document.querySelectorAll('.close-dialog'))b.onclick=()=>b.closest('dialog').close();
$('open-search').onclick=()=>{$('search-dialog').showModal();$('search-name').focus();};
$('search-form').onsubmit=async e=>{
  e.preventDefault();$('search-status').textContent='검색 중…';$('search-results').replaceChildren();
  const b=$('search-form').querySelector('button');b.disabled=true;
  try{
    const rows=await api('/api/players/search',{query:$('search-name').value.trim()});
    $('search-status').textContent=`${rows.length}명 · 최대 20명 표시`;
    for(const p of rows){
      const choice=document.createElement('button');choice.type='button';choice.className='player-choice';
      const title=text('div',p.name);title.append(document.createElement('br'),text('small',`선수 코드 ${p.pcode}`));
      choice.append(title,text('small',`${p.birthDate} · ${p.latestTeam} · ${p.primaryPosition} · ${p.role}`));
      choice.onclick=()=>selectPlayer(p.pcode,p.name);$('search-results').append(choice);
    }
  }catch(err){$('search-status').textContent=err.message;}finally{b.disabled=false;}
};
window.addEventListener('keydown',e=>{if(e.ctrlKey&&e.key==='Enter'){e.preventDefault();$('filters').requestSubmit();}});
// Player pages use the existing server session and retain the record-room draft.
const playerState={code:null,role:'batter',section:'summary',year:null,view:'basic',page:1,sort:'',descending:true,sequence:0,controller:null,profileSequence:0,profileController:null,profile:null,table:null};
const playerTabs=[['summary','종합'],['years','연도별'],['trend','그래프'],['games','날짜별'],['situations','상황별'],['opponents','상대별'],['plays','플레이로그'],['pitches','구종별']];
function initPlayerPage(){
  const root=text('main','','player-page');root.id='player-page';root.hidden=true;
  root.innerHTML=`<div class="player-breadcrumb"><a href="#records">← 기록실로 돌아가기</a><span>PLAYER PROFILE</span></div>
    <header class="player-hero"><div class="player-monogram" aria-hidden="true">KBO</div><div><p class="player-eyebrow">선수 정보</p><h1 id="player-title" tabindex="-1">선수 불러오는 중…</h1><p id="player-bio"></p><p id="player-history" class="muted"></p></div><div class="player-hero-tag">KBO<br><strong>SABERMETRICS</strong></div></header>
    <nav id="player-tabs" class="player-tabs" aria-label="선수 기록 분류"></nav>
    <form id="player-controls" class="player-controls"><label>선수 구분<select id="pp-role"><option value="batter">타자</option><option value="pitcher">투수</option></select></label><label id="pp-year-label">시즌<select id="pp-year"></select></label><label>경기<select id="pp-competition"><option>정규시즌</option><option>포스트시즌</option><option>시범경기</option><option>전체</option></select></label><label id="pp-view-label">기록<select id="pp-view"></select></label><label id="pp-opponent-label">상대 팀<select id="pp-opponent"></select></label><label id="pp-start-label">시작일<input type="date" id="pp-start"></label><label id="pp-end-label">종료일<input type="date" id="pp-end"></label><button class="button primary" type="submit">조회</button><button class="button outline" id="pp-reset" type="button">조건 초기화</button></form>
    <div id="player-status" role="status" aria-live="polite"></div><div id="player-content"></div><div class="player-pagination" id="player-pagination" hidden><button type="button" class="button outline" id="pp-prev">이전</button><span id="pp-page"></span><button type="button" class="button outline" id="pp-next">다음</button></div><p class="player-source">수집된 경기 DB 기준 · 연봉·수상·등번호·선수 사진은 제공 자료가 없어 표시하지 않습니다.</p>`;
  $('workspace').after(root);
  for(const [id,label] of playerTabs){const b=text('button',label);b.type='button';b.dataset.section=id;b.onclick=()=>{playerState.section=id;playerState.page=1;playerState.sort='';playerState.view='basic';configurePlayerControls();loadPlayerSection();};$('player-tabs').append(b);}
  $('player-controls').onsubmit=e=>{e.preventDefault();playerState.year=Number($('pp-year').value)||null;playerState.view=$('pp-view').value;playerState.page=1;playerState.sort='';loadPlayerSection();};
  $('pp-role').onchange=()=>{playerState.role=$('pp-role').value;playerState.page=1;setPlayerYears();configurePlayerControls();loadPlayerSection();};
  $('pp-competition').onchange=()=>playerRoute(true);
  $('pp-reset').onclick=()=>{$('pp-opponent').value='';$('pp-start').value=$('pp-end').value='';playerState.page=1;loadPlayerSection();};
  $('pp-prev').onclick=()=>{playerState.page--;loadPlayerSection();};$('pp-next').onclick=()=>{playerState.page++;loadPlayerSection();};
  window.addEventListener('hashchange',()=>playerRoute());
  for(const b of document.querySelectorAll('.room,.role'))b.addEventListener('click',()=>{if(playerState.code)location.hash='';});
}
async function playerRoute(keep=false){
  if(!state.session)return;
  const params=new URLSearchParams(location.hash.slice(1)),code=params.get('player');
  playerState.profileController?.abort();playerState.controller?.abort();++playerState.sequence;
  const seq=++playerState.profileSequence;
  if(!code){const previous=playerState.code;playerState.code=null;$('player-page').hidden=true;$('workspace').hidden=params.has('team');document.title='KBO Sabermetrics';if(previous&&!state.schema.length&&!params.has('team'))await changeView();return;}
  abortQuery();$('workspace').hidden=true;$('player-page').hidden=false;playerState.code=code;
  if(!keep){playerState.section='summary';playerState.view='basic';playerState.role=params.get('role')==='pitcher'?'pitcher':'batter';$('pp-competition').value='정규시즌';$('pp-opponent').value='';$('pp-start').value=$('pp-end').value='';}
  playerState.page=1;$('player-content').replaceChildren();$('player-pagination').hidden=true;$('player-status').textContent='선수 정보를 불러오는 중…';$('player-title').textContent='선수 불러오는 중…';$('player-bio').textContent=$('player-history').textContent='';
  const controller=new AbortController();playerState.profileController=controller;
  try{
    const data=await api('/api/player',{code,section:'profile',competition:$('pp-competition').value,pageSize:Math.min(25,state.catalog?.limits.maxPageSize??25)},controller.signal);
    if(seq!==playerState.profileSequence)return;playerState.profile=data;
    const p=data.profile;$('player-title').textContent=p.name;document.title=`${p.name} · 선수 기록 | KBO Sabermetrics`;
    $('player-bio').textContent=`${teamNames[p.latestTeam]??p.latestTeam} · ${p.primaryPosition} · ${p.batsThrows} · ${p.role}`;
    $('player-history').textContent=`생년월일 ${p.birthDate} · 활동 ${p.activeYears} · 선수 코드 ${p.pcode}`;
    const roles=[...new Set(data.seasons.map(s=>s.Role))];$('pp-role').replaceChildren(...roles.map(role=>new Option(role==='batter'?'타자':'투수',role)));
    if(!roles.includes(playerState.role))playerState.role=roles[0]??'batter';$('pp-role').value=playerState.role;
    options($('pp-opponent'),(state.catalog?.teams??[]).map(t=>[t,teamNames[t]??t]));
    setPlayerYears();configurePlayerControls();$('player-title').focus({preventScroll:true});await loadPlayerSection();
  }catch(e){if(e.name!=='AbortError'&&seq===playerState.profileSequence)$('player-status').textContent=e.message;}
}
function setPlayerYears(){
  const years=[...new Set((playerState.profile?.seasons??[]).filter(s=>s.Role===playerState.role).map(s=>Number(s.Year)))].sort((a,b)=>b-a);
  if(!years.includes(playerState.year))playerState.year=years[0]??null;
  $('pp-year').replaceChildren(...years.map(y=>new Option(String(y),String(y))));$('pp-year').value=String(playerState.year);
}
function configurePlayerControls(){
  const s=playerState.section;
  for(const b of $('player-tabs').children){b.classList.toggle('active',b.dataset.section===s);b.setAttribute('aria-current',b.dataset.section===s?'page':'false');}
  $('pp-year-label').hidden=['years','trend'].includes(s);
  const detailed=['games','plays','opponents','situations','pitches','direction'].includes(s);
  for(const id of ['pp-opponent-label','pp-start-label','pp-end-label'])$(id).hidden=!detailed;
  $('pp-view-label').hidden=!['years','situations','trend'].includes(s);
  let views=s==='situations'?[['runners','주자'],['inning','이닝'],['outs','아웃'],['score','점수'],['venue','홈/원정']]:s==='trend'?(playerState.role==='batter'?[['OPS','OPS'],['AVG','타율'],['HR','홈런'],['PA','타석']]:[['ERA','ERA'],['WHIP','WHIP'],['SO','탈삼진']]):[['basic','기본'],['advanced','심화'],['value','가치']];
  $('pp-view').replaceChildren(...views.map(([v,l])=>new Option(l,v)));if(views.some(([v])=>v===playerState.view))$('pp-view').value=playerState.view;playerState.view=$('pp-view').value;
}
async function loadPlayerSection(){
  playerState.controller?.abort();const controller=new AbortController();playerState.controller=controller;const seq=++playerState.sequence;
  playerState.year=Number($('pp-year').value)||null;playerState.view=$('pp-view').value;
  const s=playerState.section;$('player-content').replaceChildren();$('player-status').textContent='기록을 불러오는 중…';$('player-content').setAttribute('aria-busy','true');$('player-pagination').hidden=true;
  if(!playerState.year){$('player-status').textContent='선택한 경기 구분에 수집된 기록이 없습니다.';$('player-content').setAttribute('aria-busy','false');return;}
  const req={code:playerState.code,role:playerState.role,section:s,year:playerState.year,view:playerState.view,competition:$('pp-competition').value,page:playerState.page,pageSize:Math.min(25,state.catalog?.limits.maxPageSize??25),sort:playerState.sort,descending:playerState.descending};
  if(['games','plays','opponents','situations','pitches','direction'].includes(s)){req.opponent=$('pp-opponent').value||null;req.start=$('pp-start').value||null;req.end=$('pp-end').value||null;}
  try{
    const result=await api('/api/player',req,controller.signal);if(seq!==playerState.sequence)return;
    $('player-status').textContent='';
    if(s==='summary'){
      renderPlayerOverview(result);
      try{const direction=await api('/api/player',{...req,section:'direction',page:1},controller.signal);if(seq===playerState.sequence)renderPlayerDirection(direction);}catch(e){if(e.name!=='AbortError'&&seq===playerState.sequence)$('pp-direction').append(text('p',`타구 방향: ${e.message}`));}
    }else if(s==='trend'){renderPlayerTrend(result);renderPlayerTable(result);}
    else {
      renderPlayerTable(result);
      if(s==='years'&&playerState.page===1){
        const total=playerCard('통산 기록');$('player-content').append(total);
        try{const data=await api('/api/player',{...req,section:'career'},controller.signal);if(seq===playerState.sequence){const row={};for(const m of data.metrics)row[m.label]=m.display;total.append(playerTableElement(Object.keys(row),[row]),text('p','전체 활동 연도의 합산 기록입니다. 비율 지표는 통산 분자·분모로 다시 계산합니다.','player-note'));}}
        catch(e){if(e.name!=='AbortError'&&seq===playerState.sequence)total.append(text('p',e.message));}
      }
      if(s==='pitches'){
        const usage=playerCard('구종 비중 · 평균 구속');$('player-content').prepend(usage);
        try{const data=await api('/api/player',{...req,section:'arsenal',page:1,opponent:null,start:null,end:null},controller.signal);if(seq===playerState.sequence){usage.append(playerTableElement(data.columns,data.rows),text('p',data.note,'player-note'));}}
        catch(e){if(e.name!=='AbortError'&&seq===playerState.sequence)usage.append(text('p',e.message));}
      }
    }
  }catch(e){if(e.name!=='AbortError'&&seq===playerState.sequence)$('player-status').textContent=e.message;}
  finally{if(seq===playerState.sequence)$('player-content').setAttribute('aria-busy','false');}
}
function playerCard(title){const e=text('section','','player-card');e.append(text('h2',title));return e;}
function svgNode(tag,attrs={},content){const e=document.createElementNS('http://www.w3.org/2000/svg',tag);for(const [k,v] of Object.entries(attrs))e.setAttribute(k,String(v));if(content!==undefined)e.textContent=content;return e;}
function renderPlayerOverview(data){
  const content=$('player-content'),grid=text('div','','player-overview-grid');
  const ranking=playerCard(`${playerState.year} KBO Percentile Rankings`);ranking.classList.add('percentile-card');
  ranking.append(text('p','전체 선수 기준 · 기본·심화·가치 지표 통합','muted'));
  const metrics=data.metrics.filter(m=>m.population>0);
  if(!metrics.length)ranking.append(text('p','선택한 시즌에 비교할 수 있는 퍼센타일 기록이 없습니다.','player-empty'));
  const chart=svgNode('svg',{viewBox:`0 0 470 ${60+metrics.length*35}`,role:'img','aria-label':'선수 스탯 퍼센타일. 왼쪽 낮음, 가운데 평균, 오른쪽 우수.'});
  for(const [x,label] of [[122,'낮음'],[260,'평균'],[398,'우수']])chart.append(svgNode('text',{x,y:18,'text-anchor':'middle',class:'pct-axis'},label));
  for(const x of [122,260,398])chart.append(svgNode('line',{x1:x,x2:x,y1:27,y2:42+metrics.length*35,stroke:'#dae3e6','stroke-dasharray':'3 3'}));
  metrics.forEach((m,i)=>{
    const y=44+i*35,pct=m.percentile,color=pct===null?'#b6c6cc':pct>=90?'#e71936':pct>=65?'#ed7066':pct>=40?'#9ac6cb':'#598bbc';
    const row=svgNode('g');row.append(svgNode('title',{},`${m.label}: ${m.display}, 퍼센타일 ${pct===null?'비교군 부족':Math.round(pct)}, 비교군 ${m.population}명${m.lowerIsBetter?', 낮을수록 우수':''}`));
    row.append(svgNode('text',{x:111,y:y+5,'text-anchor':'end',class:'pct-label'},m.label));
    row.append(svgNode('rect',{x:122,y:y-10,width:276,height:22,fill:'#f0f4f5'}));
    if(pct!==null){const x=122+Math.max(0,Math.min(100,pct))*2.76;row.append(svgNode('rect',{x:122,y:y-10,width:x-122,height:22,fill:color}),svgNode('circle',{cx:x,cy:y+1,r:12,fill:color,stroke:'white','stroke-width':2}),svgNode('text',{x,y:y+5,'text-anchor':'middle',class:'pct-number'},Math.round(pct)));}
    row.append(svgNode('text',{x:453,y:y+5,'text-anchor':'end',class:'pct-value'},m.display));chart.append(row);
  });
  ranking.append(chart,text('p',data.reference,'player-note'));grid.append(ranking);
  const aside=text('div','','player-overview-aside'),stats=playerCard('주요 기록'),kpis=text('div','','player-kpis');
  const labels=playerState.role==='batter'?['WAR','PA','HR','AVG','OPS','wRC+']:['KBO fWAR','KBO fWAR v4','IP','ERA','FIP','WHIP','SO'];
  for(const m of data.metrics.filter(m=>labels.includes(m.label)).slice(0,6)){const k=text('div','','player-kpi');k.append(text('span',m.label),text('strong',m.display));kpis.append(k);}stats.append(kpis);aside.append(stats);
  const direction=playerCard('안타 방향');direction.id='pp-direction';direction.append(text('p','방향별 기록을 불러오는 중…','muted'));aside.append(direction);grid.append(aside);content.append(grid);
  const all=playerCard('시즌 기록');const row={};for(const m of data.metrics)row[m.label]=m.display;all.append(playerTableElement(Object.keys(row),[row]));content.append(all);
}
function renderPlayerDirection(data){
  const card=$('pp-direction');card.replaceChildren(text('h2','안타 방향'));const total=data.rows.reduce((n,r)=>n+Number(r.H??0),0);
  for(const r of data.rows){const line=text('div','','direction-row');line.append(text('span',r.Name),text('strong',`${r.H}개 · ${total?Math.round(Number(r.H)/total*100):0}%`));card.append(line);}if(!data.rows.length)card.append(text('p','선택 시즌의 안타 방향 기록이 없습니다.'));card.append(text('p',data.note,'player-note'));
}
function playerDisplay(value,key){if(value===null||value===undefined)return '—';if(['Team','TeamCode','Opponent'].includes(key))return String(value).split(',').map(t=>teamNames[t.trim()]??t.trim()).join(', ');if(typeof value!=='number')return String(value);if(['AVG','OBP','SLG','OPS','WPA'].includes(key))return value.toFixed(3);if(['ERA','WHIP'].includes(key))return value.toFixed(2);return String(value);}
function playerTableElement(columns,rows,sortable=false){
  const wrap=text('div','','player-table-wrap');wrap.tabIndex=0;wrap.setAttribute('aria-label','선수 기록 표, 가로 스크롤 가능');const table=document.createElement('table'),thead=document.createElement('thead'),tr=document.createElement('tr');
  for(const c of columns){const th=document.createElement('th');th.scope='col';const canSort=sortable&&(['opponents','pitches','situations'].includes(playerState.section)?['PA','AB','H','HR','BB','SO','OPS','AVG','OBP','SLG','Name'].includes(c):playerState.section==='plays'&&['Date','WPA'].includes(c));if(canSort){const b=text('button',c+(playerState.sort===c?(playerState.descending?' ↓':' ↑'):''));b.type='button';b.onclick=()=>{playerState.descending=playerState.sort===c?!playerState.descending:true;playerState.sort=c;playerState.page=1;loadPlayerSection();};th.append(b);th.setAttribute('aria-sort',playerState.sort===c?(playerState.descending?'descending':'ascending'):'none');}else th.textContent=c;th.title=STAT_HEADER_TITLES[c]??({Date:'경기 날짜',Opponent:'상대 팀',Name:'상대 / 구분',Year:'연도',Venue:'홈 / 원정',Result:'경기 / 타석 결과',Runners:'1루 / 2루 / 3루 주자',BeforeScore:'타석 전 원정:홈',AfterScore:'타석 후 원정:홈'}[c]??c);tr.append(th);}thead.append(tr);table.append(thead);const body=document.createElement('tbody');
  for(const row of rows){const r=document.createElement('tr');for(const c of columns){const cell=text('td',playerDisplay(row[c],c));r.append(cell);}body.append(r);}table.append(body);wrap.append(table);return wrap;
}
function renderPlayerTable(data){
  playerState.table=data;const card=playerCard(playerTabs.find(([id])=>id===playerState.section)?.[1]??'기록');
  if(data.rows.length)card.append(playerTableElement(data.columns,data.rows,true));else card.append(text('p','선택 조건에 해당하는 기록이 없습니다.','player-empty'));
  card.append(text('p',data.note,'player-note'));$('player-content').append(card);
  if(playerState.section!=='trend'){$('player-pagination').hidden=false;$('pp-page').textContent=`${data.page} 페이지 · ${data.rows.length}행`;$('pp-prev').disabled=data.page<=1;$('pp-next').disabled=!data.hasMore;}
}
function renderPlayerTrend(data){
  const key=playerState.view,points=data.rows.filter(r=>r[key]!==null&&r[key]!==undefined);const card=playerCard(`${key} · 연도별 추이`);$('player-content').append(card);
  if(!points.length){card.append(text('p','그래프를 그릴 기록이 없습니다.'));return;}
  const values=points.map(r=>Number(r[key])),lo=Math.min(0,...values),hi=Math.max(...values,lo+1),w=850,h=260;
  const svg=svgNode('svg',{viewBox:`0 0 ${w} ${h}`,class:'player-trend',role:'img','aria-label':`${key} 연도별 그래프`});
  for(let i=0;i<5;i++){const y=25+i*45;svg.append(svgNode('line',{x1:55,x2:825,y1:y,y2:y,stroke:'#e2e6ed'}),svgNode('text',{x:45,y:y+4,'text-anchor':'end',class:'pct-axis'},(hi-(hi-lo)*i/4).toFixed(2)));}
  const coords=points.map((r,i)=>[55+i*770/Math.max(1,points.length-1),205-(Number(r[key])-lo)/(hi-lo)*180]);svg.append(svgNode('polyline',{points:coords.map(p=>p.join(',')).join(' '),fill:'none',stroke:'#ef6a35','stroke-width':3}));
  coords.forEach(([x,y],i)=>{const g=svgNode('g');g.append(svgNode('title',{},`${points[i].Year}: ${playerDisplay(points[i][key],key)}`),svgNode('circle',{cx:x,cy:y,r:5,fill:'#ef6a35'}),svgNode('text',{x,y:235,'text-anchor':'middle',class:'pct-axis'},points[i].Year));svg.append(g);});card.append(svg);
}
const teamState={team:null,section:'overview',page:1,seq:0,controller:null};
function initTeamPage(){
  const entry=text('button','팀 정보','button outline');entry.id='open-teams';entry.onclick=()=>{const t=$('team').value||state.catalog?.teams?.[0]||'HH';location.hash=`team=${encodeURIComponent(t)}&year=${$('year').value||Math.max(...(state.catalog?.years??[new Date().getFullYear()]))}`;};$('open-search').parentElement.prepend(entry);
  const main=text('main','','player-page team-page');main.id='team-page';main.hidden=true;
  main.innerHTML='<div class="player-breadcrumb"><a href="#records">← 기록실</a><span>TEAM / 팀 정보</span></div><div class="player-hero"><div class="player-monogram" id="tp-mark"></div><div><div class="player-eyebrow">KBO TEAM PROFILE</div><h1 id="tp-title">팀 정보</h1><p id="tp-record"></p></div></div><nav id="tp-tabs" class="player-tabs" aria-label="팀 정보 탭"></nav><form id="tp-controls" class="player-controls"><label>팀<select id="tp-team"></select></label><label>시즌<select id="tp-year"></select></label><label>경기<select id="tp-competition"><option>정규시즌</option><option>포스트시즌</option><option>시범경기</option><option>전체</option></select></label><label id="tp-role-label" hidden>선수<select id="tp-role"><option value="batter">타자</option><option value="pitcher">투수</option></select></label><button class="button primary">조회</button></form><p id="tp-status" role="status"></p><div id="tp-content"></div><div id="tp-pages" class="player-pagination" hidden><button id="tp-prev" class="button outline">← 이전</button><span id="tp-page"></span><button id="tp-next" class="button outline">다음 →</button></div><p class="player-source">자체 DB에 적재된 기록 기준 · 수상·연봉·코칭스태프 정보는 현재 제공하지 않습니다.</p>';
  $('workspace').after(main);
  for(const [key,label] of [['overview','종합'],['schedule','경기 일정'],['roster','선수 기록'],['scores','득실점 분석']]){const b=text('button',label);b.dataset.section=key;b.onclick=()=>{teamState.section=key;teamState.page=1;loadTeam();};$('tp-tabs').append(b);}
  $('tp-controls').onsubmit=e=>{e.preventDefault();teamState.page=1;if($('tp-team').value!==teamState.team)location.hash=`team=${encodeURIComponent($('tp-team').value)}&year=${$('tp-year').value}`;else loadTeam();};
  $('tp-prev').onclick=()=>{teamState.page--;loadTeam();};$('tp-next').onclick=()=>{teamState.page++;loadTeam();};
  window.addEventListener('hashchange',teamRoute);
  for(const b of document.querySelectorAll('.room,.role'))b.addEventListener('click',()=>{if(teamState.team)location.hash='';});
}
async function teamRoute(){
  if(!state.catalog)return;const p=new URLSearchParams(location.hash.slice(1)),team=p.get('team');teamState.controller?.abort();++teamState.seq;
  if(!team){const old=teamState.team;teamState.team=null;$('team-page').hidden=true;if(!p.has('player')){$('workspace').hidden=false;if(old&&!state.schema.length)await changeView();}return;}
  abortQuery();$('workspace').hidden=true;$('player-page').hidden=true;$('team-page').hidden=false;teamState.team=team;teamState.page=1;teamState.section='overview';
  $('tp-team').replaceChildren(...state.catalog.teams.map(t=>new Option(teamNames[t]??t,t)));$('tp-team').value=team;
  $('tp-year').replaceChildren(...[...state.catalog.years].sort((a,b)=>b-a).map(y=>new Option(y,y)));if(p.get('year'))$('tp-year').value=p.get('year');if(!$('tp-year').value)$('tp-year').selectedIndex=0;
  await loadTeam();
}
function teamLink(code,name,role='batter'){const a=text('a',name,'player-link');a.href=`#player=${encodeURIComponent(code)}&role=${role}`;return a;}
function teamValue(v,key=''){if(v===null||v===undefined)return '—';if(typeof v==='number'&&['PCT','AVG','OBP','SLG','OPS','ERA','WHIP'].includes(key))return v.toFixed(key==='ERA'||key==='WHIP'?2:3);return String(v);}
function teamTable(columns,rows){
  const names={Name:'선수',Date:'날짜',Time:'시각',Opponent:'상대',Venue:'홈/원정',Stadium:'구장',Result:'결과',RF:'득점',RA:'실점',G:'경기',W:'승',D:'무',L:'패',PCT:'승률',Team:'팀',Rank:'순위'};
  const wrap=text('div','','player-table-wrap'),t=document.createElement('table'),h=document.createElement('thead'),hr=document.createElement('tr');for(const k of columns)hr.append(text('th',names[k]??k));h.append(hr);t.append(h);const body=document.createElement('tbody');
  for(const r of rows){const tr=document.createElement('tr');for(const k of columns){const td=text('td',teamValue(r[k],k));if(k==='Name'&&r.Code)td.replaceChildren(teamLink(r.Code,r.Name,$('tp-role').value));if(['Team','Opponent'].includes(k)&&r[k]){const a=text('a',teamNames[r[k]]??r[k],'player-link');a.href=`#team=${encodeURIComponent(r[k])}&year=${$('tp-year').value}`;td.replaceChildren(a);}if(k==='Result')td.classList.add(r[k]==='승'?'team-win':r[k]==='패'?'team-loss':'');tr.append(td);}body.append(tr);}t.append(body);wrap.append(t);if(!rows.length)wrap.append(text('p','해당 조건에 저장된 기록이 없습니다.','player-empty'));return wrap;
}
function teamCard(title,columns,rows){const c=playerCard(title);c.append(teamTable(columns,rows));return c;}
async function loadTeam(){
  $('tp-record').textContent='';
  teamState.controller?.abort();const controller=new AbortController();teamState.controller=controller;const seq=++teamState.seq;
  const section=teamState.section;for(const b of $('tp-tabs').children){b.classList.toggle('active',b.dataset.section===section);b.setAttribute('aria-current',b.dataset.section===section?'page':'false');}$('tp-role-label').hidden=section!=='roster';$('tp-content').replaceChildren();$('tp-pages').hidden=true;$('tp-status').textContent='팀 기록을 불러오는 중…';
  const year=Number($('tp-year').value);$('tp-title').textContent=`${year} ${teamNames[teamState.team]??teamState.team}`;$('tp-mark').textContent=teamNames[teamState.team]??teamState.team;document.title=`${$('tp-title').textContent} · KBO Sabermetrics`;
  try{const data=await api('/api/team',{team:teamState.team,year,competition:$('tp-competition').value,section,role:$('tp-role').value,page:teamState.page},controller.signal);if(seq!==teamState.seq)return;$('tp-status').textContent='';
    if(section==='overview')renderTeamOverview(data);else if(section==='scores')renderTeamScores(data);else{$('tp-content').append(teamCard(section==='roster'?'팀 소속 선수 기록':'경기 일정 · 결과',data.columns,data.rows));$('tp-pages').hidden=false;$('tp-page').textContent=teamState.page;$('tp-prev').disabled=teamState.page===1;$('tp-next').disabled=!data.hasMore;}
  }catch(e){if(e.name!=='AbortError'&&seq===teamState.seq)$('tp-status').textContent=e.message;}
}
function renderTeamOverview(d){
  const root=$('tp-content'),grid=text('div','','team-grid'),left=text('div'),right=text('div');grid.append(left,right);root.append(grid);
  const standing=d.standings.find(t=>t.Team===teamState.team);$('tp-record').textContent=`${standing?.Rank?standing.Rank+'위 · ':''}${d.record.W}승 ${d.record.D}무 ${d.record.L}패 · 승률 ${teamValue(d.record.PCT,'PCT')}`;
  const calendar=playerCard('최근 경기'),days=text('div','','team-calendar');for(const g of d.recent){const card=text('div','','team-game');card.append(text('small',g.Date?.slice(0,10)??'날짜 미상'),text('strong',`${g.Venue==='원정'?'@ ':''}${teamNames[g.Opponent]??g.Opponent}`),text('span',`${g.Result} ${g.RF??'—'}:${g.RA??'—'}`,g.Result==='승'?'team-win':g.Result==='패'?'team-loss':''));days.append(card);}calendar.append(days);if(!d.recent.length)calendar.append(text('p','저장된 경기가 없습니다.','player-empty'));left.append(calendar);
  const last=playerCard('이전 경기 결과');if(d.latest){last.append(text('p',`${d.latest.Date?.slice(0,10)} · ${teamNames[d.latest.Opponent]??d.latest.Opponent} · ${d.latest.Result} ${d.latest.RF}:${d.latest.RA}`));const innings=[...new Set(d.line.map(x=>x.Inning))].sort((a,b)=>a-b),rows=[d.latest.Opponent,teamState.team].map(t=>{const row={Team:t,R:t===teamState.team?d.latest.RF:d.latest.RA};for(const i of innings){const line=d.line.find(x=>x.Team===t&&x.Inning===i);row[i]=line&&line.StartScore!==null&&line.EndScore!==null?Math.max(0,line.EndScore-line.StartScore):null;}return row;});last.append(teamTable(['Team',...innings,'R'],rows),text('p','이닝 점수는 저장된 중계에서 복원합니다. 미수집 이닝은 — 표시.','player-note'));}else last.append(text('p','종료된 경기가 없습니다.'));left.append(last);
  const next=playerCard('다음 경기 일정');next.append(text('p',d.next?`${d.next.Date} · ${d.next.Time??''} · ${d.next.Venue} vs ${teamNames[d.next.Opponent]??d.next.Opponent} · ${d.next.Stadium??''}`:'DB에 저장된 향후 일정이 없습니다.'));left.append(next);
  const field=playerCard('포지션별 주요 선수'),diamond=text('div','','team-field');const positions={CF:[50,22],LF:[22,36],RF:[78,36],SS:[38,51],'2B':[62,51],'3B':[22,68],'1B':[78,68],P:[50,68],C:[50,91],DH:[85,92]};
  const park=svgNode('svg',{viewBox:'0 0 500 520',class:'team-field-art','aria-hidden':'true'});
  park.innerHTML='<defs><pattern id="team-grass" width="40" height="40" patternUnits="userSpaceOnUse"><rect width="40" height="40" fill="#47964a"/><rect width="20" height="40" fill="#3e8842"/></pattern></defs><path d="M250 435 L35 220 Q250 -20 465 220 Z" fill="#bc9869" stroke="#d1b48c" stroke-width="7"/><path d="M250 419 L49 220 Q250 1 451 220 Z" fill="url(#team-grass)"/><path d="M250 420 L135 305 Q250 158 365 305 Z" fill="#cfaa76"/><path d="M250 399 L162 311 L250 223 L338 311 Z" fill="#60a459"/><path d="M48 221 L250 420 L452 221 M250 420 L145 315 L250 210 L355 315 Z" fill="none" stroke="#fff9e8" stroke-width="2.5"/><circle cx="250" cy="315" r="19" fill="#cfaa76"/><rect x="242" y="310" width="16" height="5" rx="1" fill="#fff"/><g fill="#fff" stroke="#d6d6c9"><path d="M242 411 H258 V420 L250 427 L242 420 Z"/><path d="M138 315 L145 308 L152 315 L145 322 Z"/><path d="M243 210 L250 203 L257 210 L250 217 Z"/><path d="M348 315 L355 308 L362 315 L355 322 Z"/><rect x="227" y="407" width="10" height="22" fill="none"/><rect x="263" y="407" width="10" height="22" fill="none"/></g>';
  diamond.append(park);
  for(const p of d.field){const [x,y]=positions[p.position]??[50,50],tile=text('div','','team-fielder');tile.style.left=x+'%';tile.style.top=y+'%';tile.append(text('small',p.position==='P'?'P · 투수':p.position),teamLink(p.code,p.name,p.position==='P'?'pitcher':'batter'),text('small',`${p.position==='P'?p.innings:p.volume.toFixed(1)} ${p.position==='DH'?'PA':'이닝'}`));diamond.append(tile);}field.append(diamond,text('p','수비: 포지션별 최다 출장 이닝 · 투수: 팀 최다 투구이닝 · DH: 타석 기준','player-note'));left.append(field);
  const titles=playerCard('주요 타이틀'),list=text('div','','team-leaders');for(const l of d.leaders){const row=text('div','','team-leader');row.append(text('span',l.metric),teamLink(l.code,l.name,l.role),text('strong',teamValue(l.value,l.metric)));list.append(row);}titles.append(list,text('p','비율 지표는 규정 기준 충족 선수만 표시합니다. 해당 선수가 없으면 생략됩니다.','player-note'));right.append(titles);
  right.append(teamCard('팀별 대결 기록',['Team','G','W','D','L','PCT'],d.opponents.map(o=>({Team:o.team,...o.record}))));
  right.append(teamCard('리그 순위',['Rank','Team','G','W','D','L','PCT','RF','RA'],d.standings));
  const analysis=playerCard('팀 득실점 분석'),kpis=text('div','','player-kpis');for(const [label,v] of [['득점',d.record.RF],['실점',d.record.RA],['득실차',d.record.RF-d.record.RA]]){const k=text('div','','player-kpi');k.append(text('span',label),text('strong',v));kpis.append(k);}analysis.append(kpis);right.append(analysis);root.append(text('p',d.note,'player-note'));
}
function renderTeamScores(d){const root=$('tp-content'),grid=text('div','','team-grid');root.append(grid);for(const [key,title] of [['scored','득점 분포 및 승률'],['allowed','실점 분포 및 승률']])grid.append(teamCard(title,['점수','G','W','D','L','PCT'],d[key].map(x=>({'점수':x.score,...x.record}))));for(const side of ['득점','실점'])grid.append(teamCard(`이닝별 ${side}`,['이닝','경기','점수','평균'],d.innings.filter(x=>x.side===side).map(x=>({'이닝':x.inning,'경기':x.games,'점수':x.runs,'평균':x.average}))));for(const side of ['득점','실점'])grid.append(teamCard('이닝별 '+side+' 빈도',['이닝','0점','1점','2점','3점','4점','5점 이상'],d.inningDistribution.filter(x=>x.side===side).map(x=>({'이닝':x.inning,'0점':x.bins[0],'1점':x.bins[1],'2점':x.bins[2],'3점':x.bins[3],'4점':x.bins[4],'5점 이상':x.bins[5]}))));root.append(teamCard('이닝 시작 상황에 따른 경기 승률',['이닝','상황','G','W','D','L','PCT'],d.states.map(x=>({'이닝':x.inning,'상황':x.state,...x.record}))),text('p',d.note,'player-note'));}
const homeState={seq:0,controller:null};
function initHome(){
  const home=text('main','','home-page');home.id='home-page';home.hidden=true;
  home.innerHTML='<div class="home-heading"><div><p class="player-eyebrow">KBO SABERMETRICS</p><h1>오늘의 리그</h1><p>팀과 선수의 현재 기록, 득실점으로 바라본 남은 시즌</p></div><label>시즌 <select id="home-year"></select></label></div><p id="home-status" role="status"></p><div class="home-top-grid"><section id="home-standings" class="player-card"><h2>팀 순위 · 피타고리안 전망</h2></section></div><div class="home-leaders-grid"><section id="home-war" class="player-card"><h2>WAR TOP 10</h2></section><section id="home-batters" class="player-card"><h2>타자 주요 순위</h2></section><section id="home-pitchers" class="player-card"><h2>투수 주요 순위</h2></section></div><p id="home-leaders-note" class="player-note"></p><details class="home-method"><summary>피타고리안 승률·포스트시즌 확률 계산 안내</summary><p id="home-model-note"></p><p><a href="https://m.koreabaseball.com/About/GameManage.aspx" target="_blank" rel="noopener">KBO 경기 운영 방식</a> · <a href="https://www.baseball-reference.com/bullpen/Pythagorean_W-L" target="_blank" rel="noopener">피타고리안 승률 설명</a></p></details>';
  $('workspace').before(home);$('home-year').onchange=loadHome;
  const button=text('button','홈','room');button.onclick=()=>{if(!location.hash)homeRoute();else location.hash='';};document.querySelector('.room-nav').prepend(button);
  for(const b of document.querySelectorAll('.room[data-room],.role'))b.addEventListener('click',()=>{location.hash='records';});
  window.addEventListener('hashchange',homeRoute);
}
async function homeRoute(){
  if(!state.catalog)return;const hash=location.hash;const home=!hash||hash==='#'||hash==='#home';homeState.controller?.abort();++homeState.seq;$('home-page').hidden=!home;
  if(!home){if(hash==='#records'){$('workspace').hidden=false;if(!state.schema.length)await changeView();}return;}
  abortQuery();$('workspace').hidden=true;$('player-page').hidden=true;$('team-page').hidden=true;document.title='KBO Sabermetrics · 홈';
  if(!$('home-year').options.length)$('home-year').replaceChildren(...[...state.catalog.years].sort((a,b)=>b-a).map(y=>new Option(y,y)));
  await loadHome();
}
function homePlayer(p){const wrap=text('div','','home-player');wrap.append(teamLink(p.code,p.name,p.role),text('small',String(p.team).split(',').map(t=>teamNames[t.trim()]??t.trim()).join(', ')),text('strong',p.display));return wrap;}
function homeTable(headers,rows){const wrap=text('div','','player-table-wrap'),table=document.createElement('table'),thead=document.createElement('thead'),tr=document.createElement('tr');for(const h of headers)tr.append(text('th',h));thead.append(tr);table.append(thead);const body=document.createElement('tbody');for(const row of rows){const r=document.createElement('tr');for(const v of row){const td=document.createElement('td');if(v instanceof Node)td.append(v);else td.textContent=v??'—';r.append(td);}body.append(r);}table.append(body);wrap.append(table);return wrap;}
async function loadHome(){
  homeState.controller?.abort();const controller=new AbortController();homeState.controller=controller;const seq=++homeState.seq;const year=Number($('home-year').value);$('home-status').textContent='리그 기록을 불러오는 중…';
  for(const [id,title] of [['home-war','WAR TOP 10'],['home-standings','팀 순위 · 피타고리안 전망'],['home-batters','타자 주요 순위'],['home-pitchers','투수 주요 순위']])$(id).replaceChildren(text('h2',title),text('p','불러오는 중…','muted'));$('home-model-note').textContent='';
  const results=await Promise.allSettled(['standings','leaders'].map(async section=>{const data=await api('/api/home',{year,section},controller.signal);if(seq!==homeState.seq)return;if(section==='standings')renderHomeStandings(data,year);else renderHomeLeaders(data);}));
  if(seq!==homeState.seq)return;const errors=results.filter(x=>x.status==='rejected'&&x.reason.name!=='AbortError');$('home-status').textContent=errors.length?errors.map(x=>x.reason.message).join(' · '):`${year} 정규시즌 · 수집된 종료 경기 기준`;
  for(let i=0;i<results.length;i++)if(results[i].status==='rejected'&&results[i].reason.name!=='AbortError')for(const id of i===0?['home-standings']:['home-war','home-batters','home-pitchers']){const p=$(id).querySelector('p');if(p)p.textContent='기록을 불러오지 못했습니다. 시즌을 다시 선택해 재시도할 수 있습니다.';}
}
function renderHomeStandings(d,year){
  const card=$('home-standings');card.replaceChildren(text('h2',`${year} 팀 순위 · 피타고리안 전망`));const pct=x=>x==null?'—':(x*100).toFixed(1)+'%';
  const rows=d.rows.map(r=>{const link=text('a',teamNames[r.team]??r.team,'player-link');link.href=`#team=${encodeURIComponent(r.team)}&year=${year}`;const chance=text('span',pct(r.playoff),'home-prob');if(r.playoff!=null)chance.style.setProperty('--chance',(r.playoff*100)+'%');chance.title='피타고리안 승률 기반 자체 모델 추정';return [r.rank,link,r.g,r.w,r.d,r.l,r.gb.toFixed(1),r.pct==null?'—':r.pct.toFixed(3),r.rf,r.ra,pct(r.pyth),r.winDifference==null?'—':(r.winDifference>0?'+':'')+r.winDifference.toFixed(1),chance];});
  card.append(homeTable(['순위','팀','경기','승','무','패','승차','승률','득점','실점','피타고리안','실제−예상 승','PS 진출 추정'],rows));if(!d.rows.length)card.append(text('p','해당 시즌의 정규시즌 기록이 없습니다.','player-empty'));
  card.append(text('p',`기준일 ${d.asOf?.slice(0,10)??'—'} · ${d.forecastAvailable?'남은 상대별 대진 10,000회 시뮬레이션 · 공식 확률 아님':d.reason}`,'player-note'));$('home-model-note').textContent=d.note;
}
function renderHomeLeaders(d){
  const war=$('home-war');war.replaceChildren(text('h2','WAR TOP 10'));war.append(homeTable(['순위','선수','구분','WAR'],d.war.map((p,i)=>[i+1,homePlayer({...p,display:''}),p.role==='pitcher'?'투수':'타자',p.display])));if(!d.war.length)war.append(text('p','표시 가능한 WAR 기록이 없습니다.','player-empty'));document.getElementById("home-leaders-note").textContent=d.note;
  const names={AVG:'타율',OBP:'출루율',SLG:'장타율',OPS:'OPS',HR:'홈런',RBI:'타점',SB:'도루',ERA:'평균자책점',WHIP:'WHIP',FIP:'FIP',SO:'탈삼진','K/9':'9이닝당 삼진','BB/9':'9이닝당 볼넷'};
  for(const role of ['batter','pitcher']){const card=$(role==='batter'?'home-batters':'home-pitchers');card.replaceChildren(text('h2',role==='batter'?'타자 주요 순위':'투수 주요 순위'));card.append(homeTable(['항목','1위','2위','3위'],d.leaders.filter(x=>x.role===role).map(x=>[names[x.metric]??x.metric,...[0,1,2].map(i=>x.players[i]?homePlayer(x.players[i]):'—')])));}
}
initPlayerPage();initTeamPage();initHome();navigation();bootstrap();
