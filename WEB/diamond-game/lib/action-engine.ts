import players from "./players.json";
import arsenalData from "./arsenals.json";
import sourceData from "./action-data.json";
import localProfiles from "./player-profiles.json";
import readyBody from "./batter-colliders.json";
import {rosterBatter,rosterPitcher,MatchRoster,PlayerProfile} from "./roster";
import {BodyHit,BodyCapsule,sweepBody} from "./pitch-feedback";
import {SWING_CONTACT_MS} from "./player-motion";
import {Contact,Trajectory,carryDistance} from "./batted-ball";
export type Side="batter"|"pitcher";
export type Pace="practice"|"real"|"full";
export const PACE_SETTINGS={practice:{factor:1.85,label:"연습 · 0.54배",badge:"연습 속도 ×0.54"},real:{factor:1.15,label:"빠르게 · 0.87배",badge:"빠른 속도 ×0.87"},full:{factor:1,label:"실제 구속 · 1.00배",badge:"실제 구속 ×1.00"}} as const;
export type PitchType="fastball"|"slider"|"curve"|"changeup"|"splitter"|"sinker"|"cutter";
export type Vec={x:number;y:number};
export const PITCH_NAMES:Record<PitchType,string>={fastball:"포심",slider:"슬라이더",curve:"커브",changeup:"체인지업",splitter:"포크",sinker:"투심",cutter:"커터"};
export type PitchRecord={type:PitchType;velocity:number;usage:number};
export interface Pitch {id:number;type:PitchType;velocity:number;releaseAt:number;flightMs:number;releaseX?:number;releaseY?:number;releaseZ?:number;target:Vec;breakX:number;breakY:number;quality:number;resolved:boolean;reaction?:PitchResult;aiBatterSwing?:{at:number;aim:Vec}|null;bodyHit?:BodyHit|null}
export interface PitchResult {id:number;label:string;kind:"strike"|"ball"|"foul"|"hit"|"out"|"walk"|"hbp";outcome:string;timing:number|null;aimError:number|null;quality:number;distance:number;exitSpeed:number;launchAngle:number;direction:number;points:number;plateEnded:boolean;at:number;swingAt:number|null;swingAim?:Vec;plateLocation?:Vec;bodyHit?:BodyHit;contact?:Contact;trajectory?:Trajectory}
export interface ActionGame {format:"action-v2";code:string;mode:"ai"|"pvp";host:string;guest:string|null;hostRole:Side;batter:string;pitcher:string;pace:Pace;round:number;balls:number;strikes:number;score:number;pitchCount:number;pitch:Pitch|null;history:PitchResult[];createdAt:number;expiresAt:number;roster?:MatchRoster}
export interface ActionView {format:"action-v2";code:string;mode:"ai"|"pvp";role:Side;batter:string;pitcher:string;pace:Pace;round:number;balls:number;strikes:number;score:number;pitchCount:number;pitch:Pitch|null;history:PitchResult[];waiting:boolean;done:boolean;winner:Side|null;serverNow:number;serverReceivedAt?:number;serverSentAt?:number;expiresAt:number;roster?:MatchRoster}
export const clamp=(v:number,a:number,b:number)=>Math.max(a,Math.min(b,v));
export function rand(){return crypto.getRandomValues(new Uint32Array(1))[0]/4294967296}
function normal(){return Math.sqrt(-2*Math.log(Math.max(1e-8,rand())))*Math.cos(2*Math.PI*rand())}
export function batterStats(id:string){const live=rosterBatter(id);if(live)return {id:live.id,name:live.name,team:live.team,PA:live.pa,AB:live.ab,H:live.h,HR:live.hr,SO:live.so,AVG:live.avg,SLG:live.slg,OPS:live.ops};const b=players.batters.find(p=>p.id===id);if(!b)throw new Error("타자를 선택해 주세요.");return b}
export function pitcherStats(id:string){const live=rosterPitcher(id);if(live)return {id:live.id,name:live.name,team:live.team,TBF:live.tbf,BB:live.bb,SO:live.so,IP:Math.floor(live.outs/3)+"."+(live.outs%3),ERA:live.era,WHIP:live.whip};const p=players.pitchers.find(p=>p.id===id);if(!p)throw new Error("투수를 선택해 주세요.");return p}

export function playerProfile(id:string,side:Side):PlayerProfile|undefined{const live=side==="batter"?rosterBatter(id):rosterPitcher(id);if(live)return live.profile;const local=(localProfiles.players as Record<string,PlayerProfile>)[id];if(local)return local;const table=side==="batter"?sourceData.batters:sourceData.pitchers;return (table as Record<string,{profile?:PlayerProfile}>)[id]?.profile}
export function throwsLeft(id:string){const p=playerProfile(id,"pitcher");return p?.throws?p.throws==="L":p?.batsThrows.startsWith("좌")??false}
export function isUnderhand(id:string){const p=playerProfile(id,"pitcher");return p?.delivery==="underhand"||!!p?.batsThrows.includes("언")}
export function isSwitchHitter(id:string){const p=playerProfile(id,"batter");return p?.bats==="S"||!!p?.batsThrows.endsWith("양타")}
export function batsLeft(bid:string,pid:string){const p=playerProfile(bid,"batter");return isSwitchHitter(bid)?!throwsLeft(pid):p?.bats?p.bats==="L":!!p?.batsThrows.endsWith("좌타")}
export function batterHandLabel(bid:string,pid:string){const profile=playerProfile(bid,"batter");if(bid.includes(":")&&!["L","R","S"].includes(profile?.bats??"")&&!/(좌|우|양)타$/.test(profile?.batsThrows??""))return "타석 정보 없음";return (isSwitchHitter(bid)?"양타 · 현재 ":"")+(batsLeft(bid,pid)?"좌타":"우타")}
export function pitcherHandLabel(pid:string){const profile=playerProfile(pid,"pitcher");if(pid.includes(":")&&!["L","R"].includes(profile?.throws??"")&&!/^[좌우]/.test(profile?.batsThrows??""))return "투구손 정보 없음";return (throwsLeft(pid)?"좌투":"우투")+(isUnderhand(pid)?" · 언더핸드":"")}
export function arsenal(id:string):PitchRecord[]{const live=rosterPitcher(id);if(live)return live.arsenal;if(id.includes(":"))throw new Error("투수 구종 자료를 확인해 주세요.");const list=(arsenalData as Record<string,PitchRecord[]>)[id];return list?.length?list:[{type:"fastball",velocity:145,usage:55},{type:"slider",velocity:133,usage:30},{type:"curve",velocity:122,usage:15}]}
export function attributes(bid:string,pid:string){const b=batterStats(bid),p=pitcherStats(pid);return {contact:Math.round(clamp(100-b.SO/b.PA*170,25,95)),power:Math.round(clamp((b.SLG-b.AVG)*200+28,20,95)),control:Math.round(clamp(100-p.BB/p.TBF*400,30,95)),strikeout:Math.round(clamp(p.SO/p.TBF*240,20,95))}}
export function pickAiPitch(pid:string):PitchType{const a=arsenal(pid);let r=rand()*a.reduce((s,p)=>s+p.usage,0);for(const p of a){r-=p.usage;if(r<=0)return p.type}return a[0].type}
export function createPitch(g:ActionGame,type:PitchType,aim:Vec,quality:number,now:number):Pitch{
 const actual=arsenal(g.pitcher).find(p=>p.type===type);if(!actual)throw new Error("선수가 사용하는 구종을 선택해 주세요.");
 const p=pitcherStats(g.pitcher),control=clamp(1-p.BB/p.TBF*4,.4,.92),scatter=(1-quality)*.65+(1-control)*.3;
 const target={x:clamp(aim.x+normal()*scatter,-2,2),y:clamp(aim.y+normal()*scatter,-2,2)};
 const velocity=Math.round((actual.velocity+(quality-.5)*3+(rand()-.5)*2)*10)/10;
 const breaks:Record<PitchType,[number,number]>={fastball:[.02,.03],slider:[.42,.18],curve:[.12,.58],changeup:[-.25,.35],splitter:[-.08,.5],sinker:[-.3,.26],cutter:[.2,.1]};
 const [bx,by]=breaks[type];const hand=throwsLeft(g.pitcher)?-1:1;
 const underhand=isUnderhand(g.pitcher),scale=(playerProfile(g.pitcher,"pitcher")?.heightCm??185)/185,releaseY=(underhand?1.08:1.84)*scale;
 const pitch:Pitch={id:g.pitchCount+1,type,velocity,releaseAt:now+(g.mode==="pvp"?1900:1200),flightMs:Math.round(18.44/(velocity/3.6)*1000*PACE_SETTINGS[g.pace].factor),releaseX:-(underhand?.58:.33)*hand*scale,releaseY,releaseZ:-18.44+(underhand?.22:.12)*scale,target,breakX:bx*hand,breakY:by,quality,resolved:false};
 pitch.bodyHit=findBodyHit(g.batter,g.pitcher,pitch);return pitch;
}
export function ballPosition(p:Pitch,time:number){const u=clamp((time-p.releaseAt)/p.flightMs,0,1.35),bend=Math.sin(Math.PI*Math.min(1,u));return {x:(p.releaseX??-.33)*(1-u)+p.target.x*.5*u-p.breakX*bend,y:(p.releaseY??1.84)*(1-u)+(1.05+p.target.y*.55)*u+p.breakY*bend+.12*bend,z:(p.releaseZ??-18.44)*(1-u)}}
// Blend the final swing windup into the same contact point used by the outgoing ball.
export function incomingBallPosition(p:Pitch,result:PitchResult|undefined,time:number,side:Side){
 const base=(at:number)=>{const pos=ballPosition(p,at);if(side==="pitcher")pos.z+=Math.max(0,-17.65-(p.releaseZ??-18.44))*Math.max(0,1-(at-p.releaseAt)/p.flightMs);return pos;};
 const contact=result?.contact;if(!contact||time<contact.at-SWING_CONTACT_MS)return base(time);
 const t=clamp((time-contact.at+SWING_CONTACT_MS)/SWING_CONTACT_MS,0,1),start=base(contact.at-SWING_CONTACT_MS);
 return {x:start.x+(contact.position.x-start.x)*t,y:start.y+(contact.position.y-start.y)*t,z:start.z+(contact.position.z-start.z)*t};
}
export function findBodyHit(batter:string,pitcher:string,pitch:Pitch){
 const capsules:BodyCapsule[]=readyBody[batsLeft(batter,pitcher)?"left":"right"];
 return sweepBody(at=>ballPosition(pitch,at),pitch.releaseAt+pitch.flightMs,pitch.flightMs,capsules);
}
export function evaluatePitch(g:Pick<ActionGame,"batter"|"pitcher"|"pitch"|"pace">,swing:{at:number;aim:Vec}|null,now:number):PitchResult{
 const pitch=g.pitch!;const b=batterStats(g.batter),att=attributes(g.batter,g.pitcher),arrival=pitch.releaseAt+pitch.flightMs;
 const inZone=Math.abs(pitch.target.x)<=1&&Math.abs(pitch.target.y)<=1;
 const r:PitchResult={id:pitch.id,label:"",kind:"ball",outcome:"BALL",timing:null,aimError:null,quality:0,distance:0,exitSpeed:0,launchAngle:0,direction:0,points:0,plateEnded:false,at:now,swingAt:swing?.at??null,plateLocation:{...pitch.target},...(swing?{swingAim:{...swing.aim}}:{})};
 const bodyHit=pitch.bodyHit===undefined?findBodyHit(g.batter,g.pitcher,pitch):pitch.bodyHit;
 // A swing entered after the dead ball cannot turn a body hit into a batted ball.
 if(bodyHit&&(!swing||swing.at-SWING_CONTACT_MS>bodyHit.at)){
  r.bodyHit=bodyHit;r.swingAt=null;delete r.swingAim;
  if(inZone){r.kind="strike";r.outcome="STRIKE";r.label="데드볼 스트라이크";}
  else{r.kind="hbp";r.outcome="HBP";r.label="사구 · 몸에 맞는 공";r.points=1;r.plateEnded=true;}
  return r;
 }
 if(!swing){r.kind=inZone?"strike":"ball";r.label=inZone?"스트라이크":"볼";r.outcome=inZone?"STRIKE":"BALL";return r}
 const timing=swing.at-arrival,err=Math.hypot(swing.aim.x-pitch.target.x,swing.aim.y-pitch.target.y);
 const tolerance=(g.pace==="practice"?145:100)*(0.75+att.contact/200),radius=.34+att.contact*.0042;
 r.timing=Math.round(timing);r.aimError=Math.round(err*1000)/1000;
 const missed=Math.abs(timing)>tolerance*1.35||err>radius*1.6||swing.at-SWING_CONTACT_MS>arrival;
 if(bodyHit&&(missed||bodyHit.at<=swing.at)){r.bodyHit=bodyHit;r.kind="strike";r.label="데드볼 스트라이크";r.outcome="MISS";return r;}
 if(missed){r.kind="strike";r.label="헛스윙";r.outcome="MISS";return r}
 r.contact={at:swing.at,position:{x:pitch.target.x*.5,y:Math.max(.065,1.05+pitch.target.y*.55),z:0}};
 if(Math.abs(timing)>tolerance||err>radius){r.kind="foul";r.label="파울";r.outcome="FOUL";r.trajectory="foul";r.quality=.15;r.exitSpeed=Math.round(65+att.power*.35);r.launchAngle=20;r.direction=(timing<0?-1:1)*(batsLeft(g.batter,g.pitcher)?-1:1)*(Math.PI/2+.3);r.distance=carryDistance(r.exitSpeed,r.launchAngle,r.contact.position.y);return r}
 const q=clamp(1-(Math.abs(timing)/tolerance)*.55-(err/radius)*.55,0,1);
 const angle=clamp(24+(pitch.target.y-swing.aim.y)*44,-15,65);
 const exit=90+q*58+att.power*.43;
 const trajectory:Trajectory=angle<=10?"ground":angle<=25?"line":"fly";
 const distance=trajectory==="ground"?0:carryDistance(exit,angle,r.contact.position.y);
 const direction=clamp(timing/tolerance*.95*(batsLeft(g.batter,g.pitcher)?-1:1)+(pitch.target.x-swing.aim.x)*.2,-1.3,1.3);
 r.quality=q;r.launchAngle=angle;r.exitSpeed=exit;r.distance=distance;r.direction=direction;r.plateEnded=true;r.trajectory=trajectory;
 const outLabel=trajectory==="ground"?"땅볼 아웃":trajectory==="line"?"직선타 아웃":"뜬공 아웃";
 if(distance>=105&&angle>=14&&angle<=48){r.kind="hit";r.outcome="HR";r.label="홈런!";r.points=4}
 else if(q<.28||angle>50||(angle>28&&distance<70)){r.kind="out";r.outcome="OUT";r.label=outLabel}
 else if(distance>=80){r.kind="hit";r.outcome="2B";r.label="2루타!";r.points=2}
 else if(q>=.42){r.kind="hit";r.outcome="1B";r.label="안타!";r.points=1}
 else{r.kind="out";r.outcome="OUT";r.label=outLabel}
 return r;
}
export function aiSwing(g:ActionGame):{at:number;aim:Vec}|null{
 const p=g.pitch!,b=batterStats(g.batter),inside=Math.abs(p.target.x)<=1&&Math.abs(p.target.y)<=1;
 const current=rosterBatter(g.batter)?.discipline,legacy=(sourceData.batters as Record<string,{discipline:{ZoneSwingRate:number;ChaseRate:number;ZoneContactRate:number;OutZoneContactRate:number}}>)[g.batter]?.discipline;
 const discipline=current?{ZoneSwingRate:current.zoneSwingRate,ChaseRate:current.chaseRate,ZoneContactRate:current.zoneContactRate,OutZoneContactRate:current.outZoneContactRate}:legacy;
 if(rand()>(inside?(discipline?.ZoneSwingRate??.65):(discipline?.ChaseRate??.3)))return null;
 const timingStd=(g.pace==="practice"?80:60)*(0.7+b.SO/b.PA*2);
 const contact=inside?(discipline?.ZoneContactRate??.85):(discipline?.OutZoneContactRate??.65);
 const aimStd=.14+(1-contact)*1.6;
 return {at:p.releaseAt+p.flightMs+normal()*timingStd,aim:{x:p.target.x+normal()*aimStd,y:p.target.y+normal()*aimStd}};
}
export function finishPitch(g:ActionGame,result:PitchResult){if(!g.pitch||g.pitch.resolved)return;g.pitch.resolved=true;
 if(result.kind==="strike"){g.strikes++;if(g.strikes>=3){result.plateEnded=true;result.outcome="K";result.label="삼진 아웃";result.kind="out"}}
 if(result.kind==="foul"&&g.strikes<2)g.strikes++;
 if(result.kind==="ball"){g.balls++;if(g.balls>=4){result.plateEnded=true;result.outcome="BB";result.label="볼넷 출루";result.kind="walk";result.points=1}}
 if(result.plateEnded){g.round++;g.balls=0;g.strikes=0}
 g.score+=result.points;g.pitch.reaction=result;g.history.push(result);
}
export function actionView(g:ActionGame,id:string,now=Date.now()):ActionView{const role=g.host===id?g.hostRole:g.hostRole==="batter"?"pitcher":"batter";return {format:g.format,code:g.code,mode:g.mode,role,batter:g.batter,pitcher:g.pitcher,pace:g.pace,round:g.round,balls:g.balls,strikes:g.strikes,score:g.score,pitchCount:g.pitchCount,pitch:g.pitch,history:g.history,waiting:g.mode==="pvp"&&!g.guest,done:g.round>=6,winner:g.round>=6?(g.score>=4?"batter":"pitcher"):null,serverNow:now,expiresAt:g.expiresAt,...(g.roster?{roster:g.roster}:{})}}
