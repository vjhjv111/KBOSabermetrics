import {useState,useRef,useEffect,useCallback} from 'react';
import {Maximize2,Volume2,VolumeX,Crosshair,Target} from 'lucide-react';
import ActionScene,{type LocalSwing} from './action-scene';
import {PITCH_NAMES,PACE_SETTINGS,clamp,batterHandLabel,pitcherHandLabel,type Vec,type PitchType} from '../lib/action-engine';
import {localNow} from '../lib/game-clock';
import {SWING_CONTACT_MS} from '../lib/player-motion';
import {teamName} from '../lib/roster';
import type {SeasonResponse,SeasonGame} from '../lib/season-types';
import type {PlayerCustomization} from '../lib/player-appearance';
import './field-controls.css';

type Props={active?:boolean;state:SeasonResponse;busy:boolean;clock:React.MutableRefObject<number>;command:(body:Record<string,unknown>)=>Promise<SeasonResponse>;appearances:Record<string,PlayerCustomization>;friendly?:boolean;onNewGame?:()=>void};
export function lineScoreValue(game:SeasonGame,side:'home'|'away',inningIndex:number){
 const value=game[side==='home'?'homeLine':'awayLine'][inningIndex];
 if(value!==undefined)return value;
 return game.complete&&game.endReason==='home-ahead'&&side==='home'&&inningIndex===game.inning-1?'X':'–';
}
export function nextMatchup(game:SeasonGame){return {batter:game.half==='top'?game.awayLineup[game.awayOrder]:game.homeLineup[game.homeOrder],pitcher:game.half==='top'?game.homePitcher:game.awayPitcher};}
export function seasonDisplayAction(state:SeasonResponse,now:number){
 const action=state.action,save=state.save,game=save?.game;if(!action||!save||!game)return action;
 const pitch=action.pitch,result=pitch?.reaction,holdUntil=pitch?Math.max(pitch.releaseAt+pitch.flightMs,result?.at??0)+(result?.contact?3100:1200):0;
 const battingTeam=action.roster?.batter.team??save.teams.find(t=>t.batters.some(p=>p.id===action.batter))?.code;
 if(game.complete||pitch&&(!pitch.resolved||now<holdUntil))return battingTeam?{...action,role:battingTeam===save.team?'batter' as const:'pitcher' as const}:action;
 const ids=nextMatchup(game);if(ids.batter===action.batter&&ids.pitcher===action.pitcher)return action;
 const batter=save.teams.flatMap(t=>t.batters).find(p=>p.id===ids.batter),pitcher=save.teams.flatMap(t=>t.pitchers).find(p=>p.id===ids.pitcher);
 if(!batter||!pitcher)return action;
 return {...action,batter:ids.batter,pitcher:ids.pitcher,pitch:null,balls:0,strikes:0,roster:{season:save.season,asOf:save.asOf,revision:save.revision,batter,pitcher}};
}
const END_REASON:Record<string,string>={'walkoff':'끝내기','home-ahead':'홈팀 승리로 말 공격 생략','regulation':'정규 경기 종료','tie-12':'연장 12회 무승부'};
export function LineScore({game}:{game:SeasonGame}){
 const innings=Array.from({length:Math.max(9,game.inning)},(_,i)=>i);
 return <div className="season-line-score"><table aria-label="이닝별 득점"><thead><tr><th scope="col">팀</th>{innings.map(i=><th scope="col" key={i}>{i+1}</th>)}<th scope="col">R</th><th scope="col">H</th></tr></thead><tbody>{(['away','home'] as const).map(side=><tr key={side}><th scope="row">{teamName(game[side+'Team' as 'awayTeam'|'homeTeam'])}</th>{innings.map(i=><td key={i}>{lineScoreValue(game,side,i)}</td>)}<td className="line-runs">{game[side==='home'?'homeRuns':'awayRuns']}</td><td>{game[side==='home'?'homeHits':'awayHits']}</td></tr>)}</tbody></table></div>;
}
export default function SeasonMatch({state,busy,clock,command,appearances,friendly=false,onNewGame,active=true}:Props){
 const action=state.action!,game=state.save!.game!,save=state.save!;
 const [now,setNow]=useState(localNow()+clock.current),[swingTime,setSwingTime]=useState(-1e6),[localSwing,setLocalSwing]=useState<LocalSwing|null>(null);
 const [charging,setCharging]=useState(false),[chargeStarted,setChargeStarted]=useState(0),[pitchType,setPitchType]=useState<PitchType>('fastball');
 const [ready,setReady]=useState<boolean|null>(null),[sound,setSound]=useState(true),[automatic,setAutomatic]=useState(false),[visible,setVisible]=useState(!document.hidden);
 const aim=useRef<Vec>({x:0,y:0}),stage=useRef<HTMLDivElement>(null),audio=useRef<AudioContext|null>(null),sounds=useRef(new Set<string>()),swung=useRef(''),charge=useRef<number|null>(null),chargeKey=useRef(''),throwPointer=useRef<number|null>(null);
 const matchup=nextMatchup(game),pitches=save.teams.flatMap(t=>t.pitchers).find(p=>p.id===matchup.pitcher)?.arsenal??[],pitch=action.pitch,result=pitch?.reaction;
 const canPitch=active&&pitches.length>0&&!game.complete&&!action.waiting&&!busy&&(!pitch||pitch.resolved&&now>Math.max(pitch.releaseAt+pitch.flightMs,result?.at??0)+(result?.contact?3100:1200));
 const progress=charging?((now-chargeStarted)%1400)/1400:0;
 const current=useRef({action,canPitch,pitchType,command,sound,active});current.current={action,canPitch,pitchType,command,sound,active};
 useEffect(()=>{if(pitches.length&&!pitches.some(p=>p.type===pitchType))setPitchType(pitches[0].type);},[matchup.pitcher,pitchType]);
 useEffect(()=>{if(!active)return;const tick=setInterval(()=>{if(!document.hidden)setNow(localNow()+clock.current);},32);return()=>clearInterval(tick);},[clock,active]);
 const tone=useCallback((hit=false)=>{
  if(!current.current.sound)return;
  try{const ctx=audio.current??new AudioContext();audio.current=ctx;void ctx.resume();const at=ctx.currentTime;
   if(hit){const noise=ctx.createBuffer(1,Math.ceil(ctx.sampleRate*.05),ctx.sampleRate),data=noise.getChannelData(0);for(let i=0;i<data.length;i++)data[i]=(Math.random()*2-1)*Math.exp(-i/ctx.sampleRate*120);const source=ctx.createBufferSource(),filter=ctx.createBiquadFilter(),gain=ctx.createGain();source.buffer=noise;filter.type='bandpass';filter.frequency.value=2200;gain.gain.value=.55;source.connect(filter).connect(gain).connect(ctx.destination);source.start(at);}
   const osc=ctx.createOscillator(),gain=ctx.createGain();osc.type=hit?'triangle':'sine';osc.frequency.setValueAtTime(hit?1300:240,at);osc.frequency.exponentialRampToValueAtTime(90,at+.12);gain.gain.setValueAtTime(hit?.18:.05,at);gain.gain.exponentialRampToValueAtTime(.001,at+.13);osc.connect(gain).connect(ctx.destination);osc.start(at);osc.stop(at+.15);
  }catch{}
 },[]);
 const contact=useCallback((key:string)=>{if(sounds.current.has(key))return;sounds.current.add(key);tone(true);},[tone]);
 const swing=useCallback(()=>{
  const a=current.current.action,t=localNow()+clock.current,p=a.pitch;if(current.current.active===false||a.role!=='batter'||a.waiting||!p||p.resolved||a.done)return;
  const key=a.code+':'+p.id;if(swung.current===key)return;swung.current=key;const frozen={...aim.current},at=t+SWING_CONTACT_MS;
  setSwingTime(t);setLocalSwing({code:a.code,pitchId:p.id,at,aim:frozen});tone();
  void current.current.command({op:'swing',pitchId:p.id,inputAt:t,aim:frozen}).catch(()=>{if(swung.current===key)swung.current='';setLocalSwing(old=>old?.at===at?null:old);});
 },[clock,tone]);
 const begin=useCallback(()=>{const a=current.current.action;if(!current.current.canPitch||a.role!=='pitcher'||charge.current!==null)return;const t=localNow()+clock.current;charge.current=t;chargeKey.current=a.code+':'+a.pitchCount+':'+a.role;setChargeStarted(t);setCharging(true);try{const ctx=audio.current??new AudioContext();audio.current=ctx;void ctx.resume();}catch{}},[clock]);
 const cancel=useCallback(()=>{charge.current=null;chargeKey.current='';throwPointer.current=null;setCharging(false);},[]);
 const end=useCallback(()=>{if(charge.current===null)return;const t=charge.current,a=current.current.action,valid=chargeKey.current===a.code+':'+a.pitchCount+':'+a.role&&!a.done&&!a.waiting;cancel();if(!valid)return;const q=clamp(1-Math.abs(((localNow()+clock.current-t)%1400)/1400-.5)*2,0,1);void current.current.command({op:'pitch',previousPitch:a.pitchCount,type:current.current.pitchType,aim:{...aim.current},quality:q}).catch(()=>{});},[cancel,clock]);
 const selectPitch=(type:PitchType)=>{if(!current.current.canPitch||charge.current!==null||!pitches.some(p=>p.type===type))return;current.current.pitchType=type;setPitchType(type);};
 useEffect(()=>cancel(),[action.code,action.pitchCount,action.role,action.waiting,game.complete,cancel]);
 useEffect(()=>{
  const onKey=(e:KeyboardEvent)=>{if(!current.current.active||document.hidden||['INPUT','TEXTAREA','SELECT','BUTTON'].includes((e.target as HTMLElement).tagName))return;
   if(e.code==='Space'){e.preventDefault();if(!e.repeat){if(current.current.action.role==='batter')swing();else begin();}}
   if(e.code.startsWith('Arrow')){e.preventDefault();const sign=current.current.action.role==='pitcher'?-1:1;aim.current={x:clamp(aim.current.x+(e.code==='ArrowRight'?.12:e.code==='ArrowLeft'?-.12:0)*sign,-2,2),y:clamp(aim.current.y+(e.code==='ArrowUp'?.12:e.code==='ArrowDown'?-.12:0),-2,2)};}
  };
  const up=(e:KeyboardEvent)=>{if(e.code==='Space'&&charge.current!==null){e.preventDefault();end();}};
  const visibility=()=>{setVisible(!document.hidden);if(document.hidden){cancel();void audio.current?.suspend();}};
  window.addEventListener('keydown',onKey);window.addEventListener('keyup',up);window.addEventListener('blur',cancel);document.addEventListener('visibilitychange',visibility);
  return()=>{window.removeEventListener('keydown',onKey);window.removeEventListener('keyup',up);window.removeEventListener('blur',cancel);document.removeEventListener('visibilitychange',visibility);};
 },[swing,begin,end,cancel]);
 useEffect(()=>{if(!active){cancel();void audio.current?.suspend();}},[active,cancel]);
 useEffect(()=>()=>{void audio.current?.close();},[]);
 useEffect(()=>{if(active&&automatic&&visible&&canPitch&&action.mode!=='pvp'&&action.role==='batter'&&ready){void command({op:'ready',previousPitch:action.pitchCount}).catch(()=>setAutomatic(false));}},[active,automatic,visible,canPitch,action.mode,action.role,action.pitchCount,ready]);
 const send=(op:string)=>void command({op}).catch(()=>{});
 const displayAction=seasonDisplayAction(state,now)!;
 const batter=displayAction.roster?.batter,pitcher=displayAction.roster?.pitcher;
 return <div className="season-match">
  <LineScore game={game}/>
  <div className={'season-stage'+(!game.complete?' with-field-controls':'')} data-control-side={action.role} ref={stage} role="region" aria-label={action.role==='batter'?'타자 시점 야구장':'투수 시점 야구장'}>
   {active&&visible&&<ActionScene view={displayAction} side={displayAction.role} aim={aim} clock={clock} swingTime={swingTime} localSwing={localSwing} charging={charging} chargeStarted={chargeStarted} onSwing={swing} onContact={contact} onChargeStart={begin} onChargeEnd={end} onChargeCancel={cancel} onReady={setReady} batterId={displayAction.batter} pitcherId={displayAction.pitcher} seasonGame={game} appearances={appearances}/>}
   <div className="season-scorebug"><div className="season-score-teams"><span>{teamName(game.awayTeam)} <b>{game.awayRuns}</b></span><span>{teamName(game.homeTeam)} <b>{game.homeRuns}</b></span></div><div className="season-count"><strong>{game.complete?'종료':`${game.inning}회 ${game.half==='top'?'초':'말'}`}</strong><span>B {result?.plateEnded?0:action.balls} · S {result?.plateEnded?0:action.strikes} · O {game.complete?Math.min(3,game.outs):game.outs}</span></div><div className="base-diamond" aria-label={`주자: ${game.bases.map((r,i)=>r?i+1+'루':'').filter(Boolean).join(', ')||'없음'}`}>{[1,0,2].map(i=><i key={i} className={'base base-'+i+(game.bases[i]?' occupied':'')}/>)}</div></div>
   <div className="season-stage-tools"><span>{action.role==='batter'?'타자 시점':'투수 시점'}</span><button aria-label={sound?'소리 끄기':'소리 켜기'} onClick={()=>setSound(!sound)}>{sound?<Volume2 size={17}/>:<VolumeX size={17}/>}</button><button aria-label="전체화면" onClick={()=>{void (document.fullscreenElement?document.exitFullscreen():stage.current?.requestFullscreen())?.catch(()=>{});}}><Maximize2 size={17}/></button></div>
   {ready===false&&<div className="season-scene-error">3D 그래픽을 시작하지 못했습니다. 브라우저의 하드웨어 가속을 켜고 새로 고침해 주세요.</div>}
   {pitch&&now>=pitch.releaseAt&&<div className="season-speed"><b>{pitch.velocity.toFixed(1)}</b> km/h<small>{PITCH_NAMES[pitch.type]} · {PACE_SETTINGS[action.pace].badge}</small></div>}
   {result&&now>=(result.contact?.at??result.bodyHit?.at??result.swingAt??result.at)&&now-result.at<2500&&<div className="season-call" aria-live="polite"><strong>{result.label}</strong>{result.contact&&<span>{Math.round(result.exitSpeed)} km/h · {result.trajectory==='ground'?'땅볼':result.trajectory==='foul'?'파울':Math.round(result.distance)+' m'}</span>}</div>}
   <div className="season-batter-tag"><span>{action.role==='batter'?'현재 타석':'상대 타자'}</span><b>{batter?.name}</b><small>{batterHandLabel(batter?.id??action.batter,pitcher?.id??action.pitcher)}</small></div>
   <div className="season-pitcher-tag"><span>{action.role==='pitcher'?'마운드':'상대 투수'}</span><b>{pitcher?.name}</b><small>{pitcherHandLabel(pitcher?.id??action.pitcher)} · {game.playerStats[pitcher?.id??action.pitcher]?.pitchCount??0}구</small></div>
   {charging&&<div className="season-release"><div className="meter-track"><i className="sweet-spot"/><b style={{left:progress*100+'%'}}/></div><span>초록 구간에 놓으세요</span></div>}
   {!game.complete&&<div className="season-field-inputs" data-game-control="true" onPointerDown={e=>e.stopPropagation()} onPointerUp={e=>e.stopPropagation()} onPointerMove={e=>e.stopPropagation()} onClick={e=>e.stopPropagation()} onKeyDown={e=>e.stopPropagation()} onKeyUp={e=>e.stopPropagation()}>
    {action.role==='pitcher'?<div className="season-field-pitching">
     <div className="season-field-arsenal" role="group" aria-label="구종 선택"><div className="season-field-control-title"><Target size={13}/><span>구종 선택</span><small>좌우로 밀어 선택</small></div><div className="season-field-pitches">{pitches.map(p=><button type="button" key={p.type} disabled={!canPitch||charging} aria-pressed={pitchType===p.type} className={pitchType===p.type?'selected':''} onClick={()=>selectPitch(p.type)}><b>{PITCH_NAMES[p.type]}</b><small>{p.velocity.toFixed(1)} km/h</small></button>)}</div></div>
     <button type="button" className={'season-field-throw '+(charging?'held':'')} aria-label="누르고 놓아 투구" onBlur={cancel} disabled={action.waiting||!canPitch&&!charging} onPointerDown={e=>{if(!e.isPrimary||e.button!==0||!current.current.canPitch||charge.current!==null)return;e.preventDefault();throwPointer.current=e.pointerId;e.currentTarget.setPointerCapture(e.pointerId);begin();}} onPointerUp={e=>{if(throwPointer.current!==e.pointerId)return;throwPointer.current=null;end();}} onPointerCancel={e=>{if(throwPointer.current===e.pointerId)cancel();}} onLostPointerCapture={e=>{if(throwPointer.current===e.pointerId)cancel();}} onKeyDown={e=>{if((e.code==='Space'||e.code==='Enter')&&!e.repeat){e.preventDefault();begin();}}} onKeyUp={e=>{if(e.code==='Space'||e.code==='Enter'){e.preventDefault();end();}}}><b>{charging?'지금 놓기':'누르고 놓기'}</b><small>{action.waiting?'상대 참가 대기':charging?'초록 구간에 릴리스':'투구'}</small></button>
    </div>:<div className="season-field-batting"><div className="season-field-swing-hint"><Crosshair size={18}/><div><b>게임 화면을 클릭 · 터치해 스윙</b><small>공을 맞힐 위치를 조준하고 타이밍에 맞추세요</small></div></div>{action.mode==='pvp'?<span className="season-field-wait" role="status">{action.waiting?'상대 참가 대기':'상대 투구 대기'}</span>:<button type="button" className="season-field-next" disabled={!canPitch||!ready} onClick={()=>void command({op:'ready',previousPitch:action.pitchCount}).catch(()=>{})}>다음 투구</button>}</div>}
   </div>}
  </div>
  {game.complete?<div className="season-final"><div><small>FINAL</small><h2>{game.homeRuns===game.awayRuns?'무승부':(game.homeRuns>game.awayRuns?game.homeTeam:game.awayTeam)===save.team?'승리했습니다!':'경기 종료'}</h2><p>{END_REASON[game.endReason]??game.endReason} · {friendly?'친선 경기를 마쳤습니다.':'경기 기록과 선수 경험치가 저장되었습니다.'}</p></div><button className="season-primary" disabled={busy||(!friendly&&save.complete)} onClick={()=>friendly?onNewGame?.():send('start-game')}>{friendly?'새 친선 경기':save.complete?'시즌 완료':'다음 경기 시작'}</button></div>:<>
   <div className="season-controls"><div>{action.role==='batter'?<Crosshair size={20}/>:<Target size={20}/>}<div><b>{action.role==='batter'?'짧게 터치하면 그 위치로 스윙합니다':'화면 안에서 구종과 코스를 고르세요'}</b><small>{action.role==='batter'?'위아래로 밀면 스크롤 · 키보드는 SPACE로 스윙':'화면 안 투구 버튼을 누르다가 초록 구간에 놓기 · SPACE도 가능'}</small></div></div>{action.role==='batter'&&action.mode!=='pvp'&&<label className="season-toggle"><input type="checkbox" checked={automatic} disabled={action.waiting} onChange={e=>setAutomatic(e.target.checked)}/>자동 다음 공</label>}{action.waiting&&<span role="status">상대 참가를 기다리고 있습니다.</span>}</div>
  </>}
  <div className="season-last">{result?<><b>{result.label}</b><span>타이밍 {result.timing===null?'—':`${result.timing>0?'+':''}${result.timing} ms`}</span><span>{result.contact?`타구 속도 ${Math.round(result.exitSpeed)} km/h`:result.outcome==='HBP'?'사구 출루':''}</span></>:<span>첫 투구를 준비하고 있습니다.</span>}</div>
 </div>;
}
