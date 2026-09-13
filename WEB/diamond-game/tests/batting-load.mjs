import assert from 'node:assert/strict';
import {load} from './load-ts.mjs';
const {battingLoad,battingLoadForSwing}=load('lib/batting-load.ts'),{SWING_DURATION_MS}=load('lib/player-motion.ts');
for(const flightMs of [350,500,1000,1800]){
 const pitch={releaseAt:10000,flightMs};
 assert.equal(battingLoad(8000,pitch),0);
 assert(battingLoad(9500,pitch)>0&&battingLoad(9500,pitch)<1);
 assert.equal(battingLoad(10000,pitch),1);
 assert(battingLoad(10000+flightMs-95,pitch)>0,'Normal-timed input continues from the remaining load while the lead foot plants');
 assert.equal(battingLoad(10000+flightMs-30,pitch),0,'Taking the pitch returns to the ready body-contact envelope before arrival');
 let last=0;
 for(let t=8000;t<=10000;t+=5){const current=battingLoad(t,pitch);assert(current>=last&&current<=1);last=current;}
 for(let t=10000;t<=10000+flightMs+1000;t+=5){const current=battingLoad(t,pitch);assert(current<=last&&current>=0);last=current;}
 const settleEnd=10000+flightMs-Math.max(45,flightMs*.035);
 for(const boundary of [8950,9800,settleEnd-180,settleEnd]){
  const h=.001,before=battingLoad(boundary-h,pitch),at=battingLoad(boundary,pitch),after=battingLoad(boundary+h,pitch);
  assert(Math.abs((after-at)-(at-before))/h<.00001,'Preparation velocity stays continuous at stage boundaries');
 }
}
assert.equal(battingLoad(10000,null),0);
const slowPitch={releaseAt:10000,flightMs:1800},earlySwing=9500;
assert.equal(battingLoadForSwing(9600,slowPitch,earlySwing),battingLoad(earlySwing,slowPitch),'Swing preparation freezes at input, including an early swing');
assert(battingLoad(earlySwing+SWING_DURATION_MS,slowPitch)>0,'The pitch is still approaching when this early swing finishes');
assert.equal(battingLoadForSwing(earlySwing+SWING_DURATION_MS,slowPitch,earlySwing),0,'Finishing early does not jump back into a loaded stance');
assert.equal(battingLoadForSwing(10000,slowPitch,11000),1,'A future AI swing keeps preparing normally');
assert.equal(battingLoadForSwing(10000,slowPitch,null),1,'The next pitch can prepare again');
console.log('PASS pre-pitch loading, all pitch speeds, take recovery and continuous preparation');
