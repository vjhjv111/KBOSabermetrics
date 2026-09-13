import type {ActionView, Pace, PitchType, Vec} from './action-engine';
import type {RosterBatter, RosterPitcher} from './roster';
import type {PlayerCustomization} from './player-appearance';

/** /api/diamond/season. All mutations use requestId and the last received version. */
export interface SeasonPlayerStats {
 playerId:string; name:string; team:string; pa:number; ab:number; h:number; double:number; triple:number;
 hr:number; bb:number; hbp:number; k:number; r:number; rbi:number; sf:number; gidp:number;
 pitchCount:number; outsPitched:number; hitsAllowed:number; runsAllowed:number; walksAllowed:number;
 hitBatters:number; strikeouts:number;
}
export interface SeasonTeam {code:string;name:string;lineup:string[];batters:RosterBatter[];pitchers:RosterPitcher[]}
export interface SeasonStanding {team:string;name:string;played:number;wins:number;losses:number;ties:number;runsFor:number;runsAgainst:number;pct:number;gamesBehind:number}
export interface SeasonFixture {id:string;day:number;homeTeam:string;awayTeam:string;complete:boolean;homeRuns:number;awayRuns:number}
export interface SeasonRunner {playerId:string;pitcherId:string}
export interface SeasonGame {
 id:string;homeTeam:string;awayTeam:string;inning:number;half:'top'|'bottom';outs:number;
 homeRuns:number;awayRuns:number;homeHits:number;awayHits:number;homeLine:number[];awayLine:number[];
 homeLineup:string[];awayLineup:string[];homeOrder:number;awayOrder:number;
 homePitcher:string;awayPitcher:string;homeUsedPitchers:string[];awayUsedPitchers:string[];
 homeUsedBatters:string[];awayUsedBatters:string[];bases:(SeasonRunner|null)[];
 complete:boolean;endReason:string;playerStats:Record<string,SeasonPlayerStats>;
 participants?:string[];
 events:string[];plateAppearances:number;completedAt:number|null;
}
export interface SeasonSave {
 id:string;version:number;team:string;season:number;seasonNumber:number;asOf:string;revision:string;
 pace:Pace;day:number;totalDays:number;seriesPerPair:number;complete:boolean;
 teams:SeasonTeam[];schedule:SeasonFixture[];standings:SeasonStanding[];
 playerStats:Record<string,SeasonPlayerStats>;game:SeasonGame|null;
 appearances?:Record<string,PlayerCustomization>;
 previousSeasons:{seasonNumber:number;champion:string;wins:number;losses:number;ties:number}[];
 createdAt:number;updatedAt:number;
}
export interface SeasonResponse {save:SeasonSave|null;action:ActionView|null;serverNow:number;serverReceivedAt?:number;serverSentAt?:number}
export type SeasonCommand =
 | {op:'create';requestId:string;season:number;team:string;pace:Pace;seriesPerPair?:number}
 | ({requestId:string;version:number} & (
   {op:'start-game'|'sim-half'|'sim-game'|'sim-day'|'sim-to-player'|'next-season'|'tick'|'take'}
   | {op:'ready';previousPitch:number}
   | {op:'pitch';previousPitch:number;type:PitchType;aim:Vec;quality:number}
   | {op:'swing';pitchId:number;inputAt:number;aim:Vec}
   | {op:'substitute';slot:number;playerId:string}
   | {op:'change-pitcher';playerId:string}
   | {op:'lineup';lineup:string[]}
  ));

export interface FullMatchInfo {code:string;mode:'ai'|'pvp';team:string;hostTeam:string;guestTeam:string;version:number;waiting:boolean;expiresAt:number}
export interface FullMatchResponse extends SeasonResponse {match:FullMatchInfo;appearances:Record<string,PlayerCustomization>}
export type FullMatchCommand =
 | {op:'create';requestId:string;season:number;hostTeam:string;guestTeam:string;mode:'ai'|'pvp';pace:Pace}
 | {op:'join';requestId:string;code:string}
 | ({code:string;requestId:string;version:number} & (
   {op:'sim-half'|'sim-game'|'tick'|'take'}
   | {op:'ready';previousPitch:number}
   | {op:'pitch';previousPitch:number;type:PitchType;aim:Vec;quality:number}
   | {op:'swing';pitchId:number;inputAt:number;aim:Vec}
   | {op:'substitute';slot:number;playerId:string}
   | {op:'change-pitcher';playerId:string}
  ));
