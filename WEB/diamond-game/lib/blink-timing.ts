export type BlinkWindow=Readonly<{start:number;end:number}>;

const BUCKET_MS=6000,JITTER_MS=2000,CLOSE_MS=55,HOLD_MS=15,OPEN_MS=120;
const DURATION_MS=CLOSE_MS+HOLD_MS+OPEN_MS,INTERRUPT_OPEN_MS=35;
const smooth=(t:number)=>{const u=Math.max(0,Math.min(1,t));return u*u*(3-2*u);};

function bucketStart(bucket:number,seed:string|number){
 // Hash the full signed bucket, rather than truncating an epoch clock to 32
 // bits. Distinguish numeric and string IDs; never use runtime object UUIDs.
 const key=typeof seed+':'+String(seed)+'|'+String(bucket);let hash=2166136261;
 for(let i=0;i<key.length;i++)hash=Math.imul(hash^key.charCodeAt(i),16777619);
 hash=Math.imul(hash^(hash>>>16),0x85ebca6b);hash=Math.imul(hash^(hash>>>13),0xc2b2ae35);hash^=hash>>>16;
 return bucket*BUCKET_MS+(hash>>>0)/4294967296*JITTER_MS;
}
function closure(age:number){
 if(age<=0||age>=DURATION_MS)return 0;
 if(age<CLOSE_MS)return smooth(age/CLOSE_MS);
 if(age<CLOSE_MS+HOLD_MS)return 1;
 return 1-smooth((age-CLOSE_MS-HOLD_MS)/OPEN_MS);
}

/** Pure 0=open, 1=closed blink sample, on the caller's millisecond time axis.
 *
 * One start per 6 s bucket lies in its first 2 s: consecutive starts are 4–8 s
 * apart. A blink closes for 55 ms, holds for 15 ms, and opens for 120 ms.
 * Windows are half-open [start,end); any overlap cancels the entire scheduled
 * blink. Empty/reversed/NaN windows are ignored; infinite endpoints are valid.
 *
 * interruptAt denotes the FIRST unexpected event affecting this blink. Keep
 * that timestamp stable while it reopens; do not pass the current frame time.
 * A window with start===interruptAt is the protection added by that event.
 * For the interrupted blink only, ignore that window when reconstructing the
 * pre-event value/history, then reopen from the sampled value in at most 35 ms.
 * Other pre-existing protection still cancels the blink. Subsequent blinks use
 * every window normally. Do not backdate the new window's start: this API has
 * no provenance with which to distinguish it from a previously planned window.
 *
 * Reopening ends at the earlier of interruptAt+35 and the original blink end.
 * Backward seeks reconstruct the original pre-interruption motion; a cold
 * sample after reopening is simply open. No missed blinks are queued.
 * Non-finite or non-safe-range clocks return open; practical epoch/rAF clocks,
 * negative clocks and fractional milliseconds are accepted.
 */
export function samplePlayerBlink(now:number,seed:string|number,protectedWindows:readonly BlinkWindow[]=[],interruptAt?:number):number{
 if(!Number.isFinite(now)||Math.abs(now)>Number.MAX_SAFE_INTEGER)return 0;
 const start=bucketStart(Math.floor(now/BUCKET_MS),seed),end=start+DURATION_MS,age=now-start;
 if(age<=0||age>=DURATION_MS)return 0;
 const interrupted=interruptAt!==undefined&&Number.isFinite(interruptAt)&&interruptAt>=start&&interruptAt<end;
 for(const window of protectedWindows){
  if(!(window.end>window.start)||window.start>=end||window.end<=start)continue;
  if(interrupted&&window.start===interruptAt)continue;
  return 0;
 }
 if(interrupted&&now>=interruptAt){
  const from=closure(interruptAt-start),duration=Math.min(INTERRUPT_OPEN_MS,end-interruptAt);
  return from*(1-smooth((now-interruptAt)/duration));
 }
 return closure(age);
}
