function gameTeamScore(g,tag='span'){const line=text(tag,'');line.append(teamNameNode(g.Away),document.createTextNode(` ${g.AScore??'—'} : ${g.HS??'—'} `),teamNameNode(g.Home));return line;}
function gameStatusLabel(status){return ['RESULT','ENDED'].includes(status)?'종료':['BEFORE','READY'].includes(status)?'예정':['STARTED','PLAY'].includes(status)?'진행 중':status==='CANCEL'?'취소':status??'';}
const gameState={seq:0,controller:null,month:null,timer:null};
const gameRoot=text('main','','game-page');gameRoot.id='game-page';gameRoot.hidden=true;$('workspace').before(gameRoot);
const gameNav=text('button','경기일정','room');gameNav.dataset.route='games';gameNav.onclick=()=>{location.hash='games';};document.querySelector('.room-nav').append(gameNav);
window.addEventListener('hashchange',gameRoute);
async function gameRoute(){
  clearTimeout(gameState.timer);if(!state.catalog)return;const params=new URLSearchParams(location.hash.slice(1));const active=location.hash==='#games'||params.has('game');gameState.controller?.abort();const seq=++gameState.seq;gameRoot.hidden=!active;if(!active)return;
  for(const id of ['workspace','home-page','player-page','team-page'])$(id).hidden=true;abortQuery();
  gameState.controller=new AbortController();const signal=gameState.controller.signal;
  gameRoot.replaceChildren(text('h1',params.has('game')?'경기 상세':'경기일정'),text('p','불러오는 중…'));
  try{if(params.has('game')){const d=await api('/api/games',{section:'detail',id:params.get('game')},signal);if(seq===gameState.seq){renderGame(d);if(!['RESULT','ENDED','CANCEL'].includes(d.game.Status))gameState.timer=setTimeout(gameRoute,60000);}}else{
    gameState.month??=state.catalog.maxDate?.slice(0,7)??`${Math.max(...state.catalog.years)}-01`;const [year,month]=gameState.month.split('-').map(Number);const d=await api('/api/games',{section:'calendar',year,month},signal);if(seq===gameState.seq)renderCalendar(d,year,month);
  }}catch(e){if(e.name!=='AbortError'&&seq===gameState.seq)gameRoot.replaceChildren(text('h1','경기일정'),text('p',e.message));}
}
function renderCalendar(d,year,month){
  document.title='경기일정 · FANZAI';gameRoot.replaceChildren(text('h1','경기일정'));
  const controls=text('div','','game-month-controls');const prev=text('button','← 이전 달','button outline'),next=text('button','다음 달 →','button outline'),input=document.createElement('input');input.type='month';input.value=gameState.month;input.min='1900-01';input.max='2200-12';input.setAttribute('aria-label','조회 월');
  input.onchange=()=>{if(input.validity.valid&&input.value){gameState.month=input.value;gameRoute();}};
  function shift(delta){const date=new Date(year,month-1+delta,1);if(date.getFullYear()<1900||date.getFullYear()>2200)return;gameState.month=`${date.getFullYear()}-${String(date.getMonth()+1).padStart(2,'0')}`;gameRoute();}prev.onclick=()=>shift(-1);next.onclick=()=>shift(1);controls.append(prev,input,next);gameRoot.append(controls,text('p','수집된 정규시즌·포스트시즌 경기를 표시합니다. 경기를 누르면 일정·선발 정보 또는 수집된 상세 기록을 볼 수 있습니다.','player-note'));
  const wrap=text('div','','game-calendar-scroll'),grid=text('div','','game-calendar');for(const day of ['일','월','화','수','목','금','토'])grid.append(text('div',day,'calendar-weekday'));
  for(let i=0;i<new Date(year,month-1,1).getDay();i++)grid.append(text('div','','calendar-blank'));
  for(let day=1;day<=new Date(year,month,0).getDate();day++){const cell=text('section','','calendar-day'),date=`${gameState.month}-${String(day).padStart(2,'0')}`;const dateLabel=text('strong',`${month}월 ${day}일`,'calendar-date');dateLabel.append(text('span',` (${['일','월','화','수','목','금','토'][new Date(year,month-1,day).getDay()]})`,'mobile-weekday'));cell.append(dateLabel);const games=d.rows.filter(g=>g.Date?.slice(0,10)===date);for(const g of games){const a=text('a','','calendar-game');a.href=`#game=${encodeURIComponent(g.Id)}`;a.append(gameTeamScore(g),text('small',`${gameStatusLabel(g.Status)} · ${g.Stadium??''}${g.Round?.startsWith('kbo_ps')?' · 포스트시즌':''}`));cell.append(a);}if(!games.length)cell.append(text('small','수집 경기 없음','muted'));grid.append(cell);}wrap.tabIndex=0;wrap.setAttribute('aria-label','경기 달력');wrap.append(grid);gameRoot.append(wrap);
}
function gameScoreboard(d){
  const g=d.game;const count=Math.max(d.original?.home?.length??0,d.original?.away?.length??0,...d.innings.map(i=>Number(i.Inning)),9);const rows=['away','home'].map(side=>{const home=side==='home',code=g[home?'Home':'Away'];const original=d.original?.[side];return [teamNameNode(code),...Array.from({length:count},(_,i)=>{if(original?.[i]!=null)return original[i];const x=d.innings.find(x=>x.Team===code&&Number(x.Inning)===i+1);return x?.Start!=null&&x?.Finish!=null?x.Finish-x.Start:'—';}),g[home?'HS':'AScore'],g[home?'HH':'AH'],g[home?'HE':'AE'],g[home?'HB':'AB']];});return homeTable(['팀',...Array.from({length:count},(_,i)=>i+1),'R','H','E','B'],rows);
}
function renderGame(d){
  const g=d.game,title=`${teamNames[g.Away]??g.Away} ${g.AScore??'—'} : ${g.HS??'—'} ${teamNames[g.Home]??g.Home}`;document.title=title+' · 경기 상세';const back=text('a','← 경기 달력','player-link');back.href='#games';gameRoot.replaceChildren(back,gameTeamScore(g,'h1'),text('p',`${g.Date?.slice(0,10)} · ${g.Stadium??''} · ${g.StatusText??gameStatusLabel(g.Status)}`));
  if(d.note)gameRoot.append(text('p',d.note,'player-note'));
  if(d.preview){
    if(g.Status!=='CANCEL')for(const side of ['away','home']){
      const card=playerCard(`${teamNames[g[side==='away'?'Away':'Home']]??g[side==='away'?'Away':'Home']} · 선발 라인업`);
      const players=d.lineups?.[side]??[],pitcher=d.lineups?.[side+'Pitcher'];
      card.append(text('p',`선발투수: ${pitcher??'미발표'}`));
      if(players.length)card.append(homeTable(['타순','선수','포지션'],players.map(p=>[p.order,p.name,p.position])));
      else card.append(text('p','선발 라인업 미발표 또는 수집 대기 중'));
      gameRoot.append(card);
    }
    return;
  }
  const board=playerCard('이닝별 점수');board.append(gameScoreboard(d));const decisions=text('p');decisions.textContent=d.decisions.map(p=>`${p.label} ${p.name}`).join(' · ');board.append(decisions,text('p',d.original?'원본 이닝 점수 기준':'중계 점수에서 복원 · 미수집 이닝은 — 표시','player-note'));gameRoot.append(board);
  const probability=playerCard('경기 승리확률');probability.append(gameProbability(d));gameRoot.append(probability);
  gameRoot.append(gameHighlights(d));
  for(const side of ['Away','Home']){const code=g[side];for(const role of ['batter','pitcher']){const rows=(role==='batter'?d.batters:d.pitchers).filter(x=>x.Team===code);const columns=role==='batter'?['BatOrder','Name','Position','PA','AB','H','HR','R','RBI','BB','HBP','SO']:['Name','IP','NP','H','HR','R','ER','BB','HBP','SO'];const labels={BatOrder:'타순',Name:'선수',Position:'위치'};const card=playerCard('');card.querySelector('h2').append(teamNameNode(code),document.createTextNode(` · ${role==='batter'?'타자':'투수'} 박스스코어`));card.append(homeTable(columns.map(k=>labels[k]??k),rows.map(r=>columns.map(k=>k==='Name'?teamLink(r.Code,r.Name,role):r[k]))));if(!rows.length)card.append(text('p','수집된 기록이 없습니다.'));gameRoot.append(card);}}
  gameRoot.append(gamePlayLog(d));
}
function gamePlayLabel(p,g){return `${p.Inning??'—'}회${p.Team===g.Away?'초':p.Team===g.Home?'말':''}`;}
function gameWpa(value){return value==null?'—':`${value>0?'+':''}${Number(value).toFixed(1)}%p`;}
function rankedGamePlays(plays){return plays.filter(p=>p.WPA!=null&&Number.isFinite(Number(p.WPA))).sort((a,b)=>Math.abs(b.WPA)-Math.abs(a.WPA)||a.Seq-b.Seq).slice(0,5);}
function gameHighlights(d){
  const card=playerCard('WPA 변동 TOP 5'),plays=rankedGamePlays(d.plays??[]);card.classList.add('game-highlights');
  card.append(text('p','공격팀 기준 WPA 절댓값 순위 · +는 공격팀에 유리한 변화, −는 불리한 변화입니다. 원본 WPA가 없는 중계는 제외합니다.','player-note'));
  card.append(homeTable(['순위','이닝','공격팀','순간','WPA'],plays.map((p,i)=>{const a=text('a',[p.Title,...(p.events??[]).filter(line=>line.includes(' : '))].filter(Boolean).join(' · ')||'중계 보기','player-link');a.href=`#play-${p.Seq}`;a.onclick=e=>{e.preventDefault();const target=document.getElementById(`play-${p.Seq}`);if(target){target.open=true;target.scrollIntoView({behavior:'smooth',block:'center'});target.focus();}};return [1+plays.filter(x=>Math.abs(x.WPA)>Math.abs(p.WPA)).length,gamePlayLabel(p,d.game),teamNameNode(p.Team),a,gameWpa(p.WPA)];})));
  if(!plays.length)card.append(text('p','수집된 WPA 데이터가 없습니다.'));return card;
}
// 타석별 미니 스트라이크존 — 배경·선수 이미지 없이 존과 번호가 매겨진 투구 위치만 그립니다.
// X는 홈플레이트 기준 원본 좌표(ft), z는 그 투구의 존 하단(0)~상단(1)으로 정규화된 값입니다.
const PITCH_ZONE_HALF_WIDTH = 0.7083;
const pitchCategoryColors = { ball: '#3ba866', strike: '#df3131', foul: '#e0a72b', inplay: '#3989dc', other: '#8a97a8' };
const pitchCategoryLabels = { ball: '볼', strike: '스트라이크', foul: '파울', inplay: '타격', other: '결과 미상' };
// FANZAI_FIXED_STRIKE_ZONE_V1_BEGIN
// Fixed CSS-pixel scale: expand the canvas, never rescale the strike zone.
// x retains the API's feet unit. z is relative to each pitch's zone (bottom 0, top 1).
// This is a normalized display, not a physical-height comparison between batters.
const PITCH_ZONE_DRAWING = Object.freeze({
  zoneWidth: 60,
  zoneHeight: 80,
  minWidth: 160,
  minHeight: 160,
  maxWidth: 480,
  maxHeight: 480,
  radius: 9,
  padding: 12
});

function pitchZoneGeometry(pitches){
  const cfg=PITCH_ZONE_DRAWING;
  const valid=(Array.isArray(pitches)?pitches:[])
    .filter(p=>p && Number.isFinite(p.x) && Number.isFinite(p.z));
  if(!valid.length)return null;

  const scaleX=cfg.zoneWidth/(2*PITCH_ZONE_HALF_WIDTH);
  const scaleY=cfg.zoneHeight;
  const inset=cfg.radius+cfg.padding;
  let halfWidth=cfg.minWidth/2,halfHeight=cfg.minHeight/2;
  for(const p of valid){
    // Bound BEFORE multiplication so even corrupt finite coordinates cannot overflow.
    const dx=Math.min(Math.abs(p.x),(cfg.maxWidth/2-inset)/scaleX)*scaleX;
    const dy=Math.min(Math.abs(p.z-0.5),(cfg.maxHeight/2-inset)/scaleY)*scaleY;
    halfWidth=Math.max(halfWidth,dx+inset);
    halfHeight=Math.max(halfHeight,dy+inset);
  }
  const width=Math.min(cfg.maxWidth,2*Math.ceil(halfWidth));
  const height=Math.min(cfg.maxHeight,2*Math.ceil(halfHeight));
  const centerX=width/2,centerY=height/2;
  const xLimit=(centerX-inset)/scaleX;
  const zLimit=(centerY-inset)/scaleY;
  const points=valid.map(p=>{
    // Beyond the safety limit, show a distinctly marked boundary indicator.
    // Project onto the boundary along the original direction; never pretend it is
    // an actual pitch at the boundary, and retain the original coordinates in the tooltip.
    const factor=Math.max(1,Math.abs(p.x)/xLimit,Math.abs(p.z-0.5)/zLimit);
    const dx=(p.x/factor)*scaleX;
    const dy=((0.5-p.z)/factor)*scaleY;
    const distance=Math.hypot(dx,dy);
    return {pitch:p,cx:centerX+dx,cy:centerY+dy,outside:factor>1,
      ux:distance>0?dx/distance:0,uy:distance>0?dy/distance:0};
  });
  return {width,height,centerX,centerY,
    zone:{x:centerX-cfg.zoneWidth/2,y:centerY-cfg.zoneHeight/2,
      width:cfg.zoneWidth,height:cfg.zoneHeight},
    points,outsideCount:points.filter(p=>p.outside).length};
}

function renderPitchZone(pitches){
  const geometry=pitchZoneGeometry(pitches);
  if(!geometry)return null;
  const cfg=PITCH_ZONE_DRAWING;
  const svg=svgNode('svg',{
    viewBox:`0 0 ${geometry.width} ${geometry.height}`,
    width:geometry.width,height:geometry.height,
    class:'game-pitch-canvas',role:'img',
    'aria-label':`타석 투구 위치 ${geometry.points.length}구 · 고정 스트라이크존${geometry.outsideCount?` · 표시 범위 밖 ${geometry.outsideCount}구`:''}`,
    'data-outside-count':geometry.outsideCount
  });
  const description=svgNode('desc');
  description.textContent='스트라이크존은 항상 60×80 CSS 픽셀입니다. 투구가 벗어난 만큼 그림 영역만 확대합니다. 점선 원과 화살표는 표시 한계 밖의 투구이며 원래 좌표는 각 투구 설명에 있습니다.';
  svg.append(description);
  svg.append(svgNode('rect',{
    ...geometry.zone,class:'game-strike-zone',fill:'#f0f2f4',
    stroke:'#b7bfc8','stroke-width':1
  }));
  for(const point of geometry.points){
    const p=point.pitch;
    const color=(['ball','strike','foul','inplay'].includes(p.category)
      ?pitchCategoryColors[p.category]:pitchCategoryColors.other);
    const parts=[p.num!=null?`${p.num}구`:null,
      pitchCategoryLabels[p.category]||pitchCategoryLabels.other,
      Number.isFinite(p.speed)?`${Math.round(p.speed)}km/h`:null,p.type||null,
      p.balls!=null&&p.strikes!=null?`${p.balls}-${p.strikes}`:null,
      `x=${p.x.toFixed(3)} ft`,
      `z=${p.z.toFixed(3)} (존 하단 0 · 상단 1)`];
    if(point.outside)parts.push('표시 범위 밖 — 점선 원은 경계 방향 표시이며 실제 투구 위치가 아닙니다.');
    const tip=parts.filter(Boolean).join(' · ');
    const group=svgNode('g',{
      class:point.outside?'game-pitch-point is-outside':'game-pitch-point',
      tabindex:0,role:'img','aria-label':tip,
      'data-outside':String(point.outside)
    });
    const title=svgNode('title');title.textContent=tip;group.append(title);
    if(point.outside){
      const start=cfg.radius+1,end=cfg.radius+7,wing=3;
      const ax=point.cx+point.ux*end,ay=point.cy+point.uy*end;
      const bx=point.cx+point.ux*(end-4),by=point.cy+point.uy*(end-4);
      group.append(svgNode('path',{
        d:`M ${point.cx+point.ux*start} ${point.cy+point.uy*start} L ${ax} ${ay} M ${bx-point.uy*wing} ${by+point.ux*wing} L ${ax} ${ay} L ${bx+point.uy*wing} ${by-point.ux*wing}`,
        fill:'none',stroke:color,'stroke-width':1.5,'pointer-events':'none'
      }));
    }
    const attributes={cx:point.cx,cy:point.cy,r:cfg.radius,fill:color};
    if(point.outside)Object.assign(attributes,{fill:'#fff',stroke:color,'stroke-width':1.5,'stroke-dasharray':'3 2'});
    const dot=svgNode('circle',attributes);
    const label=svgNode('text',{
      x:point.cx,y:point.cy+3.5,'text-anchor':'middle','font-size':9,
      'font-weight':700,fill:point.outside?color:'#fff','pointer-events':'none'
    });
    label.textContent=p.num??'';
    group.append(dot,label);svg.append(group);
  }
  return svg;
}

function wrapPitchZone(svg){
  const panel=text('div','','game-pitch-panel');
  const viewport=text('div','','game-pitch-zone');
  viewport.tabIndex=0;
  viewport.setAttribute('role','region');
  viewport.setAttribute('aria-label','투구 위치 그림. 화면보다 큰 경우 좌우·위아래로 스크롤할 수 있습니다.');
  viewport.append(svg);panel.append(viewport);
  const scrollHint=text('p','', 'game-pitch-scroll-hint');
  scrollHint.hidden=true;panel.append(scrollHint);
  const outsideCount=Number(svg.getAttribute('data-outside-count'));
  if(outsideCount>0){
    panel.append(text('p',`점선 원·화살표: 표시 범위 밖 ${outsideCount}구. 실제 좌표는 투구에 마우스를 올려 확인하세요.`,
      'game-pitch-zone-note'));
  }
  return panel;
}
function centerPitchZoneViewport(panel){
  const viewport=panel.querySelector('.game-pitch-zone');
  if(!viewport || viewport.clientWidth<=0 || viewport.clientHeight<=0)return;
  // Called when the containing <details> is opened, after it has a layout size.
  // The zone is immediately visible even on a small screen; manual pan is then preserved.
  const extraX=Math.max(0,viewport.scrollWidth-viewport.clientWidth);
  const extraY=Math.max(0,viewport.scrollHeight-viewport.clientHeight);
  viewport.scrollLeft=extraX/2;
  viewport.scrollTop=extraY/2;
  const hint=panel.querySelector('.game-pitch-scroll-hint');
  if(hint){
    hint.hidden=extraX<1 && extraY<1;
    hint.textContent=`${extraX>=1?'↔ ':''}${extraY>=1?'↕ ':''}그림을 스크롤해 모든 투구 위치를 확인하세요.`;
  }
}
// FANZAI_FIXED_STRIKE_ZONE_V1_END
function gamePlayLog(d){
  const card=playerCard('플레이로그');card.append(text('p','경기 진행 순서 · 각 중계를 펼치면 투구 위치·주루·교체 등 수집된 상세 내용을 볼 수 있습니다.','player-note'));
  for(const p of d.plays??[]){
    const item=document.createElement('details');item.className='game-play';item.id=`play-${p.Seq}`;item.tabIndex=-1;
    const summary=text('summary',`${gamePlayLabel(p,d.game)} · ${p.Title||'중계'} · ${p.AA??'—'}:${p.AH??'—'} · WPA ${gameWpa(p.WPA)}`);
    const body=text('div','','game-play-body');
    const lines=text('div','','game-play-events');for(const line of p.events??[])lines.append(text('p',line));if(!p.events?.length)lines.append(text('p','상세 중계 내용이 없습니다.'));
    const zone=renderPitchZone(p.pitches);
    if(zone){body.classList.add('game-play-body-fixed-zone');const panel=wrapPitchZone(zone);body.append(panel,lines);item.addEventListener('toggle',()=>{if(item.open)centerPitchZoneViewport(panel);});}
    else body.append(lines);
    item.append(summary,body);card.append(item);
  }
  if(!d.plays?.length)card.append(text('p','수집된 플레이로그가 없습니다.'));return card;
}
function gameProbabilityPoints(d){
  const plays=new Map((d.plays??[]).map(p=>[String(p.Seq),p]));
  const rows=(d.probability??[]).filter(p=>p.Home!=null&&p.Away!=null&&Number.isFinite(Number(p.Home))&&Number.isFinite(Number(p.Away))&&Number(p.Home)>=0&&Number(p.Home)<=100&&Number(p.Away)>=0&&Number(p.Away)<=100).map(p=>{
    const team=p.Team??plays.get(String(p.Seq))?.Team,inning=Number(p.Inning);
    const half=team===d.game.Away?0:team===d.game.Home?1:null;
    return {...p,inning,half,slot:(inning-1)*2+(half??0.5),label:`${inning}회${half===0?' 초':half===1?' 말':' (초·말 미제공)'}`};
  }).filter(p=>Number.isInteger(p.inning)&&p.inning>=1&&p.inning<=30);
  const groups=new Map();for(const p of rows){if(!groups.has(p.slot))groups.set(p.slot,[]);groups.get(p.slot).push(p);}
  const innings=Math.max(9,...rows.map(p=>p.inning));
  for(const group of groups.values())group.forEach((p,i)=>{p.x=120+(p.slot+(i+1)/(group.length+1)*(p.half==null?0.4:1))/(innings*2)*760;});
  return {points:rows,innings};
}
function gameProbability(d){
  const wrap=text('div','','game-probability-wrap'),{points,innings}=gameProbabilityPoints(d);
  if(!points.length){wrap.append(text('p','수집된 승리확률 데이터가 없습니다.'));return wrap;}
  // Keep the orientation stable while a live game becomes final.
  const homeName=teamNames[d.game.Home]??d.game.Home,awayName=teamNames[d.game.Away]??d.game.Away;
  const chart=svgNode('svg',{viewBox:'0 0 900 310',class:'game-probability',role:'group','aria-label':`${homeName}·${awayName} 이닝별 승리확률. 각 점에서 확률과 타석을 확인할 수 있습니다.`});
  for(const pct of [0,25,50,75,100]){
    const y=260-pct*2.2;chart.append(svgNode('line',{x1:120,y1:y,x2:880,y2:y,stroke:pct===50?'#a8b5c5':'#e3e8ef'}));
    const t=svgNode('text',{x:5,y:y+4,fill:'#758499','font-size':12});t.textContent=pct===50?'양 팀 50%':`${pct>50?homeName:awayName} ${pct>50?pct:100-pct}%`;if(pct!==50)markTeamName(t,d.game[pct>50?'Home':'Away']);chart.append(t);
  }
  for(let inning=1;inning<=innings;inning++){
    const x=120+(inning-1)/innings*760;
    chart.append(svgNode('line',{x1:x,y1:40,x2:x,y2:264,stroke:'#e3e8ef','stroke-dasharray':'3 4'}));
    if(innings<=12||inning%2===1){const t=svgNode('text',{x:x+380/innings,y:282,'text-anchor':'middle',fill:'#596779','font-size':11});t.textContent=`${inning}회`;chart.append(t);}
  }
  const axis=svgNode('text',{x:500,y:302,'text-anchor':'middle',fill:'#758499','font-size':10});axis.textContent='각 이닝의 왼쪽은 초, 오른쪽은 말 · 이닝 안에서는 수집 순서';chart.append(axis);
  // Never bridge an uncollected half-inning, or invent a 50% starting point.
  let segment=[];function flush(){if(segment.length>1)chart.append(svgNode('polyline',{points:segment.map(p=>`${p.x},${260-Number(p.Home)*2.2}`).join(' '),fill:'none',stroke:'#3989dc','stroke-width':2}));segment=[];}
  for(const p of points){const prev=segment.at(-1);if(prev&&(p.slot<prev.slot||p.slot-prev.slot>1))flush();segment.push(p);}flush();
  const tip=text('div','','game-probability-tooltip');tip.hidden=true;
  const cursor=svgNode('line',{x1:120,x2:120,y1:40,y2:260,stroke:'#526b89','stroke-dasharray':'4 3',visibility:'hidden','pointer-events':'none'});
  const selected=svgNode('circle',{r:5,fill:'#e85c2b',visibility:'hidden','pointer-events':'none'});
  const readout=text('p','그래프 위에 마우스를 올리거나 점을 선택하면 해당 타석의 확률을 확인할 수 있습니다.','player-note');
  const describe=p=>`${p.label} · ${p.Title??'중계 기록'}\n${homeName} ${Number(p.Home).toFixed(1)}% / ${awayName} ${Number(p.Away).toFixed(1)}%${p.WPA==null?'':` · WPA ${Number(p.WPA)>0?'+':''}${Number(p.WPA).toFixed(1)}%p`}`;
  function show(p){
    const description=describe(p);readout.textContent=description;tip.textContent=description;tip.hidden=false;
    cursor.setAttribute('x1',p.x);cursor.setAttribute('x2',p.x);cursor.setAttribute('visibility','visible');
    selected.setAttribute('cx',p.x);selected.setAttribute('cy',260-Number(p.Home)*2.2);selected.setAttribute('visibility','visible');
    const bounds=chart.getBoundingClientRect(),box=wrap.getBoundingClientRect();
    tip.style.left=`${Math.max(0,Math.min(bounds.left-box.left+p.x/900*bounds.width+10,box.width-tip.offsetWidth))}px`;
    tip.style.top=`${bounds.top-box.top+12}px`;
  }
  function hide(){tip.hidden=true;cursor.setAttribute('visibility','hidden');selected.setAttribute('visibility','hidden');}
  for(const p of points){
    const dot=svgNode('circle',{cx:p.x,cy:260-Number(p.Home)*2.2,r:4,fill:'#3989dc',tabindex:0,'aria-label':describe(p)});
    dot.addEventListener('focus',()=>show(p));dot.addEventListener('blur',hide);dot.addEventListener('pointerdown',()=>show(p));
    const title=svgNode('title');title.textContent=describe(p);dot.append(title);chart.append(dot);
  }
  chart.append(cursor,selected);
  chart.addEventListener('pointermove',e=>{const rect=chart.getBoundingClientRect(),x=(e.clientX-rect.left)/rect.width*900;const nearest=points.reduce((a,b)=>Math.abs(b.x-x)<Math.abs(a.x-x)?b:a);if(Math.abs(nearest.x-x)>760/(innings*2)){hide();return;}show(nearest);});
  chart.addEventListener('pointerleave',hide);
  wrap.append(chart,tip,readout,text('p',`수집 범위: ${points[0].label} ~ ${points.at(-1).label}. 비어 있는 구간은 미수집 또는 승리확률 미제공 구간입니다. 경기 시작 확률을 임의로 추가하지 않습니다.`,'player-note'),text('p',`상단 ${homeName}(홈팀), 하단 ${awayName}(원정팀). 중계 승리확률이며 PS 진출확률과 다릅니다.`,'player-note'));
  return wrap;
}

async function renderPitchLocations(req,seq,signal){
  const card=playerCard('구종별 투구 위치');$('player-content').append(card);const status=text('p','투구 위치를 불러오는 중…','player-note');card.append(status);const rows=[];
  try{let more=true,page=1;while(more){const d=await api('/api/games',{section:'locations',code:req.code,year:req.year,competition:req.competition,page},signal);if(seq!==playerState.sequence)return;rows.push(...d.rows);more=d.hasMore;status.textContent=`${rows.length.toLocaleString()}구 확인 중…`;if(more&&page===100)throw new Error('조회 가능한 투구 수를 초과했습니다.');page++;}
    const groups=[...new Set(rows.map(p=>p.Type))],colors=['#df3131','#ed9b21','#37a866','#cbad18','#8960e8','#219eac','#df63a5','#4f78bd'];const grid=text('div','','pitch-location-grid');let missing=0;
    const valid=rows.filter(p=>p.X!=null&&p.Z!=null&&p.Top!=null&&p.Bottom!=null&&p.Top>p.Bottom&&[p.X,p.Z,p.Top,p.Bottom].every(Number.isFinite));const extent=Math.max(2,...valid.map(p=>Math.abs(p.X))),minZ=Math.min(-.6,...valid.map(p=>(p.Z-p.Bottom)/(p.Top-p.Bottom))),maxZ=Math.max(1.7,...valid.map(p=>(p.Z-p.Bottom)/(p.Top-p.Bottom)));
    for(const [i,type] of groups.entries()){const all=rows.filter(p=>p.Type===type),points=valid.filter(p=>p.Type===type);missing+=all.length-points.length;const panel=text('section','','pitch-location-card'),h=text('h3',type);h.style.color=colors[i%colors.length];panel.append(h,text('small',`${all.length.toLocaleString()}구 (${(all.length/rows.length*100).toFixed(1)}%) · 좌표 ${points.length.toLocaleString()}구`));const canvas=document.createElement('canvas');canvas.width=640;canvas.height=680;canvas.setAttribute('role','img');canvas.setAttribute('aria-label',`${type} 투구 위치 ${points.length}개`);const ctx=canvas.getContext('2d');ctx.scale(2,2);const x=v=>20+(v+extent)/(extent*2)*280,y=v=>310-(v-minZ)/(maxZ-minZ)*280;
      ctx.fillStyle='#f0f2f4';ctx.fillRect(x(-.7083),y(1),x(.7083)-x(-.7083),y(0)-y(1));ctx.strokeStyle='#b7bfc8';ctx.strokeRect(x(-.7083),y(1),x(.7083)-x(-.7083),y(0)-y(1));ctx.globalAlpha=.35;ctx.fillStyle=colors[i%colors.length];for(const p of points){ctx.beginPath();ctx.arc(x(p.X),y((p.Z-p.Bottom)/(p.Top-p.Bottom)),2.5,0,Math.PI*2);ctx.fill();}ctx.globalAlpha=1;panel.append(canvas);grid.append(panel);}
    status.textContent=`${rows.length.toLocaleString()}구 중 ${valid.length.toLocaleString()}구 표시 · 좌표/존 누락 ${missing.toLocaleString()}구. 모든 구종은 같은 축이며 세로 위치는 각 타자의 존 하단 0·상단 1로 정규화했습니다. 원본 X와 계산된 홈플레이트 높이를 사용합니다.`;if(!rows.length)status.textContent='선택 조건의 투구 기록이 없습니다.';card.append(grid);
  }catch(e){if(e.name!=='AbortError'&&seq===playerState.sequence)status.textContent=e.message;}
}
