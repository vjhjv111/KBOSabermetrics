export type SeasonTab='title'|'game'|'league'|'career'|'practice'|'friendly';

/** Invitations enter their own room; an ordinary visit never starts gameplay. */
export function initialSeasonTab(search:string):SeasonTab{
 const params=new URLSearchParams(search);
 return params.has('room')?'friendly':params.has('match')?'practice':'title';
}
