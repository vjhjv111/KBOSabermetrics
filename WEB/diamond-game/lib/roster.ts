export type RosterPitchType="fastball"|"slider"|"curve"|"changeup"|"splitter"|"sinker"|"cutter";
export type PlayerProfile={batsThrows:string;heightCm?:number;throws?:string;bats?:string;delivery?:string};
export type Discipline={zonePitchRate?:number;zoneSwingRate?:number;chaseRate?:number;zoneContactRate?:number;outZoneContactRate?:number};
export type RosterPlayer={id:string;playerId:string;name:string;team:string;profile:PlayerProfile;discipline:Discipline;sampleNote?:string};
export type RosterBatter=RosterPlayer&{pa:number;ab:number;h:number;hr:number;so:number;avg:number;slg:number;ops:number};
export type RosterPitcher=RosterPlayer&{tbf:number;bb:number;so:number;outs:number;er:number;era:number|null;whip:number|null;arsenal:{type:RosterPitchType;velocity:number;usage:number}[];arsenalSource:"observed"|"default"};
export type RosterTeam={code:string;name:string};
export type MatchRoster={season:number;asOf:string;revision:string;batter:RosterBatter;pitcher:RosterPitcher};
export type RosterResponse={season:number;seasons:number[];asOf:string;revision:string;teams:RosterTeam[];batters:RosterBatter[];pitchers:RosterPitcher[]};

const batters=new Map<string,RosterBatter>(),pitchers=new Map<string,RosterPitcher>();
let pinned:MatchRoster|null=null;
const pitchTypes=new Set<RosterPitchType>(["fastball","slider","curve","changeup","splitter","sinker","cutter"]);
const finite=(value:unknown):value is number=>typeof value==="number"&&Number.isFinite(value);
function validatePlayer(player:RosterPlayer,season:number){
 if(!player||typeof player.playerId!=="string"||player.id!==`${season}:${player.playerId}`||!player.name||typeof player.team!=="string"||!player.profile||!player.discipline)throw new Error("선수 기록 형식을 확인하지 못했습니다.");
}
function validateBatter(player:RosterBatter,season:number){
 validatePlayer(player,season);
 if(![player.pa,player.ab,player.h,player.hr,player.so,player.avg,player.slg,player.ops].every(finite)||player.pa<=0)throw new Error("타자 기록을 확인하지 못했습니다.");
}
function validatePitcher(player:RosterPitcher,season:number){
 validatePlayer(player,season);
 if(![player.tbf,player.bb,player.so,player.outs,player.er].every(finite)||player.tbf<=0||!(player.era===null||finite(player.era))||!(player.whip===null||finite(player.whip))||!Array.isArray(player.arsenal)||!player.arsenal.length||player.arsenal.some(p=>!pitchTypes.has(p.type)||!finite(p.velocity)||p.velocity<=0||!finite(p.usage)||p.usage<=0))throw new Error("투수 기록과 구종을 확인하지 못했습니다.");
}
export function parseRoster(value:unknown):RosterResponse{
 const data=value as RosterResponse;
 if(!data||!Number.isInteger(data.season)||!Array.isArray(data.seasons)||!data.seasons.every(Number.isInteger)||typeof data.asOf!=="string"||typeof data.revision!=="string"||!Array.isArray(data.teams)||!Array.isArray(data.batters)||!Array.isArray(data.pitchers))throw new Error("선수 목록을 확인하지 못했습니다.");
 data.batters.forEach(p=>validateBatter(p,data.season));data.pitchers.forEach(p=>validatePitcher(p,data.season));
 if(new Set(data.batters.map(p=>p.id)).size!==data.batters.length||new Set(data.pitchers.map(p=>p.id)).size!==data.pitchers.length)throw new Error("선수 목록이 중복되어 있습니다.");
 return data;
}
export function registerRoster(data:RosterResponse){
 for(const [id] of batters)if(id.startsWith(data.season+":"))batters.delete(id);
 for(const [id] of pitchers)if(id.startsWith(data.season+":"))pitchers.delete(id);
 data.batters.forEach(player=>batters.set(player.id,player));data.pitchers.forEach(player=>pitchers.set(player.id,player));
}
export function pinMatchRoster(value:MatchRoster|null){
 if(value){validateBatter(value.batter,value.season);validatePitcher(value.pitcher,value.season);}
 pinned=value;
}
export function rosterBatter(id:string){return pinned&&pinned.batter.id===id?pinned.batter:batters.get(id)}
export function rosterPitcher(id:string){return pinned&&pinned.pitcher.id===id?pinned.pitcher:pitchers.get(id)}
export function filterPlayers<T extends RosterPlayer>(players:T[],team:string,query:string):T[]{
 const search=query.trim().toLocaleLowerCase("ko-KR");
 return players.filter(player=>(!team||player.team===team)&&(!search||player.name.toLocaleLowerCase("ko-KR").includes(search)||player.playerId.includes(search)));
}
export function preserveSelection<T extends RosterPlayer>(players:T[],current:string){
 if(players.some(player=>player.id===current))return current;
 const playerId=current.includes(":")?current.slice(current.indexOf(":")+1):current;
 return players.find(player=>player.playerId===playerId)?.id??players[0]?.id??"";
}
const teamNames:Record<string,string>={HH:"한화",HT:"KIA",KT:"KT",LG:"LG",LT:"롯데",NC:"NC",OB:"두산",SK:"SSG",SS:"삼성",WO:"키움"};
export function teamName(code:string,teams:RosterTeam[]=[]){return teams.find(team=>team.code===code)?.name??teamNames[code]??code}
export function playerOptionLabel(player:RosterPlayer,players:RosterPlayer[],teams:RosterTeam[]=[]){
 const label=player.name+" · "+teamName(player.team,teams);
 if(!players.some(other=>other.id!==player.id&&other.name===player.name&&other.team===player.team))return label;
 const profile=player.profile,description=profile.batsThrows??"";
 const hands=/^[좌우]|(?:좌|우|양)타$/.test(description)?description:
  (profile.throws==="L"?"좌투":profile.throws==="R"?"우투":"")+(profile.bats==="L"?"좌타":profile.bats==="R"?"우타":profile.bats==="S"?"양타":"");
 return label+(hands?" · "+hands:"")+" · ID "+player.playerId;
}
export function numberLabel(value:number|null|undefined,digits:number){return finite(value)?value.toFixed(digits):"—"}
