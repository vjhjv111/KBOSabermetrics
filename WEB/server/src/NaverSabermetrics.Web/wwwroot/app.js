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
  options($('year'),c.years, String(Math.max(...c.years)));
  options($('team'),c.teams.map(t=>[t,teamNames[t]?`${t} · ${teamNames[t]}`:t]));
  options($('opponent'),c.teams.map(t=>[t,teamNames[t]?`${t} · ${teamNames[t]}`:t]));
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
  await changeView();
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
function makeSortHeader(c){
  const th=document.createElement('th');th.scope='col';
  th.className=c.key==='Applied'?'applied':c.key==='Name'?'name':c.key==='Rank'?'rank':c.kind==='text'?'text':'';
  if(c.sortable){
    const b=text('button',c.label);b.type='button';b.title=`${c.label} 기준 서버 정렬`;
    if(state.applied?.sortBy===c.key){th.setAttribute('aria-sort',state.applied.descending?'descending':'ascending');b.append(text('span',state.applied.descending?'▼':'▲','sort-arrow'));}
    b.onclick=()=>{if(!state.applied)return;const desc=state.applied.sortBy===c.key?!state.applied.descending:true;query({...state.applied,sortBy:c.key,descending:desc,page:1});};
    th.append(b);
  }else th.textContent=c.label;
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
      if(['WrcPlus','OPS','ERA'].includes(col.key))td.classList.add('stat-emphasis');
      if(col.key==='Name'&&row.entityCode&&state.room!=='team'){
        const b=text('button',value,'player-link');b.title='이 선수만 조회';b.type='button';b.onclick=()=>selectPlayer(row.entityCode,value);
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
  state.playerCode=code;$('player-name').value=name;$('player-chip').hidden=false;$('player-chip').querySelector('span').textContent=`${name} · ${code}`;
  markDirty();$('search-dialog').close();
  // Explicit navigation uses the current draft, not an old response's hidden filters.
  try{query(readRequest());}catch(e){showError(e.message);}
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
navigation();bootstrap();
