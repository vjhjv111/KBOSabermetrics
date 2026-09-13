import {useState,useEffect} from 'react';
import {ChevronUp,ChevronDown} from 'lucide-react';
import type {SeasonSave} from '../lib/season-types';

export function replaceLineupSlot(lineup:string[],slot:number,playerId:string){
 if(!Number.isInteger(slot)||slot<0||slot>=lineup.length||!playerId||lineup.includes(playerId))return null;
 return lineup.map((id,i)=>i===slot?playerId:id);
}
export function TeamManagement({save,busy,pitchInFlight=false,send}:{save:SeasonSave;busy:boolean;pitchInFlight?:boolean;send:(op:string,extra?:Record<string,unknown>)=>void}){
 const team=save.teams.find(t=>t.code===save.team)!,game=save.game,home=game?.homeTeam===save.team;
 const [slot,setSlot]=useState(0),[bench,setBench]=useState(''),[relief,setRelief]=useState('');
 const inGame=!!game&&!game.complete,lineup=inGame?(home?game.homeLineup:game.awayLineup):team.lineup;
 const used=inGame?(home?game.homeUsedBatters:game.awayUsedBatters):[];
 const available=team.batters.filter(p=>!lineup.includes(p.id)&&!used.includes(p.id));
 const currentPitcher=inGame?(home?game.homePitcher:game.awayPitcher):'';
 const usedPitchers=inGame?(home?game.homeUsedPitchers:game.awayUsedPitchers):[];
 const relievers=team.pitchers.filter(p=>p.id!==currentPitcher&&!usedPitchers.includes(p.id));
 const blocked=busy||inGame&&pitchInFlight;
 useEffect(()=>{setSlot(0);setBench('');setRelief('');},[game?.id,inGame]);
 const reorder=(index:number,delta:number)=>{if(busy||index+delta<0||index+delta>=lineup.length)return;const updated=[...lineup];[updated[index],updated[index+delta]]=[updated[index+delta],updated[index]];send('lineup',{lineup:updated});};
 const replace=()=>{if(blocked||!available.some(p=>p.id===bench))return;if(inGame)send('substitute',{slot,playerId:bench});else{const updated=replaceLineupSlot(lineup,slot,bench);if(updated)send('lineup',{lineup:updated});}};
 return <section className="season-panel season-team-management"><h2>{inGame?'선수 교체':'선발 라인업'}</h2><ol className="season-lineup">{lineup.map((id,i)=><li key={i}><span>{i+1}</span><b>{team.batters.find(p=>p.id===id)?.name??id}</b>{id.includes('career_')&&<small>MY</small>}{!inGame&&<div><button type="button" aria-label={`${i+1}번 타자 앞으로`} disabled={busy||i===0} onClick={()=>reorder(i,-1)}><ChevronUp size={14}/></button><button type="button" aria-label={`${i+1}번 타자 뒤로`} disabled={busy||i===lineup.length-1} onClick={()=>reorder(i,1)}><ChevronDown size={14}/></button></div>}</li>)}</ol>
  <label>{inGame?'교체할 타순':'편입할 타순'}<select disabled={blocked} value={slot} onChange={e=>setSlot(Number(e.target.value))}>{lineup.map((id,i)=><option key={i} value={i}>{i+1}번 · {team.batters.find(p=>p.id===id)?.name}</option>)}</select></label><label>벤치 타자<select disabled={blocked||!available.length} value={available.some(p=>p.id===bench)?bench:''} onChange={e=>setBench(e.target.value)}><option value="">{available.length?'선수를 고르세요':'출전 가능한 벤치 타자가 없습니다'}</option>{available.map(p=><option value={p.id} key={p.id}>{p.name}</option>)}</select></label><button type="button" disabled={blocked||!available.some(p=>p.id===bench)} onClick={replace}>{inGame?'타자 교체':'선발 라인업에 편입'}</button>
  {inGame&&<><label>불펜<select disabled={blocked||!relievers.length} value={relievers.some(p=>p.id===relief)?relief:''} onChange={e=>setRelief(e.target.value)}><option value="">{relievers.length?'선수를 고르세요':'등판 가능한 투수가 없습니다'}</option>{relievers.map(p=><option value={p.id} key={p.id}>{p.name} · ERA {p.era?.toFixed(2)??'—'}</option>)}</select></label><button type="button" disabled={blocked||!relievers.some(p=>p.id===relief)} onClick={()=>send('change-pitcher',{playerId:relief})}>투수 교체</button><p className="season-note">{pitchInFlight?'투구 진행 중입니다. 이 공이 끝나면 교체할 수 있습니다.':'한 번 빠진 선수는 재출전할 수 없습니다.'}</p></>}
 </section>;
}
