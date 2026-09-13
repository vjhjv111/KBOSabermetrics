import type {PlayerCustomization} from './player-appearance';
import type {SeasonPlayerStats} from './season-types';
export const SKILLS={contact:'컨택',power:'파워',discipline:'선구안',speed:'주력',fielding:'수비',velocity:'구속',control:'제구',stamina:'체력'} as const;
export type Skill=keyof typeof SKILLS;
export type GameRatings=Record<Skill,number>;
export type CareerAppearance=Required<PlayerCustomization>;
export interface CareerPlayer {
 id:string;version:number;name:string;team:string;position:string;bats:'L'|'R'|'S';throws:'L'|'R';
 delivery:'overhand'|'sidearm'|'underhand';archetype:string;appearance:CareerAppearance;ratings:GameRatings;
 level:number;xp:number;nextLevelXp:number;trainingPoints:number;games:number;stats:SeasonPlayerStats;
 rewards:{gameId:string;xp:number;levels:number;summary:string;at:number}[];trainingLog:string[];
 createdAt:number;updatedAt:number;
}
export interface CareerResponse {player:CareerPlayer|null}
