import {useState,useEffect,useRef,useCallback} from 'react';
import {Trophy,UserRound,CalendarDays,Play,ArrowRight,RefreshCw,Save} from 'lucide-react';
import Practice from './page';
import SeasonMatch from './season-match';
import CareerPage from './career-page';
import Exhibition from './exhibition-page';
import {TeamManagement} from './team-management';
import SaveCodePanel,{SaveCodeNotice} from './save-code-panel';
import TitleScreen from './title-screen';
import {initialSeasonTab,type SeasonTab} from '../lib/title-navigation';
import {useSeasonClient,type SaveCodeInfo} from '../lib/season-client';
import {useSeasonPresentation,seasonPitchKey} from '../lib/season-presentation';
import {teamName,fullGameTeams,type RosterTeam} from '../lib/roster';
import {PACE_SETTINGS,type Pace} from '../lib/action-engine';
import {playerAppearance,type PlayerCustomization} from '../lib/player-appearance';
import type {SeasonSave,SeasonPlayerStats} from '../lib/season-types';
import './season.css';
import {useGameVisibility} from '../lib/game-visibility';

const defaultTeams:RosterTeam[]=['HH','HT','KT','LG','LT','NC','OB','SK','SS','WO'].map(code=>({code,name:teamName(code)}));
const EMPTY_APPEARANCES:Record<string,PlayerCustomization>={};
type Tab=SeasonTab;
export default function SeasonPage(){
 const [tab,setTab]=useState<Tab>(()=>initialSeasonTab(location.search));
 const [savePanelOpen,setSavePanelOpen]=useState(false),[saveNotice,setSaveNotice]=useState<{code:string|null}|null>(null);
 const checkedSaveProfiles=useRef(new Map<string,Promise<SaveCodeInfo|null>>());
 const visible=useGameVisibility();
 const client=useSeasonClient(visible&&tab==='game'&&!savePanelOpen),{state,career,roster,busy,loading,error}=client;
 const [immediatePitch,setImmediatePitch]=useState<string|null>(null);
 const presentation=useSeasonPresentation(state,client.clock,immediatePitch,visible);
 const command=useCallback(async(body:Record<string,unknown>)=>{const result=await client.command(body);if(String(body.op).startsWith('sim-'))setImmediatePitch(seasonPitchKey(result));return result;},[client.command]);
 const [team,setTeam]=useState('LG'),[pace,setPace]=useState<Pace>('practice'),[series,setSeries]=useState(8),[yearBusy,setYearBusy]=useState(false);
 const [statKind,setStatKind]=useState<'batting'|'pitching'>('batting'),[scheduleDay,setScheduleDay]=useState<number|null>(null);
 const save=presentation.state?.save,game=save?.game,teams=roster?.teams??defaultTeams;
 const playableTeams=fullGameTeams(roster),leagueReady=playableTeams.length===10&&playableTeams.some(t=>t.code===team);
 const appearances=save?.appearances??EMPTY_APPEARANCES;
 const saveProfileKey=save?'league:'+save.id:career.player?'career:'+career.player.id:'';
 useEffect(()=>{
  if(loading||!saveProfileKey)return;
  let check=checkedSaveProfiles.current.get(saveProfileKey);
  if(!check){check=(async()=>{const info=await client.getSaveCode();return info.code||!info.hasData?null:client.saveCode();})();checkedSaveProfiles.current.set(saveProfileKey,check);}
  let cancelled=false;
  void check.then(issued=>{if(!cancelled&&issued)setSaveNotice({code:issued.code});}).catch(()=>{if(!cancelled)setSaveNotice({code:null});});
  return()=>{cancelled=true;};
 },[loading,saveProfileKey,client.getSaveCode,client.saveCode]);
 useEffect(()=>{if(career.player)setTeam(career.player.team);},[career.player?.id]);
 useEffect(()=>setScheduleDay(null),[save?.id,save?.seasonNumber]);
 useEffect(()=>{
  const resize=()=>window.parent.postMessage({type:'diamond:height',height:Math.ceil(document.body.getBoundingClientRect().height)},location.origin);
  const observer=new ResizeObserver(resize);observer.observe(document.body);resize();return()=>observer.disconnect();
 },[]);
 const send=(op:string,extra:Record<string,unknown>={})=>{if(presentation.pending)return;void command({op,...extra}).catch(()=>{});};
 const openTab=(next:Tab)=>{setTab(next);if(next!=='practice'&&next!=='friendly')history.replaceState(null,'',location.pathname);if(next==='game'&&save)void client.reload().catch(()=>{});if(next==='career')void client.refreshCareer().catch(()=>{});};
 const selectedTeam=save?.team??team;
 const accent=playerAppearance(selectedTeam,'').jersey;
 const fixture=save?.schedule.find(f=>f.day===save.day&&(f.homeTeam===save.team||f.awayTeam===save.team));
 const teamRoster=save?.teams.find(t=>t.code===save.team);
 const played=save?.standings.find(s=>s.team===save.team);
 return <main className={'season-app'+(tab==='title'?' season-app--title':'')} style={{'--club-color':accent} as React.CSSProperties}>
  <header className="season-header"><a className="season-brand" href="/diamond/" aria-label="DIAMOND 타이틀 화면" onClick={e=>{e.preventDefault();openTab('title');}}>DIAMOND<span>KBO BASEBALL</span></a>{tab!=='title'&&<nav aria-label="게임 메뉴">{([{id:'game',label:'경기',icon:Play},{id:'league',label:'리그',icon:Trophy},{id:'career',label:'마이 플레이어',icon:UserRound},{id:'friendly',label:'친선 경기',icon:CalendarDays},{id:'practice',label:'타석 연습',icon:Play}] as const).map(t=><button key={t.id} aria-current={tab===t.id?'page':undefined} className={tab===t.id?'active':''} onClick={()=>openTab(t.id)}><t.icon size={17}/>{t.label}</button>)}</nav>}<button className="save-code-header-button" type="button" aria-haspopup="dialog" aria-expanded={savePanelOpen} onClick={()=>setSavePanelOpen(true)}><Save size={16} aria-hidden="true"/>저장·불러오기</button><div className="season-save-indicator"><i/>{loading?'연결 중':save?'진행 자동 저장':'새 시즌'}</div></header>
  {saveNotice&&<SaveCodeNotice code={saveNotice.code} onOpen={()=>setSavePanelOpen(true)} onDismiss={()=>setSaveNotice(null)}/>}
  {savePanelOpen&&<SaveCodePanel getSaveCode={client.getSaveCode} saveCode={client.saveCode} loadSaveCode={client.loadSaveCode} onClose={()=>setSavePanelOpen(false)} busy={busy} hasData={!!save||!!career.player}/>}
  {tab==='title'?<TitleScreen onNavigate={openTab} onOpenSave={()=>setSavePanelOpen(true)} onRetry={()=>void client.reload().catch(()=>{})} loading={loading} error={error} savedTeam={save?teamName(save.team):null} careerName={career.player?.name??null}/>:tab==='practice'?<Practice suspended={savePanelOpen}/>:<div className="season-content">
   {error&&<div className="season-error" role="alert"><span>{error}</span><button onClick={()=>client.setError('')} aria-label="알림 닫기">×</button></div>}
   {loading?<div className="season-loading"><RefreshCw size={28}/><h2>구단 기록과 저장된 시즌을 불러옵니다</h2><p>처음에는 선수 데이터를 준비하는 데 잠시 걸릴 수 있습니다.</p></div>:tab==='friendly'?<Exhibition active={visible} suspended={savePanelOpen} roster={roster} loadRoster={client.loadRoster}/>:tab==='career'?presentation.pending?<section className="season-panel"><h2>수비 플레이 중</h2><p className="season-note">플레이가 끝나면 선수 기록을 표시합니다.</p></section>:<CareerPage active={visible&&!savePanelOpen} key={career.player?.id??'new'} player={career.player} teams={teams} leagueTeam={save?.team} busy={busy} edit={client.editCareer} onLeague={()=>openTab(save?'game':'league')}/>:!save?<>
    <div className="season-welcome"><div><span>YOUR TEAM. YOUR SEASON.</span><h1>끝까지 뛰는 야구.</h1><p>타석에서 보고, 마운드에서 던지고.<br/>9이닝의 승부를 넘어 나만의 시즌을 만드세요.</p><div className="season-features"><span>9이닝 실시간 경기</span><span>10개 구단 리그</span><span>선수 생성과 육성</span></div></div><div className="season-welcome-diamond"><i/><b>DIAMOND</b><small>PLAY BALL</small></div></div>
    <div className="season-setup"><section className="season-panel"><div className="season-section-heading"><h2>구단 선택</h2><span>{roster?.season??'—'} 기록</span></div><div className="club-grid">{teams.map(t=><button key={t.code} disabled={!!career.player} aria-pressed={selectedTeam===t.code} className={selectedTeam===t.code?'selected':''} style={{'--team-color':playerAppearance(t.code,'').jersey} as React.CSSProperties} onClick={()=>setTeam(t.code)}><strong>{t.code}</strong><span>{t.name}</span></button>)}</div>{career.player&&<p className="season-note">{career.player.name} 선수가 {teamName(team,teams)}에서 데뷔합니다.</p>}</section>
    <section className="season-panel season-settings"><h2>새 리그 시작</h2><label>기록 시즌<select value={roster?.season??''} disabled={busy||presentation.pending||yearBusy||!roster} onChange={e=>{setYearBusy(true);void client.loadRoster(Number(e.target.value)).catch(e=>client.setError(e.message)).finally(()=>setYearBusy(false));}}>{roster?.seasons.map(y=><option key={y} value={y}>{y} 정규시즌</option>)}</select></label><label>시즌 길이<select value={series} onChange={e=>setSeries(Number(e.target.value))}><option value={8}>정규 시즌 · 팀당 144경기</option><option value={4}>하프 시즌 · 팀당 72경기</option><option value={1}>미니 시즌 · 팀당 18경기</option></select></label><label>게임 속도<select value={pace} onChange={e=>setPace(e.target.value as Pace)}>{(Object.keys(PACE_SETTINGS) as Pace[]).map(p=><option key={p} value={p}>{PACE_SETTINGS[p].label}</option>)}</select></label><p className="season-note">3아웃 공수 교대 · 9이닝 · 연장 12회<br/>타격과 투구를 직접 조작하고, 원하는 구간은 자동 진행할 수 있습니다.</p><button className="season-primary" disabled={busy||presentation.pending||yearBusy||!roster||!leagueReady} onClick={()=>send('create',{season:roster?.season,team,pace,seriesPerPair:series})}>리그 시작 <ArrowRight size={17}/></button>{roster&&!leagueReady&&<p className="season-note" role="status">이 시즌은 9명 타순과 투수를 갖춘 구단이 {playableTeams.length}개입니다. 정규 리그는 10개 구단의 기록이 있는 시즌을 선택해 주세요. 친선 경기는 두 구단으로도 진행할 수 있습니다.</p>}{!roster&&<button onClick={()=>void client.loadRoster().catch(e=>client.setError(e.message))}>선수 기록 다시 불러오기</button>}<button onClick={()=>openTab('career')}>먼저 내 선수 만들기</button></section></div>
   </>:tab==='game'?<>
    <div className="season-section-heading"><div><span>SEASON {save.seasonNumber} · {presentation.masked?'수비 플레이 중':<>DAY {Math.min(save.day,save.totalDays)} / {save.totalDays}</>}</span><h1>{teamName(save.team)}의 시즌</h1><p>{presentation.masked?'플레이가 끝나면 경기 기록을 표시합니다.':<>{played?.wins??0}승 {played?.losses??0}패 {played?.ties??0}무 · {save.season} 실제 기록 기반</>}</p></div><button onClick={()=>openTab('league')}>순위와 일정 <ArrowRight size={16}/></button></div>
    <div className="season-game-layout"><div>{state?.action&&game?<SeasonMatch active={visible} suspended={savePanelOpen} key={game.id} state={state} immediatePitch={immediatePitch} busy={busy||savePanelOpen} clock={client.clock} command={command} appearances={appearances}/>:<section className="season-match-intro"><span>{save.complete?'SEASON COMPLETE':'NEXT GAME'}</span><h2>{save.complete?'시즌을 마쳤습니다':`${teamName(fixture?.awayTeam??'')} vs ${teamName(fixture?.homeTeam??'')}`}</h2><p>{save.complete?'순위와 개인 기록을 확인하고 다음 시즌에 도전하세요.':'타석과 마운드를 오가며 팀을 승리로 이끄세요.'}</p><button className="season-primary" disabled={busy||presentation.pending} onClick={()=>save.complete?openTab('league'):send('start-game')}>{save.complete?'시즌 결과 보기':'경기 시작'} <Play size={18}/></button></section>}
    <section className="season-panel season-events"><h2>경기 기록</h2>{presentation.masked?<p className="season-note" role="status">수비 플레이 중 · 결과는 플레이가 끝나면 표시됩니다.</p>:game?.events.length?<ol>{game.events.slice(-14).reverse().map((e,i)=><li key={game.events.length-i}>{e}</li>)}</ol>:<p className="season-note">경기가 시작되면 투구 결과와 득점 상황이 표시됩니다.</p>}</section></div>
    <aside><section className="season-panel"><h2>경기 진행</h2><div className="season-auto-actions">{career.player&&<button disabled={busy||presentation.pending||save.complete||!!state?.action?.waiting} onClick={()=>send('sim-to-player')}>내 선수 차례까지</button>}<button disabled={busy||presentation.pending||save.complete||!!game?.complete} onClick={()=>send('sim-half')}>이번 공수 자동 진행</button><button disabled={busy||presentation.pending||save.complete||!!game?.complete} onClick={()=>send('sim-game')}>이번 경기 자동 진행</button><button disabled={busy||presentation.pending||save.complete||!!game&&!game.complete} onClick={()=>send('sim-day')}>다음 일정 자동 진행</button></div><p className="season-note">우리 경기가 끝나면 같은 날 다른 구단의 경기 결과도 반영됩니다.</p></section>{teamRoster&&!presentation.masked&&<TeamManagement save={save} busy={busy||presentation.pending} pitchInFlight={presentation.pending||!!state?.action?.pitch&&!state.action.pitch.resolved} send={send}/>}<section className="season-panel"><h2>마이 플레이어</h2>{career.player?<><p><b>{career.player.name}</b>{!presentation.pending&&<> · LV {career.player.level}</>}</p><p className="season-note">{presentation.pending?"수비 플레이 중":<>훈련 {career.player.trainingPoints} PT · {career.player.games}경기 출전</>}</p></>:<p className="season-note">나만의 선수를 만들어 구단 라인업에 합류하세요.</p>}<button onClick={()=>openTab('career')}>{career.player?'선수 육성':'선수 만들기'}</button></section></aside></div>
   </>:presentation.masked?<section className="season-panel"><h2>수비 플레이 중</h2><p className="season-note">플레이가 끝나면 순위와 경기 기록을 표시합니다.</p><button onClick={()=>openTab('game')}>경기로 돌아가기</button></section>:<>
    <div className="season-section-heading"><div><span>LEAGUE CENTER</span><h1>{save.seasonNumber}번째 시즌</h1><p>{save.season} 기록 기반 · 팀당 {save.totalDays}경기 · {save.complete?'시즌 종료':save.day+'일차'}</p></div><button className="season-primary" disabled={busy||presentation.pending} onClick={()=>save.complete?send('next-season'):openTab('game')}>{save.complete?'다음 시즌 시작':'경기로 돌아가기'} <ArrowRight size={16}/></button></div>
    {save.complete&&<div className="season-champion"><Trophy size={36}/><div><small>LEAGUE CHAMPION</small><h2>{save.standings[0]?.name}</h2><p>{save.standings[0]?.wins}승 · 승률 {save.standings[0]?.pct.toFixed(3)}</p></div></div>}
    <div className="season-league-grid"><section className="season-panel"><h2>구단 순위</h2><div className="season-table-scroll"><table className="season-table"><thead><tr><th>순위</th><th>구단</th><th>경기</th><th>승</th><th>패</th><th>무</th><th>승률</th><th>게임차</th></tr></thead><tbody>{save.standings.map((s,i)=><tr key={s.team} className={s.team===save.team?'my-team':''}><td>{i+1}</td><th>{s.name}</th><td>{s.played}</td><td>{s.wins}</td><td>{s.losses}</td><td>{s.ties}</td><td>{s.pct.toFixed(3)}</td><td>{s.gamesBehind||'—'}</td></tr>)}</tbody></table></div></section>
    <section className="season-panel"><div className="season-section-heading"><h2>경기 일정</h2><select aria-label="일정 일자" value={scheduleDay??Math.min(save.day,save.totalDays)} onChange={e=>setScheduleDay(Number(e.target.value))}>{Array.from({length:save.totalDays},(_,i)=><option value={i+1} key={i}>{i+1}일차</option>)}</select></div><div className="season-fixtures">{save.schedule.filter(f=>f.day===(scheduleDay??Math.min(save.day,save.totalDays))).map(f=><div key={f.id} className={f.homeTeam===save.team||f.awayTeam===save.team?'my-team':''}><span>{teamName(f.awayTeam)}</span><strong>{f.complete?`${f.awayRuns} : ${f.homeRuns}`:'VS'}</strong><span>{teamName(f.homeTeam)}</span><small>{f.complete?'종료':'예정'}</small></div>)}</div><button className="season-primary" disabled={busy||presentation.pending||save.complete||!!game&&!game.complete} onClick={()=>send('sim-day')}>하루 자동 진행</button>{game&&!game.complete&&<p className="season-note">진행 중인 경기를 먼저 마쳐 주세요.</p>}</section></div>
    <section className="season-panel"><div className="season-section-heading"><h2>개인 기록</h2><div className="season-segment"><button className={statKind==='batting'?'selected':''} onClick={()=>setStatKind('batting')}>타자</button><button className={statKind==='pitching'?'selected':''} onClick={()=>setStatKind('pitching')}>투수</button></div></div><PlayerStats stats={Object.values(save.playerStats)} kind={statKind}/></section>
    {save.previousSeasons.length>0&&<section className="season-panel"><h2>지난 시즌</h2><div className="season-history">{save.previousSeasons.map(s=><div key={s.seasonNumber}><b>시즌 {s.seasonNumber}</b><span>우승 {teamName(s.champion)}</span><small>우리 팀 {s.wins}승 {s.losses}패 {s.ties}무</small></div>)}</div></section>}
   </>}
   <footer className="season-footer"><span>DIAMOND · 실제 기록에서 시작하는 나의 야구</span><span>이 브라우저에 연결된 시즌 자동 저장 · {save?save.asOf+' 기록 기준':'경기와 선수 자동 저장'}</span></footer>
  </div>}
 </main>;
}

function PlayerStats({stats,kind}:{stats:SeasonPlayerStats[];kind:'batting'|'pitching'}){
 const list=stats.filter(p=>kind==='batting'?p.pa>0:p.outsPitched>0).sort((a,b)=>kind==='batting'?b.h-a.h:b.strikeouts-a.strikeouts).slice(0,30);
 return list.length?<div className="season-table-scroll"><table className="season-table"><thead><tr><th>선수</th><th>구단</th>{(kind==='batting'?['타율','타수','안타','홈런','타점','볼넷']:['이닝','탈삼진','피안타','실점','볼넷','투구수']).map(c=><th key={c}>{c}</th>)}</tr></thead><tbody>{list.map(p=><tr key={p.playerId} className={p.playerId.includes('career_')?'my-team':''}><th>{p.name}</th><td>{teamName(p.team)}</td>{(kind==='batting'?[p.ab?(p.h/p.ab).toFixed(3):'.000',p.ab,p.h,p.hr,p.rbi,p.bb]:[`${Math.floor(p.outsPitched/3)}.${p.outsPitched%3}`,p.strikeouts,p.hitsAllowed,p.runsAllowed,p.walksAllowed,p.pitchCount]).map((v,i)=><td key={i}>{v}</td>)}</tr>)}</tbody></table></div>:<p className="season-note">경기를 완료하면 선수들의 시즌 기록이 집계됩니다.</p>;
}
