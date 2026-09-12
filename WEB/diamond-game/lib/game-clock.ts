/** Monotonic local time shared by animation, request timing and input timestamps. */
export function localNow(){return typeof performance!=="undefined"?performance.timeOrigin+performance.now():Date.now()}
export type ClockSample={clientStart:number;clientEnd:number;serverReceived:number;serverSent:number};
export function clockSample(s:ClockSample){
 const elapsed=s.clientEnd-s.clientStart,processing=s.serverSent-s.serverReceived;
 if(!Object.values(s).every(Number.isFinite)||elapsed<0||processing<0||processing>elapsed+5)return null;
 return {offset:((s.serverReceived-s.clientStart)+(s.serverSent-s.clientEnd))/2,rtt:Math.max(0,elapsed-processing),at:s.clientEnd};
}
export function createClockSync(){
 let samples:{offset:number;rtt:number;at:number}[]=[];
 return {sample(s:ClockSample){const next=clockSample(s);if(!next)return null;samples=samples.filter(x=>next.at-x.at<20000);samples.push(next);if(samples.length>60)samples.shift();return samples.reduce((best,x)=>x.rtt<best.rtt?x:best).offset}};
}
